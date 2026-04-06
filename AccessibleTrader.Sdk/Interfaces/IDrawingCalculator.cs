using System.Collections.Generic;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Sdk.Interfaces
{
    /// <summary>
    /// Calculates the data arrays for a single drawing tool type.
    /// Register one implementation per <see cref="DrawingType"/>; <c>DrawingService</c>
    /// dispatches to the matching calculator via the DI-injected registry.
    /// Drop a new calculator class in Core/Services/Drawing/Calculators/ and register
    /// it in ServiceCollectionExtensions to add support for a new drawing tool type.
    /// </summary>
    public interface IDrawingCalculator
    {
        /// <summary>The drawing tool type this calculator handles.</summary>
        DrawingType DrawingType { get; }

        /// <summary>
        /// Produces the named data arrays that the renderer will draw for this tool.
        /// Returns an empty dictionary when required anchors are missing.
        /// </summary>
        Dictionary<string, double[]> Calculate(DrawingData drawing, IReadOnlyList<Ohlcv> chartData);
    }
}
