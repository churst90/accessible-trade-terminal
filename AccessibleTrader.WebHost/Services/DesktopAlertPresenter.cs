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

        /// <summary>Whether <see cref="Notify"/> can reach a desktop at all — this machine's
        /// toast tool was found (<c>notify-send</c> on Linux, <c>terminal-notifier</c> or
        /// <c>osascript</c> on macOS, Windows PowerShell on Windows).</summary>
        bool CanNotify { get; }

        /// <summary>What is carrying the toasts, in words, for the delivery panel's hint.</summary>
        string DescribeToast();

        /// <summary>A desktop toast. No-op when this machine has no toast tool.</summary>
        /// <param name="urgent">Critical urgency — used for the monitor reporting on ITSELF
        /// (a feed it can no longer watch), not for an alert firing normally. Only Linux honours
        /// it; macOS and Windows have no urgency level on a notification.</param>
        void Notify(string title, string text, bool urgent);

        /// <summary>Speaks with no browser in the picture: Orca then <c>spd-say</c> on Linux —
        /// the same ladder the in-session speech manager uses — <c>say</c> on macOS, SAPI on
        /// Windows.</summary>
        void Speak(string text);

        /// <summary>
        /// Whether a toast raised through <see cref="Notify"/> is read aloud by the desktop's
        /// screen reader on its own, so that <see cref="Speak"/> of the same text would be heard
        /// twice. See <see cref="DesktopDeliveryPlan.ToastIsSpoken"/> for the per-desktop rule and
        /// <see cref="DesktopAnnouncement"/> for the one place that applies it. Defaults to false
        /// so a test double that records both channels keeps recording both.
        /// </summary>
        bool ToastIsSpoken => false;
    }

    /// <summary>
    /// <b>The one rule for sound, toast and speech with no browser attached</b>, shared by the
    /// alert monitor, the bar-close announcer and the order announcer so they cannot drift.
    ///
    /// <para>Until 2026-09-11 each of them raised the toast and then spoke the same sentence, and
    /// on a desktop whose screen reader reads notifications (Cody's: MATE + Orca) every
    /// announcement arrived twice. Now: the toast goes out whenever there is a toast tool; the
    /// sentence is spoken as well ONLY when the toast does not already reach the screen reader
    /// (<see cref="IDesktopAlertPresenter.ToastIsSpoken"/>) or the user has asked for both
    /// (<see cref="SettingsKeys.DesktopSpeakBesideToast"/>, which is the DEFAULT — see
    /// <see cref="SpeakBesideToast"/> for why the plan's per-desktop guess is not trusted on its
    /// own). With no toast tool at all, speech is the only channel and always runs.</para>
    ///
    /// <para>Because the toast can now be the ONLY spoken route, its body carries the whole
    /// sentence — including the narration ladder on a bar close — not a shortened visual form.
    /// Three separate attempts, not one: a machine with no audio player must still get the toast,
    /// and a broken toast must not take speech down with it.</para>
    /// </summary>
    public static class DesktopAnnouncement
    {
        /// <summary>
        /// The user's "also speak directly" switch — <b>default ON</b>.
        ///
        /// <para>It shipped default-off for about an hour on 2026-09-11, on the reading that Orca
        /// reads every MATE notification. Cody, same afternoon, listening: <i>"I don't hear orca
        /// read any notification, if it did it would say 'notification'"</i>. So on his desktop the
        /// toast is SHOWN and NOT spoken, and a default that trusted <see cref="IDesktopAlertPresenter.ToastIsSpoken"/>
        /// would have turned every headless announcement into silence — the one failure this
        /// monitor exists to prevent. The safe default for a blind user is speech; the switch is
        /// the way to stop a doubling on a desktop whose screen reader DOES read toasts.</para>
        /// </summary>
        public static bool SpeakBesideToast(ISettingsManager? settings)
        {
            try { return settings?.GetSetting(SettingsKeys.DesktopSpeakBesideToast)?.ToObject<bool>() ?? true; }
            catch { return true; }
        }

        /// <summary>Whether <see cref="IDesktopAlertPresenter.Speak"/> should run after the toast.</summary>
        public static bool ShouldSpeak(IDesktopAlertPresenter presenter, bool speakBesideToast)
            => speakBesideToast || !(presenter.CanNotify && presenter.ToastIsSpoken);

        /// <summary>Sound (optional), toast, and speech under the rule above. Each channel is its own
        /// attempt; a failure is logged and the next channel still runs.</summary>
        /// <param name="toastBody">What the notification shows under <paramref name="title"/>. A
        /// screen reader that reads the toast reads the title and then this, so a body that
        /// repeats the title would be heard twice — see <c>HeadlessOrderAnnouncer.ToastBody</c>.</param>
        /// <param name="speech">The whole sentence, for the direct speech route.</param>
        public static void Present(
            IDesktopAlertPresenter presenter, bool speakBesideToast,
            string title, string toastBody, string speech, bool urgent, bool withSound, ILogger? logger)
        {
            if (withSound) Try(() => presenter.PlayNotificationSound(), "sound", logger);
            Try(() => presenter.Notify(title, toastBody, urgent), "toast", logger);
            if (ShouldSpeak(presenter, speakBesideToast))
                Try(() => presenter.Speak(speech), "speech", logger);
        }

        private static void Try(Action deliver, string channel, ILogger? logger)
        {
            try { deliver(); }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "Desktop announcement could not deliver the {Channel}.", channel);
            }
        }
    }

    /// <summary>
    /// The real one: PATH probes at construction, external processes at delivery. Everything
    /// that made <see cref="LocalBackgroundMonitor"/> untestable now lives here, where a test
    /// substitutes the interface instead.
    ///
    /// <para>
    /// <b>What changed on 2026-09-06.</b> Every probe used to go through
    /// <c>WebHostSpeechManager.FindOnPath</c>, whose first line returns null on anything that is
    /// not Linux — so on Windows and macOS this class reported no toast, no speech and no sound,
    /// and the whole background-notification feature was Linux-only without ever saying so. The
    /// per-OS decision moved to <see cref="DesktopDeliveryPlan"/>, which is pure and therefore
    /// testable from any of the three; what is left here is the part that genuinely needs the
    /// machine: spawning the process.
    /// </para>
    /// </summary>
    public sealed class ProcessDesktopAlertPresenter : IDesktopAlertPresenter
    {
        private readonly ILogger<ProcessDesktopAlertPresenter> _logger;
        private readonly DesktopDeliveryPlan _plan;
        private readonly string _soundPath;

        public ProcessDesktopAlertPresenter(
            IPlatformPathService paths, ILogger<ProcessDesktopAlertPresenter> logger)
            : this(paths, logger, DesktopDeliveryPlan.ForCurrentMachine())
        {
        }

        /// <summary>Test seam: hand in a plan built for a machine this one is not.</summary>
        internal ProcessDesktopAlertPresenter(
            IPlatformPathService paths,
            ILogger<ProcessDesktopAlertPresenter> logger,
            DesktopDeliveryPlan plan)
        {
            _logger = logger;
            _plan = plan;

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

        /// <summary>What is carrying the toasts, for the alert-delivery panel's hint.</summary>
        public string DescribeToast() => _plan.DescribeToast();

        public string Describe() => _plan.Describe();

        public void PlayNotificationSound()
        {
            if (!File.Exists(_soundPath)) return;
            Run(_plan.SoundCommand(_soundPath));
        }

        public bool CanNotify => _plan.CanNotify;

        public bool ToastIsSpoken => _plan.ToastIsSpoken;

        public void Notify(string title, string text, bool urgent)
            => Run(_plan.ToastCommand(title, text, urgent));

        public void Speak(string text) => Run(_plan.SpeechCommand(text));

        private void Run(DesktopCommand? command)
        {
            if (command == null) return;
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = command.File,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                foreach (var a in command.Args) psi.ArgumentList.Add(a);
                using var _ = Process.Start(psi);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Background delivery command {File} failed.", command.File);
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
