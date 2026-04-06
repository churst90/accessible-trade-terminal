using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Linq;
using System.Runtime.Loader;

namespace AccessibleTrader.Core.Services
{
    public interface IPluginLoaderService
    {
        IEnumerable<T> LoadPlugins<T>(string directory) where T : class;
        void UnloadAll();
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

        public IEnumerable<T> LoadPlugins<T>(string directory) where T : class
        {
            Console.WriteLine($"PluginLoaderService: Searching for plugins in {directory}...");
            if (!Directory.Exists(directory)) 
            {
                Console.WriteLine($"PluginLoaderService: Directory does not exist: {directory}");
                return Enumerable.Empty<T>();
            }

            var plugins = new List<T>();
            var dlls = Directory.GetFiles(directory, "AccessibleTrader.Plugins.*.dll", SearchOption.AllDirectories);
            Console.WriteLine($"PluginLoaderService: Found {dlls.Length} matching DLLs.");

            foreach (var dll in dlls)
            {
                try
                {
                    Console.WriteLine($"PluginLoaderService: Loading assembly from {dll}");
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
                        Console.WriteLine($"PluginLoaderService: Partial type load for {Path.GetFileName(dll)}. Some types skipped.");
                        types = ex.Types.Where(t => t != null && typeof(T).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract).Cast<Type>();
                    }

                    Console.WriteLine($"PluginLoaderService: Found {types.Count()} types implementing {typeof(T).Name} in {Path.GetFileName(dll)}");

                    foreach (var type in types)
                    {
                        try
                        {
                            if (Activator.CreateInstance(type) is T instance)
                            {
                                plugins.Add(instance);
                                Console.WriteLine($"PluginLoaderService: Successfully created instance of {type.FullName}");
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"PluginLoaderService: Failed to instantiate {type.Name} from {Path.GetFileName(dll)}: {ex.Message}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"PluginLoaderService: Error loading DLL {dll}: {ex.Message}");
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
