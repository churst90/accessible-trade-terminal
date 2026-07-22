#if TRAY_ICON
// EXPERIMENTAL — compiled only with -p:EnableWindowsTrayIcon=true (see csproj).
// Written on the Linux box where the Windows TFM cannot build; verify on a
// Windows session before enabling by default:
//   1. dotnet build AccessibleTrader.BlazorClient -f net10.0-windows10.0.19041.0 -p:EnableWindowsTrayIcon=true
//   2. Run; close the window → app should HIDE to the tray, audio/alerts keep running.
//   3. Tray icon double-click / "Restore" → window returns. "Exit" → really quits.
//   4. Flip <EnableWindowsTrayIcon> default to true in the csproj.

using System;
using H.NotifyIcon;
using H.NotifyIcon.Core;
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

            var menu = new PopupMenu();
            menu.Items.Add(new PopupMenuItem("Restore", (_, _) => Restore()));
            menu.Items.Add(new PopupMenuSeparator());
            menu.Items.Add(new PopupMenuItem("Exit", (_, _) => Exit()));
            _tray.ContextMenu = menu;

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
