namespace AccessibleTrader.BlazorClient;

public partial class App : Application
{
    private readonly MainPage _mainPage;
    private readonly AccessibleTrader.Core.Services.ISettingsManager _settings;

	public App(MainPage mainPage, AccessibleTrader.Core.Services.ISettingsManager settings)
	{
		InitializeComponent();
        _mainPage = mainPage;
        _settings = settings;
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		var window = new Window(_mainPage) { Title = "Accessible Trade Terminal" };
        window.Destroying += (s, e) => {
            Application.Current?.Quit();
        };
#if TRAY_ICON
        // Windows tray (csproj-gated): with "Minimize to tray on exit" on, close hides to the
        // tray and the terminal keeps running — feeds, alerts, and audio stay live. The switch
        // is off by default, and the callback is read at close time rather than here, so
        // flipping it takes effect on the next close instead of the next launch.
        Platforms.Windows.TrayIconService.Initialize(window, MinimizeToTrayEnabled);
#endif
        return window;
	}

    /// <summary>Settings → General, "Minimize to tray on exit". Absent means off.</summary>
    private bool MinimizeToTrayEnabled()
        => _settings
            .GetSetting(AccessibleTrader.Core.Services.DesktopWindowSettings.MinimizeToTrayKey)
            ?.ToObject<bool>() ?? false;
}
