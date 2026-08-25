using System.Collections.Immutable;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Indicators
{
    /// <summary>
    /// Builds a <see cref="WorkspaceState"/> from a raw bar list plus a set of indicator codes,
    /// without touching the live chart pipeline. This is what lets the screener and the
    /// respect-ranking analyzer evaluate the SAME condition trees and the SAME indicators the
    /// live chart uses, against symbols that are not loaded on any chart.
    ///
    /// <para>
    /// CANDLES / PRICE / VOLUME are pseudo-indicators: <c>CoreIndicatorProvider.Calculate</c> is a
    /// no-op for them because the live <c>IndicatorOrchestrator</c> projects <c>state.Data</c> into
    /// their component arrays directly. This builder replicates that projection so offline
    /// evaluation sees the same columns the live app produces — component names are kept in sync
    /// with <c>CoreIndicatorProvider</c>'s metadata on purpose.
    /// </para>
    /// </summary>
    public interface IOfflineWorkspaceBuilder
    {
        /// <summary>
        /// Computes <paramref name="indicatorCodes"/> over <paramref name="bars"/> and returns a
        /// state whose <c>ActiveSeries</c> carries one series per code. Indicators that throw are
        /// skipped and named in <paramref name="failures"/> rather than failing the whole build —
        /// one broken indicator must not blank an entire screen.
        /// </summary>
        Task<WorkspaceState> BuildAsync(
            ChartIdentity identity,
            IReadOnlyList<Ohlcv> bars,
            IEnumerable<string> indicatorCodes,
            IDictionary<string, string>? failures = null,
            CancellationToken ct = default);
    }

    /// <inheritdoc cref="IOfflineWorkspaceBuilder"/>
    public sealed class OfflineWorkspaceBuilder : IOfflineWorkspaceBuilder
    {
        /// <summary>Codes projected straight from bars instead of going through a provider.</summary>
        public static readonly IReadOnlyList<string> CoreProjectedCodes = new[] { "CANDLES", "PRICE", "VOLUME" };

        private readonly IIndicatorEngine _engine;

        public OfflineWorkspaceBuilder(IIndicatorEngine engine) => _engine = engine;

        public async Task<WorkspaceState> BuildAsync(
            ChartIdentity identity,
            IReadOnlyList<Ohlcv> bars,
            IEnumerable<string> indicatorCodes,
            IDictionary<string, string>? failures = null,
            CancellationToken ct = default)
        {
            var seriesList = ImmutableList.CreateBuilder<ChartSeries>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var rawCode in indicatorCodes)
            {
                ct.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(rawCode)) continue;

                string code = rawCode.Trim().ToUpperInvariant();
                if (!seen.Add(code)) continue;

                Dictionary<string, double[]> result;
                switch (code)
                {
                    case "CANDLES": result = ProjectCandles(bars); break;
                    case "PRICE":   result = ProjectPrice(bars);   break;
                    case "VOLUME":  result = ProjectVolume(bars);  break;
                    default:
                        try
                        {
                            // Empty parameters = the provider's declared defaults, matching what a
                            // fresh chart load feeds them. "__symbol" is the well-known routing hint
                            // cross-series providers read to pick the right asset's dataset.
                            var parameters = new Dictionary<string, object> { ["__symbol"] = identity.Symbol ?? string.Empty };
                            result = await _engine.CalculateAsync(code, bars, parameters, ct).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException) { throw; }
                        catch (Exception ex)
                        {
                            failures?.TryAdd(code, ex.Message);
                            continue;
                        }
                        break;
                }

                if (result.Count == 0) continue;

                var config = new SeriesConfig
                {
                    Name = code,
                    FriendlyName = code,
                    IndicatorCode = code,
                    Pane = "Main"
                };
                seriesList.Add(new ChartSeries(config, new SeriesDataBuffer
                {
                    SeriesId = config.Id,
                    ComponentData = result
                }));
            }

            return WorkspaceState.Initial with
            {
                Identity = identity,
                ActiveSeries = seriesList.ToImmutable()
            };
        }

        // Component names mirror CoreIndicatorProvider's CANDLES metadata exactly — the condition
        // evaluator looks components up by these names, so they must not drift.
        private static Dictionary<string, double[]> ProjectCandles(IReadOnlyList<Ohlcv> bars)
        {
            int n = bars.Count;
            var body = new double[n];
            var upper = new double[n];
            var lower = new double[n];
            for (int i = 0; i < n; i++)
            {
                body[i] = bars[i].Close;
                upper[i] = bars[i].High;
                lower[i] = bars[i].Low;
            }
            return new Dictionary<string, double[]>
            {
                ["body"] = body,
                ["upper_wick"] = upper,
                ["lower_wick"] = lower,
            };
        }

        private static Dictionary<string, double[]> ProjectPrice(IReadOnlyList<Ohlcv> bars)
        {
            var close = new double[bars.Count];
            for (int i = 0; i < bars.Count; i++) close[i] = bars[i].Close;
            return new Dictionary<string, double[]> { ["line"] = close };
        }

        private static Dictionary<string, double[]> ProjectVolume(IReadOnlyList<Ohlcv> bars)
        {
            var vol = new double[bars.Count];
            for (int i = 0; i < bars.Count; i++) vol[i] = bars[i].Volume;
            return new Dictionary<string, double[]> { ["Volume"] = vol };
        }
    }
}
