using System.Runtime.InteropServices;

namespace AccessibleTrader.WebHost.Services
{
    /// <summary>Which desktop this process is sitting on. Explicit rather than probed at the
    /// point of use, so every branch below can be built and asserted from any of the three.</summary>
    public enum DesktopOs { Linux, Windows, MacOS, Unknown }

    /// <summary>One external command, already split into file plus argument vector — never a
    /// shell string. The whole point: nothing here is concatenated into something a shell will
    /// re-parse, so a symbol containing a quote, an ampersand or a newline cannot change what
    /// runs.</summary>
    public sealed record DesktopCommand(string File, IReadOnlyList<string> Args);

    /// <summary>
    /// <b>What this machine can actually do with a toast, a sentence, and a sound</b> — decided
    /// once, per operating system, from a probe of the filesystem.
    ///
    /// <para>
    /// ── Why this is its own class ──────────────────────────────────────────────
    /// Background notification was a <b>Linux-only</b> feature until 2026-09-06, and the reason
    /// was one line: <c>WebHostSpeechManager.FindOnPath</c> opens with
    /// <c>if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) return null;</c>. Every probe in
    /// <see cref="ProcessDesktopAlertPresenter"/> went through it, so on Windows and macOS the
    /// presenter reported no toast, no speech and no sound, <see cref="LocalDesktopNotifier"/>
    /// reported itself unavailable, and the alert-delivery panel silently hid its three switches.
    /// The feature was not missing. It was never asked about.
    /// </para>
    ///
    /// <para>
    /// ── Why it is separate from the presenter ──────────────────────────────────
    /// Choosing the command is pure; running it is not. Splitting them means the Windows and
    /// macOS command lines — including the two escaping rules that are the sharp edge here — are
    /// asserted character by character from the Linux box this repo is developed on. What stays
    /// untestable without the hardware is exactly one thing: whether the command, once spawned,
    /// does what its documentation says. That is recorded as unverified rather than assumed.
    /// </para>
    /// </summary>
    public sealed class DesktopDeliveryPlan
    {
        /// <summary>Windows PowerShell's own registered AUMID. An unpackaged process has no
        /// identity of its own to hang a toast on; borrowing the shipped-with-Windows one is the
        /// standard route and is why the toast is spawned through <c>powershell.exe</c> rather
        /// than raised in-process. The MAUI head does have identity, and uses the Windows App SDK
        /// (<c>WindowsDesktopNotifier</c>) instead.</summary>
        public const string WindowsPowerShellAumid =
            @"{1AC14E77-02E7-4E5D-B744-2EB1AE5198B7}\WindowsPowerShell\v1.0\powershell.exe";

        private readonly string? _toastTool;
        private readonly string? _speechTool;
        private readonly string? _soundTool;

        private DesktopDeliveryPlan(
            DesktopOs os, string? toastTool, ToastKind toast,
            string? speechTool, SpeechKind speech, string? soundTool, SoundKind sound)
        {
            Os = os;
            _toastTool = toastTool; Toast = toast;
            _speechTool = speechTool; Speech = speech;
            _soundTool = soundTool; Sound = sound;
        }

        /// <summary>How the toast is raised on this machine.</summary>
        public enum ToastKind { None, NotifySend, TerminalNotifier, OsaScript, PowerShellToast }

        /// <summary>How a sentence is spoken with no browser in the picture.</summary>
        public enum SpeechKind { None, Orca, SpdSay, MacSay, WindowsSapi }

        /// <summary>How the notification sound is played.</summary>
        public enum SoundKind { None, PulseAudio, AfPlay, PowerShellSoundPlayer }

        public DesktopOs Os { get; }
        public ToastKind Toast { get; }
        public SpeechKind Speech { get; }
        public SoundKind Sound { get; }

        public bool CanNotify => Toast != ToastKind.None;
        public bool CanSpeak => Speech != SpeechKind.None;
        public bool CanPlaySound => Sound != SoundKind.None;

        /// <summary>The OS this process is on, mapped onto the three desktops that have a
        /// delivery path. Anything else is <see cref="DesktopOs.Unknown"/> and delivers nothing,
        /// which is honest rather than silently trying Linux commands.</summary>
        public static DesktopOs CurrentOs =>
              RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? DesktopOs.Linux
            : RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? DesktopOs.Windows
            : RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? DesktopOs.MacOS
            : DesktopOs.Unknown;

        /// <summary>Build the plan for this machine.</summary>
        public static DesktopDeliveryPlan ForCurrentMachine() => For(CurrentOs, File.Exists);

        /// <summary>
        /// Build the plan for <paramref name="os"/>, asking <paramref name="fileExists"/> about
        /// each candidate absolute path. The probe is injected so a test can describe a machine
        /// it is not running on — a Mac with <c>terminal-notifier</c>, a Mac without it, a
        /// Windows box whose PowerShell is missing.
        /// </summary>
        public static DesktopDeliveryPlan For(DesktopOs os, Func<string, bool> fileExists)
        {
            string? Find(params string[] candidates)
            {
                foreach (var c in candidates)
                    if (fileExists(c)) return c;
                return null;
            }

            // Joined by hand, not with Path.Combine: the separator has to match the TARGET
            // operating system, and Path.Combine uses the one belonging to the machine the code
            // is running on. Building a Windows path on Linux through it yields
            // "C:\Windows/System32\..." — which is what a test for the Windows branch, run on
            // this repo's Linux box, discovered the first time it ran.
            string? InDirs(string exe, params string[] dirs)
                => Find(dirs.Select(d => d + "/" + exe).ToArray());

            switch (os)
            {
                case DesktopOs.Linux:
                {
                    string[] dirs = { "/usr/bin", "/usr/local/bin", "/bin" };
                    var notifySend = InDirs("notify-send", dirs);
                    var gdbus = InDirs("gdbus", dirs);
                    var spdSay = InDirs("spd-say", dirs);
                    var player = InDirs("paplay", dirs) ?? InDirs("pw-play", dirs);
                    // Orca first, spd-say second: the same ladder the in-session speech manager
                    // climbs, so a headless sentence arrives in the user's own voice and rate.
                    return new DesktopDeliveryPlan(
                        os,
                        notifySend, notifySend != null ? ToastKind.NotifySend : ToastKind.None,
                        gdbus ?? spdSay,
                        gdbus != null ? SpeechKind.Orca : spdSay != null ? SpeechKind.SpdSay : SpeechKind.None,
                        player, player != null ? SoundKind.PulseAudio : SoundKind.None);
                }

                case DesktopOs.MacOS:
                {
                    // /opt/homebrew/bin is Apple-silicon Homebrew; /usr/local/bin is Intel's.
                    string[] dirs = { "/usr/local/bin", "/opt/homebrew/bin", "/usr/bin", "/bin" };
                    var termNotifier = InDirs("terminal-notifier", dirs);
                    // osascript and say are part of macOS itself — no Homebrew, no install step.
                    var osascript = Find("/usr/bin/osascript");
                    var say = Find("/usr/bin/say");
                    var afplay = Find("/usr/bin/afplay");
                    // terminal-notifier when it is there (its toast carries an app name and
                    // survives in Notification Center); osascript otherwise, which needs nothing
                    // installed at all.
                    var toastTool = termNotifier ?? osascript;
                    var toastKind = termNotifier != null ? ToastKind.TerminalNotifier
                                  : osascript != null ? ToastKind.OsaScript
                                  : ToastKind.None;
                    return new DesktopDeliveryPlan(
                        os,
                        toastTool, toastKind,
                        say, say != null ? SpeechKind.MacSay : SpeechKind.None,
                        afplay, afplay != null ? SoundKind.AfPlay : SoundKind.None);
                }

                case DesktopOs.Windows:
                {
                    // Windows PowerShell 5.1 specifically, not pwsh: the toast is raised through
                    // WinRT projection, which 5.1 has built in and PowerShell 7 does not without
                    // an extra assembly. SAPI and SoundPlayer would work in either, but one tool
                    // for all three keeps the probe (and the failure mode) single.
                    var root = (Environment.GetEnvironmentVariable("SystemRoot") ?? @"C:\Windows")
                        .TrimEnd('\\');
                    var ps = Find(
                        root + @"\System32\WindowsPowerShell\v1.0\powershell.exe",
                        root + @"\SysWOW64\WindowsPowerShell\v1.0\powershell.exe");
                    return new DesktopDeliveryPlan(
                        os,
                        ps, ps != null ? ToastKind.PowerShellToast : ToastKind.None,
                        ps, ps != null ? SpeechKind.WindowsSapi : SpeechKind.None,
                        ps, ps != null ? SoundKind.PowerShellSoundPlayer : SoundKind.None);
                }

                default:
                    return new DesktopDeliveryPlan(
                        DesktopOs.Unknown, null, ToastKind.None, null, SpeechKind.None, null, SoundKind.None);
            }
        }

        /// <summary>One line for the monitor's startup log and the settings panel's hint.</summary>
        public string Describe()
        {
            var speech = Speech switch
            {
                SpeechKind.Orca => "orca",
                SpeechKind.SpdSay => "spd-say",
                SpeechKind.MacSay => "say",
                SpeechKind.WindowsSapi => "sapi",
                _ => "none",
            };
            return $"os: {Os.ToString().ToLowerInvariant()}, speech: {speech}, "
                 + $"toast: {(CanNotify ? Toast.ToString() : "none")}, sound: {CanPlaySound}";
        }

        /// <summary>What the alert-delivery panel tells the user is carrying their toasts.</summary>
        public string DescribeToast() => Toast switch
        {
            ToastKind.NotifySend =>
                "notify-send (the desktop's notification daemon; MATE, GNOME and KDE all show it)",
            ToastKind.TerminalNotifier =>
                "terminal-notifier (macOS Notification Center; VoiceOver announces it)",
            ToastKind.OsaScript =>
                "macOS Notification Center (osascript; VoiceOver announces it)",
            ToastKind.PowerShellToast =>
                "Windows notifications (Action Center; Narrator, NVDA and JAWS read it)",
            _ => Os switch
            {
                DesktopOs.Linux => "notify-send is not installed",
                DesktopOs.MacOS => "no notification path found on this Mac",
                DesktopOs.Windows => "Windows PowerShell was not found, so no toast path",
                _ => "no desktop notification path on this host",
            },
        };

        // ── The three commands ───────────────────────────────────────────────

        /// <param name="urgent">Critical urgency — the monitor reporting on ITSELF (a feed it can
        /// no longer watch), never an alert firing normally. Only Linux's notify-send has the
        /// concept; on macOS and Windows the flag is dropped, and that is a real difference in
        /// what the user gets, not an implementation detail worth hiding.</param>
        public DesktopCommand? ToastCommand(string title, string text, bool urgent)
        {
            if (_toastTool == null) return null;
            return Toast switch
            {
                ToastKind.NotifySend => new DesktopCommand(_toastTool, new[]
                {
                    "--app-name=Accessible Trade Terminal",
                    urgent ? "--urgency=critical" : "--urgency=normal",
                    title, text,
                }),

                ToastKind.TerminalNotifier => new DesktopCommand(_toastTool, new[]
                {
                    "-title", title, "-message", text, "-sender", "com.apple.Terminal",
                }),

                // One -e argument holding one AppleScript statement. The two string literals in
                // it are the only place user text meets a parser, hence AppleScriptLiteral.
                ToastKind.OsaScript => new DesktopCommand(_toastTool, new[]
                {
                    "-e",
                    $"display notification {AppleScriptLiteral(text)} "
                        + $"with title {AppleScriptLiteral("Accessible Trade Terminal")} "
                        + $"subtitle {AppleScriptLiteral(title)}",
                }),

                ToastKind.PowerShellToast => new DesktopCommand(_toastTool, new[]
                {
                    "-NoProfile", "-NonInteractive", "-WindowStyle", "Hidden",
                    "-Command", WindowsToastScript(title, text),
                }),

                _ => null,
            };
        }

        public DesktopCommand? SpeechCommand(string text)
        {
            if (_speechTool == null) return null;
            return Speech switch
            {
                SpeechKind.Orca => new DesktopCommand(_speechTool, new[]
                {
                    "call", "--session",
                    "--dest=org.gnome.Orca1.Service",
                    "--object-path=/org/gnome/Orca1/Service",
                    "--method=org.gnome.Orca1.Service.PresentMessage",
                    text,
                }),

                SpeechKind.SpdSay => new DesktopCommand(_speechTool, new[] { text }),

                SpeechKind.MacSay => new DesktopCommand(_speechTool, new[] { text }),

                // SAPI, not NVDA or JAWS: there is no supported command-line route into a running
                // Windows screen reader. The toast is the path that reaches one; this is the
                // spoken fallback for a machine where a toast was refused or missed.
                SpeechKind.WindowsSapi => new DesktopCommand(_speechTool, new[]
                {
                    "-NoProfile", "-NonInteractive", "-WindowStyle", "Hidden",
                    "-Command",
                    "Add-Type -AssemblyName System.Speech; "
                        + "(New-Object System.Speech.Synthesis.SpeechSynthesizer)"
                        + $".Speak({PowerShellLiteral(text)})",
                }),

                _ => null,
            };
        }

        public DesktopCommand? SoundCommand(string wavPath)
        {
            if (_soundTool == null) return null;
            return Sound switch
            {
                SoundKind.PulseAudio => new DesktopCommand(_soundTool, new[] { wavPath }),
                SoundKind.AfPlay => new DesktopCommand(_soundTool, new[] { wavPath }),
                SoundKind.PowerShellSoundPlayer => new DesktopCommand(_soundTool, new[]
                {
                    "-NoProfile", "-NonInteractive", "-WindowStyle", "Hidden",
                    "-Command",
                    $"(New-Object System.Media.SoundPlayer {PowerShellLiteral(wavPath)}).PlaySync()",
                }),
                _ => null,
            };
        }

        // ── The two escaping rules ───────────────────────────────────────────

        /// <summary>
        /// An AppleScript double-quoted literal. Backslash first (or it would escape the
        /// escapes), then the quote. AppleScript has no line-continuation inside a literal, so a
        /// newline in an alert name would be a syntax error rather than a long toast — CR and LF
        /// become spaces.
        /// </summary>
        public static string AppleScriptLiteral(string s)
        {
            var escaped = (s ?? string.Empty)
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\r", " ")
                .Replace("\n", " ");
            return "\"" + escaped + "\"";
        }

        /// <summary>
        /// A PowerShell single-quoted literal. Inside single quotes PowerShell expands nothing —
        /// no <c>$variable</c>, no backtick escape, no subexpression — so doubling the apostrophe
        /// is the whole rule, and it is why every Windows script here is built with single quotes
        /// rather than double.
        /// </summary>
        public static string PowerShellLiteral(string s)
            => "'" + (s ?? string.Empty).Replace("'", "''") + "'";

        /// <summary>
        /// The unpackaged Windows toast: load the WinRT notification types, fill a two-line
        /// ToastText02 template, and show it through PowerShell's own AUMID.
        ///
        /// <para><b>UNVERIFIED AT RUNTIME.</b> This repo is developed on Linux and there is no
        /// Windows box in the loop; the script is asserted character for character by
        /// <c>DesktopDeliveryPlanTests</c>, which proves what is spawned and nothing about what
        /// happens next. Whether the toast appears, and whether Narrator reads it, is an open
        /// item in docs/TODO.md.</para>
        /// </summary>
        public static string WindowsToastScript(string title, string body)
            => "[Windows.UI.Notifications.ToastNotificationManager, Windows.UI.Notifications, ContentType=WindowsRuntime] > $null; "
             + "$x = [Windows.UI.Notifications.ToastNotificationManager]::GetTemplateContent([Windows.UI.Notifications.ToastTemplateType]::ToastText02); "
             + "$t = $x.GetElementsByTagName('text'); "
             + $"$t.Item(0).AppendChild($x.CreateTextNode({PowerShellLiteral(title)})) > $null; "
             + $"$t.Item(1).AppendChild($x.CreateTextNode({PowerShellLiteral(body)})) > $null; "
             + $"[Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier({PowerShellLiteral(WindowsPowerShellAumid)})"
             + ".Show([Windows.UI.Notifications.ToastNotification]::new($x))";
    }
}
