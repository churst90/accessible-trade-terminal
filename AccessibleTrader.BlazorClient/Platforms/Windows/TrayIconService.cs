#if TRAY_ICON
// Windows tray icon (on by default; compiled with -p:EnableWindowsTrayIcon=true).
// Needs a real Windows-session smoke test: with "Minimize to tray on exit" ON,
// close the window → app hides to the tray (audio/alerts keep running);
// double-click / "Restore" → window returns; "Quit" really quits. With the
// setting OFF (the default), close must actually close.

using System;
using H.NotifyIcon;
using Microsoft.Maui.Controls;
using Microsoft.UI.Windowing;

namespace AccessibleTrader.BlazorClient.Platforms.Windows
{
    /// <summary>
    /// Windows tray icon + close-to-tray, <b>behind the user's own switch</b>.
    ///
    /// <para>
    /// With "Minimize to tray on exit" on (Settings → General), the close button hides the
    /// window, the process keeps running — audio, alerts and fills all keep announcing — and the
    /// tray menu offers Restore / Quit. With it off, which is the default, closing the window
    /// closes the app the way every other window on the desktop does.
    /// </para>
    ///
    /// <para>
    /// <b>Why the default is off (Cody, 2026-09-06).</b> An application that does not close when
    /// you close it is a surprise, and for a screen-reader user a surprise with no announcement
    /// is worse than an extra keystroke. The switch says what it now does when it is turned on,
    /// and the tray menu always carries a Quit so there is a way out that does not need the
    /// window.
    /// </para>
    ///
    /// <para>
    /// <b>The setting is read at close time, not at startup</b>, through the callback handed to
    /// <see cref="Initialize"/>. Flipping the checkbox therefore takes effect on the next close
    /// rather than the next launch, and nothing here has to care whether the settings file had
    /// finished loading when the window was created.
    /// </para>
    ///
    /// <para>
    /// <b>UNVERIFIED AT RUNTIME.</b> This file compiles only on a Windows build (the Windows TFM
    /// is excluded on this repo's Linux CI) and has never been exercised in a Windows session.
    /// The four steps at the top of this file are the smoke test that is still owed.
    /// </para>
    /// </summary>
    public static class TrayIconService
    {
        private static TaskbarIcon? _tray;
        private static AppWindow? _appWindow;
        private static bool _reallyExit;
        private static Func<bool> _minimizeToTray = static () => false;

        /// <param name="minimizeToTrayEnabled">Read at every close. False — the default — means
        /// the close button closes the app.</param>
        public static void Initialize(
            Microsoft.Maui.Controls.Window mauiWindow, Func<bool> minimizeToTrayEnabled)
        {
            _minimizeToTray = minimizeToTrayEnabled ?? (static () => false);

            mauiWindow.HandlerChanged += (_, _) =>
            {
                if (mauiWindow.Handler?.PlatformView is not Microsoft.UI.Xaml.Window native) return;
                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(native);
                var id = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
                _appWindow = AppWindow.GetFromWindowId(id);

                // Close button → hide to tray, but only if the user asked for that.
                _appWindow.Closing += (_, e) =>
                {
                    if (_reallyExit) return;
                    if (!SafeMinimizeToTray()) return;
                    e.Cancel = true;
                    _appWindow.Hide();
                };

                CreateTray(native);
            };
        }

        // A settings read must never be the reason a window refuses to close: if it throws,
        // fall through to the ordinary close.
        private static bool SafeMinimizeToTray()
        {
            try { return _minimizeToTray(); }
            catch { return false; }
        }

        private static void CreateTray(Microsoft.UI.Xaml.Window native)
        {
            if (_tray != null) return;
            _tray = new TaskbarIcon
            {
                // A tray icon is a control with no visible label, so this tooltip IS its
                // accessible name — it is what Narrator, NVDA and JAWS read when the user
                // arrives on it in the notification area.
                ToolTipText = "Accessible Trade Terminal — running. Double-click to restore.",
            };

            _tray.LeftClickCommand = new Command(Restore);
            _tray.DoubleClickCommand = new Command(Restore);

            // H.NotifyIcon.Maui takes a MAUI MenuFlyout via FlyoutBase.ContextFlyout
            // (not the WinUI ContextMenu/PopupMenu API). Reachable from the keyboard the way
            // every notification-area item is: focus the icon and press the Applications key
            // or Shift+F10.
            var menu = new MenuFlyout
            {
                new MenuFlyoutItem { Text = "Restore", Command = new Command(Restore) },
                new MenuFlyoutSeparator(),
                // "Quit", not "Exit": this is the way out that does not need the window, and it
                // has to read as final.
                new MenuFlyoutItem { Text = "Quit", Command = new Command(Exit) },
            };
            FlyoutBase.SetContextFlyout(_tray, menu);

            _tray.ForceCreate();
        }

        private static void Restore()
        {
            _appWindow?.Show();
            // Bring to foreground so keyboard focus lands back in the terminal.
            if (_appWindow != null)
                (_appWindow.Presenter as OverlappedPresenter)?.Restore();
        }

        private static void Exit()
        {
            _reallyExit = true;
            _tray?.Dispose();
            _tray = null;
            Microsoft.Maui.Controls.Application.Current?.Quit();
        }
    }
}
#endif
