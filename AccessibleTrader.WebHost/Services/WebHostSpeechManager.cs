using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using AccessibleTrader.Core.Services;
using Microsoft.Extensions.Logging;

namespace AccessibleTrader.WebHost.Services
{
    /// <summary>
    /// Decorator over <see cref="BlazorSpeechManager"/> that adds a
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
    /// <summary>Discriminated backend the speech manager picks at startup.</summary>
    public enum SpeechBackend { OrcaDBus, SpdSay, BrowserTts }

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
                // "Browser voice" empties the live region so a screen reader
                // that IS running (despite the user's choice) can't double-speak.
                if (_inner is AccessibleTrader.BlazorClient.Services.BlazorSpeechManager b)
                    b.LiveRegionEnabled = value != SpeechOutputMode.BrowserVoice;
            }
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
                case SpeechBackend.OrcaDBus:
                    _ = Task.Run(() => SpeakViaOrca(text, interrupt));
                    break;
                case SpeechBackend.SpdSay:
                    _ = Task.Run(() => SpeakViaSpdSay(text, interrupt));
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
                    _ = Task.Run(SilenceViaSpdSay);
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
                    "Orca PresentMessage failed; one speech phrase dropped.");
            }
        }

        private void SpeakViaSpdSay(string text, bool interrupt)
        {
            try
            {
                if (interrupt) StartSpdSay("-S");
                StartSpdSay(text);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "spd-say invocation failed; one speech phrase dropped.");
            }
        }

        private void SilenceViaSpdSay()
        {
            try { if (_spdSayPath != null) StartSpdSay("-S"); }
            catch (Exception ex) { _logger.LogWarning(ex, "spd-say -S failed."); }
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
