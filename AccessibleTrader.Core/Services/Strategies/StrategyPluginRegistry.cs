using AccessibleTrader.Sdk.Strategies;
using Microsoft.Extensions.Logging;

namespace AccessibleTrader.Core.Services.Strategies
{
    /// <summary>
    /// Scans configured directories for DLLs implementing <see cref="IStrategyPlugin"/>,
    /// loads them via the existing <see cref="IPluginLoaderService"/> (which owns the
    /// AssemblyLoadContext lifecycle + <see cref="PluginTrustPolicy"/> trust check), and
    /// caches the returned <see cref="ITradingStrategy"/> templates.
    ///
    /// Kept separate from the trading-provider loader registration so the two plugin sets
    /// stay independently testable: the loader service is stateless across calls to
    /// <c>LoadPlugins</c>, but <see cref="PluginLoaderService.UnloadAll"/> touches every
    /// context it owns — if we reused the same loader for both sets, a "reload strategies"
    /// gesture would tear down the live provider plugins too. Strategy-plugin scanning
    /// therefore constructs a fresh <see cref="PluginLoaderService"/> per call.
    /// </summary>
    public interface IStrategyPluginRegistry
    {
        /// <summary>
        /// The set of template <see cref="ITradingStrategy"/> objects exported by every
        /// successfully-loaded plugin. Empty until <see cref="Initialize"/> runs. Safe to
        /// call from any thread after initialisation — the underlying list is a
        /// single-assignment snapshot.
        /// </summary>
        IReadOnlyList<ITradingStrategy> Templates { get; }

        /// <summary>The names (not file paths) of each plugin whose template set was cached.</summary>
        IReadOnlyList<string> LoadedPluginNames { get; }

        /// <summary>
        /// Scans every configured strategy directory and populates <see cref="Templates"/>.
        /// Idempotent: a second call is a no-op. Failures in one directory don't prevent
        /// others from loading — each DLL is guarded and logged independently.
        /// </summary>
        void Initialize();

        /// <summary>
        /// Tears down every plugin <see cref="System.Runtime.Loader.AssemblyLoadContext"/>
        /// owned by this registry, disposes any <see cref="System.IDisposable"/> templates,
        /// clears the cache, and allows a subsequent <see cref="Initialize"/> call to
        /// rescan from scratch. Safe to call if <see cref="Initialize"/> was never called.
        /// </summary>
        void UnloadAll();
    }

    /// <inheritdoc />
    public sealed class StrategyPluginRegistry : IStrategyPluginRegistry
    {
        private readonly ILogger<StrategyPluginRegistry> _logger;
        private readonly PluginTrustPolicy _trust;
        private readonly IReadOnlyList<string> _directories;
        private PluginLoaderService? _loader;
        private List<ITradingStrategy> _templates = new();
        private List<string> _pluginNames = new();
        private bool _initialised;
        private readonly object _lock = new();

        /// <summary>
        /// Strategy-plugin DLL filename pattern. Matches only DLLs whose name has the
        /// <c>AccessibleTrader.Plugins.Strategy.</c> prefix — keeps the trading-provider
        /// set (which uses just <c>AccessibleTrader.Plugins.</c>) out of the scan.
        /// </summary>
        public const string StrategyPluginSearchPattern = "AccessibleTrader.Plugins.Strategy.*.dll";

        public StrategyPluginRegistry(
            ILogger<StrategyPluginRegistry> logger,
            PluginTrustPolicy trust,
            IEnumerable<string> directories)
        {
            _logger = logger;
            _trust = trust;
            _directories = directories?.ToList() ?? new List<string>();
        }

        public IReadOnlyList<ITradingStrategy> Templates => _templates;
        public IReadOnlyList<string> LoadedPluginNames => _pluginNames;

        public void Initialize()
        {
            lock (_lock)
            {
                if (_initialised) return;
                _initialised = true;

                var loaderLogger = Microsoft.Extensions.Logging.Abstractions.NullLogger<PluginLoaderService>.Instance;
                _loader = new PluginLoaderService(loaderLogger, _trust);

                var plugins = new List<IStrategyPlugin>();
                foreach (var dir in _directories)
                {
                    if (string.IsNullOrWhiteSpace(dir)) continue;
                    try
                    {
                        var found = _loader.LoadPlugins<IStrategyPlugin>(dir, StrategyPluginSearchPattern);
                        plugins.AddRange(found);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Strategy-plugin scan failed in {Dir}.", dir);
                    }
                }

                var templates = new List<ITradingStrategy>();
                var names = new List<string>();
                foreach (var p in plugins)
                {
                    try
                    {
                        var set = p.GetStrategies();
                        if (set == null) continue;
                        foreach (var s in set)
                        {
                            if (s != null) templates.Add(s);
                        }
                        names.Add(p.Name);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex,
                            "Strategy plugin '{Plugin}' threw in GetStrategies(); skipping.",
                            p?.Name ?? "<unknown>");
                    }
                }

                _templates = templates;
                _pluginNames = names;

                if (templates.Count > 0)
                    _logger.LogInformation(
                        "Loaded {Count} strategy template(s) from {Plugins} plugin(s).",
                        templates.Count, names.Count);
            }
        }

        public void UnloadAll()
        {
            lock (_lock)
            {
                foreach (var t in _templates)
                {
                    if (t is IDisposable d)
                    {
                        try { d.Dispose(); } catch { /* defensive — plugin bug, swallow */ }
                    }
                }
                _templates = new List<ITradingStrategy>();
                _pluginNames = new List<string>();
                try { _loader?.UnloadAll(); } catch { }
                _loader = null;
                _initialised = false;
            }
        }
    }

    /// <summary>
    /// Thin wrapper that materialises the directory list a <see cref="StrategyPluginRegistry"/>
    /// should scan. Keeping the list resolution out of the registry ctor lets tests construct
    /// a registry from a single temp directory without platform-service plumbing, while
    /// production uses both the host ship-directory and the user drop-in directory.
    /// </summary>
    public static class StrategyPluginDirectories
    {
        public const string UserSubdirectoryName = "Strategies";

        /// <summary>
        /// <list type="bullet">
        ///   <item><c>{BaseDirectory}/Strategies/</c> — shipped alongside the app.</item>
        ///   <item><c>{LocalApplicationData}/AccessibleTrader/Strategies/</c> — user drop-ins.</item>
        /// </list>
        /// Both are created if missing so first-run doesn't skip them.
        /// </summary>
        public static IReadOnlyList<string> Default()
        {
            var baseDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, UserSubdirectoryName);
            // PlatformPaths, not GetFolderPath: an empty return on Unix yields a RELATIVE path,
            // which would scan the process's working directory for strategy plugins. Machine-level
            // by design — these are executable, not per-user state.
            var userDir = Path.Combine(
                AccessibleTrader.Core.Services.PlatformPaths.AppDataRoot(), UserSubdirectoryName);
            try { Directory.CreateDirectory(baseDir); } catch { }
            try { Directory.CreateDirectory(userDir); } catch { }
            return new[] { baseDir, userDir };
        }
    }
}
