using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services
{
    public interface IScriptingService
    {
        Task<ScriptResult> ExecuteScriptAsync(string code, List<Ohlcv> data);
    }

    public record ScriptResult(bool Success, List<double?> Data, string ErrorMessage = "");

    public class ScriptGlobals
    {
        public List<Ohlcv> Data { get; set; } = new();
        // Helpers for easier scripting
        public IEnumerable<double> Close => Data.Select(x => x.Close);
        public IEnumerable<double> Open => Data.Select(x => x.Open);
        public IEnumerable<double> High => Data.Select(x => x.High);
        public IEnumerable<double> Low => Data.Select(x => x.Low);
        public IEnumerable<double> Volume => Data.Select(x => x.Volume);
    }

    /// <summary>
    /// C# scripting engine for user-defined indicator calculations.
    /// Scripts receive OHLCV data via <see cref="ScriptGlobals"/> and must return an
    /// <c>IEnumerable&lt;double&gt;</c> or <c>IEnumerable&lt;double?&gt;</c> to be plotted as a series component.
    /// </summary>
    /// <remarks>
    /// STUB: No UI entry point — Phase 10 scripting roadmap. Wire to ScriptEditorModal (Alt+,).
    /// </remarks>
    public class ScriptingService : IScriptingService
    {
        // Host policy, when the head supplies one — same wall as
        // RoslynScriptingService.ThrowIfScriptsDisabled, because this class runs
        // user code IN PROCESS (CSharpScript, no sandbox at all).
        private readonly DemoPolicy? _demo;

        public ScriptingService(DemoPolicy? demo = null) => _demo = demo;

        public async Task<ScriptResult> ExecuteScriptAsync(string code, List<Ohlcv> data)
        {
            if (_demo != null && !_demo.AllowCustomScripts)
                throw new InvalidOperationException(
                    "Custom scripts are disabled on this host: this path runs user code " +
                    "in-process, which is a desktop-only (Full mode) capability.");

            if (string.IsNullOrWhiteSpace(code))
                return new ScriptResult(false, new(), "Script code is empty.");

            try
            {
                var options = ScriptOptions.Default
                    .WithReferences(typeof(Enumerable).Assembly, typeof(Ohlcv).Assembly)
                    .WithImports("System", "System.Collections.Generic", "System.Linq", "AccessibleTrader.Sdk.Models");

                var globals = new ScriptGlobals { Data = data };
                
                // Expecting the script to return an IEnumerable<double> or IEnumerable<double?>
                var script = CSharpScript.Create<object>(code, options, typeof(ScriptGlobals));
                var result = await script.RunAsync(globals).ConfigureAwait(false);

                if (result.ReturnValue is IEnumerable<double> doubleList)
                {
                    return new ScriptResult(true, doubleList.Select(x => (double?)x).ToList());
                }
                else if (result.ReturnValue is IEnumerable<double?> nullableDoubleList)
                {
                    return new ScriptResult(true, nullableDoubleList.ToList());
                }
                else
                {
                    return new ScriptResult(false, new(), $"Script must return an IEnumerable of doubles. Current return type: {result.ReturnValue?.GetType().Name ?? "null"}");
                }
            }
            catch (CompilationErrorException ex)
            {
                string errors = string.Join(". ", ex.Diagnostics.Select(d => $"Line {d.Location.GetLineSpan().StartLinePosition.Line + 1}: {d.GetMessage()}"));
                return new ScriptResult(false, new(), errors);
            }
            catch (Exception ex)
            {
                return new ScriptResult(false, new(), ex.Message);
            }
        }
    }
}
