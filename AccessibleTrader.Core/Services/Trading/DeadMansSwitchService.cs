using System.Globalization;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Plugins;
using Microsoft.Extensions.Logging;

namespace AccessibleTrader.Core.Services.Trading
{
    /// <summary>
    /// The dead-man's switch (Cody 2026-11-26). With <see cref="SettingsKeys.DeadMansSwitch"/>
    /// on, every venue with a stored key that offers one (<see cref="IDeadMansSwitch"/>,
    /// Hyperliquid today) is told every <see cref="RenewEvery"/> to cancel the account's open
    /// orders <see cref="SettingsKeys.DeadMansSwitchSeconds"/> from now. While the terminal
    /// runs, that time never arrives. A crash, a forced shutdown or a lost connection stops the
    /// renewals and the venue cancels. A normal close (<see cref="ClearAsync"/>) and switching
    /// the setting off clear it, so orders stay until the user cancels them: Cody's rule.
    ///
    /// <para>
    /// Off by default and never armed where live trading is not allowed (the hosted terminal).
    /// Paper mode does not disarm it: the venue's orders are real whichever mode the ticket is
    /// in. On the WebHost it is per browser circuit, so two open tabs both renew it and closing
    /// one clears it until the other's next renewal, at most <see cref="RenewEvery"/> later.
    /// </para>
    /// </summary>
    public sealed class DeadMansSwitchService : IDisposable, IAsyncDisposable
    {
        /// <summary>How often the time is pushed forward. The shortest allowed wait is
        /// <see cref="MinSeconds"/>, so even then one renewal can fail without the switch firing.</summary>
        public static readonly TimeSpan RenewEvery = TimeSpan.FromSeconds(20);
        public const int DefaultSeconds = 60;
        public const int MinSeconds = 30;
        public const int MaxSeconds = 3600;

        private readonly IDataService _data;
        private readonly IApiKeyService _keys;
        private readonly ISettingsManager _settings;
        private readonly IEventBus _bus;
        private readonly DemoPolicy _demo;
        private readonly TimeProvider _time;
        private readonly ILogger<DeadMansSwitchService>? _logger;

        private readonly SemaphoreSlim _gate = new(1, 1);
        private readonly CancellationTokenSource _cts = new();
        // Venue name → the switch and the wait it was last set with. Only venues the switch is
        // SET on are here; one whose renewal failed stays, because its earlier time still stands.
        private readonly Dictionary<string, (IDeadMansSwitch Venue, int Seconds, DateTimeOffset FiresAt)> _armed =
            new(StringComparer.OrdinalIgnoreCase);
        // Venues whose last attempt failed, so the failure is said once and the recovery once.
        private readonly HashSet<string> _failing = new(StringComparer.OrdinalIgnoreCase);
        private int _started;
        private bool _closed;

        public DeadMansSwitchService(IDataService data, IApiKeyService keys, ISettingsManager settings,
            IEventBus bus, DemoPolicy demo, ILogger<DeadMansSwitchService>? logger = null, TimeProvider? time = null)
        {
            _data = data;
            _keys = keys;
            _settings = settings;
            _bus = bus;
            _demo = demo;
            _logger = logger;
            _time = time ?? TimeProvider.System;
        }

        /// <summary>The user's wait, clamped to what the switch allows.</summary>
        public static int ClampSeconds(int seconds) => Math.Clamp(seconds, MinSeconds, MaxSeconds);

        private bool Enabled =>
            _demo.AllowLiveTrading
            && (_settings.GetSetting(SettingsKeys.DeadMansSwitch)?.ToObject<bool>() ?? false);

        private int Seconds =>
            ClampSeconds(_settings.GetSetting(SettingsKeys.DeadMansSwitchSeconds)?.ToObject<int>() ?? DefaultSeconds);

        /// <summary>Venues the switch is set on now; for tests and the status line.</summary>
        public IReadOnlyCollection<string> ArmedVenues
        {
            get { lock (_armed) return _armed.Keys.ToList(); }
        }

        /// <summary>Starts the renewals; idempotent. Called once providers and keys are known.</summary>
        public void Start()
        {
            if (Interlocked.Exchange(ref _started, 1) != 0) return;
            _ = RunAsync(_cts.Token);
        }

        private async Task RunAsync(CancellationToken ct)
        {
            using var timer = new PeriodicTimer(RenewEvery, _time);
            try
            {
                do { await TickAsync().ConfigureAwait(false); }
                while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false));
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { _logger?.LogError(ex, "Dead-man's switch loop stopped."); }
        }

        /// <summary>Applies the setting now, after Settings is saved, instead of at the next renewal.</summary>
        public void Reconcile() => _ = TickAsync();

        /// <summary>
        /// One renewal: sets or pushes forward the cancel time on every venue that offers it, or,
        /// with the setting off, clears it wherever it was set. Never throws.
        /// </summary>
        public async Task TickAsync()
        {
            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_closed) return;
                if (!Enabled)
                {
                    await DisarmAllAsync(announce: true).ConfigureAwait(false);
                    return;
                }
                int seconds = Seconds;
                foreach (var (name, venue) in await VenuesAsync().ConfigureAwait(false))
                    await RenewAsync(name, venue, seconds).ConfigureAwait(false);
            }
            catch (Exception ex) { _logger?.LogWarning(ex, "Dead-man's switch renewal failed."); }
            finally { _gate.Release(); }
        }

        private async Task RenewAsync(string name, IDeadMansSwitch venue, int seconds)
        {
            var firesAt = _time.GetUtcNow().AddSeconds(seconds);
            string? error;
            try { error = await venue.ScheduleCancelAsync(firesAt).ConfigureAwait(false); }
            catch (Exception ex)
            {
                _logger?.LogDebug("Dead-man's switch on {Venue}: {Type}.", name, ex.GetType().Name);
                error = "no answer from " + name;
            }

            (IDeadMansSwitch Venue, int Seconds, DateTimeOffset FiresAt) was;
            bool wasArmed;
            lock (_armed) wasArmed = _armed.TryGetValue(name, out was);

            if (error == null)
            {
                lock (_armed) _armed[name] = (venue, seconds, firesAt);
                bool recovered = _failing.Remove(name);
                if (!wasArmed || was.Seconds != seconds)
                    Say($"Dead-man's switch on for {name}: if the terminal stops for {seconds} seconds, "
                      + $"{name} cancels your open orders there, which may include stops and targets. "
                      + "Positions stay open. Closing the terminal normally keeps your orders.",
                        SpeechChannel.Interface);
                else if (recovered)
                    Say($"Dead-man's switch renewed on {name} again.", SpeechChannel.Interface);
                return;
            }

            if (!_failing.Add(name)) return;   // said once per failure streak
            Say(wasArmed
                    ? $"Dead-man's switch could not be renewed on {name}: {error}. Unless it is renewed, "
                    + $"{name} cancels your open orders there at {Clock(was.FiresAt)} UTC."
                    : $"Dead-man's switch could not be set on {name}: {error}. Your orders there are not covered.",
                SpeechChannel.Critical);
        }

        private async Task<List<(string Name, IDeadMansSwitch Venue)>> VenuesAsync()
        {
            var found = new List<(string, IDeadMansSwitch)>();
            List<ApiKeyConfig> stored;
            try { stored = await _keys.GetAllKeysAsync().ConfigureAwait(false); }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Dead-man's switch could not read the stored keys.");
                return found;
            }
            foreach (var name in stored
                .Where(k => k.IsActive && !k.AllowsWithdrawal && !string.IsNullOrEmpty(k.ApiKey))
                .Select(k => k.Provider)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                IMarketDataProvider? provider;
                try { provider = await _data.GetProviderAsync(name).ConfigureAwait(false); }
                catch { continue; }
                var venue = provider as IDeadMansSwitch ?? provider?.GetCapability<IDeadMansSwitch>();
                if (venue is { CanScheduleCancel: true }) found.Add((name, venue));
            }
            return found;
        }

        private async Task DisarmAllAsync(bool announce)
        {
            List<KeyValuePair<string, (IDeadMansSwitch Venue, int Seconds, DateTimeOffset FiresAt)>> armed;
            lock (_armed) armed = _armed.ToList();
            foreach (var (name, entry) in armed)
            {
                string? error;
                try { error = await entry.Venue.ScheduleCancelAsync(null).ConfigureAwait(false); }
                catch (Exception ex)
                {
                    _logger?.LogDebug("Dead-man's switch clear on {Venue}: {Type}.", name, ex.GetType().Name);
                    error = "no answer from " + name;
                }
                if (error == null)
                {
                    lock (_armed) _armed.Remove(name);
                    _failing.Remove(name);
                    if (announce)
                        Say($"Dead-man's switch off for {name}. Your open orders there stay until you cancel them.",
                            SpeechChannel.Interface);
                }
                else if (announce)
                {
                    // Kept in _armed so the next tick tries again; the old time still stands.
                    Say($"Dead-man's switch could not be cleared on {name}: {error}. {name} still cancels your "
                      + $"open orders there at {Clock(entry.FiresAt)} UTC unless it is cleared.",
                        SpeechChannel.Critical);
                }
            }
        }

        /// <summary>
        /// The normal close: clears the switch everywhere it is set, so orders stay, and stops
        /// renewing. Bounded by <paramref name="limit"/> so a dead network cannot hold the exit.
        /// </summary>
        public async Task ClearAsync(TimeSpan limit)
        {
            try { _cts.Cancel(); } catch (ObjectDisposedException) { return; }
            if (!await _gate.WaitAsync(limit).ConfigureAwait(false)) return;
            try
            {
                if (_closed) return;
                _closed = true;
                await DisarmAllAsync(announce: false).WaitAsync(limit).ConfigureAwait(false);
            }
            catch (Exception ex) { _logger?.LogWarning(ex, "Dead-man's switch was not cleared on close."); }
            finally { _gate.Release(); }
        }

        private static string Clock(DateTimeOffset at) =>
            at.UtcDateTime.ToString("HH:mm:ss", CultureInfo.InvariantCulture);

        private void Say(string text, SpeechChannel channel) =>
            _bus.Publish(new AnnouncementEvent(text, Interrupt: false, Channel: channel));

        /// <summary>A normal close on the WebHost: the circuit's scope ends and clears the switch.</summary>
        public async ValueTask DisposeAsync()
        {
            try { await ClearAsync(CloseLimit).ConfigureAwait(false); }
            catch { /* best effort: the close must not throw */ }
        }

        public void Dispose()
        {
            try { ClearAsync(CloseLimit).GetAwaiter().GetResult(); }
            catch { /* best effort: the close must not throw */ }
        }

        /// <summary>How long a normal close waits for the venues to confirm the switch is cleared.</summary>
        public static readonly TimeSpan CloseLimit = TimeSpan.FromSeconds(3);
    }
}
