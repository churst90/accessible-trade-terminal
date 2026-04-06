using Foundation;

namespace AccessibleTrader.BlazorClient;

[Register("AppDelegate")]
public class AppDelegate : MauiUIApplicationDelegate
{
    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

    // TODO Phase 7: Wire Mac Catalyst keyboard input.
    // Override PressesBegan in a UIViewController subclass (or use UIKeyCommand arrays) and
    // route to IPlatformApplication.Current?.Services?.GetService<IInputService>()?.ProcessKey(...).
}
