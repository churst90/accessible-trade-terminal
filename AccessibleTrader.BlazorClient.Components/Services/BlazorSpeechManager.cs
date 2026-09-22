using Microsoft.Extensions.Logging;
using AccessibleTrader.Core.Services;

namespace AccessibleTrader.BlazorClient.Services
{
    public class BlazorSpeechManager : ISpeechManager, IDisposable
    {
        private readonly ILogger<BlazorSpeechManager> _logger;
        private readonly IServiceProvider _services;
        private readonly INvdaControllerClient _nvda;
        private readonly IJawsApiClient _jaws;
        private IJournalService? _journal; // resolved lazily to avoid construction-order coupling
        private string _queuedText = string.Empty;

        /// <summary>
        /// How long a "is NVDA running" answer is trusted before it is asked again.
        ///
        /// <para>
        /// There used to be no such interval, because the question was asked exactly once, in the
        /// constructor, and the answer latched for the life of the process — a singleton on the
        /// desktop head, so for the life of the app. A user who started NVDA after the terminal,
        /// or restarted it after a crash, stayed on the fallback path for ever with nothing said.
        /// The constructor's own comment promised "Immediate check, then background monitor" and
        /// there was no monitor.
        /// </para>
        ///
        /// <para>
        /// Two seconds rather than per-utterance: the P/Invoke is cheap but not free, and speech
        /// here is driven by arrow-key repeat, so per-utterance would put a native call on the
        /// hot path of the most common interaction in the application.
        /// </para>
        /// </summary>
        internal static readonly TimeSpan DefaultReaderProbeInterval = TimeSpan.FromSeconds(2);

        private readonly TimeSpan _readerProbeInterval;

        // ONE TIMESTAMP PER READER, and that is not incidental. The first cut of the JAWS path
        // shared a single stamp between both probes, so asking about JAWS reset the clock that
        // decided whether to re-ask about NVDA — a probe forced after NVDA threw was silently
        // consumed by the JAWS check on the very next line, and the NVDA answer stayed stale.
        // Two callers sharing one piece of throttle state is not a throttle, it is a race.
        private DateTime _lastReaderProbeUtc = DateTime.MinValue;   // NVDA
        private DateTime _lastJawsProbeUtc = DateTime.MinValue;
        private bool _readerRunning;
        private bool _jawsRunning;
        private bool _muteReported;
        private SpeechOutputStatus? _reportedStatus;

        public bool IsActive => OutputStatus != SpeechOutputStatus.Mute;

        /// <summary>
        /// <b>Which channel speech is leaving by, or that it is not leaving at all.</b> A value
        /// anyone can ask for, which is the point: before 2026-09-21 the equivalent was a string
        /// nothing in the application ever read, so "the terminal is mute" was a state the user
        /// could only infer from the absence of sound.
        /// </summary>
        public SpeechOutputStatus OutputStatus =>
            IsNvdaUsable() ? SpeechOutputStatus.NvdaDirect
            : IsJawsUsable() ? SpeechOutputStatus.JawsDirect
            : (LiveRegionEnabled && OnSpeak != null) ? SpeechOutputStatus.LiveRegion
            : SpeechOutputStatus.Mute;

        public string SpeechMode => OutputStatus switch
        {
            SpeechOutputStatus.NvdaDirect => "NVDA Direct",
            SpeechOutputStatus.JawsDirect => "JAWS Direct",
            SpeechOutputStatus.LiveRegion => "ARIA Live",
            _ => "None",
        };

        public bool IsSpeechEnabled { get; set; } = true;

        /// <summary>
        /// When false the ARIA live region is skipped (journal + NVDA paths
        /// unaffected). The WebHost sets this for the "Browser voice" speech
        /// output mode so a screen reader that IS running won't double-speak;
        /// MAUI never touches it.
        /// </summary>
        public bool LiveRegionEnabled { get; set; } = true;

        private Action<string>? _onSpeak;
        public Action<string>? OnSpeak
        {
            get => _onSpeak;
            set
            {
                _onSpeak = value;
                if (_onSpeak != null && !string.IsNullOrEmpty(_queuedText))
                {
                    _onSpeak(_queuedText);
                    _queuedText = string.Empty;
                }
                // A live region arriving is a recovery from Mute, so a later loss of every path
                // is worth reporting again rather than being swallowed as "already said".
                if (_onSpeak != null) _muteReported = false;
            }
        }

        public BlazorSpeechManager(ILogger<BlazorSpeechManager> logger, IServiceProvider services,
                                   INvdaControllerClient? nvda = null, TimeSpan? readerProbeInterval = null,
                                   IJawsApiClient? jaws = null)
        {
            _logger = logger;
            _services = services;
            _nvda = nvda ?? new NvdaControllerClient();
            // NVDA is tried first only because it is the reader this application was developed
            // against; the two are never both running in practice, so the order is a tie-break
            // and not a preference.
            _jaws = jaws ?? (OperatingSystem.IsWindows()
                ? new JawsApiClient()
                : (IJawsApiClient)new NullJawsApiClient());
            // A PARAMETER rather than a const, so a test can drive the re-probe without sleeping
            // and without reaching into a private field. The first draft of the tests did reach
            // in — it reset the probe stamp directly — and a sabotage restoring the old
            // ask-once-and-latch behaviour passed, because the stamp the helper poked was the
            // very field the latch keyed on. A test that reaches into an implementation detail
            // ends up agreeing with any implementation that shares it.
            _readerProbeInterval = readerProbeInterval ?? DefaultReaderProbeInterval;

            // ── The install-level fact, asked once and reported once ────────────────────
            //
            // Whether the CLIENT LIBRARY is present is a fact about the build, not about the
            // user, and it cannot change while the process runs. It is worth one line at
            // startup because on the desktop head it decides whether the chart can speak at
            // all: the chart is a native SkiaSharp canvas on top of the BlazorWebView, so a
            // reader focused on the chart is not reading the web view's DOM and the live-region
            // fallback cannot reach it. Reported from Cody's Windows VM on 2026-09-21 — the
            // chart was silent, F2 said nothing, and the same build served over the web was
            // fine, which is exactly the shape this difference produces.
            if (!_nvda.IsClientLibraryAvailable)
            {
                _logger.LogWarning(
                    "nvdaControllerClient64.dll could not be loaded, so NVDA-direct speech is unavailable. "
                  + "On the desktop head the chart canvas is a native control, so the ARIA live-region "
                  + "fallback cannot reach a screen reader while the chart has focus. Copy the x64 "
                  + "nvdaControllerClient64.dll into this folder, beside AccessibleTrader.BlazorClient.exe "
                  + "(from a source build: vendor/nvda/, then rebuild). See docs/PLATFORMS.md.");
            }
        }

        private IJournalService? Journal
        {
            get
            {
                // Lazy resolve so we don't force JournalService construction during ctor.
                if (_journal == null)
                {
                    try { _journal = _services.GetService(typeof(IJournalService)) as IJournalService; }
                    catch { /* journal is best-effort — never break speech */ }
                }
                return _journal;
            }
        }

        /// <summary>
        /// Whether the NVDA path can carry this utterance: the library is present AND a reader
        /// answered recently. The two are deliberately separate — the first is permanent and the
        /// second is not, and collapsing them into one latched bool is what made a reader started
        /// after the terminal invisible for ever.
        /// </summary>
        /// <summary>
        /// Whether the NVDA path can carry this utterance: the library is present AND a reader
        /// answered recently. The two are deliberately separate — the first is permanent and the
        /// second is not, and collapsing them into one latched bool is what made a reader started
        /// after the terminal invisible for ever.
        /// </summary>
        private bool IsNvdaUsable()
        {
            // Guarded before the throttle: a missing client library is a fact about the INSTALL
            // and must never cost a failing P/Invoke per probe.
            if (!_nvda.IsClientLibraryAvailable) return false;

            var now = DateTime.UtcNow;
            if (now - _lastReaderProbeUtc >= _readerProbeInterval)
            {
                _lastReaderProbeUtc = now;
                _readerRunning = _nvda.IsReaderRunning();
            }
            return _readerRunning;
        }

        /// <summary>
        /// Whether JAWS can carry this utterance. There is no install-level fact to cache
        /// separately: the COM object exists only while JAWS is running, so "installed" and
        /// "running" are one question.
        /// </summary>
        private bool IsJawsUsable()
        {
            var now = DateTime.UtcNow;
            if (now - _lastJawsProbeUtc >= _readerProbeInterval)
            {
                _lastJawsProbeUtc = now;
                _jawsRunning = _jaws.IsReaderRunning();
            }
            return _jawsRunning;
        }

        public void Speak(string text, bool interrupt = false)
        {
            if (string.IsNullOrWhiteSpace(text) || !IsSpeechEnabled) return;

            // Mirror every spoken phrase into the journal so it can be reviewed/copied later.
            // Done before the NVDA call so even speech that's interrupted is captured — and,
            // since 2026-09-21, so that a MUTE terminal still has a written record of every
            // sentence it could not say.
            try { Journal?.AddSpeech(text); } catch { /* never let journal break speech */ }

            ReportPathIfChanged();

            if (IsNvdaUsable())
            {
                try
                {
                    if (interrupt) _nvda.CancelSpeech();
                    _nvda.Speak(text);
                    return;
                }
                catch (Exception ex)
                {
                    // A throw here is the library going away mid-session (NVDA crashed, the DLL
                    // was replaced). Drop the cached "running" answer so the next probe asks
                    // again, and fall through to the live region for THIS utterance rather than
                    // losing it.
                    _readerRunning = false;
                    _lastReaderProbeUtc = DateTime.MinValue;
                    _logger.LogWarning(ex, "NVDA-direct speech failed; falling back to the live region.");
                }
            }

            if (IsJawsUsable())
            {
                try
                {
                    _jaws.Speak(text, interrupt);
                    return;
                }
                catch (Exception ex)
                {
                    _jawsRunning = false;
                    _lastJawsProbeUtc = DateTime.MinValue;
                    _logger.LogWarning(ex, "JAWS speech failed; falling back to the live region.");
                }
            }

            if (LiveRegionEnabled && OnSpeak != null)
            {
                OnSpeak(text);
                return;
            }

            // ── Nothing carried it ──────────────────────────────────────────────────────
            //
            // Queue it, exactly as before, so a live region attaching a moment later still says
            // the first thing the terminal wanted to say. But queuing is not delivery, and the
            // old code's silence about that is the defect: a desktop launch with no DLL and no
            // rendered layout dropped every sentence into a one-deep buffer and told nobody.
            // The queue is one utterance, so the second onwards are LOST, and this is the only
            // place that knows it.
            if (!LiveRegionEnabled) return;
            _queuedText = text;
            ReportMuteOnce();
        }

        /// <summary>
        /// <b>Writes WHICH WAY speech is leaving into the journal, once, and again whenever it
        /// changes.</b>
        ///
        /// <para>
        /// The journal earns this specifically. On 2026-09-21 the desktop head was silent and the
        /// question was whether speech was not being GENERATED or not being DELIVERED; the journal
        /// answered it in one keystroke, because it holds every sentence the terminal composed
        /// whether or not anything carried it. It is therefore the one channel known to reach a
        /// user whose speech is broken — which makes it the right place to say why.
        /// </para>
        ///
        /// <para>
        /// Three booleans fully determine the answer, so all three are printed rather than the
        /// conclusion alone: a reader that is running with no library present is a staging
        /// problem, a library present with no reader running is NVDA not started, and a live
        /// region attached while the chart has focus is the case that looks like working speech
        /// and is not — on this head the chart is a native canvas over the web view, so nothing
        /// is watching that region while the user is on the chart.
        /// </para>
        /// </summary>
        private void ReportPathIfChanged()
        {
            var status = OutputStatus;
            if (_reportedStatus == status) return;
            _reportedStatus = status;

            string detail =
                $"Speech output path: {SpeechMode}. "
              + $"NVDA client library present: {(_nvda.IsClientLibraryAvailable ? "yes" : "no")}. "
              + $"NVDA running: {(_readerRunning ? "yes" : "no")}. "
              + $"JAWS running: {(_jawsRunning ? "yes" : "no")}. "
              + $"Live region attached: {(OnSpeak != null ? "yes" : "no")}"
              + (LiveRegionEnabled ? "" : " (live region disabled by the browser-voice setting)")
              + ".";

            _logger.LogInformation("{Detail}", detail);
            try
            {
                Journal?.Add(new JournalEntry(DateTime.Now, JournalEntryKind.Info,
                                              "Speech", null, detail));
            }
            catch { /* best-effort */ }
        }

        private void ReportMuteOnce()
        {
            if (_muteReported) return;
            _muteReported = true;

            const string message =
                "The terminal has no way to speak: the NVDA Controller Client is not loadable and no "
              + "live region is attached. Speech is being written to the journal only. Copy the x64 "
              + "nvdaControllerClient64.dll from the NVDA controllerClient download into the "
              + "application folder, beside AccessibleTrader.BlazorClient.exe, and restart. "
              + "See docs/PLATFORMS.md.";

            // Error, not warning: on this application a dead speech channel is a dead application.
            _logger.LogError(message);
            try
            {
                Journal?.Add(new JournalEntry(DateTime.Now, JournalEntryKind.Error,
                                              "Speech", null, message));
            }
            catch { /* best-effort */ }
        }

        public void Silence()
        {
            if (IsNvdaUsable())
            {
                try { _nvda.CancelSpeech(); } catch { /* best-effort */ }
            }
            else if (IsJawsUsable())
            {
                try { _jaws.StopSpeech(); } catch { /* best-effort */ }
            }

            OnSpeak?.Invoke("");
            _queuedText = "";
        }

        public void Dispose() { }
    }
}
