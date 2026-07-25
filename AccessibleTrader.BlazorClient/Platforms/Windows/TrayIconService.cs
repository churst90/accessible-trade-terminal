#if TRAY_ICON
// Windows tray icon (on by default; compiled with -p:EnableWindowsTrayIcon=true).
// Needs a real Windows-session smoke test: close the window → app hides to the tray
// (audio/alerts keep running); double-click / "Restore" → window returns; "Exit"
// really quits.

using System;
using H.NotifyIcon;
using Microsoft.Maui.Controls;
using Microsoft.UI.Windowing;

namespace AccessibleTrader.BlazorClient.Platforms.Windows
{
    /// <summary>
    /// Windows tray icon + close-to-tray. Today, closing the window kills the
    /// app — and with it every feed, resting paper order, and pending alert.
    /// With the tray: the close button hides the window, the process keeps
    /// running (audio, alerts, fills all keep announcing), and the tray menu
    /// offers Restore / Exit — only Exit actually quits.
    /// </summary>
    public static class TrayIconService
    {
        private static TaskbarIcon? _tray;
        private static AppWindow? _appWindow;
        private static bool _reallyExit;

        public static void Initialize(Microsoft.Maui.Controls.Window mauiWindow)
        {
            mauiWindow.HandlerChanged += (_, _) =>
            {
                if (mauiWindow.Handler?.PlatformView is not Microsoft.UI.Xaml.Window native) return;
                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(native);
                var id = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
                _appWindow = AppWindow.GetFromWindowId(id);

                // Close button → hide to tray instead of quitting.
                _appWindow.Closing += (_, e) =>
                {
                    if (_reallyExit) return;
                    e.Cancel = true;
                    _appWindow.Hide();
                };

                CreateTray(native);
            };
        }

        private static void CreateTray(Microsoft.UI.Xaml.Window native)
        {
            if (_tray != null) return;
            _tray = new TaskbarIcon
            {
                ToolTipText = "Accessible Trade Terminal — running. Double-click to restore.",
            };

            _tray.LeftClickCommand = new Command(Restore);
            _tray.DoubleClickCommand = new Command(Restore);

            // H.NotifyIcon.Maui takes a MAUI MenuFlyout via FlyoutBase.ContextFlyout
            // (not the WinUI ContextMenu/PopupMenu API).
            var menu = new MenuFlyout
            {
                new MenuFlyoutItem { Text = "Restore", Command = new Command(Restore) },
                new MenuFlyoutSeparator(),
                new MenuFlyoutItem { Text = "Exit", Command = new Command(Exit) },
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
