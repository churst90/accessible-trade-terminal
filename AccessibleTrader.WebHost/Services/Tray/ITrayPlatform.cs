using System;
using System.Collections.Generic;

namespace AccessibleTrader.WebHost.Services.Tray
{
    /// <summary>One menu entry. Label is evaluated FRESH each time the menu is shown so
    /// dynamic text (monitoring on/off, silence remaining) is always current.</summary>
    public sealed class TrayMenuItem
    {
        public int Id { get; }
        public Func<string> Label { get; }
        public Action OnActivate { get; }
        public TrayMenuItem(int id, Func<string> label, Action onActivate)
        {
            Id = id; Label = label; OnActivate = onActivate;
        }
    }

    /// <summary>The icon's accessible title plus its menu — everything a platform needs to render.</summary>
    public sealed class TrayModel
    {
        public string InitialTitle { get; }
        public IReadOnlyList<TrayMenuItem> Items { get; }
        public TrayModel(string initialTitle, IReadOnlyList<TrayMenuItem> items)
        {
            InitialTitle = initialTitle; Items = items;
        }
    }

    /// <summary>
    /// The OS-specific surface behind the tray. The platform renders the icon + menu (calling
    /// each item's Label() at show time and OnActivate() on click) and provides the small set
    /// of OS actions the menu needs. All methods must be safe to call from any thread and must
    /// never throw — a platform that can't create a tray degrades to a no-op so the server
    /// keeps running headless.
    ///
    /// Implementations: LinuxTrayPlatform (StatusNotifier/D-Bus, verified), WindowsTrayPlatform
    /// (Shell_NotifyIcon), MacTrayPlatform (NSStatusItem).
    /// </summary>
    public interface ITrayPlatform : IDisposable
    {
        /// <summary>True once the icon is actually showing. False means the platform bailed
        /// (no session bus, wrong OS, etc.) and the caller should carry on without a tray.</summary>
        bool Initialize(TrayModel model);

        /// <summary>Update the icon's accessible title/tooltip (e.g. the unread-alert count).</summary>
        void UpdateLabel(string title);

        void Speak(string text);
        void OpenUrl(string url);
        void CopyToClipboard(string text);
    }
}
