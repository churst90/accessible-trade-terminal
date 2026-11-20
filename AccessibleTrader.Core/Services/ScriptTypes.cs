using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services
{
    /// <summary>
    /// The answer from <see cref="IRoslynScriptingService.ExecuteSimpleAsync"/>: the series a
    /// one-expression script produced, or why it produced none.
    /// </summary>
    public record ScriptResult(bool Success, List<double?> Data, string ErrorMessage = "");

    /// <summary>What a one-expression script can read: the bars and their columns.</summary>
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
}
