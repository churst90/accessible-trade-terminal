using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Linq;
using System.Runtime.Loader;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;

namespace AccessibleTrader.Core.Services
{
    public interface IPluginLoaderService
    {
        /// <summary>
        /// Scans <paramref name="directory"/> for DLLs matching <paramref name="searchPattern"/>
        /// and instantiates every concrete type assignable to <typeparamref name="T"/>.
        /// The default pattern matches the trading-provider plugin convention
        /// (<c>AccessibleTrader.Plugins.*.dll</c>); strategy-plugin scanning passes
        /// the strategy-specific pattern so the two sets don't cross-contaminate.
        /// </summary>
        IEnumerable<T> LoadPlugins<T>(string directory, string searchPattern = "AccessibleTrader.Plugins.*.dll") where T : class;
        void UnloadAll();
    }

    /// <summary>
    /// Policy that decides which plugin DLLs are allowed to load.
    /// Populate <see cref="TrustedSha256"/> with the hex SHA-256 digest of every
    /// first-party DLL that should load without a prompt. When
    /// <see cref="RequireTrusted"/> is <c>true</c>, any DLL whose hash is not
    /// in the allow-list is skipped. When <c>false</c>, unknown DLLs still load
    /// but are logged as a warning so a build pipeline can flag them.
    /// </summary>
    public sealed class PluginTrustPolicy
    {
        public HashSet<string> TrustedSha256 { get; } = new(StringComparer.OrdinalIgnoreCase);
        public bool RequireTrusted { get; set; }

        /// <summary>
        /// Hashes a plugin DLL and returns the hex SHA-256 digest. Returns
        /// <c>null</c> if the file can't be read.
        /// </summary>
        public static string? ComputeSha256(string path)
        {
            try
            {
                using var fs = File.OpenRead(path);
                var digest = SHA256.HashData(fs);
                return Convert.ToHexString(digest);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Loads a trusted-hash manifest from disk and populates the policy.
        /// The manifest is a newline-separated file of hex SHA-256 digests
        /// (64 chars each); blank lines and anything after '#' on a line are
        /// ignored so entries can be annotated with a filename.
        ///
        /// Typical shape:
        ///   # Generated 2026-04-16 by tools/hash-plugins.sh
        ///   A1B2C3…  # AccessibleTrader.Plugins.Binance.dll
        ///   D4E5F6…  # AccessibleTrader.Plugins.Coinbase.dll
        ///   …
        ///
        /// Returns the number of hashes loaded. Missing file returns 0 without
        /// throwing — a fresh install with no manifest should still start.
        /// </summary>
        public int LoadManifest(string manifestPath)
        {
            if (!File.Exists(manifestPath)) return 0;
            int loaded = 0;
            foreach (var rawLine in File.ReadAllLines(manifestPath))
            {
                if (string.IsNullOrWhiteSpace(rawLine)) continue;
                var line = rawLine;
                var hashIdx = line.IndexOf('#');
                if (hashIdx >= 0) line = line[..hashIdx];
                line = line.Trim();
                if (line.Length == 0) continue;
                // Tolerate a leading "sha256:" prefix and colon-separated hex.
                if (line.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
                    line = line[7..].Trim();
                line = line.Replace(":", "").Replace(" ", "").Trim();
                if (line.Length != 64) continue;
                // Only accept hex.
                bool hex = true;
                foreach (var c in line)
                {
                    if (!(c >= '0' && c <= '9') && !(c >= 'a' && c <= 'f') && !(c >= 'A' && c <= 'F'))
                    { hex = false; break; }
                }
                if (!hex) continue;
                if (TrustedSha256.Add(line)) loaded++;
            }
            return loaded;
        }
    }

    /// <summary>
    /// Custom AssemblyLoadContext to isolate plugin dependencies and prevent DLL hell (e.g., Newtonsoft.Json version conflicts).
    /// </summary>
    public class PluginLoadContext : AssemblyLoadContext
    {
        private readonly AssemblyDependencyResolver _resolver;

        public PluginLoadContext(string pluginPath) : base(isCollectible: true)
        {
            _resolver = new AssemblyDependencyResolver(pluginPath);
        }

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            // AGGRESSIVE SHARING: If the assembly belongs to core .NET namespaces or our app's infrastructure,
            // we MUST use the version already loaded in the Default context. 
            // This prevents "TypeCastExceptions" when passing objects (like Rx Observables) across ALC boundaries.
            bool isCoreNamespace = assemblyName.Name != null && (
                assemblyName.Name.StartsWith("System.", StringComparison.OrdinalIgnoreCase) ||
                assemblyName.Name.StartsWith("Microsoft.", StringComparison.OrdinalIgnoreCase) ||
                (assemblyName.Name.StartsWith("AccessibleTrader.", StringComparison.OrdinalIgnoreCase) && 
                 !assemblyName.Name.Contains(".Plugins.", StringComparison.OrdinalIgnoreCase)) || // Don't share the plugins themselves!
                assemblyName.Name.Equals("Newtonsoft.Json", StringComparison.OrdinalIgnoreCase) ||
                assemblyName.Name.Equals("netstandard", StringComparison.OrdinalIgnoreCase)
            );

            if (isCoreNamespace)
            {
                // Returning null here tells the ALC to look in the Default context.
                return null;
            }

            // For third-party libraries unique to the plugin (e.g. Binance.Net, Alpaca.Markets),
            // we try to resolve them from the plugin's own folder.
            string? assemblyPath = _resolver.ResolveAssemblyToPath(assemblyName);
            if (assemblyPath != null)
            {
                return LoadFromAssemblyPath(assemblyPath);
            }

            return null;
        }

        protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
        {
            string? libraryPath = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
            if (libraryPath != null)
            {
                return LoadUnmanagedDllFromPath(libraryPath);
            }

            return IntPtr.Zero;
        }
    }

    public class PluginLoaderService : IPluginLoaderService
    {
        private readonly List<PluginLoadContext> _contexts = new();
        private readonly ILogger<PluginLoaderService> _logger;
        private readonly PluginTrustPolicy _trust;

        public PluginLoaderService(ILogger<PluginLoaderService> logger, PluginTrustPolicy? trust = null)
        {
            _logger = logger;
            _trust  = trust ?? new PluginTrustPolicy();
        }

        public IEnumerable<T> LoadPlugins<T>(string directory, string searchPattern = "AccessibleTrader.Plugins.*.dll") where T : class
        {
            _logger.LogDebug("Searching for plugins in {Directory} (pattern {Pattern}).", directory, searchPattern);
            if (!Directory.Exists(directory))
            {
                _logger.LogDebug("Plugin directory does not exist: {Directory}.", directory);
                return Enumerable.Empty<T>();
            }

            var plugins = new List<T>();
            var dlls = Directory.GetFiles(directory, searchPattern, SearchOption.AllDirectories);
            _logger.LogDebug("Found {Count} matching plugin DLLs.", dlls.Length);

            foreach (var dll in dlls)
            {
                try
                {
                    var hash = PluginTrustPolicy.ComputeSha256(dll);
                    bool trusted = hash != null && _trust.TrustedSha256.Contains(hash);

                    if (!trusted)
                    {
                        if (_trust.RequireTrusted)
                        {
                            _logger.LogWarning(
                                "Plugin {Dll} (sha256 {Hash}) is not in the trusted allow-list. " +
                                "Skipping load (PluginTrustPolicy.RequireTrusted=true).",
                                Path.GetFileName(dll), hash ?? "unreadable");
                            continue;
                        }

                        _logger.LogWarning(
                            "Loading UNVERIFIED plugin {Dll} (sha256 {Hash}). Add this hash to the " +
                            "trusted allow-list if the publisher is known, or enable RequireTrusted " +
                            "to block unverified plugins entirely.",
                            Path.GetFileName(dll), hash ?? "unreadable");
                    }
                    else
                    {
                        _logger.LogDebug("Plugin {Dll} matches trusted hash.", Path.GetFileName(dll));
                    }

                    _logger.LogDebug("Loading assembly from {Dll}.", dll);
                    var context = new PluginLoadContext(dll);
                    _contexts.Add(context);

                    var assembly = context.LoadFromAssemblyPath(dll);
                    IEnumerable<Type> types;
                    try
                    {
                        types = assembly.GetTypes()
                            .Where(t => typeof(T).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract);
                    }
                    catch (ReflectionTypeLoadException ex)
                    {
                        _logger.LogWarning("Partial type load for {Dll}. Some types skipped.", Path.GetFileName(dll));
                        types = ex.Types.Where(t => t != null && typeof(T).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract).Cast<Type>();
                    }

                    _logger.LogDebug("Found {Count} types implementing {Interface} in {Dll}.", types.Count(), typeof(T).Name, Path.GetFileName(dll));

                    foreach (var type in types)
                    {
                        try
                        {
                            if (Activator.CreateInstance(type) is T instance)
                            {
                                plugins.Add(instance);
                                _logger.LogDebug("Created plugin instance: {Type}.", type.FullName);
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to instantiate {Type} from {Dll}.", type.Name, Path.GetFileName(dll));
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error loading plugin DLL {Dll}.", dll);
                }
            }

            return plugins;
        }

        public void UnloadAll()
        {
            foreach (var context in _contexts)
            {
                try { context.Unload(); } catch { }
            }
            _contexts.Clear();
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
    }
}
