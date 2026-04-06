using Android.App;
using Android.Content.PM;
using Android.OS;
using AccessibleTrader.Core.Services;
using Microsoft.Extensions.DependencyInjection;

namespace AccessibleTrader.BlazorClient;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    public override bool DispatchKeyEvent(Android.Views.KeyEvent? e)
    {
        if (e?.Action == Android.Views.KeyEventActions.Down)
        {
            var service = IPlatformApplication.Current?.Services?.GetService<IInputService>();
            if (service != null)
            {
                // Normalize Android KeyCode to a string matching the shortcut manager's key names
                string key  = e.KeyCode.ToString().Replace("Keycode", "").ToUpper();
                bool shift  = e.MetaState.HasFlag(Android.Views.MetaKeyStates.ShiftOn);
                bool ctrl   = e.MetaState.HasFlag(Android.Views.MetaKeyStates.CtrlOn);
                bool alt    = e.MetaState.HasFlag(Android.Views.MetaKeyStates.AltOn);
                service.ProcessKey(key, shift, ctrl, alt);
            }
        }
        return base.DispatchKeyEvent(e);
    }
}
