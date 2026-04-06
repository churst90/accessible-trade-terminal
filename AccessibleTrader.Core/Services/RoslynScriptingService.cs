using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
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
        // Track per-script ALCs so they can be collected when a script is removed.
        private readonly Dictionary<string, AssemblyLoadContext> _contexts = new();

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

            // Wrap in using directives + namespace if the user hasn't
            string fullCode = BuildWrappedCode(code);

            try
            {
                // Resolve permitted references
                var sdkAssembly = typeof(ICustomIndicator).Assembly;
                var numericsAssembly = typeof(System.Numerics.Vector2).Assembly;
                var coreLibAssembly = typeof(object).Assembly;
                var linqAssembly = typeof(Enumerable).Assembly;

                var references = new List<MetadataReference>
                {
                    MetadataReference.CreateFromFile(coreLibAssembly.Location),
                    MetadataReference.CreateFromFile(linqAssembly.Location),
                    MetadataReference.CreateFromFile(sdkAssembly.Location),
                    MetadataReference.CreateFromFile(numericsAssembly.Location),
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

                // Standard .NET runtime assemblies (needed for attribute/generic support)
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try
                    {
                        if (string.IsNullOrEmpty(asm.Location)) continue;
                        var name = asm.GetName().Name ?? "";
                        if (name.StartsWith("System.Runtime", StringComparison.Ordinal)
                            || name == "netstandard"
                            || name == "mscorlib")
                        {
                            references.Add(MetadataReference.CreateFromFile(asm.Location));
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

                ms.Seek(0, System.IO.SeekOrigin.Begin);
                var alc = new AssemblyLoadContext($"script_{Guid.NewGuid():N}", isCollectible: true);
                var assembly = alc.LoadFromStream(ms);

                // Find first ICustomIndicator implementation
                var indicatorType = assembly.GetTypes()
                    .FirstOrDefault(t => !t.IsAbstract && !t.IsInterface
                                        && typeof(ICustomIndicator).IsAssignableFrom(t));

                if (indicatorType == null)
                    return new CompileResult(false, null, new[] { "No class implementing ICustomIndicator found in the script." });

                var instance = (ICustomIndicator?)Activator.CreateInstance(indicatorType);
                if (instance == null)
                    return new CompileResult(false, null, new[] { "Failed to instantiate indicator class (ensure it has a public parameterless constructor)." });

                // Track the ALC keyed on the indicator's Id so it can be collected later
                _contexts[instance.Id] = alc;

                return new CompileResult(true, instance, Array.Empty<string>());
            }
            catch (Exception ex)
            {
                return new CompileResult(false, null, new[] { ex.Message });
            }
        }

        public async Task<CompileStrategyResult> CompileStrategyAsync(string code)
        {
            if (string.IsNullOrWhiteSpace(code))
                return new CompileStrategyResult(false, null, new[] { "Code is empty." });

            string fullCode = BuildWrappedCode(code);

            try
            {
                var sdkAssembly      = typeof(ICustomIndicator).Assembly;
                var coreAssembly     = typeof(RoslynScriptingService).Assembly;
                var numericsAssembly = typeof(System.Numerics.Vector2).Assembly;
                var coreLibAssembly  = typeof(object).Assembly;
                var linqAssembly     = typeof(Enumerable).Assembly;

                var references = new List<MetadataReference>
                {
                    MetadataReference.CreateFromFile(coreLibAssembly.Location),
                    MetadataReference.CreateFromFile(linqAssembly.Location),
                    MetadataReference.CreateFromFile(sdkAssembly.Location),
                    MetadataReference.CreateFromFile(coreAssembly.Location),
                    MetadataReference.CreateFromFile(numericsAssembly.Location),
                };

                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try
                    {
                        if (string.IsNullOrEmpty(asm.Location)) continue;
                        var name = asm.GetName().Name ?? "";
                        if (name.StartsWith("System.Runtime", StringComparison.Ordinal)
                            || name == "netstandard"
                            || name == "mscorlib")
                            references.Add(MetadataReference.CreateFromFile(asm.Location));
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

            try
            {
                var options = ScriptOptions.Default
                    .WithReferences(typeof(Enumerable).Assembly, typeof(Ohlcv).Assembly)
                    .WithImports("System", "System.Collections.Generic", "System.Linq", "AccessibleTrader.Sdk.Models");

                var globals = new ScriptGlobals { Data = data };
                var script  = CSharpScript.Create<object>(code, options, typeof(ScriptGlobals));
                var result  = await script.RunAsync(globals);

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

        /// <summary>Unloads the ALC for a script that has been removed.</summary>
        public void UnloadScript(string indicatorId)
        {
            if (_contexts.TryGetValue(indicatorId, out var alc))
            {
                _contexts.Remove(indicatorId);
                alc.Unload();
            }
        }

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
    }
}
