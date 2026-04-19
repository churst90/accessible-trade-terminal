using Foundation;

namespace AccessibleTrader.BlazorClient;

[Register("AppDelegate")]
public class AppDelegate : MauiUIApplicationDelegate
{
    // Keyboard input is handled by Platforms/MacCatalyst/KeyboardPageHandler
    // (registered via handlers.AddHandler<ContentPage, KeyboardPageHandler>() in MauiProgram).
    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}
