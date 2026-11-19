namespace AccessibleTrader.BlazorClient;

public partial class App : Application
{
    private readonly MainPage _mainPage;
    private readonly AccessibleTrader.Core.Services.ISettingsManager _settings;
    private readonly AccessibleTrader.Core.Services.Notifications.WindowVisibilityPresence _presence;
    private readonly AccessibleTrader.Core.Services.ChangeLog.ChangeLogCatchUpService _changeLog;

	public App(
        MainPage mainPage,
        AccessibleTrader.Core.Services.ISettingsManager settings,
        AccessibleTrader.Core.Services.Notifications.WindowVisibilityPresence presence,
        AccessibleTrader.Core.Services.ChangeLog.ChangeLogCatchUpService changeLog)
	{
		InitializeComponent();
        _mainPage = mainPage;
        _settings = settings;
        _presence = presence;
        _changeLog = changeLog;
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		var window = new Window(_mainPage) { Title = "Accessible Trade Terminal" };
        window.Destroying += (s, e) => {
            MarkAway();
            Application.Current?.Quit();
        };
        // MAUI's own lifecycle gives foreground/background without a new hook: Resumed offers
        // the Alt+U catch-up; Stopped and Destroying record when the user left. The launch
        // offer is AppStartupService's (step 9b): Created fires before the page and its speech
        // exist. The tray's visibility callback below covers Windows' hide-to-tray, where the
        // window is hidden without Stopped.
        window.Resumed += (_, _) => OfferCatchUp();
        window.Stopped += (_, _) => MarkAway();
#if TRAY_ICON
        // Windows tray (csproj-gated): with "Minimize to tray on exit" on — which is the
        // DEFAULT since 2026-09-11 — closing the window hides to the tray and the terminal keeps
        // running: feeds, alerts and audio stay live, and every terminal event arrives as a
        // system notification because the window is no longer reading anything aloud. The
        // callback is read at close time rather than here, so flipping the checkbox takes effect
        // on the next close instead of the next launch.
        Platforms.Windows.TrayIconService.Initialize(
            window, MinimizeToTrayEnabled, visible =>
            {
                _presence.SetVisible(visible);
                if (visible) OfferCatchUp(); else MarkAway();
            });
#endif
        return window;
	}

    /// <summary>Settings → General, "Minimize to tray on exit". ON by default since
    /// 2026-09-11 — see DesktopWindowSettings for Cody's reasoning.</summary>
    private bool MinimizeToTrayEnabled()
        => AccessibleTrader.Core.Services.DesktopWindowSettings.MinimizeToTray(_settings);

    /// <summary>Record the moment the user left, for the next return prompt. The store logs a
    /// failed write itself, so closing the app never waits on an error path.</summary>
    private void MarkAway()
    {
        try { _changeLog.SetLastPresentAsync(DateTime.UtcNow).GetAwaiter().GetResult(); }
        catch { /* the change log must never stop the app closing */ }
    }

    /// <summary>Offer (never read) the catch-up if something changed while away.</summary>
    private void OfferCatchUp()
    {
        try { _ = _changeLog.OfferOnReturnAsync(DateTime.UtcNow); }
        catch { /* offering is optional; losing it must not affect startup */ }
    }
}
