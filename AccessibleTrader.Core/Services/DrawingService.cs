using System.Collections.Generic;
using System.Linq;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services
{
    // Extension retained here for any existing consumers outside the Drawing subsystem.
    public static class ReadOnlyListExtensions
    {
        public static int FindIndex<T>(this IReadOnlyList<T> list, System.Predicate<T> match)
        {
            for (int i = 0; i < list.Count; i++)
                if (match(list[i])) return i;
            return -1;
        }
    }

    public interface IDrawingService
    {
        Dictionary<string, double[]> CalculateDrawingData(DrawingData drawing, IReadOnlyList<Ohlcv> chartData);
    }

    /// <summary>
    /// Registry/dispatcher for drawing tool calculators.
    /// Resolves the correct <see cref="IDrawingCalculator"/> from the DI-injected set and
    /// delegates calculation to it. Add new tools by dropping a calculator into
    /// Core/Services/Drawing/Calculators/ and registering it in ServiceCollectionExtensions.
    /// </summary>
    public class DrawingService : IDrawingService
    {
        private readonly IReadOnlyDictionary<DrawingType, IDrawingCalculator> _calculators;

        public DrawingService(IEnumerable<IDrawingCalculator> calculators)
        {
            _calculators = calculators.ToDictionary(c => c.DrawingType);
        }

        public Dictionary<string, double[]> CalculateDrawingData(DrawingData drawing, IReadOnlyList<Ohlcv> chartData)
        {
            if (chartData == null || !chartData.Any())
                return new Dictionary<string, double[]>();

            return _calculators.TryGetValue(drawing.Type, out var calculator)
                ? calculator.Calculate(drawing, chartData)
                : new Dictionary<string, double[]>();
        }
    }
}
