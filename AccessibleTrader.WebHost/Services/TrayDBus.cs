using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AccessibleTrader.WebHost.Services.Tray;
using Tmds.DBus;

namespace AccessibleTrader.WebHost.Services
{
    // StatusNotifierItem + DBusMenu implementations, proven end-to-end by the tray spike and
    // the integrated runtime check (icon shows, Orca reaches it, menu clicks reach managed
    // code). Gotchas already solved: a dedicated Connection (not the shared auto-connect one),
    // public D-Bus types, and the nested-variant DBusMenu layout. Driven by the shared
    // TrayModel so the menu/labels are the same across platforms.

    [DBusInterface("org.kde.StatusNotifierWatcher")]
    public interface IStatusNotifierWatcher : IDBusObject
    {
        Task RegisterStatusNotifierItemAsync(string Service);
    }

    [Dictionary]
    public class StatusNotifierItemProperties
    {
        public string Category = "ApplicationStatus";
        public string Id = "accessible-trade-terminal";
        public string Title = "Accessible Trade Terminal";
        public string Status = "Active";
        public uint WindowId = 0;
        public string IconName = "utilities-system-monitor";
        public string IconThemePath = "";
        public string ToolTipTitle = "Accessible Trade Terminal";
        public bool ItemIsMenu = true;
        public ObjectPath Menu = new ObjectPath("/MenuBar");
    }

    [DBusInterface("org.kde.StatusNotifierItem")]
    public interface IStatusNotifierItem : IDBusObject
    {
        Task ContextMenuAsync(int X, int Y);
        Task ActivateAsync(int X, int Y);
        Task SecondaryActivateAsync(int X, int Y);
        Task ScrollAsync(int Delta, string Orientation);
        // Void signals the host watches to re-read Title/ToolTip. Tmds.DBus wires its own
        // emit handler through these Watch methods at registration, so RaiseChanged() (which
        // invokes the stored handlers) is what actually pushes the signal onto the bus.
        Task<IDisposable> WatchNewTitleAsync(Action handler, Action<Exception>? onError = null);
        Task<IDisposable> WatchNewToolTipAsync(Action handler, Action<Exception>? onError = null);
        Task<object> GetAsync(string prop);
        Task<StatusNotifierItemProperties> GetAllAsync();
        Task SetAsync(string prop, object val);
        Task<IDisposable> WatchPropertiesAsync(Action<PropertyChanges> handler);
    }

    public sealed class TrayItem : IStatusNotifierItem
    {
        private readonly Func<string> _title;
        private Action? _newTitle;
        private Action? _newToolTip;
        private readonly object _sigLock = new();
        public TrayItem(Func<string> title) => _title = title;

        public ObjectPath ObjectPath => new ObjectPath("/StatusNotifierItem");

        /// <summary>Emit NewTitle + NewToolTip so the host re-reads the (already-fresh) label.</summary>
        public void RaiseChanged()
        {
            Action? t, tt;
            lock (_sigLock) { t = _newTitle; tt = _newToolTip; }
            t?.Invoke();
            tt?.Invoke();
        }

        public Task<IDisposable> WatchNewTitleAsync(Action handler, Action<Exception>? onError = null)
        {
            lock (_sigLock) _newTitle += handler;
            return Task.FromResult<IDisposable>(new Unsub(() => { lock (_sigLock) _newTitle -= handler; }));
        }

        public Task<IDisposable> WatchNewToolTipAsync(Action handler, Action<Exception>? onError = null)
        {
            lock (_sigLock) _newToolTip += handler;
            return Task.FromResult<IDisposable>(new Unsub(() => { lock (_sigLock) _newToolTip -= handler; }));
        }

        public Task ActivateAsync(int X, int Y) => Task.CompletedTask;
        public Task SecondaryActivateAsync(int X, int Y) => Task.CompletedTask;
        public Task ContextMenuAsync(int X, int Y) => Task.CompletedTask;
        public Task ScrollAsync(int Delta, string Orientation) => Task.CompletedTask;

        public Task<object> GetAsync(string prop) => Task.FromResult<object>(prop switch
        {
            "Category" => "ApplicationStatus",
            "Id" => "accessible-trade-terminal",
            "Title" => _title(),
            "Status" => "Active",
            "WindowId" => (uint)0,
            "IconName" => "utilities-system-monitor",
            "IconThemePath" => "",
            "ToolTipTitle" => _title(),
            "ItemIsMenu" => true,
            "Menu" => new ObjectPath("/MenuBar"),
            _ => "",
        });

        public Task<StatusNotifierItemProperties> GetAllAsync()
            => Task.FromResult(new StatusNotifierItemProperties { Title = _title(), ToolTipTitle = _title() });

        public Task SetAsync(string prop, object val) => Task.CompletedTask;
        public Task<IDisposable> WatchPropertiesAsync(Action<PropertyChanges> handler)
            => Task.FromResult<IDisposable>(new NoopDisposable());
    }

    [Dictionary]
    public class DbusMenuProperties
    {
        public uint Version = 3;
        public string Status = "normal";
        public string TextDirection = "ltr";
        public string[] IconThemePath = Array.Empty<string>();
    }

    [DBusInterface("com.canonical.dbusmenu")]
    public interface IDbusMenu : IDBusObject
    {
        Task<(uint revision, (int, IDictionary<string, object>, object[]) layout)> GetLayoutAsync(
            int ParentId, int RecursionDepth, string[] PropertyNames);
        Task<(int, IDictionary<string, object>)[]> GetGroupPropertiesAsync(int[] Ids, string[] PropertyNames);
        Task<object> GetPropertyAsync(int Id, string Name);
        Task EventAsync(int Id, string EventId, object Data, uint Timestamp);
        Task<bool> AboutToShowAsync(int Id);
        Task<object> GetAsync(string prop);
        Task<DbusMenuProperties> GetAllAsync();
        Task SetAsync(string prop, object val);
        Task<IDisposable> WatchPropertiesAsync(Action<PropertyChanges> handler);
    }

    public sealed class TrayMenu : IDbusMenu
    {
        private readonly IReadOnlyList<TrayMenuItem> _items;
        private readonly DbusMenuProperties _props = new();

        public TrayMenu(IReadOnlyList<TrayMenuItem> items) => _items = items;

        public ObjectPath ObjectPath => new ObjectPath("/MenuBar");

        private static (int, IDictionary<string, object>, object[]) Leaf(int id, string label) =>
            (id, new Dictionary<string, object> { ["label"] = label, ["enabled"] = true, ["visible"] = true },
             Array.Empty<object>());

        public Task<(uint, (int, IDictionary<string, object>, object[]))> GetLayoutAsync(
            int ParentId, int RecursionDepth, string[] PropertyNames)
        {
            var children = new object[_items.Count];
            for (int i = 0; i < _items.Count; i++) children[i] = Leaf(_items[i].Id, _items[i].Label());
            var root = (0,
                (IDictionary<string, object>)new Dictionary<string, object> { ["children-display"] = "submenu" },
                children);
            return Task.FromResult(((uint)1, root));
        }

        public Task<(int, IDictionary<string, object>)[]> GetGroupPropertiesAsync(int[] Ids, string[] PropertyNames)
        {
            var list = _items
                .Where(x => Ids.Length == 0 || Array.IndexOf(Ids, x.Id) >= 0)
                .Select(x => (x.Id, (IDictionary<string, object>)new Dictionary<string, object>
                {
                    ["label"] = x.Label(), ["enabled"] = true, ["visible"] = true,
                }))
                .ToArray();
            return Task.FromResult(list);
        }

        public Task<object> GetPropertyAsync(int Id, string Name)
        {
            var match = _items.FirstOrDefault(x => x.Id == Id);
            return Task.FromResult<object>(Name == "label" && match != null ? match.Label() : (object)"");
        }

        public Task EventAsync(int Id, string EventId, object Data, uint Timestamp)
        {
            if (EventId == "clicked")
            {
                var match = _items.FirstOrDefault(x => x.Id == Id);
                match?.OnActivate();
            }
            return Task.CompletedTask;
        }

        // Return true = "the menu may have changed, re-fetch the layout before showing".
        // Our labels are dynamic (monitoring on/off, Silence ⇄ Resume with minutes left), so
        // the host MUST re-read them each open — returning false told it to reuse a stale cache,
        // which is why the labels never flipped after a toggle.
        public Task<bool> AboutToShowAsync(int Id) => Task.FromResult(true);

        public Task<object> GetAsync(string prop) => Task.FromResult<object>(prop switch
        {
            "Version" => _props.Version,
            "Status" => _props.Status,
            "TextDirection" => _props.TextDirection,
            "IconThemePath" => _props.IconThemePath,
            _ => "",
        });

        public Task<DbusMenuProperties> GetAllAsync() => Task.FromResult(_props);
        public Task SetAsync(string prop, object val) => Task.CompletedTask;
        public Task<IDisposable> WatchPropertiesAsync(Action<PropertyChanges> handler)
            => Task.FromResult<IDisposable>(new NoopDisposable());
    }

    public sealed class NoopDisposable : IDisposable { public void Dispose() { } }

    public sealed class Unsub : IDisposable
    {
        private Action? _dispose;
        public Unsub(Action dispose) => _dispose = dispose;
        public void Dispose() { System.Threading.Interlocked.Exchange(ref _dispose, null)?.Invoke(); }
    }
}
