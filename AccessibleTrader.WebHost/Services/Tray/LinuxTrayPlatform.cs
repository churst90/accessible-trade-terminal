using System.Diagnostics;
using Tmds.DBus;

namespace AccessibleTrader.WebHost.Services.Tray
{
    /// <summary>
    /// Linux tray via the freedesktop StatusNotifierItem + DBusMenu protocols (Tmds.DBus).
    /// No GUI toolkit; the menu is exposed to AT-SPI so Orca can navigate it. Verified on
    /// MATE/GNOME. Speech goes through Orca's D-Bus (spd-say fallback), the browser through
    /// xdg-open, the clipboard through wl-copy / xclip / xsel.
    /// </summary>
    public sealed class LinuxTrayPlatform : ITrayPlatform
    {
        private readonly ILogger _logger;
        private readonly string? _gdbus;
        private readonly string? _spdSay;
        private readonly string? _clip;
        private Connection? _connection;
        private TrayItem? _item;
        private volatile string _title = "Accessible Trade Terminal";

        public LinuxTrayPlatform(ILogger logger)
        {
            _logger = logger;
            _gdbus = WebHostSpeechManager.FindOnPath("gdbus", File.Exists);
            _spdSay = WebHostSpeechManager.FindOnPath("spd-say", File.Exists);
            _clip = WebHostSpeechManager.FindOnPath("wl-copy", File.Exists)
                 ?? WebHostSpeechManager.FindOnPath("xclip", File.Exists)
                 ?? WebHostSpeechManager.FindOnPath("xsel", File.Exists);
        }

        public bool Initialize(TrayModel model)
        {
            try
            {
                _title = model.InitialTitle;
                _connection = new Connection(Address.Session!);
                _connection.ConnectAsync().GetAwaiter().GetResult();

                var menu = new TrayMenu(model.Items);
                _item = new TrayItem(() => _title);
                _connection.RegisterObjectAsync(menu).GetAwaiter().GetResult();
                _connection.RegisterObjectAsync(_item).GetAwaiter().GetResult();

                string serviceName = $"org.kde.StatusNotifierItem-{Environment.ProcessId}-1";
                _connection.RegisterServiceAsync(serviceName).GetAwaiter().GetResult();

                var watcher = _connection.CreateProxy<IStatusNotifierWatcher>(
                    "org.kde.StatusNotifierWatcher", "/StatusNotifierWatcher");
                watcher.RegisterStatusNotifierItemAsync(serviceName).GetAwaiter().GetResult();

                _logger.LogInformation("Linux tray registered ({Service}).", serviceName);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Linux tray could not be registered; continuing headless.");
                try { _connection?.Dispose(); } catch { /* ignore */ }
                _connection = null;
                return false;
            }
        }

        // Update the fresh-read title, then emit NewTitle/NewToolTip so the host re-reads and
        // the panel/AT label reflects the new unread count without waiting for a focus/hover.
        public void UpdateLabel(string title)
        {
            _title = title;
            try { _item?.RaiseChanged(); } catch (Exception ex) { _logger.LogDebug(ex, "Tray label signal failed."); }
        }

        public void Speak(string text)
        {
            if (_gdbus != null && Run(_gdbus, "call", "--session",
                    "--dest=org.gnome.Orca1.Service", "--object-path=/org/gnome/Orca1/Service",
                    "--method=org.gnome.Orca1.Service.PresentMessage", text))
                return;
            if (_spdSay != null) Run(_spdSay, text);
        }

        public void OpenUrl(string url) => Run("xdg-open", url);

        public void CopyToClipboard(string text)
        {
            if (_clip == null) return;
            string[] args =
                _clip.EndsWith("xclip", StringComparison.Ordinal) ? new[] { "-selection", "clipboard" }
                : _clip.EndsWith("xsel", StringComparison.Ordinal) ? new[] { "--clipboard", "--input" }
                : Array.Empty<string>(); // wl-copy takes stdin directly
            RunWithStdin(_clip, text, args);
        }

        private bool Run(string file, params string[] args)
        {
            try
            {
                var psi = new ProcessStartInfo { FileName = file, UseShellExecute = false, CreateNoWindow = true };
                foreach (var a in args) psi.ArgumentList.Add(a);
                using var _ = Process.Start(psi);
                return true;
            }
            catch (Exception ex) { _logger.LogDebug(ex, "Tray command {File} failed.", file); return false; }
        }

        private void RunWithStdin(string file, string stdin, string[] args)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = file, UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true,
                };
                foreach (var a in args) psi.ArgumentList.Add(a);
                using var p = Process.Start(psi);
                if (p != null) { p.StandardInput.Write(stdin); p.StandardInput.Close(); }
            }
            catch (Exception ex) { _logger.LogDebug(ex, "Clipboard command {File} failed.", file); }
        }

        public void Dispose()
        {
            try { _connection?.Dispose(); } catch { /* ignore */ }
            _connection = null;
        }
    }
}
