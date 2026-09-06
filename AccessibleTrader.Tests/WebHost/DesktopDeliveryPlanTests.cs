using AccessibleTrader.WebHost.Services;

namespace AccessibleTrader.Tests.WebHost;

/// <summary>
/// <b>The per-OS delivery paths, asserted from a machine that is none of them.</b>
///
/// <para>
/// Background notification was Linux-only until 2026-09-06 and nothing said so. The cause was one
/// line in <c>WebHostSpeechManager.FindOnPath</c> — <c>if (!IsOSPlatform(Linux)) return null;</c> —
/// which every probe in <see cref="ProcessDesktopAlertPresenter"/> went through, so on Windows and
/// macOS the presenter reported no toast, no speech and no sound and the alert-delivery panel
/// hid its three switches. That defect was invisible precisely because it could not be reached
/// from the development box.
/// </para>
///
/// <para>
/// <see cref="DesktopDeliveryPlan"/> exists to make it reachable: the OS and the filesystem probe
/// are both parameters, so a Mac without <c>terminal-notifier</c> and a Windows box with a moved
/// <c>SystemRoot</c> are ordinary test cases. <b>What this file proves is what gets spawned, and
/// nothing about what happens next</b> — whether a Windows toast actually appears, and whether
/// Narrator reads it, is recorded as unverified in docs/TODO.md rather than assumed here.
/// </para>
/// </summary>
public class DesktopDeliveryPlanTests
{
    // A probe that says yes to exactly the paths it was handed.
    private static Func<string, bool> Has(params string[] present)
        => path => present.Contains(path, StringComparer.Ordinal);

    private const string Ps = @"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe";

    // ── The defect this class was written for ────────────────────────────────

    /// <summary>
    /// The regression guard for the whole feature. Each of the three desktops, fully equipped,
    /// must offer all three deliveries. Before this change, two of the three rows were false
    /// across the board.
    /// </summary>
    [Theory]
    [InlineData(DesktopOs.Linux)]
    [InlineData(DesktopOs.MacOS)]
    [InlineData(DesktopOs.Windows)]
    public void Every_desktop_can_toast_speak_and_play_when_its_tools_are_present(DesktopOs os)
    {
        var plan = FullyEquipped(os);

        Assert.True(plan.CanNotify, $"{os} should have a toast path");
        Assert.True(plan.CanSpeak, $"{os} should have a headless speech path");
        Assert.True(plan.CanPlaySound, $"{os} should have a sound path");
    }

    [Fact]
    public void An_unknown_desktop_delivers_nothing_rather_than_trying_linux_commands()
    {
        var plan = DesktopDeliveryPlan.For(DesktopOs.Unknown, _ => true);

        Assert.False(plan.CanNotify);
        Assert.False(plan.CanSpeak);
        Assert.False(plan.CanPlaySound);
        Assert.Null(plan.ToastCommand("t", "b", urgent: false));
        Assert.Null(plan.SpeechCommand("hello"));
        Assert.Null(plan.SoundCommand("/tmp/a.wav"));
    }

    private static DesktopDeliveryPlan FullyEquipped(DesktopOs os) => os switch
    {
        DesktopOs.Linux => DesktopDeliveryPlan.For(os, Has(
            "/usr/bin/notify-send", "/usr/bin/gdbus", "/usr/bin/spd-say", "/usr/bin/paplay")),
        DesktopOs.MacOS => DesktopDeliveryPlan.For(os, Has(
            "/usr/local/bin/terminal-notifier", "/usr/bin/osascript", "/usr/bin/say", "/usr/bin/afplay")),
        DesktopOs.Windows => DesktopDeliveryPlan.For(os, Has(Ps)),
        _ => DesktopDeliveryPlan.For(os, _ => false),
    };

    // ── Linux: unchanged behaviour ───────────────────────────────────────────

    [Fact]
    public void Linux_uses_notify_send_with_the_app_name_and_the_urgency_it_was_given()
    {
        var plan = FullyEquipped(DesktopOs.Linux);

        var normal = plan.ToastCommand("BTC/USD", "crossed 100,000", urgent: false)!;
        Assert.Equal("/usr/bin/notify-send", normal.File);
        Assert.Contains("--urgency=normal", normal.Args);
        Assert.Contains("--app-name=Accessible Trade Terminal", normal.Args);
        Assert.Equal(new[] { "BTC/USD", "crossed 100,000" }, normal.Args.TakeLast(2));

        var urgent = plan.ToastCommand("BTC/USD", "feed is dead", urgent: true)!;
        Assert.Contains("--urgency=critical", urgent.Args);
    }

    /// <summary>Orca first, spd-say second — the same ladder the in-session speech manager
    /// climbs, so the headless sentence arrives in the user's own voice and rate.</summary>
    [Fact]
    public void Linux_speaks_through_orca_when_gdbus_is_there_and_spd_say_when_it_is_not()
    {
        var withOrca = DesktopDeliveryPlan.For(DesktopOs.Linux, Has("/usr/bin/gdbus", "/usr/bin/spd-say"));
        var spoken = withOrca.SpeechCommand("BTC is up")!;
        Assert.Equal("/usr/bin/gdbus", spoken.File);
        Assert.Contains("--method=org.gnome.Orca1.Service.PresentMessage", spoken.Args);
        Assert.Equal("BTC is up", spoken.Args.Last());

        var withoutOrca = DesktopDeliveryPlan.For(DesktopOs.Linux, Has("/usr/bin/spd-say"));
        var fallback = withoutOrca.SpeechCommand("BTC is up")!;
        Assert.Equal("/usr/bin/spd-say", fallback.File);
        Assert.Equal(new[] { "BTC is up" }, fallback.Args);
    }

    // ── macOS ────────────────────────────────────────────────────────────────

    [Fact]
    public void A_mac_with_terminal_notifier_prefers_it()
    {
        var plan = FullyEquipped(DesktopOs.MacOS);
        var toast = plan.ToastCommand("BTC/USD", "crossed 100,000", urgent: false)!;

        Assert.Equal("/usr/local/bin/terminal-notifier", toast.File);
        Assert.Equal("BTC/USD", toast.Args[toast.Args.ToList().IndexOf("-title") + 1]);
        Assert.Equal("crossed 100,000", toast.Args[toast.Args.ToList().IndexOf("-message") + 1]);
    }

    /// <summary>The point of the osascript branch: it needs nothing installed. A Mac with no
    /// Homebrew at all still gets its toasts.</summary>
    [Fact]
    public void A_mac_without_terminal_notifier_still_toasts_through_osascript()
    {
        var plan = DesktopDeliveryPlan.For(DesktopOs.MacOS,
            Has("/usr/bin/osascript", "/usr/bin/say", "/usr/bin/afplay"));

        var toast = plan.ToastCommand("BTC/USD", "crossed 100,000", urgent: false)!;
        Assert.Equal("/usr/bin/osascript", toast.File);
        Assert.Equal("-e", toast.Args[0]);
        Assert.Equal(
            "display notification \"crossed 100,000\" with title \"Accessible Trade Terminal\" subtitle \"BTC/USD\"",
            toast.Args[1]);
    }

    [Fact]
    public void A_mac_speaks_with_say_and_plays_with_afplay()
    {
        var plan = FullyEquipped(DesktopOs.MacOS);

        Assert.Equal("/usr/bin/say", plan.SpeechCommand("BTC is up")!.File);
        Assert.Equal(new[] { "BTC is up" }, plan.SpeechCommand("BTC is up")!.Args);
        Assert.Equal("/usr/bin/afplay", plan.SoundCommand("/tmp/alert.wav")!.File);
        Assert.Equal(new[] { "/tmp/alert.wav" }, plan.SoundCommand("/tmp/alert.wav")!.Args);
    }

    /// <summary>Apple-silicon Homebrew installs to /opt/homebrew/bin, Intel's to
    /// /usr/local/bin. Probing only one of them would have made the feature depend on which Mac
    /// the user bought.</summary>
    [Fact]
    public void Apple_silicon_homebrew_is_probed_too()
    {
        var plan = DesktopDeliveryPlan.For(DesktopOs.MacOS,
            Has("/opt/homebrew/bin/terminal-notifier", "/usr/bin/say"));

        Assert.Equal("/opt/homebrew/bin/terminal-notifier", plan.ToastCommand("t", "b", false)!.File);
    }

    // ── Windows ──────────────────────────────────────────────────────────────

    [Fact]
    public void Windows_raises_the_toast_through_windows_powershell_and_its_own_aumid()
    {
        var plan = FullyEquipped(DesktopOs.Windows);
        var toast = plan.ToastCommand("BTC/USD", "crossed 100,000", urgent: false)!;

        Assert.Equal(Ps, toast.File);
        Assert.Contains("-NoProfile", toast.Args);
        Assert.Contains("-NonInteractive", toast.Args);
        var script = toast.Args.Last();
        Assert.Contains("ToastNotificationManager", script);
        Assert.Contains("'BTC/USD'", script);
        Assert.Contains("'crossed 100,000'", script);
        // An unpackaged process has no identity of its own to hang a toast on; PowerShell's is
        // borrowed. Without it the toast is silently dropped by the shell.
        Assert.Contains(DesktopDeliveryPlan.WindowsPowerShellAumid, script);
    }

    [Fact]
    public void Windows_speaks_through_sapi_and_plays_through_soundplayer()
    {
        var plan = FullyEquipped(DesktopOs.Windows);

        var speech = plan.SpeechCommand("BTC is up")!;
        Assert.Equal(Ps, speech.File);
        Assert.Contains("System.Speech", speech.Args.Last());
        Assert.Contains(".Speak('BTC is up')", speech.Args.Last());

        var sound = plan.SoundCommand(@"C:\data\alert.wav")!;
        Assert.Contains(@"System.Media.SoundPlayer 'C:\data\alert.wav'", sound.Args.Last());
        Assert.Contains("PlaySync()", sound.Args.Last());
    }

    /// <summary>A Windows install on a drive other than C:, or a locked-down box with PowerShell
    /// removed, gets no delivery rather than a command that cannot start.</summary>
    [Fact]
    public void Windows_without_powershell_has_no_delivery_at_all()
    {
        var plan = DesktopDeliveryPlan.For(DesktopOs.Windows, _ => false);

        Assert.False(plan.CanNotify);
        Assert.False(plan.CanSpeak);
        Assert.False(plan.CanPlaySound);
    }

    // ── The escaping, which is where user text meets a parser ────────────────

    /// <summary>
    /// A symbol, an alert name and a strategy name are all user-supplied and all reach these
    /// scripts. This repo has already shipped one defect of exactly this shape — an API key
    /// containing <c>&amp;</c> truncated at the ampersand because it was pasted into a URL — so
    /// the quoting rules get their own cases rather than being trusted by inspection.
    /// </summary>
    [Theory]
    [InlineData("plain", "'plain'")]
    [InlineData("Cody's alert", "'Cody''s alert'")]
    [InlineData("$env:PATH", "'$env:PATH'")]          // single quotes expand nothing in PowerShell
    [InlineData("a`nb", "'a`nb'")]                     // ...including the backtick escape
    [InlineData("$(Get-Date)", "'$(Get-Date)'")]
    public void PowerShell_literals_double_the_apostrophe_and_expand_nothing_else(string input, string expected)
        => Assert.Equal(expected, DesktopDeliveryPlan.PowerShellLiteral(input));

    [Theory]
    [InlineData("plain", "\"plain\"")]
    [InlineData("say \"hi\"", "\"say \\\"hi\\\"\"")]
    [InlineData(@"back\slash", "\"back\\\\slash\"")]
    public void AppleScript_literals_escape_the_backslash_before_the_quote(string input, string expected)
        => Assert.Equal(expected, DesktopDeliveryPlan.AppleScriptLiteral(input));

    /// <summary>AppleScript has no line continuation inside a string literal, so a newline in an
    /// alert name would be a syntax error and the toast would simply never appear.</summary>
    [Fact]
    public void A_newline_in_an_alert_name_does_not_break_the_applescript_statement()
    {
        var literal = DesktopDeliveryPlan.AppleScriptLiteral("line one\nline two\r\nthree");

        Assert.DoesNotContain("\n", literal);
        Assert.DoesNotContain("\r", literal);
        Assert.Equal("\"line one line two  three\"", literal);
    }

    /// <summary>The quote survives the whole way out to the argument vector, not just the
    /// helper — the escaping is only worth anything if the command builder actually calls it.</summary>
    [Fact]
    public void An_apostrophe_in_a_symbol_survives_into_the_windows_toast_command()
    {
        var plan = FullyEquipped(DesktopOs.Windows);
        var script = plan.ToastCommand("Cody's alert", "it's done", urgent: false)!.Args.Last();

        Assert.Contains("'Cody''s alert'", script);
        Assert.Contains("'it''s done'", script);
    }

    [Fact]
    public void A_quote_in_a_symbol_survives_into_the_osascript_command()
    {
        var plan = DesktopDeliveryPlan.For(DesktopOs.MacOS, Has("/usr/bin/osascript"));
        var statement = plan.ToastCommand("the \"good\" alert", "fired", urgent: false)!.Args[1];

        Assert.Contains("subtitle \"the \\\"good\\\" alert\"", statement);
    }

    // ── What the user is told ────────────────────────────────────────────────

    /// <summary>
    /// The delivery panel's hint is the only place a user learns why their switches are missing.
    /// "notify-send is not installed" on a Mac was a wrong answer to a real question.
    /// </summary>
    [Fact]
    public void The_toast_description_names_this_desktops_path_not_linuxs()
    {
        Assert.Contains("notify-send", FullyEquipped(DesktopOs.Linux).DescribeToast());
        Assert.Contains("Notification Center", FullyEquipped(DesktopOs.MacOS).DescribeToast());
        Assert.Contains("Action Center", FullyEquipped(DesktopOs.Windows).DescribeToast());

        var bareMac = DesktopDeliveryPlan.For(DesktopOs.MacOS, _ => false);
        Assert.Contains("Mac", bareMac.DescribeToast());
        Assert.DoesNotContain("notify-send", bareMac.DescribeToast());

        var bareWindows = DesktopDeliveryPlan.For(DesktopOs.Windows, _ => false);
        Assert.Contains("PowerShell", bareWindows.DescribeToast());
        Assert.DoesNotContain("notify-send", bareWindows.DescribeToast());
    }

    [Fact]
    public void Describe_names_the_operating_system_so_one_log_line_settles_which_branch_ran()
    {
        Assert.Contains("os: macos", FullyEquipped(DesktopOs.MacOS).Describe());
        Assert.Contains("speech: say", FullyEquipped(DesktopOs.MacOS).Describe());
        Assert.Contains("os: windows", FullyEquipped(DesktopOs.Windows).Describe());
        Assert.Contains("speech: sapi", FullyEquipped(DesktopOs.Windows).Describe());
        Assert.Contains("speech: orca", FullyEquipped(DesktopOs.Linux).Describe());
    }

    // ── The one line that made the whole feature Linux-only ──────────────────

    /// <summary>
    /// <b>The regression guard proper.</b> The cases above prove the Windows and macOS branches
    /// build the right commands; this one stops the presenter from routing around them the way
    /// it did for a year. <c>WebHostSpeechManager.FindOnPath</c> opens with
    /// <c>if (!IsOSPlatform(Linux)) return null;</c> — a correct guard for the in-session Linux
    /// speech manager it belongs to, and a silent feature-killer anywhere else. Its reappearance
    /// in the presenter is the defect, so it is the thing banned.
    /// </summary>
    [Fact]
    public void The_presenter_does_not_probe_through_the_linux_only_path_finder()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AccessibleTrader.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);

        var services = Path.Combine(dir!.FullName, "AccessibleTrader.WebHost", "Services");
        string presenter = File.ReadAllText(Path.Combine(services, "DesktopAlertPresenter.cs"));
        string plan = File.ReadAllText(Path.Combine(services, "DesktopDeliveryPlan.cs"));

        // Vacuity floor: the right files, still doing the job the ban is protecting.
        Assert.Contains("class ProcessDesktopAlertPresenter", presenter, StringComparison.Ordinal);
        Assert.Contains("_plan.ToastCommand", presenter, StringComparison.Ordinal);
        Assert.Contains("_plan.SpeechCommand", presenter, StringComparison.Ordinal);
        Assert.Contains("_plan.SoundCommand", presenter, StringComparison.Ordinal);
        Assert.Contains("case DesktopOs.Windows", plan, StringComparison.Ordinal);
        Assert.Contains("case DesktopOs.MacOS", plan, StringComparison.Ordinal);

        // The CALL, not the word: both files name FindOnPath in prose, explaining what it did
        // and why nothing here goes through it. Banning the identifier outright would make the
        // guard fail on its own documentation.
        Assert.DoesNotContain("FindOnPath(", presenter, StringComparison.Ordinal);
        Assert.DoesNotContain("FindOnPath(", plan, StringComparison.Ordinal);

        // ...and the probe stays injected, so a machine the developer is not sitting at can
        // still be described by a test.
        Assert.Contains("Func<string, bool> fileExists", plan, StringComparison.Ordinal);
    }

    /// <summary>
    /// <see cref="LocalDesktopNotifier"/> is what the alert-delivery panel asks "can you show a
    /// toast, and what with". It answers from the plan, so the panel's hint is right on every
    /// desktop rather than saying "notify-send is not installed" to a Mac user.
    /// </summary>
    [Fact]
    public void The_notifier_reports_this_machines_path_rather_than_linuxs()
    {
        var mac = new StubPresenter(FullyEquipped(DesktopOs.MacOS));
        var notifier = new LocalDesktopNotifier(mac);

        Assert.True(notifier.IsAvailable);
        Assert.Contains("Notification Center", notifier.Describe());

        notifier.Notify("BTC/USD", "crossed 100,000");
        // Ordinary news, never critical: critical is the monitor's word for a feed it can no
        // longer watch.
        Assert.Equal(("BTC/USD", "crossed 100,000", false), Assert.Single(mac.Toasts));
    }

    [Fact]
    public void A_machine_with_no_toast_path_says_so_instead_of_offering_dead_switches()
    {
        var bare = new StubPresenter(DesktopDeliveryPlan.For(DesktopOs.MacOS, _ => false));

        Assert.False(new LocalDesktopNotifier(bare).IsAvailable);
    }

    /// <summary>The presenter's own answers, driven by a plan, with nothing spawned.</summary>
    private sealed class StubPresenter : IDesktopAlertPresenter
    {
        private readonly DesktopDeliveryPlan _plan;
        public readonly List<(string, string, bool)> Toasts = new();

        public StubPresenter(DesktopDeliveryPlan plan) => _plan = plan;

        public string Describe() => _plan.Describe();
        public string DescribeToast() => _plan.DescribeToast();
        public bool CanNotify => _plan.CanNotify;
        public void PlayNotificationSound() { }
        public void Notify(string title, string text, bool urgent) => Toasts.Add((title, text, urgent));
        public void Speak(string text) { }
    }
}
