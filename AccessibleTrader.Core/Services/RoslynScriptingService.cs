using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Scripting;
using AccessibleTrader.Core.Services.Scripting;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Strategies;

namespace AccessibleTrader.Core.Services
{
    /// <summary>
    /// Result returned by <see cref="RoslynScriptingService.CompileIndicatorAsync"/>.
    /// </summary>
    public record CompileResult(
        bool Success,
        ICustomIndicator? Indicator,
        string[] Errors);

    public record CompileStrategyResult(
        bool Success,
        ITradingStrategy? Strategy,
        string[] Errors);

    /// <summary>
    /// Compiles user-written C# scripts into <see cref="ICustomIndicator"/> instances using Roslyn.
    /// Each compilation runs in its own <see cref="AssemblyLoadContext"/> for isolation.
    /// </summary>
    /// <remarks>
    /// Sandbox policy — allowed references:
    ///   AccessibleTrader.Sdk, System.Numerics, Skender.Stock.Indicators.
    /// Blocked: System.IO, System.Net, System.Reflection (any emit path).
    /// Scripts must define a class that implements <see cref="ICustomIndicator"/>.
    /// </remarks>
    public interface IRoslynScriptingService
    {
        /// <summary>
        /// Compiles <paramref name="code"/> and, on success, returns an instance of the first
        /// <see cref="ICustomIndicator"/> type found in the compiled assembly.
        /// </summary>
        Task<CompileResult> CompileIndicatorAsync(string code);

        /// <summary>
        /// Compiles <paramref name="code"/> and, on success, returns an instance of the first
        /// <see cref="ITradingStrategy"/> type found in the compiled assembly.
        /// </summary>
        Task<CompileStrategyResult> CompileStrategyAsync(string code);

        /// <summary>
        /// Executes the simple (legacy) scripting model: runs <paramref name="code"/> as a C# script
        /// expression with OHLCV globals and returns a flat double array.
        /// </summary>
        Task<ScriptResult> ExecuteSimpleAsync(string code, List<Ohlcv> data);
    }

    public class RoslynScriptingService : IRoslynScriptingService
    {
        // Track per-script ALCs so they can be collected when a script is
        // removed. Populated when the in-process (dev) path runs. The
        // out-of-process path uses _outOfProcessHosts instead.
        private readonly Dictionary<string, AssemblyLoadContext> _contexts = new();

        // Out-of-process host per indicator Id. Owns the worker process and
        // is disposed by UnloadScript. Populated on the default (production)
        // compile path; empty when ACCESSIBLETRADER_SCRIPT_IN_PROCESS=1.
        private readonly Dictionary<string, OutOfProcessScriptHost> _outOfProcessHosts = new();

        private readonly IScriptWorkerLauncher _workerLauncher;

        /// <summary>
        /// Resolves the worker executable next to the currently running host
        /// binary. The BlazorClient Release/Debug build output puts the
        /// worker in the same directory (both target <c>net10.0</c>), so
        /// <see cref="AppContext.BaseDirectory"/> is the right probe point.
        /// Overridable for testing.
        /// </summary>
        private readonly Func<string> _workerPathResolver;

        public RoslynScriptingService()
            : this(CreateDefaultLauncher(), DefaultWorkerPathResolver)
        {
        }

        public RoslynScriptingService(IScriptWorkerLauncher workerLauncher, Func<string> workerPathResolver)
        {
            _workerLauncher = workerLauncher ?? throw new ArgumentNullException(nameof(workerLauncher));
            _workerPathResolver = workerPathResolver ?? throw new ArgumentNullException(nameof(workerPathResolver));
        }

        /// <summary>
        /// Picks the OS-appropriate launcher for the current platform. If the
        /// OS-level sandbox primitive a launcher needs isn't available at
        /// runtime (bwrap missing on Linux, sandbox-exec masked on macOS,
        /// AppContainer creation failing on Windows), the launcher throws
        /// <see cref="ScriptSandboxUnavailableException"/> at launch time
        /// rather than silently running the worker unsandboxed. The
        /// <c>ACCESSIBLETRADER_ALLOW_UNSANDBOXED_SCRIPTS=1</c> env var is the
        /// explicit, security-event-logged opt-out (see
        /// <see cref="SandboxPolicy"/>).
        /// </summary>
        public static IScriptWorkerLauncher CreateDefaultLauncher()
        {
            // iOS + macCatalyst: explicit refusal. iOS has never had a usable
            // sandbox primitive for a child process that runs arbitrary user IL;
            // macCatalyst joined 2026-04-24 because the self-contained
            // macCatalyst build cannot reference the net10.0 ScriptWorker. Both
            // surface ScriptingNotSupportedOnPlatformException at compile time
            // rather than silently dropping into the in-process path.
            if (OperatingSystem.IsIOS())
                return new RefusingScriptWorkerLauncher("iOS");
            if (OperatingSystem.IsMacCatalyst())
                return new RefusingScriptWorkerLauncher("macCatalyst");
            if (OperatingSystem.IsAndroid())
                return new AndroidIsolatedProcessLauncher();
            if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.OSX))
                return new MacSandboxExecLauncher();
            if (OperatingSystem.IsWindows())
                return new WindowsAppContainerLauncher();
            // Linux (incl. the WebHost): bubblewrap sandbox when bwrap is present;
            // if it isn't, LinuxBwrapLauncher refuses to launch (with an install
            // hint in the exception message) instead of silently running the
            // worker unsandboxed.
            if (OperatingSystem.IsLinux())
                return new LinuxBwrapLauncher();
            return new DefaultProcessLauncher();
        }

        /// <summary>
        /// Exposed so the BlazorClient's DI container can pass the same
        /// resolver to the constructor it picks (existing parameterless
        /// constructor uses this internally). Returns the path the
        /// default resolver produces for the current host build.
        /// </summary>
        public static string DefaultWorkerPathResolver()
        {
            var baseDir = AppContext.BaseDirectory;
            var exeName = OperatingSystem.IsWindows()
                ? "AccessibleTrader.ScriptWorker.exe"
                : "AccessibleTrader.ScriptWorker";
            return Path.Combine(baseDir, exeName);
        }

        /// <summary>
        /// <c>true</c> when the caller has opted into the legacy in-process
        /// execution path via <c>ACCESSIBLETRADER_SCRIPT_IN_PROCESS=1</c> or
        /// <c>=true</c>. Default is <c>false</c> — user scripts execute in
        /// the out-of-process worker so a sandbox escape cannot reach the
        /// trading host. In-process is retained for breakpoint debugging.
        /// </summary>
        /// <remarks>
        /// The env var is honored ONLY in <c>DEBUG</c> builds. In <c>RELEASE</c>
        /// builds it is ignored and the getter always returns <c>false</c> — a
        /// compromised deployment or misconfigured installer setting the env
        /// var must never be able to silently downgrade retail users to the
        /// unsandboxed in-process path.
        /// </remarks>
        private static bool InProcessOptIn
        {
            get
            {
#if DEBUG
                var v = Environment.GetEnvironmentVariable("ACCESSIBLETRADER_SCRIPT_IN_PROCESS");
                return !string.IsNullOrEmpty(v)
                    && (v.Equals("1", StringComparison.Ordinal)
                     || v.Equals("true", StringComparison.OrdinalIgnoreCase));
#else
                return false;
#endif
            }
        }

        private static readonly string[] _requiredUsings =
        {
            "System",
            "System.Collections.Generic",
            "System.Linq",
            "AccessibleTrader.Sdk.Interfaces",
            "AccessibleTrader.Sdk.Models",
        };

        public async Task<CompileResult> CompileIndicatorAsync(string code)
        {
            if (string.IsNullOrWhiteSpace(code))
                return new CompileResult(false, null, new[] { "Code is empty." });

            // Cheap lexical pre-flight (unsafe, stackalloc, DllImport) before paying
            // for a full compile.
            var lexical = PreflightSandboxLexical(code);
            if (lexical.Length > 0)
                return new CompileResult(false, null, lexical);

            // Wrap in using directives + namespace if the user hasn't
            string fullCode = BuildWrappedCode(code);

            try
            {
                // Resolve permitted references. In the split .NET BCL the
                // types an indicator reasonably uses live across multiple
                // assemblies: Dictionary in System.Collections, arrays and
                // Span in System.Runtime, IEnumerable in System.Linq etc.
                // We pin a few via typeof(...).Assembly and then additionally
                // scan the AppDomain for anything else the running host has
                // already loaded. See BuildSystemReferences() for the rule.
                var references = new List<MetadataReference>
                {
                    MetadataReference.CreateFromFile(typeof(object).Assembly.Location),                                 // System.Private.CoreLib
                    MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location),                            // System.Linq
                    MetadataReference.CreateFromFile(typeof(System.Collections.Generic.Dictionary<,>).Assembly.Location),// System.Collections
                    MetadataReference.CreateFromFile(typeof(ICustomIndicator).Assembly.Location),                      // AccessibleTrader.Sdk
                    MetadataReference.CreateFromFile(typeof(System.Numerics.Vector2).Assembly.Location),               // System.Numerics.Vectors
                };

                // Try to add Skender if present in the default ALC
                try
                {
                    var skenderAsm = AppDomain.CurrentDomain.GetAssemblies()
                        .FirstOrDefault(a => a.GetName().Name == "Skender.Stock.Indicators");
                    if (skenderAsm != null)
                        references.Add(MetadataReference.CreateFromFile(skenderAsm.Location));
                }
                catch { /* optional dependency */ }

                // Scan AppDomain for every System.* / Microsoft.* assembly
                // and include it. This is deliberately broad — the semantic
                // sandbox walker (AnalyzeSandbox) rejects types in blocked
                // namespaces regardless of whether they're in the reference
                // set, so widening the reference surface doesn't weaken
                // security.
                var already = new HashSet<string>(references
                    .Select(r => (r as PortableExecutableReference)?.FilePath ?? "")
                    .Where(s => s.Length > 0),
                    StringComparer.OrdinalIgnoreCase);
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try
                    {
                        if (string.IsNullOrEmpty(asm.Location)) continue;
                        if (already.Contains(asm.Location)) continue;
                        var name = asm.GetName().Name ?? "";
                        if (name.StartsWith("System.", StringComparison.Ordinal)
                            || name.StartsWith("Microsoft.", StringComparison.Ordinal)
                            || name == "netstandard"
                            || name == "mscorlib")
                        {
                            references.Add(MetadataReference.CreateFromFile(asm.Location));
                            already.Add(asm.Location);
                        }
                    }
                    catch { /* skip inaccessible */ }
                }

                var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest);
                var syntaxTree   = CSharpSyntaxTree.ParseText(fullCode, parseOptions);

                var options = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                    .WithOptimizationLevel(OptimizationLevel.Release)
                    .WithNullableContextOptions(NullableContextOptions.Enable);

                var compilation = CSharpCompilation.Create(
                    $"CustomIndicator_{Guid.NewGuid():N}",
                    new[] { syntaxTree },
                    references,
                    options);

                // Semantic sandbox pass — rejects any call-site reference to a
                // blocked namespace/type/member. Runs before Emit so we never
                // produce IL for malicious user code.
                var sandboxErrors = AnalyzeSandbox(compilation, syntaxTree);
                if (sandboxErrors.Length > 0)
                    return new CompileResult(false, null, sandboxErrors);

                using var ms = new System.IO.MemoryStream();
                var emitResult = compilation.Emit(ms);

                if (!emitResult.Success)
                {
                    var errors = emitResult.Diagnostics
                        .Where(d => d.Severity == DiagnosticSeverity.Error)
                        .Select(d => $"Line {d.Location.GetLineSpan().StartLinePosition.Line + 1}: {d.GetMessage()}")
                        .ToArray();
                    return new CompileResult(false, null, errors);
                }

                // Default path: spawn the worker process and hand it the
                // compiled assembly bytes. The returned ICustomIndicator is
                // a proxy that dispatches Calculate() over stdio.
                //
                // Dev/debug path (ACCESSIBLETRADER_SCRIPT_IN_PROCESS=1):
                // load into a collectible ALC in-process so breakpoints hit.
                // The in-process path is strictly weaker security-wise —
                // a sandbox escape runs in the host — so it emits a loud
                // warning in the returned errors array as a "harmless but
                // visible" signal.
                var assemblyBytes = ms.ToArray();
                if (InProcessOptIn)
                {
                    return LoadIndicatorInProcess(assemblyBytes);
                }
                return await LoadIndicatorOutOfProcessAsync(assemblyBytes).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                return new CompileResult(false, null, new[] { ex.Message });
            }
        }

        /// <summary>
        /// Legacy in-process load. Only reachable via
        /// <c>ACCESSIBLETRADER_SCRIPT_IN_PROCESS=1</c>. Keeps the ALC
        /// collectible and tracks it so <see cref="UnloadScript"/> can
        /// unload it explicitly.
        /// </summary>
        private CompileResult LoadIndicatorInProcess(byte[] assemblyBytes)
        {
            using var ms = new MemoryStream(assemblyBytes);
            var alc = new AssemblyLoadContext($"script_{Guid.NewGuid():N}", isCollectible: true);
            var assembly = alc.LoadFromStream(ms);

            var indicatorType = assembly.GetTypes()
                .FirstOrDefault(t => !t.IsAbstract && !t.IsInterface
                                    && typeof(ICustomIndicator).IsAssignableFrom(t));

            if (indicatorType == null)
                return new CompileResult(false, null, new[] { "No class implementing ICustomIndicator found in the script." });

            var instance = (ICustomIndicator?)Activator.CreateInstance(indicatorType);
            if (instance == null)
                return new CompileResult(false, null, new[] { "Failed to instantiate indicator class (ensure it has a public parameterless constructor)." });

            _contexts[instance.Id] = alc;
            return new CompileResult(true, instance, Array.Empty<string>());
        }

        /// <summary>
        /// Default out-of-process load. Spawns
        /// <c>AccessibleTrader.ScriptWorker</c> via the configured
        /// <see cref="IScriptWorkerLauncher"/>, sends the assembly, waits
        /// for the worker's <c>Ready</c> response, and returns a proxy that
        /// forwards <c>Calculate</c> calls over stdio.
        /// </summary>
        private async Task<CompileResult> LoadIndicatorOutOfProcessAsync(byte[] assemblyBytes)
        {
            string workerPath;
            try
            {
                workerPath = _workerPathResolver();

                // The Android launcher hosts the worker inside a bound
                // Service rather than a separate executable, so it
                // ignores workerPath entirely. Skip the File.Exists
                // check on Android — otherwise a dev build would dead-
                // end here with a bogus "worker not found" message.
                if (!OperatingSystem.IsAndroid() && !File.Exists(workerPath))
                    return new CompileResult(false, null, new[]
                    {
                        $"ScriptWorker executable not found at '{workerPath}'. " +
                        "Run a Release build of AccessibleTrader.ScriptWorker or opt into the in-process dev path " +
                        "with ACCESSIBLETRADER_SCRIPT_IN_PROCESS=1."
                    });
            }
            catch (Exception ex)
            {
                return new CompileResult(false, null, new[] { "ScriptWorker path resolution failed: " + ex.Message });
            }

            var scriptId = Guid.NewGuid().ToString("N");
            OutOfProcessScriptHost? host = null;
            try
            {
                host = await OutOfProcessScriptHost.StartAsync(
                    _workerLauncher, workerPath, assemblyBytes, scriptId).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                if (host != null) await host.DisposeAsync().ConfigureAwait(false);
                return new CompileResult(false, null, new[] { "ScriptWorker failed to start: " + ex.Message });
            }

            var proxy = new OutOfProcessIndicator(host);
            _outOfProcessHosts[proxy.Id] = host;
            return new CompileResult(true, proxy, Array.Empty<string>());
        }

        public async Task<CompileStrategyResult> CompileStrategyAsync(string code)
        {
            if (string.IsNullOrWhiteSpace(code))
                return new CompileStrategyResult(false, null, new[] { "Code is empty." });

            var lexical = PreflightSandboxLexical(code);
            if (lexical.Length > 0)
                return new CompileStrategyResult(false, null, lexical);

            string fullCode = BuildWrappedCode(code);

            try
            {
                // Same broadened reference strategy as CompileIndicatorAsync —
                // pin the common BCL types and then scan AppDomain for any
                // System.* / Microsoft.* / netstandard / mscorlib assembly
                // loaded at compile time. Semantic sandbox walker still
                // rejects blocked namespaces independently of the reference
                // surface.
                var references = new List<MetadataReference>
                {
                    MetadataReference.CreateFromFile(typeof(object).Assembly.Location),                                 // System.Private.CoreLib
                    MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location),                            // System.Linq
                    MetadataReference.CreateFromFile(typeof(System.Collections.Generic.Dictionary<,>).Assembly.Location),// System.Collections
                    MetadataReference.CreateFromFile(typeof(ICustomIndicator).Assembly.Location),                      // AccessibleTrader.Sdk
                    MetadataReference.CreateFromFile(typeof(RoslynScriptingService).Assembly.Location),                // AccessibleTrader.Core
                    MetadataReference.CreateFromFile(typeof(System.Numerics.Vector2).Assembly.Location),               // System.Numerics.Vectors
                };

                var already = new HashSet<string>(references
                    .Select(r => (r as PortableExecutableReference)?.FilePath ?? "")
                    .Where(s => s.Length > 0),
                    StringComparer.OrdinalIgnoreCase);
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try
                    {
                        if (string.IsNullOrEmpty(asm.Location)) continue;
                        if (already.Contains(asm.Location)) continue;
                        var name = asm.GetName().Name ?? "";
                        if (name.StartsWith("System.", StringComparison.Ordinal)
                            || name.StartsWith("Microsoft.", StringComparison.Ordinal)
                            || name == "netstandard"
                            || name == "mscorlib")
                        {
                            references.Add(MetadataReference.CreateFromFile(asm.Location));
                            already.Add(asm.Location);
                        }
                    }
                    catch { /* skip */ }
                }

                var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest);
                var syntaxTree   = CSharpSyntaxTree.ParseText(fullCode, parseOptions);

                var options = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                    .WithOptimizationLevel(OptimizationLevel.Release)
                    .WithNullableContextOptions(NullableContextOptions.Enable);

                var compilation = CSharpCompilation.Create(
                    $"CustomStrategy_{Guid.NewGuid():N}",
                    new[] { syntaxTree },
                    references,
                    options);

                var sandboxErrors = AnalyzeSandbox(compilation, syntaxTree);
                if (sandboxErrors.Length > 0)
                    return new CompileStrategyResult(false, null, sandboxErrors);

                using var ms = new System.IO.MemoryStream();
                var emitResult = compilation.Emit(ms);

                if (!emitResult.Success)
                {
                    var errors = emitResult.Diagnostics
                        .Where(d => d.Severity == DiagnosticSeverity.Error)
                        .Select(d => $"Line {d.Location.GetLineSpan().StartLinePosition.Line + 1}: {d.GetMessage()}")
                        .ToArray();
                    return new CompileStrategyResult(false, null, errors);
                }

                ms.Seek(0, System.IO.SeekOrigin.Begin);
                var alc      = new AssemblyLoadContext($"strategy_{Guid.NewGuid():N}", isCollectible: true);
                var assembly = alc.LoadFromStream(ms);

                var stratType = assembly.GetTypes()
                    .FirstOrDefault(t => !t.IsAbstract && !t.IsInterface
                                         && typeof(ITradingStrategy).IsAssignableFrom(t));

                if (stratType == null)
                    return new CompileStrategyResult(false, null, new[] { "No class implementing ITradingStrategy found in the script." });

                var instance = (ITradingStrategy?)Activator.CreateInstance(stratType);
                if (instance == null)
                    return new CompileStrategyResult(false, null, new[] { "Failed to instantiate strategy class." });

                _contexts[instance.Id] = alc;
                return new CompileStrategyResult(true, instance, Array.Empty<string>());
            }
            catch (Exception ex)
            {
                return new CompileStrategyResult(false, null, new[] { ex.Message });
            }
        }

        public async Task<ScriptResult> ExecuteSimpleAsync(string code, List<Ohlcv> data)
        {
            if (string.IsNullOrWhiteSpace(code))
                return new ScriptResult(false, new(), "Script code is empty.");

            var lexical = PreflightSandboxLexical(code);
            if (lexical.Length > 0)
                return new ScriptResult(false, new(), string.Join(" ", lexical));

            try
            {
                var options = ScriptOptions.Default
                    .WithReferences(typeof(Enumerable).Assembly, typeof(Ohlcv).Assembly)
                    .WithImports("System", "System.Collections.Generic", "System.Linq", "AccessibleTrader.Sdk.Models");

                // Semantic sandbox: run the same namespace/type/member walker on
                // the script's compilation. CSharpScript.Create builds a
                // Compilation internally; we pull it out via GetCompilation().
                var script  = CSharpScript.Create<object>(code, options, typeof(ScriptGlobals));
                var compilation = script.GetCompilation();
                foreach (var tree in compilation.SyntaxTrees)
                {
                    var sandboxErrors = AnalyzeSandbox(compilation, tree);
                    if (sandboxErrors.Length > 0)
                        return new ScriptResult(false, new(), string.Join(" ", sandboxErrors));
                }

                var globals = new ScriptGlobals { Data = data };
                var result  = await script.RunAsync(globals).ConfigureAwait(false);

                if (result.ReturnValue is IEnumerable<double> doubles)
                    return new ScriptResult(true, doubles.Select(x => (double?)x).ToList());
                if (result.ReturnValue is IEnumerable<double?> nullables)
                    return new ScriptResult(true, nullables.ToList());

                return new ScriptResult(false, new(),
                    $"Script must return IEnumerable<double>. Got: {result.ReturnValue?.GetType().Name ?? "null"}");
            }
            catch (CompilationErrorException ex)
            {
                string errors = string.Join(" ", ex.Diagnostics.Select(d =>
                    $"L{d.Location.GetLineSpan().StartLinePosition.Line + 1}: {d.GetMessage()}"));
                return new ScriptResult(false, new(), errors);
            }
            catch (Exception ex)
            {
                return new ScriptResult(false, new(), ex.Message);
            }
        }

        /// <summary>
        /// Release resources for a removed script. Unloads the in-process
        /// ALC if the legacy dev path was used, and/or disposes the
        /// out-of-process worker host (which sends <c>Shutdown</c> and
        /// kills the worker after the grace window).
        /// </summary>
        public void UnloadScript(string indicatorId)
        {
            if (_contexts.TryGetValue(indicatorId, out var alc))
            {
                _contexts.Remove(indicatorId);
                alc.Unload();
            }
            if (_outOfProcessHosts.TryGetValue(indicatorId, out var host))
            {
                _outOfProcessHosts.Remove(indicatorId);
                // Fire-and-forget; we don't want UnloadScript to block the
                // UI thread waiting for a worker to drain. The host's
                // DisposeAsync does the graceful shutdown + SIGKILL fallback
                // itself.
                _ = host.DisposeAsync().AsTask();
            }
        }

        // Namespace-level blocks. Any symbol whose containing namespace starts with
        // one of these prefixes is rejected. Chosen to close off filesystem, network,
        // process control, reflection/emit, native interop, and the assembly loader.
        private static readonly string[] _blockedNamespaces = new[]
        {
            "System.IO",
            "System.Net",
            "System.Diagnostics",                 // Process, EventLog, Debug.Assert etc.
            "System.Reflection",                  // covers Emit, Metadata, PortableExecutable
            "System.Runtime.InteropServices",
            "System.Runtime.Loader",
            "System.Security",                    // permissions, principals, DPAPI
            "System.Threading.Channels",          // overkill for indicator code
            "System.Xml",                         // XML parsers with entity-expansion risk
            "Microsoft.Win32",                    // registry, Task Scheduler
            "Microsoft.CodeAnalysis",             // no nested Roslyn inside scripts
        };

        // Specific type/member blocks that survive even though the top-level
        // namespace (e.g. System) is allowed. Matches "ContainingType.MethodName"
        // on the resolved symbol.
        private static readonly HashSet<string> _blockedMembers = new(StringComparer.Ordinal)
        {
            // String-keyed type lookup defeats every namespace filter if allowed.
            "System.Type.GetType",
            "System.Type.InvokeMember",
            "System.Type.GetMethod",
            "System.Type.GetMethods",
            "System.Type.GetField",
            "System.Type.GetFields",
            "System.Type.GetProperty",
            "System.Type.GetProperties",
            "System.Type.GetConstructor",
            "System.Type.GetMembers",
            "System.Type.GetMember",
            "System.Type.MakeGenericType",
            "System.Type.MakeArrayType",
            "System.Activator.CreateInstance",
            "System.Activator.CreateInstanceFrom",
            "System.Delegate.CreateDelegate",
            "System.AppDomain.Load",
            "System.AppDomain.CreateInstance",
            "System.AppDomain.CreateInstanceAndUnwrap",
            "System.GC.GetTotalMemory",           // minor infoleak, harmless to block
            "System.Environment.Exit",
            "System.Environment.FailFast",
        };

        // Whole types that shouldn't be touched at all. These live in otherwise
        // allowed namespaces (most in "System") so namespace-level filtering
        // misses them.
        private static readonly HashSet<string> _blockedTypes = new(StringComparer.Ordinal)
        {
            "System.AppDomain",
            "System.GCHandle",
            "System.Runtime.CompilerServices.Unsafe",
            "System.Runtime.CompilerServices.RuntimeHelpers",
            "System.Buffers.NativeMemory",
        };

        private static string BuildWrappedCode(string code)
        {
            // If code already has 'using' or 'namespace' declarations treat as full code.
            if (code.TrimStart().StartsWith("using ", StringComparison.Ordinal)
                || code.Contains("namespace "))
                return code;

            var sb = new StringBuilder();
            foreach (var u in _requiredUsings) sb.AppendLine($"using {u};");
            sb.AppendLine();
            sb.AppendLine("namespace CustomIndicators {");
            sb.AppendLine(code);
            sb.AppendLine("}");
            return sb.ToString();
        }

        /// <summary>
        /// Lexical pre-flight: reject `unsafe`, `stackalloc`, and `[DllImport]`
        /// before we even spin up the compiler. These are quick to catch on
        /// the raw source and don't need semantic resolution.
        /// </summary>
        internal static string[] PreflightSandboxLexical(string code)
        {
            var violations = new List<string>();
            if (System.Text.RegularExpressions.Regex.IsMatch(code, @"\bunsafe\b"))
                violations.Add("Blocked: 'unsafe' code is not allowed in user scripts.");
            if (System.Text.RegularExpressions.Regex.IsMatch(code, @"\bstackalloc\b"))
                violations.Add("Blocked: 'stackalloc' is not allowed in user scripts.");
            if (System.Text.RegularExpressions.Regex.IsMatch(code, @"\bfixed\s*\("))
                violations.Add("Blocked: 'fixed' pointer blocks are not allowed in user scripts.");
            if (code.Contains("DllImport", StringComparison.Ordinal))
                violations.Add("Blocked: native interop (DllImport) is not allowed in user scripts.");
            if (code.Contains("LibraryImport", StringComparison.Ordinal))
                violations.Add("Blocked: native interop (LibraryImport) is not allowed in user scripts.");
            return violations.ToArray();
        }

        /// <summary>
        /// Semantic sandbox validation. Walks the syntax tree using the bound
        /// semantic model and rejects references to any type in a blocked
        /// namespace, any explicitly blocked type, or any blocked member. This
        /// is stronger than source-string matching because it works on resolved
        /// symbols — it sees through usings, aliases, whitespace tricks, and
        /// fully-qualified names alike.
        /// </summary>
        internal static string[] AnalyzeSandbox(Compilation compilation, SyntaxTree tree)
        {
            var model = compilation.GetSemanticModel(tree);
            var walker = new SandboxWalker(model);
            walker.Visit(tree.GetRoot());
            return walker.Violations.ToArray();
        }

        private sealed class SandboxWalker : CSharpSyntaxWalker
        {
            private readonly SemanticModel _model;
            public List<string> Violations { get; } = new();

            public SandboxWalker(SemanticModel model) : base(SyntaxWalkerDepth.Node) { _model = model; }

            public override void VisitIdentifierName(IdentifierNameSyntax node)
            {
                CheckSymbol(_model.GetSymbolInfo(node).Symbol, node);
                base.VisitIdentifierName(node);
            }

            public override void VisitGenericName(GenericNameSyntax node)
            {
                CheckSymbol(_model.GetSymbolInfo(node).Symbol, node);
                base.VisitGenericName(node);
            }

            public override void VisitQualifiedName(QualifiedNameSyntax node)
            {
                CheckSymbol(_model.GetSymbolInfo(node).Symbol, node);
                base.VisitQualifiedName(node);
            }

            public override void VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
            {
                CheckSymbol(_model.GetSymbolInfo(node).Symbol, node);
                base.VisitMemberAccessExpression(node);
            }

            public override void VisitInvocationExpression(InvocationExpressionSyntax node)
            {
                CheckSymbol(_model.GetSymbolInfo(node).Symbol, node);
                base.VisitInvocationExpression(node);
            }

            public override void VisitObjectCreationExpression(ObjectCreationExpressionSyntax node)
            {
                var typeSymbol = _model.GetTypeInfo(node).Type;
                if (typeSymbol != null) CheckType(typeSymbol, node);
                base.VisitObjectCreationExpression(node);
            }

            public override void VisitAttribute(AttributeSyntax node)
            {
                CheckSymbol(_model.GetSymbolInfo(node).Symbol, node);
                base.VisitAttribute(node);
            }

            private void CheckSymbol(ISymbol? symbol, SyntaxNode node)
            {
                if (symbol == null) return;

                // Resolve to a containing type for methods/fields/props/events,
                // or treat the symbol itself as the type if it is one.
                ITypeSymbol? containingType = symbol switch
                {
                    ITypeSymbol t                => t,
                    IMethodSymbol m              => m.ContainingType,
                    IFieldSymbol f               => f.ContainingType,
                    IPropertySymbol p            => p.ContainingType,
                    IEventSymbol e               => e.ContainingType,
                    _                            => null
                };

                if (containingType != null) CheckType(containingType, node);

                // Blocked-member check (e.g. Type.GetType, Activator.CreateInstance).
                if (symbol is IMethodSymbol method)
                {
                    var containing = method.ContainingType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat.WithGlobalNamespaceStyle(SymbolDisplayGlobalNamespaceStyle.Omitted));
                    if (containing != null)
                    {
                        var key = $"{containing}.{method.Name}";
                        if (_blockedMembers.Contains(key))
                            Report(node, $"member '{key}' is not allowed in user scripts.");
                    }
                }
            }

            private void CheckType(ITypeSymbol type, SyntaxNode node)
            {
                // Strip nullable / array / pointer wrappers to the element type
                // so "File[]" or "File?" still flag as blocked.
                var element = type;
                while (element is IArrayTypeSymbol arr) element = arr.ElementType;
                if (element is INamedTypeSymbol named && named.IsGenericType)
                {
                    foreach (var arg in named.TypeArguments) CheckType(arg, node);
                }

                var fqName = element.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat.WithGlobalNamespaceStyle(SymbolDisplayGlobalNamespaceStyle.Omitted));
                if (_blockedTypes.Contains(fqName))
                {
                    Report(node, $"type '{fqName}' is not allowed in user scripts.");
                    return;
                }

                var ns = element.ContainingNamespace?.ToDisplayString() ?? "";
                foreach (var blocked in _blockedNamespaces)
                {
                    if (ns == blocked || ns.StartsWith(blocked + ".", StringComparison.Ordinal))
                    {
                        Report(node, $"type '{fqName}' is in blocked namespace '{ns}'.");
                        return;
                    }
                }
            }

            private void Report(SyntaxNode node, string message)
            {
                var line = node.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                var msg = $"Line {line}: {message}";
                // Suppress duplicates — walking the tree visits the same node
                // through several overrides (MemberAccess → Identifier etc).
                if (!Violations.Contains(msg)) Violations.Add(msg);
            }
        }
    }
}
