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
using AccessibleTrader.Core.Services.Strategies;
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
    /// Sandbox policy — the compile-time reference set is a fixed declared list
    /// (<c>_frameworkReferenceNames</c> plus AccessibleTrader.Sdk, AccessibleTrader.Core for
    /// strategies, and Skender.Stock.Indicators), identical on every host.
    /// Blocked: System.IO, System.Net, System.Reflection (any emit path) — see
    /// <c>_blockedNamespaces</c> for the full policy, which the semantic walker enforces
    /// independently of what is referenced.
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

        // Host policy, when the head supplies one. Compiling user code is the
        // "server-side Roslyn = RCE" line in DemoPolicy: the Razor @if that
        // hides the scripts modal is presentation, THIS is the enforcement.
        private readonly DemoPolicy? _demo;

        public RoslynScriptingService()
            : this(CreateDefaultLauncher(), DefaultWorkerPathResolver)
        {
        }

        public RoslynScriptingService(IScriptWorkerLauncher workerLauncher, Func<string> workerPathResolver,
            DemoPolicy? demo = null)
        {
            _workerLauncher = workerLauncher ?? throw new ArgumentNullException(nameof(workerLauncher));
            _workerPathResolver = workerPathResolver ?? throw new ArgumentNullException(nameof(workerPathResolver));
            _demo = demo;
        }

        /// <summary>
        /// Every compile/execute entry point starts here. A Blazor Server refactor
        /// that dispatches an event to a never-rendered component, or any new
        /// caller that skips the UI entirely, must hit this wall — not the sandbox,
        /// which exists for ACCIDENTS in trusted-user code, not for hostile tenants.
        /// </summary>
        private void ThrowIfScriptsDisabled()
        {
            if (_demo != null && !_demo.AllowCustomScripts)
                throw new InvalidOperationException(
                    "Custom scripts are disabled on this host: compiling user code runs it " +
                    "server-side, which is a desktop-only (Full mode) capability.");
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

        /// <summary>
        /// The framework assemblies a user script may compile against, declared by name and
        /// resolved from the directory that holds <c>System.Private.CoreLib</c>.
        ///
        /// <para>
        /// Until 2026-08-25 this set was built by scanning
        /// <c>AppDomain.CurrentDomain.GetAssemblies()</c> for anything called <c>System.*</c> or
        /// <c>Microsoft.*</c>, which made <b>what a script can even name</b> a function of what
        /// the host happened to have loaded when the user pressed Compile. The desktop head, the
        /// WebHost and a unit-test process each had a different answer, and so did two runs of the
        /// same head depending on which features the user had opened first. Two of the four
        /// escapes found in that audit were invisible in a bare test process for exactly this
        /// reason — <c>dynamic</c> compiled only because <c>Microsoft.CSharp</c> was loaded, and
        /// <c>Console.WriteLine</c> only because <c>System.Console</c> was — so the probe had to
        /// force-load both to see the real answer. A boundary that moves with unrelated feature
        /// work is not a boundary.
        /// </para>
        ///
        /// <para>
        /// The reference set has never been the wall — <see cref="SandboxWalker"/> is, and it
        /// refuses blocked namespaces whether or not their assembly is referenced. So this list is
        /// not trying to be minimal; it is trying to be <i>the same everywhere</i>. Where breadth
        /// is a free choice it buys diagnostic quality: <c>System.Console</c> is referenced
        /// deliberately so a script's <c>Console.WriteLine</c> is refused with "type
        /// 'System.Console' is not allowed in user scripts" rather than with a bare
        /// "the name 'Console' does not exist". <c>Microsoft.CSharp</c> is the one deliberate
        /// omission: nothing legitimate in an indicator needs the dynamic binder, the walker
        /// refuses <c>dynamic</c> before Emit regardless, and leaving it out means the escape
        /// cannot reach the emit step even if that rule is ever weakened.
        /// </para>
        /// </summary>
        private static readonly string[] _frameworkReferenceNames =
        {
            "System.Runtime",                  // the facade nearly every BCL type is forwarded through
            "netstandard",                     // scripts pasted from netstandard-targeting sources
            "System.Collections",              // List<T>, Dictionary<K,V>, HashSet<T>
            "System.Collections.Immutable",    // WorkspaceState.ActiveSeries is an ImmutableList
            "System.ObjectModel",              // ReadOnlyCollection, ObservableCollection
            "System.Linq",
            "System.Memory",                   // Span/ReadOnlySpan — Calculate's own signature
            "System.Numerics.Vectors",
            "System.Runtime.Extensions",       // legacy facade some pasted sources still name
            "System.Text.RegularExpressions",  // symbol/pattern matching in strategy code
            "System.Console",                  // referenced ONLY so the refusal reads properly
        };

        /// <summary>Exposed for the reference-determinism guard in the test suite.</summary>
        internal static IReadOnlyList<string> FrameworkReferenceNames => _frameworkReferenceNames;

        /// <summary>
        /// Thrown when the framework reference set cannot be resolved from disk. Surfaced to the
        /// script author as a plain error rather than letting Roslyn report a wall of CS0518
        /// "predefined type is not defined" diagnostics that read as a bug in their script.
        /// </summary>
        internal sealed class ReferenceSetUnavailableException : Exception
        {
            public ReferenceSetUnavailableException(string message) : base(message) { }
        }

        /// <summary>
        /// Builds the compile-time reference set. Identical on every host, because every entry is
        /// either pinned by <c>typeof</c> or resolved by name from the runtime directory — never
        /// from the list of assemblies this process happens to have loaded.
        /// </summary>
        /// <param name="includeHostCore">
        /// Strategy scripts additionally compile against AccessibleTrader.Core, because
        /// <c>BaseStrategy</c> and the strategy helper types live there. Indicators do not.
        /// </param>
        internal static List<MetadataReference> BuildReferences(bool includeHostCore)
        {
            var references = new List<MetadataReference>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void AddFile(string? path)
            {
                if (string.IsNullOrEmpty(path)) return;
                if (!seen.Add(path)) return;
                try { references.Add(MetadataReference.CreateFromFile(path)); }
                catch { /* unreadable file — the missing-reference check below reports the effect */ }
            }

            var coreLib = typeof(object).Assembly.Location;
            AddFile(coreLib);
            AddFile(typeof(ICustomIndicator).Assembly.Location);                 // AccessibleTrader.Sdk
            if (includeHostCore)
                AddFile(typeof(RoslynScriptingService).Assembly.Location);       // AccessibleTrader.Core

            var runtimeDir = string.IsNullOrEmpty(coreLib) ? "" : (Path.GetDirectoryName(coreLib) ?? "");
            if (runtimeDir.Length == 0 || !File.Exists(Path.Combine(runtimeDir, "System.Runtime.dll")))
                throw new ReferenceSetUnavailableException(
                    "Script compilation is unavailable on this build: the framework reference set " +
                    "could not be resolved from disk" +
                    (runtimeDir.Length == 0 ? " (the runtime has no file location — single-file publish?)." : $" under '{runtimeDir}'."));

            foreach (var name in _frameworkReferenceNames)
            {
                var path = Path.Combine(runtimeDir, name + ".dll");
                if (File.Exists(path)) AddFile(path);
            }

            // Skender is a hard package dependency of AccessibleTrader.Core, so it sits beside
            // Core's own binary on every head. Resolved from THERE rather than from the loaded
            // assembly list, for the same reason as everything else here.
            var coreDir = Path.GetDirectoryName(typeof(RoslynScriptingService).Assembly.Location);
            if (!string.IsNullOrEmpty(coreDir))
            {
                var skender = Path.Combine(coreDir, "Skender.Stock.Indicators.dll");
                if (File.Exists(skender)) AddFile(skender);
            }

            return references;
        }

        public async Task<CompileResult> CompileIndicatorAsync(string code)
        {
            ThrowIfScriptsDisabled();
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
                // The declared, host-independent reference set. See
                // _frameworkReferenceNames for why this is a fixed list rather than a
                // scan of whatever the running process has loaded.
                var references = BuildReferences(includeHostCore: false);

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
            ThrowIfScriptsDisabled();
            if (string.IsNullOrWhiteSpace(code))
                return new CompileStrategyResult(false, null, new[] { "Code is empty." });

            var lexical = PreflightSandboxLexical(code);
            if (lexical.Length > 0)
                return new CompileStrategyResult(false, null, lexical);

            string fullCode = BuildWrappedCode(code);

            try
            {
                // Same declared reference set as CompileIndicatorAsync, plus
                // AccessibleTrader.Core — BaseStrategy and the strategy helper types live there.
                var references = BuildReferences(includeHostCore: true);

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

                // The causality gate. A scripted INDICATOR is probed at registration and its
                // look-ahead components are simply not offered to the strategy builder; a strategy
                // has no equivalent half-measure — there is no "draws but does not trade" mode for
                // something whose entire output is orders — so the gate is here, at the one door
                // every script strategy comes through, and it refuses.
                var causality = ScriptStrategyCausalityProbe.Probe(instance);
                if (causality.Refused)
                {
                    alc.Unload();
                    return new CompileStrategyResult(false, null, causality.Findings.ToArray());
                }

                _contexts[instance.Id] = alc;
                // Notes ride back in Errors on a SUCCESSFUL result, which is the same channel the
                // in-process indicator path uses for its "this is the unsandboxed path" warning.
                return new CompileStrategyResult(true, instance, causality.Notes.ToArray());
            }
            catch (Exception ex)
            {
                return new CompileStrategyResult(false, null, new[] { ex.Message });
            }
        }

        public async Task<ScriptResult> ExecuteSimpleAsync(string code, List<Ohlcv> data)
        {
            ThrowIfScriptsDisabled();
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
            // Properties, which the member check could not see until 2026-08-25. These are the
            // three doors from a Type — which every script can obtain with typeof — onto the
            // reflection surface. Reaching THROUGH one was already refused (the objects live in
            // System.Reflection); holding one is now refused too, so the escape has to be caught
            // at its first token rather than at its second.
            "System.Type.Assembly",
            "System.Type.Module",
            "System.Type.TypeHandle",
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
            "System.Runtime.InteropServices.GCHandle",
            "System.Runtime.CompilerServices.Unsafe",
            "System.Runtime.CompilerServices.RuntimeHelpers",
            "System.Runtime.InteropServices.NativeMemory",

            // Reading the host's environment. Every one of these compiled and
            // reached the worker before they were listed: Environment.
            // GetEnvironmentVariables() hands a script the whole environment
            // block of the process that launched it, which on a machine that
            // configures credentials that way is the credentials. Neither type
            // has a legitimate use inside an indicator, whose entire input is
            // the bars and parameters it is given.
            //
            // Blocking the TYPE rather than listing members is deliberate:
            // Environment.CurrentDirectory is a property, and until this pass
            // the member list was consulted for methods only.
            "System.Environment",
            "System.AppContext",

            // System.Console lives in an allowed namespace, and in the worker
            // stdout IS the IPC pipe. Writes are neutralised at the worker end
            // (WorkerDispatcher.IsolateConsole redirects them to stderr, so a
            // script author's debug print still reaches the host log) — this
            // entry is the compile-time half, so the script is TOLD rather than
            // silently having its output swallowed.
            "System.Console",
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

            /// <summary>
            /// <c>dynamic</c> is refused outright, and it is the single most important rule in
            /// this class.
            ///
            /// <para>
            /// Every other rule here works on RESOLVED symbols — that is what makes the walker
            /// stronger than string matching. A dynamic member access has no resolved symbol at
            /// all: <c>GetSymbolInfo</c> returns null and every check below returns early. So
            /// <c>dynamic asm = typeof(object).Assembly; asm.GetType("System.Diagnostics.Process")</c>
            /// compiled clean and reached the worker, while the identical static code was refused
            /// on the very first token. One keyword turned the whole blocklist off. Verified by
            /// compiling it, not by reading the code — see
            /// <c>HostileScriptTests.Rejects_DynamicDispatch_*</c>.
            /// </para>
            ///
            /// <para>
            /// Whether that escape even compiled used to depend on whether <c>Microsoft.CSharp</c>
            /// happened to be loaded in the host process, because the reference set was built by
            /// scanning the running AppDomain. A security boundary that varies with the host's
            /// assembly load order is not a boundary; the set is a declared list now (see
            /// <c>_frameworkReferenceNames</c>) and this rule makes the answer the same
            /// everywhere regardless.
            /// </para>
            /// </summary>
            public override void Visit(SyntaxNode? node)
            {
                if (node is ExpressionSyntax expression)
                {
                    var info = _model.GetTypeInfo(expression);
                    if (info.Type?.TypeKind == TypeKind.Dynamic || info.ConvertedType?.TypeKind == TypeKind.Dynamic)
                        Report(node, "'dynamic' is not allowed in user scripts — it would bypass every " +
                                     "type and member restriction the sandbox applies.");
                }
                base.Visit(node);
            }

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
                var symbol = _model.GetSymbolInfo(node).Symbol;
                if (symbol == null) CheckUnresolvedName(node); else CheckSymbol(symbol, node);
                base.VisitQualifiedName(node);
            }

            public override void VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
            {
                var symbol = _model.GetSymbolInfo(node).Symbol;
                if (symbol == null) CheckUnresolvedName(node); else CheckSymbol(symbol, node);
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

            /// <summary>
            /// Diagnostic-quality rule, not a security rule: a fully-qualified name that resolves
            /// to NOTHING but reads as a blocked namespace gets the sandbox's own message.
            ///
            /// <para>
            /// Since the reference set became a fixed list, not every blocked assembly is in it —
            /// <c>System.Diagnostics.Process</c>, for instance, is reachable from no reference the
            /// script gets. Roslyn's answer for that is "the type or namespace name 'Process' does
            /// not exist in the namespace 'System.Diagnostics'", which reads to a script author
            /// like a typo on their part rather than a policy on ours. This is pure string
            /// matching and would be a weak rule on its own; it is safe here precisely because it
            /// only fires on a name that already failed to bind, so the strongest thing it can do
            /// is replace one refusal's wording with another's. It can never refuse a script that
            /// would otherwise have compiled.
            /// </para>
            /// </summary>
            private void CheckUnresolvedName(SyntaxNode node)
            {
                var text = node.ToString();
                foreach (var blocked in _blockedNamespaces)
                {
                    if (text.StartsWith(blocked + ".", StringComparison.Ordinal))
                    {
                        Report(node, $"type '{text}' is in blocked namespace '{blocked}'.");
                        return;
                    }
                }
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
                // Properties and fields count, not only methods: the list is a list of things a
                // script must not touch, and a property is touched by being read. It was
                // methods-only until 2026-08-25, which is why every entry in the list happened
                // to be a method — the shape of the check had quietly defined the policy.
                if (symbol is IMethodSymbol or IPropertySymbol or IFieldSymbol or IEventSymbol)
                {
                    var containing = symbol.ContainingType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat.WithGlobalNamespaceStyle(SymbolDisplayGlobalNamespaceStyle.Omitted));
                    if (containing != null)
                    {
                        var key = $"{containing}.{symbol.Name}";
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
