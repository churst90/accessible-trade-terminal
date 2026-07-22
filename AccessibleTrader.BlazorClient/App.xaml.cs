namespace AccessibleTrader.BlazorClient;

public partial class App : Application
{
    private readonly MainPage _mainPage;

	public App(MainPage mainPage)
	{
		InitializeComponent();
        _mainPage = mainPage;
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		var window = new Window(_mainPage) { Title = "Accessible Trade Terminal" };
        window.Destroying += (s, e) => {
            Application.Current?.Quit();
        };
#if TRAY_ICON
        // Windows tray (EXPERIMENTAL, csproj-gated): close hides to the tray and
        // the terminal keeps running — feeds, alerts, and audio stay live.
        Platforms.Windows.TrayIconService.Initialize(window);
#endif
        return window;
	}
}
