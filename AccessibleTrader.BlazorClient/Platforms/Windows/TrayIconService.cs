#if TRAY_ICON
// Windows tray icon (on by default; compiled with -p:EnableWindowsTrayIcon=true).
// Needs a real Windows-session smoke test, and it has never had one:
//   1. With "Minimize to tray on exit" ON (the DEFAULT since 2026-09-11), close the window or
//      press Alt+F4 → the app hides to the tray, a notification says it is still running, and
//      audio/alerts keep going.
//   2. While hidden, a bar close on the focused chart now arrives as a NOTIFICATION (it is
//      suppressed while the window is visible, because the live region says it).
//   3. Double-click / "Restore" → the window returns AND the focused chart stops notifying.
//   4. "Quit" really quits.
//   5. With the setting turned OFF, close must actually close.

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
        private static Func<bool> _minimizeToTray = static () => true;
        private static Action<bool>? _onVisibilityChanged;

        /// <param name="minimizeToTrayEnabled">Read at every close. TRUE is the default since
        /// 2026-09-11 (Cody) — the close button hides to the tray and the terminal keeps
        /// watching.</param>
        /// <param name="onVisibilityChanged">Told whenever the window hides or comes back, so
        /// the notification layer knows the live region has stopped reaching anybody and every
        /// terminal event — the focused chart's bar close included — must take the OS
        /// notification channel instead. See <c>WindowVisibilityPresence</c>.</param>
        public static void Initialize(
            Microsoft.Maui.Controls.Window mauiWindow,
            Func<bool> minimizeToTrayEnabled,
            Action<bool>? onVisibilityChanged = null)
        {
            _minimizeToTray = minimizeToTrayEnabled ?? (static () => true);
            _onVisibilityChanged = onVisibilityChanged;

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
                    SetVisible(false);
                    Announce();
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

        /// <summary>Neither the visibility callback nor a notification may stop a window
        /// closing, or refuse to let one come back.</summary>
        private static void SetVisible(bool visible)
        {
            try { _onVisibilityChanged?.Invoke(visible); }
            catch { /* a broken listener is not a reason to keep the window open */ }
        }

        /// <summary>
        /// The MAUI analogue of the WebHost's farewell notification. A sighted user sees the
        /// window go and the tray icon appear; with a screen reader there is nothing at all to
        /// tell you the terminal is still watching — Alt+F4 and silence is indistinguishable
        /// from Alt+F4 and gone.
        /// </summary>
        private static void Announce()
        {
            try
            {
                _tray?.ShowNotification(
                    title: "Accessible Trade Terminal",
                    message: "Still running in the notification area. Alerts, order fills and bar "
                           + "closes arrive here as notifications. Restore or Quit from the tray icon.");
            }
            catch { /* best effort: the app is already hidden and must stay hidden */ }
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
            // The live region is reading again, so the focused chart stops toasting.
            SetVisible(true);
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
