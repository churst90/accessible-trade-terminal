namespace AccessibleTrader.BlazorClient;

public partial class App : Application
{
    private readonly MainPage _mainPage;
    private readonly AccessibleTrader.Core.Services.ISettingsManager _settings;
    private readonly AccessibleTrader.Core.Services.Notifications.WindowVisibilityPresence _presence;

	public App(
        MainPage mainPage,
        AccessibleTrader.Core.Services.ISettingsManager settings,
        AccessibleTrader.Core.Services.Notifications.WindowVisibilityPresence presence)
	{
		InitializeComponent();
        _mainPage = mainPage;
        _settings = settings;
        _presence = presence;
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		var window = new Window(_mainPage) { Title = "Accessible Trade Terminal" };
        window.Destroying += (s, e) => {
            Application.Current?.Quit();
        };
#if TRAY_ICON
        // Windows tray (csproj-gated): with "Minimize to tray on exit" on — which is the
        // DEFAULT since 2026-09-11 — closing the window hides to the tray and the terminal keeps
        // running: feeds, alerts and audio stay live, and every terminal event arrives as a
        // system notification because the window is no longer reading anything aloud. The
        // callback is read at close time rather than here, so flipping the checkbox takes effect
        // on the next close instead of the next launch.
        Platforms.Windows.TrayIconService.Initialize(
            window, MinimizeToTrayEnabled, visible => _presence.SetVisible(visible));
#endif
        return window;
	}

    /// <summary>Settings → General, "Minimize to tray on exit". ON by default since
    /// 2026-09-11 — see DesktopWindowSettings for Cody's reasoning.</summary>
    private bool MinimizeToTrayEnabled()
        => AccessibleTrader.Core.Services.DesktopWindowSettings.MinimizeToTray(_settings);
}
