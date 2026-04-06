using System;
using Foundation;
using UIKit;
using Microsoft.Maui.Handlers;
using AccessibleTrader.Core.Services;
using Microsoft.Extensions.DependencyInjection;

namespace AccessibleTrader.BlazorClient;

/// <summary>
/// Custom ContentPage handler for iOS that intercepts UIKit PressesBegan and routes
/// hardware keyboard events (external Bluetooth keyboards) to IInputService.ProcessKey.
/// Registered in MauiProgram.cs via #if IOS ConfigureMauiHandlers.
/// Shares the same UIKit PressesBegan / MapKey pattern as the Mac Catalyst implementation.
/// </summary>
public class KeyboardPageHandler : PageHandler
{
    protected override void ConnectHandler(Microsoft.Maui.Platform.ContentView platformView)
    {
        base.ConnectHandler(platformView);
        EnsureKeyboardController(platformView);
    }

    private static void EnsureKeyboardController(UIView view)
    {
        var window = view.Window;
        if (window?.RootViewController == null) return;
        if (window.RootViewController is KeyboardViewController) return;

        var existing = window.RootViewController;
        var kbVc     = new KeyboardViewController();
        kbVc.AddChildViewController(existing);
        if (existing.View != null)
        {
            kbVc.View!.Frame = existing.View.Frame;
            kbVc.View.AddSubview(existing.View);
        }
        existing.DidMoveToParentViewController(kbVc);
        window.RootViewController = kbVc;
    }
}

/// <summary>
/// UIViewController that overrides PressesBegan to forward hardware keyboard
/// events to IInputService.ProcessKey on iOS (external keyboards).
/// </summary>
internal sealed class KeyboardViewController : UIViewController
{
    // UIKit special key characters (Unicode private use area)
    private const string ArrowUp    = "\uF700";
    private const string ArrowDown  = "\uF701";
    private const string ArrowLeft  = "\uF702";
    private const string ArrowRight = "\uF703";
    private const string F1  = "\uF704"; private const string F2  = "\uF705";
    private const string F3  = "\uF706"; private const string F4  = "\uF707";
    private const string F5  = "\uF708"; private const string F6  = "\uF709";
    private const string F7  = "\uF70A"; private const string F8  = "\uF70B";
    private const string Home = "\uF729"; private const string End = "\uF72B";
    private const string PageUp   = "\uF72C"; private const string PageDown = "\uF72D";
    private const string Escape   = "\u001B"; private new const string Delete = "\u007F";

    public override bool CanBecomeFirstResponder => true;

    public override void PressesBegan(NSSet<UIPress> presses, UIPressesEvent evt)
    {
        foreach (UIPress press in presses)
        {
            var uiKey = press.Key;
            if (uiKey == null) continue;

            string key   = MapKey(uiKey.Characters ?? string.Empty,
                                  uiKey.CharactersIgnoringModifiers ?? string.Empty);
            bool   shift = uiKey.ModifierFlags.HasFlag(UIKeyModifierFlags.Shift);
            bool   ctrl  = uiKey.ModifierFlags.HasFlag(UIKeyModifierFlags.Control);
            bool   alt   = uiKey.ModifierFlags.HasFlag(UIKeyModifierFlags.Alternate);

            if (!string.IsNullOrEmpty(key))
            {
                var service = IPlatformApplication.Current?.Services?.GetService<IInputService>();
                service?.ProcessKey(key, shift, ctrl, alt);
            }
        }
        base.PressesBegan(presses, evt);
    }

    private static string MapKey(string chars, string charsIgnoringMods)
    {
        var c = string.IsNullOrEmpty(chars) ? charsIgnoringMods : chars;
        return c switch
        {
            ArrowLeft  => "LEFT",
            ArrowRight => "RIGHT",
            ArrowUp    => "UP",
            ArrowDown  => "DOWN",
            Home       => "HOME",
            End        => "END",
            PageUp     => "PAGEUP",
            PageDown   => "PAGEDOWN",
            Escape     => "ESCAPE",
            Delete     => "BACKSPACE",
            F1 => "F1", F2 => "F2", F3 => "F3", F4 => "F4",
            F5 => "F5", F6 => "F6", F7 => "F7", F8 => "F8",
            " " => " ",
            "\r" or "\n" => "ENTER",
            _ when c.Length == 1 => c.ToUpperInvariant(),
            _ => string.Empty
        };
    }
}
