using AccessibleTrader.Core.Services;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;

namespace AccessibleTrader.WebHost.Services.Tray
{
    /// <summary>
    /// The cross-platform tray host (local HostMode.Full only). Picks the OS platform
    /// implementation, resolves the bound URL once the server is listening, and hands both to a
    /// <see cref="TrayController"/> that owns the menu behaviour. Never registered on the hosted
    /// multi-user server. A platform that can't create an icon degrades to a no-op — the server
    /// keeps running headless.
    /// </summary>
    public sealed class DesktopTrayService : IHostedService
    {
        private readonly IServiceScopeFactory _scopes;
        private readonly IHostApplicationLifetime _lifetime;
        private readonly IServer _server;
        private readonly DemoPolicy _demo;
        private readonly RecentAlertsBuffer _alerts;
        private readonly AlertSnooze _snooze;
        private readonly ILogger<DesktopTrayService> _logger;

        private TrayController? _controller;
        private string _baseUrl = "http://localhost:5000";

        public DesktopTrayService(
            IServiceScopeFactory scopes, IHostApplicationLifetime lifetime, IServer server,
            DemoPolicy demo, RecentAlertsBuffer alerts, AlertSnooze snooze,
            ILogger<DesktopTrayService> logger)
        {
            _scopes = scopes; _lifetime = lifetime; _server = server;
            _demo = demo; _alerts = alerts; _snooze = snooze; _logger = logger;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            if (_demo.Mode != HostMode.Full) return Task.CompletedTask;
            _lifetime.ApplicationStarted.Register(OnStarted);
            return Task.CompletedTask;
        }

        private void OnStarted()
        {
            try
            {
                var addresses = _server.Features.Get<IServerAddressesFeature>()?.Addresses;
                _baseUrl = addresses?.FirstOrDefault()?.Replace("0.0.0.0", "localhost")
                                     ?.Replace("[::]", "localhost") ?? _baseUrl;

                ITrayPlatform? platform =
                    OperatingSystem.IsLinux() ? new LinuxTrayPlatform(_logger)
                    : OperatingSystem.IsWindows() ? new WindowsTrayPlatform(_logger)
                    : OperatingSystem.IsMacOS() ? new MacTrayPlatform(_logger)
                    : null;
                if (platform == null) { _logger.LogInformation("No tray platform for this OS; running headless."); return; }

                _controller = new TrayController(platform, new TrayController.Config
                {
                    BaseUrl = () => _baseUrl,
                    Alerts = _alerts,
                    Snooze = _snooze,
                    GetMonitoring = GetMonitoring,
                    SetMonitoring = SetMonitoring,
                    ArmedAlertCount = ArmedAlertCount,
                    Exit = _lifetime.StopApplication,
                });

                if (_controller.Start())
                    _logger.LogInformation("Desktop tray started (base URL {Url}).", _baseUrl);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Desktop tray failed to start; continuing headless.");
            }
        }

        private bool GetMonitoring()
        {
            using var scope = _scopes.CreateScope();
            var settings = scope.ServiceProvider.GetRequiredService<ISettingsManager>();
            return settings.GetSetting(LocalBackgroundMonitor.SettingKey)?.ToObject<bool>() ?? false;
        }

        private void SetMonitoring(bool value)
        {
            using var scope = _scopes.CreateScope();
            var settings = scope.ServiceProvider.GetRequiredService<ISettingsManager>();
            settings.SetSetting(LocalBackgroundMonitor.SettingKey, Newtonsoft.Json.Linq.JToken.FromObject(value));
            // MUST persist: ISettingsManager is scoped, so SetSetting alone only mutates this
            // throwaway scope's in-memory copy. SaveSettings writes settings.json; the next
            // read (a fresh scope in GetMonitoring, and the monitor's per-poll scope) loads it.
            settings.SaveSettings();
        }

        private int ArmedAlertCount()
        {
            try
            {
                using var scope = _scopes.CreateScope();
                var library = scope.ServiceProvider.GetRequiredService<IWorkspaceLibraryService>();
                return library.LoadAlerts().Count(a => a.IsActive);
            }
            catch (Exception ex) { _logger.LogDebug(ex, "Tray armed-alert count failed."); return 0; }
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            try { _controller?.Dispose(); }
            catch (Exception ex) { _logger.LogDebug(ex, "Tray dispose failed."); }
            return Task.CompletedTask;
        }
    }
}
