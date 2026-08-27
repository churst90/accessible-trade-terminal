using System.Diagnostics;
using System.Runtime.InteropServices;
using AccessibleTrader.Core.Services;

namespace AccessibleTrader.WebHost.Services
{
    /// <summary>Discriminated backend the speech manager picks at startup.</summary>
    public enum SpeechBackend { OrcaDBus, SpdSay, BrowserTts }

    /// <summary>
    /// Decorator over <c>BlazorSpeechManager</c> that adds a
    /// real-speech output channel. Inner manager still journals and writes
    /// to the ARIA live region; on top of that, this decorator picks the
    /// best out-of-band path at startup:
    ///
    /// <list type="bullet">
    /// <item><b>Orca D-Bus</b> (preferred on Linux when Orca is running):
    /// invokes <c>org.gnome.Orca1.Service.PresentMessage</c> via
    /// <c>gdbus</c>. Speech goes THROUGH Orca, so the user's Orca voice /
    /// rate / pitch / verbosity are applied — same voice they hear
    /// everywhere else on the desktop.</item>
    /// <item><b>SpeechDispatcher <c>spd-say</c></b>: bypasses Orca but
    /// uses the system speech daemon. Works without a screen reader, but
    /// honours only the SpeechDispatcher-level default module (typically
    /// espeak-ng), not Orca's voice overlay.</item>
    /// <item><b>Browser SpeechSynthesis</b>: publishes
    /// <see cref="BrowserSpeakRequest"/> for <c>BrowserSpeechBridge</c> to
    /// pass to <c>window.speechSynthesis.speak</c>. Used on Windows,
    /// macOS, headless Linux, and the public-website demo deploy.</item>
    /// </list>
    /// </summary>
    public sealed class WebHostSpeechManager : ISpeechManager, IBrowserSpeechOutput
    {
        private const string OrcaBus  = "org.gnome.Orca1.Service";
        private const string OrcaPath = "/org/gnome/Orca1/Service";
        private const string OrcaIface = "org.gnome.Orca1.Service";

        private readonly ISpeechManager _inner;
        private readonly IEventBus _eventBus;
        private readonly ILogger<WebHostSpeechManager> _logger;
        private readonly string? _spdSayPath;
        private readonly string? _gdbusPath;
        private readonly bool _orcaAvailable;
        private readonly SpeechBackend _backend;

        public WebHostSpeechManager(
            ISpeechManager inner,
            IEventBus eventBus,
            ILogger<WebHostSpeechManager> logger)
            : this(inner, eventBus, logger,
                   spdSayPath:    FindOnPath("spd-say", File.Exists),
                   gdbusPath:     FindOnPath("gdbus",    File.Exists),
                   orcaAvailable: false /* probed below */ )
        {
            _orcaAvailable = _gdbusPath != null && ProbeOrca(_gdbusPath, _logger);
            _backend = SelectBackend(_gdbusPath, _spdSayPath, _orcaAvailable);
            ApplyLiveRegionPolicy();
            LogBackend();
        }

        // Internal test seam. Skips the OS probes — callers pass pre-computed
        // probe results. Used by WebHostSpeechManagerBackendSelectionTests +
        // WebHostSpeechManagerForwardingTests.
        internal WebHostSpeechManager(
            ISpeechManager inner,
            IEventBus eventBus,
            ILogger<WebHostSpeechManager> logger,
            string? spdSayPath,
            string? gdbusPath,
            bool orcaAvailable)
        {
            _inner = inner;
            _eventBus = eventBus;
            _logger = logger;
            _spdSayPath = spdSayPath;
            _gdbusPath = gdbusPath;
            _orcaAvailable = orcaAvailable;
            _backend = SelectBackend(_gdbusPath, _spdSayPath, _orcaAvailable);
            ApplyLiveRegionPolicy();
        }

        /// <summary>
        /// Pure picker: maps the three probe results to a discrete backend.
        /// Order is Orca → spd-say → browser. Exposed internally so tests
        /// can pin the priority without spawning processes.
        /// </summary>
        internal static SpeechBackend SelectBackend(string? gdbusPath, string? spdSayPath, bool orcaAvailable)
        {
            if (gdbusPath is not null && orcaAvailable) return SpeechBackend.OrcaDBus;
            if (spdSayPath is not null) return SpeechBackend.SpdSay;
            return SpeechBackend.BrowserTts;
        }

        internal SpeechBackend Backend => _backend;

        // ── IBrowserSpeechOutput (the double-speech fix) ─────────────────────
        // Only meaningful when the browser is the last speech hop; with a
        // server-side backend (Orca/spd-say) the browser never speaks and the
        // live region is the screen reader's channel as always.
        public bool IsBrowserTtsBackend => _backend == SpeechBackend.BrowserTts;

        private SpeechOutputMode _outputMode = SpeechOutputMode.Both;
        public SpeechOutputMode Mode
        {
            get => _outputMode;
            set
            {
                _outputMode = value;
                ApplyLiveRegionPolicy();
            }
        }

        /// <summary>
        /// Exactly ONE sink may vocalize a Speak call. With a server-side backend
        /// (Orca D-Bus / spd-say) the server IS the voice — the ARIA live region
        /// must stay empty, or a browser-side screen reader reads it while the
        /// server speaks the same text and the user hears everything twice
        /// (found live 2026-07-23: local WebHost in Chrome + Orca — Chrome
        /// announces live regions reliably where Firefox often didn't, which is
        /// why the double never showed before). With the browser as the last
        /// hop, the live region serves "screen reader" mode and is emptied only
        /// for "browser voice" mode, as before. Remote-browser access to a Full
        /// host that has a local Orca is out of scope — Full mode is local-use
        /// by design (same assumption as the local background monitor).
        /// </summary>
        internal static bool ShouldEnableLiveRegion(SpeechBackend backend, SpeechOutputMode mode)
            => backend == SpeechBackend.BrowserTts && mode != SpeechOutputMode.BrowserVoice;

        /// <summary>
        /// Emits one phrase through the ARIA live region when the out-of-band voice failed.
        ///
        /// <para>Both failure paths used to log "one speech phrase dropped" and stop there, so
        /// the user heard NOTHING — <b>including for an order rejection</b>. The live region is
        /// deliberately disabled for server-side backends (<see cref="ShouldEnableLiveRegion"/>)
        /// because leaving it on would double-speak every phrase Orca already said; but a
        /// phrase Orca did NOT say is exactly the case it should cover.</para>
        ///
        /// <para>One-shot: the flag is restored immediately, so the normal double-speak
        /// suppression is untouched.</para>
        /// </summary>
        private void FallBackToLiveRegion(string text)
        {
            try
            {
                if (_inner is not AccessibleTrader.BlazorClient.Services.BlazorSpeechManager b) return;

                bool previous = b.LiveRegionEnabled;
                try
                {
                    b.LiveRegionEnabled = true;
                    b.Speak(text, interrupt: false);
                }
                finally
                {
                    b.LiveRegionEnabled = previous;
                }
            }
            catch (Exception ex)
            {
                // Last resort failed too. Nothing further to try; at least say so in the log.
                _logger.LogWarning(ex, "Live-region speech fallback also failed; phrase dropped.");
            }
        }

        private void ApplyLiveRegionPolicy()
        {
            if (_inner is AccessibleTrader.BlazorClient.Services.BlazorSpeechManager b)
                b.LiveRegionEnabled = ShouldEnableLiveRegion(_backend, _outputMode);
        }

        private void LogBackend()
        {
            switch (_backend)
            {
                case SpeechBackend.OrcaDBus:
                    _logger.LogInformation(
                        "Speech: routing through Orca via {Bus}.PresentMessage (honours Orca's voice/rate config).",
                        OrcaBus);
                    break;
                case SpeechBackend.SpdSay:
                    _logger.LogInformation(
                        "Speech: using SpeechDispatcher via {Path} (Orca D-Bus unavailable; falls back to system default voice module).",
                        _spdSayPath);
                    break;
                case SpeechBackend.BrowserTts:
                    _logger.LogInformation(
                        "Speech: neither Orca D-Bus nor spd-say found; falling back to browser SpeechSynthesis.");
                    break;
            }
        }

        public bool IsSpeechEnabled
        {
            get => _inner.IsSpeechEnabled;
            set => _inner.IsSpeechEnabled = value;
        }

        public Action<string>? OnSpeak
        {
            get => _inner.OnSpeak;
            set => _inner.OnSpeak = value;
        }

        public void Speak(string text, bool interrupt = false)
        {
            _inner.Speak(text, interrupt);
            if (string.IsNullOrWhiteSpace(text) || !_inner.IsSpeechEnabled) return;

            switch (_backend)
            {
                // ONE consumer, in order — not a Task.Run per phrase.
                //
                // Two Speak calls in quick succession each used to schedule an independent
                // thread-pool work item, and the pool gives no ordering guarantee. Since the
                // process-start order is what determines speech-dispatcher's queue order,
                // "Stop loss hit. Sold 1 BTC at 61,200." followed by "Order rejected." could
                // be spoken in the WRONG ORDER, with no cue that it had happened.
                case SpeechBackend.OrcaDBus:
                case SpeechBackend.SpdSay:
                    Enqueue(new SpeechWork(text, interrupt, IsSilence: false));
                    break;
                case SpeechBackend.BrowserTts:
                    // "Screen reader" mode: the live region (written by _inner
                    // above) is the whole story — no browser voice on top.
                    if (_outputMode != SpeechOutputMode.ScreenReader)
                        _eventBus.Publish(new BrowserSpeakRequest(text, interrupt));
                    break;
            }
        }

        public void Silence()
        {
            _inner.Silence();
            switch (_backend)
            {
                case SpeechBackend.OrcaDBus:
                case SpeechBackend.SpdSay:
                    // spd-say -S stops every priority on the dispatcher, which
                    // is what Orca's voice flows through too — clips both.
                    // Through the SAME queue as Speak, or a Silence can overtake the phrase
                    // it was meant to follow.
                    Enqueue(new SpeechWork("", Interrupt: false, IsSilence: true));
                    break;
                case SpeechBackend.BrowserTts:
                    _eventBus.Publish(new BrowserSpeakRequest(string.Empty, true));
                    break;
            }
        }

        private void SpeakViaOrca(string text, bool interrupt)
        {
            try
            {
                // Orca's PresentMessage queues at default priority. To get
                // interrupting behaviour for navigation we cancel current
                // SpeechDispatcher output first; Orca's voice still flows
                // through the dispatcher, so -S clips it cleanly.
                if (interrupt && _spdSayPath != null) StartSpdSay("-S");

                var psi = new ProcessStartInfo
                {
                    FileName = _gdbusPath!,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                psi.ArgumentList.Add("call");
                psi.ArgumentList.Add("--session");
                psi.ArgumentList.Add($"--dest={OrcaBus}");
                psi.ArgumentList.Add($"--object-path={OrcaPath}");
                psi.ArgumentList.Add($"--method={OrcaIface}.PresentMessage");
                psi.ArgumentList.Add(text);
                using var _p = Process.Start(psi);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Orca PresentMessage failed; falling back to the live region.");
                FallBackToLiveRegion(text);
            }
        }

        private void SpeakViaSpdSay(string text, bool interrupt)
        {
            try
            {
                // AWAIT the cancel before starting the text.
                //
                // These were two separate un-awaited processes: StartSpdSay did Process.Start
                // and never waited. So the cancel could land AFTER the text and clip the very
                // message it was meant to clear the way for — the interrupt eating its own
                // utterance.
                if (interrupt) RunSpdSayToCompletion("-S");
                StartSpdSay(text);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "spd-say invocation failed; falling back to the live region.");
                FallBackToLiveRegion(text);
            }
        }

        private void SilenceViaSpdSay()
        {
            try { if (_spdSayPath != null) RunSpdSayToCompletion("-S"); }
            catch (Exception ex) { _logger.LogWarning(ex, "spd-say -S failed."); }
        }

        /// <summary>
        /// Starts spd-say and waits for it to exit, so whatever follows is genuinely after it.
        /// Bounded: a hung spd-say must not stall the whole speech queue, and speaking late is
        /// better than never speaking again.
        /// </summary>
        private void RunSpdSayToCompletion(string arg)
        {
            var psi = new ProcessStartInfo
            {
                FileName = _spdSayPath!,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            psi.ArgumentList.Add(arg);
            using var p = Process.Start(psi);
            p?.WaitForExit(1000);
        }

        // ── The single speech consumer ───────────────────────────────────────

        private readonly record struct SpeechWork(string Text, bool Interrupt, bool IsSilence);

        private readonly System.Threading.Channels.Channel<SpeechWork> _speechQueue =
            System.Threading.Channels.Channel.CreateUnbounded<SpeechWork>(
                new System.Threading.Channels.UnboundedChannelOptions
                {
                    SingleReader = true,
                    SingleWriter = false,
                });

        private Task? _speechPump;
        private readonly object _pumpGate = new();

        private void Enqueue(SpeechWork work)
        {
            EnsurePump();
            _speechQueue.Writer.TryWrite(work);
        }

        private void EnsurePump()
        {
            if (_speechPump != null) return;
            lock (_pumpGate)
            {
                _speechPump ??= Task.Run(PumpAsync);
            }
        }

        private async Task PumpAsync()
        {
            await foreach (var work in _speechQueue.Reader.ReadAllAsync().ConfigureAwait(false))
            {
                try
                {
                    if (work.IsSilence)
                    {
                        SilenceViaSpdSay();
                        continue;
                    }

                    if (_backend == SpeechBackend.OrcaDBus) SpeakViaOrca(work.Text, work.Interrupt);
                    else SpeakViaSpdSay(work.Text, work.Interrupt);
                }
                catch (Exception ex)
                {
                    // The pump must survive anything: if it dies the user goes permanently
                    // silent, which is the worst failure this component has.
                    _logger.LogWarning(ex, "Speech pump item failed; continuing.");
                }
            }
        }

        private void StartSpdSay(string arg)
        {
            var psi = new ProcessStartInfo
            {
                FileName = _spdSayPath!,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            psi.ArgumentList.Add(arg);
            using var _p = Process.Start(psi);
        }

        internal static string? FindOnPath(string exe, Func<string, bool> fileExists)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) return null;
            foreach (var dir in new[] { "/usr/bin", "/usr/local/bin", "/bin" })
            {
                var p = Path.Combine(dir, exe);
                if (fileExists(p)) return p;
            }
            return null;
        }

        // Returns true if Orca's D-Bus service answers GetVersion within
        // ~1 second. Done synchronously at startup so the chosen speech
        // backend is locked in before any Speak() call.
        private static bool ProbeOrca(string gdbus, ILogger log)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = gdbus,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                psi.ArgumentList.Add("call");
                psi.ArgumentList.Add("--session");
                psi.ArgumentList.Add($"--dest={OrcaBus}");
                psi.ArgumentList.Add($"--object-path={OrcaPath}");
                psi.ArgumentList.Add($"--method={OrcaIface}.GetVersion");
                using var p = Process.Start(psi);
                if (p is null) { log.LogDebug("Orca probe: gdbus failed to start."); return false; }
                if (!p.WaitForExit(5000))
                {
                    log.LogDebug("Orca probe: gdbus timed out after 5 s.");
                    try { p.Kill(); } catch { }
                    return false;
                }
                var stdout = p.StandardOutput.ReadToEnd().Trim();
                var stderr = p.StandardError.ReadToEnd().Trim();
                log.LogInformation(
                    "Orca probe: exit={Exit} stdout=\"{Out}\" stderr=\"{Err}\" DBUS_SESSION_BUS_ADDRESS={Bus} XDG_RUNTIME_DIR={Xdg}",
                    p.ExitCode, stdout, stderr,
                    Environment.GetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS") ?? "<unset>",
                    Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR") ?? "<unset>");
                return p.ExitCode == 0;
            }
            catch (Exception ex)
            {
                log.LogDebug(ex, "Orca probe threw.");
                return false;
            }
        }
    }

    /// <summary>
    /// Event published when speech should be rendered through the browser's
    /// SpeechSynthesis API. Consumed by <c>BrowserSpeechBridge</c>.
    /// Bypassed when <see cref="WebHostSpeechManager"/> detects
    /// <c>spd-say</c> at startup.
    /// </summary>
    public sealed record BrowserSpeakRequest(string Text, bool Interrupt);
}
