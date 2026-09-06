using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Alerts;
using AccessibleTrader.Core.Services.Notifications;
using Microsoft.Extensions.DependencyInjection;

namespace AccessibleTrader.WebHost.Services
{
    /// <summary>
    /// <b>One DI scope for the life of the process — the terminal's session with no UI.</b>
    ///
    /// <para>
    /// ── Why this exists ────────────────────────────────────────────────────────
    /// "Close the browser and keep getting notifications" was never a missing feature; it was a
    /// SERVICE-LIFETIME boundary. On the WebHost, <see cref="IEventBus"/>,
    /// <see cref="IWorkspaceStore"/>, <see cref="IDataService"/>,
    /// <c>IOrderExecutionService</c> and <see cref="DesktopNotificationService"/> are all
    /// <c>AddScoped</c>, which for Blazor Server means <b>per circuit</b>. Close the browser,
    /// the circuit is disposed, and every subscription in that list goes with it.
    /// <see cref="LocalBackgroundMonitor"/> survived only by being a PARALLEL implementation —
    /// its own evaluator, its own fetch loop, its own delivery — and it created a throwaway
    /// scope per poll, so nothing inside one could hold a subscription for longer than a
    /// single 60-second tick.
    /// </para>
    ///
    /// <para>
    /// This class is the smallest thing that fixes that: <b>one scope, created once, kept for
    /// the process lifetime.</b> "Browser closed" becomes "a session that happens to have no
    /// UI", and every existing in-session subscriber works unchanged inside it. It is
    /// deliberately NOT a re-lifetiming of the container — scoped-per-circuit is correct for
    /// everything a circuit actually owns, and changing that would be a far larger and riskier
    /// change than the problem needs.
    /// </para>
    ///
    /// <para>
    /// ── THE HAZARD, and it is the narration bug inverted ──────────────────────
    /// Two subscribers speaking about the same event is one LOST utterance (2026-09-05). Two
    /// long-lived sessions subscribing to two buses is the mirror image: a DOUBLED one. There
    /// are now two of several services alive in one process — one set per circuit, one set
    /// here. The invariant that keeps it to exactly one delivery is stated in two places and
    /// nowhere else:
    /// </para>
    /// <list type="bullet">
    ///   <item><b>Evaluation</b> — <see cref="CircuitAlertCoverage"/>. A symbol an open circuit
    ///   is watching is skipped headless; a symbol nobody has on screen is taken headless. Each
    ///   fired alert therefore has exactly one producer.</item>
    ///   <item><b>Delivery</b> — <see cref="DesktopNotificationCategories"/>. The headless
    ///   <see cref="DesktopNotificationService"/> is built WITHOUT the Alerts category, because
    ///   <see cref="LocalBackgroundMonitor"/> already delivers its own alert (sound, toast,
    ///   speech) under its own opt-in switch — and, since Phase 2, without OrderFills either,
    ///   because <see cref="HeadlessOrderAnnouncer"/> owns those and is the only one of the two
    ///   that can ask <see cref="CircuitOrderCoverage"/> whether a browser is already saying
    ///   it.</item>
    ///   <item><b>Fills</b> — <see cref="CircuitOrderCoverage"/>. A venue whose stream an open
    ///   circuit has hooked announces through that circuit; every other venue is ours. Asked at
    ///   delivery time, because browsers open and close between a subscription and a fill.</item>
    /// </list>
    ///
    /// <para>
    /// ── Lifetime and laziness ─────────────────────────────────────────────────
    /// The scope is created on first use rather than at startup: nothing needs it until the
    /// monitor's first poll, and building the settings/workspace stack during host startup
    /// would move real work into a path that currently has none. Registered as a singleton, so
    /// the container disposes it (and therefore the scope, and therefore every subscription in
    /// it) on shutdown.
    /// </para>
    ///
    /// <para>
    /// ── SAFETY LINE, restated here because this is the class that makes it possible ───────
    /// <b>The headless session REPORTS; it never ACTS.</b> Nothing resolved here places an
    /// order, moves a stop, or runs a strategy. Phase 2 makes that line load-bearing rather
    /// than aspirational: an <c>IOrderExecutionService</c> now lives in this scope, subscribed
    /// to real venues with real credentials, and it is here to LISTEN. Anything that acts on
    /// the market stays in-session, where a person is present.
    /// </para>
    /// </summary>
    public sealed class HeadlessSession : IDisposable
    {
        private readonly IServiceScopeFactory _scopes;
        private readonly ILogger<HeadlessSession> _logger;
        private readonly object _gate = new();

        private IServiceScope? _scope;
        private readonly List<IDisposable> _subscribers = new();
        private bool _disposed;

        /// <summary>One caller at a time inside <see cref="EnsureDataReadyAsync"/>.</summary>
        private readonly SemaphoreSlim _dataGate = new(1, 1);

        public HeadlessSession(IServiceScopeFactory scopes, ILogger<HeadlessSession> logger)
        {
            _scopes = scopes;
            _logger = logger;
        }

        /// <summary>Whether the long-lived scope has been created yet.</summary>
        public bool IsStarted { get { lock (_gate) return _scope != null; } }

        /// <summary>
        /// The process-lifetime service provider. Creates the scope and its long-lived
        /// subscribers on first call.
        /// </summary>
        public IServiceProvider Services
        {
            get
            {
                lock (_gate)
                {
                    ObjectDisposedException.ThrowIf(_disposed, this);
                    if (_scope == null) Start();
                    return _scope!.ServiceProvider;
                }
            }
        }

        /// <summary>Resolve a required service from the long-lived scope.</summary>
        public T Get<T>() where T : notnull => Services.GetRequiredService<T>();

        /// <summary>Resolve an optional service from the long-lived scope.</summary>
        public T? GetOptional<T>() where T : class => Services.GetService<T>();

        /// <summary>
        /// Plugins loaded and every stored key applied to its provider — the preamble both
        /// headless loops need before they can ask a provider anything.
        ///
        /// <para>
        /// <b>It is serialised, and that is the point.</b> Since Phase 2 there are TWO
        /// <see cref="BackgroundService"/>s ticking on the same 60-second interval against this
        /// ONE scope, and therefore against one scoped <see cref="IDataService"/> whose
        /// <c>InitializeAsync</c> is not thread-safe: it guards on a plain bool set at the END of
        /// the method and appends to plain <see cref="List{T}"/>s in between. Two loops starting
        /// together would both enter it and mutate those lists concurrently — a corrupted
        /// provider list or an <see cref="InvalidOperationException"/> out of a collection, on
        /// the very tick the whole feature exists to serve. One gate, one caller at a time.
        /// </para>
        ///
        /// <para>
        /// <c>ConfigureStoredKeyProvidersAsync</c> stays INSIDE the gate rather than being
        /// hoisted to startup: it is how a key saved five minutes ago reaches its provider, so it
        /// belongs on the poll. It skips anything already configured, so the repeat costs a loop
        /// over the key list.
        /// </para>
        /// </summary>
        public async Task EnsureDataReadyAsync()
        {
            var sp = Services;
            await _dataGate.WaitAsync().ConfigureAwait(false);
            try
            {
                var data = sp.GetRequiredService<IDataService>();
                await data.InitializeAsync(sp.GetRequiredService<IPluginLoaderService>()).ConfigureAwait(false);
                await data.ConfigureStoredKeyProvidersAsync().ConfigureAwait(false);
            }
            finally
            {
                _dataGate.Release();
            }
        }

        // ── Startup ──────────────────────────────────────────────────────────

        /// <remarks>Call under <see cref="_gate"/>.</remarks>
        private void Start()
        {
            _scope = _scopes.CreateScope();
            var sp = _scope.ServiceProvider;

            // Force-created, because a subscriber nobody resolves is a subscriber that never
            // subscribes — the same reason MainLayout eagerly injects these on the browser side
            // and the circuit handler force-creates the in-session alert recorder.
            //
            // What is DELIBERATELY not here is as important as what is:
            //   • InSessionAlertRecorder — LocalBackgroundMonitor files its own alerts into
            //     RecentAlertsBuffer directly, and a second recorder on this bus would put every
            //     background alert in the tray list twice.
            //   • AccessibilityFeedbackCoordinator / SonificationManager — they speak and sound
            //     THROUGH THE BROWSER. With no browser attached they would deliver to nobody
            //     while looking, from the log, like delivery happened.
            TryAdd<AlertDeliveryService>(sp,
                "email / Telegram / webhook fan-out for alerts fired with no browser attached");

            // Order events — fills, stops, take-profits, and orders that leave the book —
            // spoken/toasted/sounded with no browser attached. HeadlessOrderWatch keeps the
            // venue streams hooked; this is what turns the resulting bus event into something
            // the user hears. Created here rather than by the watch so it exists BEFORE any
            // subscription can fire: a fill published into no subscriber is the same silence
            // one layer down.
            try
            {
                if (sp.GetService<IDesktopAlertPresenter>() is { } presenter)
                    _subscribers.Add(new HeadlessOrderAnnouncer(
                        sp.GetRequiredService<IEventBus>(),
                        presenter,
                        sp.GetService<ILogger<HeadlessOrderAnnouncer>>()));
                else
                    _logger.LogInformation(
                        "Headless session has no desktop presenter; order events will not be announced.");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Headless session could not start order announcements.");
            }

            // New bars, per-category opt-in, exactly as in a circuit — but NOT alerts, and
            // NOT order fills. See DesktopNotificationCategories for why the mask and not a
            // settings check, and HeadlessOrderAnnouncer for why fills left this instance:
            // this service cannot ask CircuitOrderCoverage anything, so it would toast a fill
            // an open browser session was already announcing.
            try
            {
                // Constructed by hand rather than through ActivatorUtilities: the class has two
                // constructors that differ only by the mask, and "whichever one the reflection
                // matcher liked best" is not a thing to leave to chance when the wrong answer is
                // a silent double toast.
                _subscribers.Add(new DesktopNotificationService(
                    sp.GetRequiredService<IEventBus>(),
                    sp.GetRequiredService<IWorkspaceStore>(),
                    sp.GetRequiredService<ISettingsManager>(),
                    sp.GetRequiredService<IDesktopNotifier>(),
                    DesktopNotificationCategories.NewBars,
                    sp.GetService<ILogger<DesktopNotificationService>>()));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Headless session could not start desktop toasts for fills and new bars.");
            }

            _logger.LogInformation(
                "Headless session started ({Count} long-lived subscriber(s)). Alerts keep evaluating with no browser attached.",
                _subscribers.Count);
        }

        private void TryAdd<T>(IServiceProvider sp, string what) where T : class, IDisposable
        {
            try
            {
                if (sp.GetService<T>() is { } service) _subscribers.Add(service);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Headless session could not start {Service} ({What}).", typeof(T).Name, what);
            }
        }

        public void Dispose()
        {
            IServiceScope? scope;
            List<IDisposable> subscribers;
            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true;
                scope = _scope;
                _scope = null;
                subscribers = new List<IDisposable>(_subscribers);
                _subscribers.Clear();
            }

            // Anything CreateInstance'd is not owned by the scope, so it is disposed here;
            // anything resolved FROM the scope is disposed twice, which every service in this
            // list tolerates (CompositeDisposable.Dispose is idempotent).
            foreach (var s in subscribers)
            {
                try { s.Dispose(); } catch (Exception ex) { _logger.LogDebug(ex, "Headless subscriber dispose failed."); }
            }
            scope?.Dispose();
            _dataGate.Dispose();
        }
    }
}
