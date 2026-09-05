using System.Diagnostics;
using AccessibleTrader.Core.Services;

namespace AccessibleTrader.WebHost.Services
{
    /// <summary>
    /// <b>How a background alert reaches somebody sitting at the machine</b> — a sound, a
    /// desktop toast, and speech — behind one seam.
    ///
    /// <para>
    /// ── Why this exists ────────────────────────────────────────────────────────
    /// It is not an abstraction for its own sake. <see cref="LocalBackgroundMonitor"/> probed the
    /// PATH for <c>notify-send</c>, <c>gdbus</c>, <c>spd-say</c> and <c>paplay</c> IN ITS
    /// CONSTRUCTOR and every delivery path called <c>Process.Start</c>, so the class could not be
    /// constructed in a test without touching the filesystem and could not be exercised without
    /// spawning processes on the machine running the suite. That is why its dead-feed escalation
    /// — the same one the hosted monitor had, in the monitor a desktop user HEARS — went
    /// untested while its hosted twin was pinned by four cases.
    /// </para>
    ///
    /// <para>
    /// The policy stays in the monitor: what to say, when it is worth saying, and once only.
    /// This interface owns only the delivery, which is the part that needs a real desktop.
    /// </para>
    /// </summary>
    public interface IDesktopAlertPresenter
    {
        /// <summary>What is actually available, for the monitor's one startup log line.</summary>
        string Describe();

        /// <summary>The notification sound. Silent when no player is installed.</summary>
        void PlayNotificationSound();

        /// <summary>Whether <see cref="Notify"/> can reach a desktop at all (<c>notify-send</c> found on the PATH).</summary>
        bool CanNotify { get; }

        /// <summary>A desktop toast. No-op when <c>notify-send</c> is not installed.</summary>
        /// <param name="urgent">Critical urgency — used for the monitor reporting on ITSELF
        /// (a feed it can no longer watch), not for an alert firing normally.</param>
        void Notify(string title, string text, bool urgent);

        /// <summary>Speaks, Orca first (the user's own voice and rate) then <c>spd-say</c> —
        /// the same ladder the in-session speech manager uses.</summary>
        void Speak(string text);
    }

    /// <summary>
    /// The real one: PATH probes at construction, external processes at delivery. Everything
    /// that made <see cref="LocalBackgroundMonitor"/> untestable now lives here, where a test
    /// substitutes the interface instead.
    /// </summary>
    public sealed class ProcessDesktopAlertPresenter : IDesktopAlertPresenter
    {
        private readonly ILogger<ProcessDesktopAlertPresenter> _logger;
        private readonly string _soundPath;
        private readonly string? _notifySend;
        private readonly string? _gdbus;
        private readonly string? _spdSay;
        private readonly string? _player;

        public ProcessDesktopAlertPresenter(
            IPlatformPathService paths, ILogger<ProcessDesktopAlertPresenter> logger)
        {
            _logger = logger;

            _notifySend = WebHostSpeechManager.FindOnPath("notify-send", File.Exists);
            _gdbus = WebHostSpeechManager.FindOnPath("gdbus", File.Exists);
            _spdSay = WebHostSpeechManager.FindOnPath("spd-say", File.Exists);
            _player = WebHostSpeechManager.FindOnPath("paplay", File.Exists)
                   ?? WebHostSpeechManager.FindOnPath("pw-play", File.Exists);

            // Notification sound: app-data/sounds/alert.wav. Users (or the future
            // factory sound bank) drop their own file there; until then a small
            // generated two-tone beep means the feature is audible on day one.
            var soundsDir = Path.Combine(paths.AppDataDirectory, "sounds");
            Directory.CreateDirectory(soundsDir);
            _soundPath = Path.Combine(soundsDir, "alert.wav");
            try
            {
                if (!File.Exists(_soundPath))
                    File.WriteAllBytes(_soundPath, GenerateDefaultBeepWav());
            }
            catch (Exception ex) { _logger.LogWarning(ex, "Could not write default alert sound."); }
        }

        public string Describe() =>
            $"speech: {(_gdbus != null ? "orca" : _spdSay != null ? "spd-say" : "none")}, "
            + $"toast: {_notifySend != null}, sound: {_player != null}";

        public void PlayNotificationSound()
        {
            if (_player != null && File.Exists(_soundPath)) Run(_player, _soundPath);
        }

        public bool CanNotify => _notifySend != null;

        public void Notify(string title, string text, bool urgent)
        {
            if (_notifySend == null) return;
            Run(_notifySend, "--app-name=Accessible Trade Terminal",
                urgent ? "--urgency=critical" : "--urgency=normal", title, text);
        }

        public void Speak(string text)
        {
            if (_gdbus != null && TrySpeakViaOrca(text)) return;
            if (_spdSay != null) Run(_spdSay, text);
        }

        private bool TrySpeakViaOrca(string text)
        {
            try
            {
                Run(_gdbus!, "call", "--session",
                    "--dest=org.gnome.Orca1.Service",
                    "--object-path=/org/gnome/Orca1/Service",
                    "--method=org.gnome.Orca1.Service.PresentMessage",
                    text);
                return true;
            }
            catch { return false; }
        }

        private void Run(string file, params string[] args)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = file,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                foreach (var a in args) psi.ArgumentList.Add(a);
                using var _ = Process.Start(psi);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Background delivery command {File} failed.", file);
            }
        }

        // ── Default sound (replace by dropping your own alert.wav) ───────────

        /// <summary>16-bit mono 22.05 kHz WAV: a two-tone rising blip (660→880 Hz,
        /// 300 ms total) with linear decay. Small, unambiguous, replaceable.</summary>
        public static byte[] GenerateDefaultBeepWav()
        {
            const int rate = 22050;
            const double dur = 0.3;
            int samples = (int)(rate * dur);
            var pcm = new short[samples];
            for (int i = 0; i < samples; i++)
            {
                double t = (double)i / rate;
                double freq = t < dur / 2 ? 660 : 880;
                double envelope = 1.0 - (double)i / samples;
                pcm[i] = (short)(Math.Sin(2 * Math.PI * freq * t) * envelope * short.MaxValue * 0.4);
            }

            using var ms = new MemoryStream();
            using var w = new BinaryWriter(ms);
            int dataLen = samples * 2;
            w.Write("RIFF"u8); w.Write(36 + dataLen); w.Write("WAVE"u8);
            w.Write("fmt "u8); w.Write(16); w.Write((short)1); w.Write((short)1);
            w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16);
            w.Write("data"u8); w.Write(dataLen);
            foreach (var s in pcm) w.Write(s);
            w.Flush();
            return ms.ToArray();
        }
    }
}
