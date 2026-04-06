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
		var window = new Window(_mainPage) { Title = "Accessible Trader Terminal" };
        window.Destroying += (s, e) => {
            Application.Current?.Quit();
        };
        return window;
	}
}
