using System.Collections.Generic;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Indicators
{
    /// <summary>
    /// Registers VPVR, VPFR, and TPO profile indicators in the IndicatorService catalogue
    /// so they appear in the AddIndicatorModal under the "Volume Profile" category.
    ///
    /// Profile indicators do NOT go through IIndicatorOrchestrator. Their data is computed
    /// on-the-fly at render time by ProfileService inside ProfileRenderLayer. When a profile
    /// series is added via SeriesManagementService, IsProfile is set to true and the renderer
    /// handles the rest automatically.
    ///
    /// Calculate and CalculateIncremental return empty dictionaries — the orchestrator skips
    /// profile series (they have no time-indexed component data to populate).
    /// </summary>
    public class ProfileIndicatorProvider : IIndicatorProvider
    {
        public string Name => "ProfileIndicators";

        public List<IndicatorMetadata> GetIndicators() => new()
        {
            new IndicatorMetadata
            {
                Code        = "VPVR",
                Causality = ComponentCausality.Causal,
                Name        = "Volume Profile (Visible Range)",
                Category    = "Profile",
                Description = "Volume distribution across price levels for the visible viewport. " +
                              "Recomputes as you pan or zoom. POC in gold; Value Area (70%) in teal.",
                Components  = new List<IndicatorComponentMetadata>
                {
                    new() { Name = "Profile", DisplayType = ComponentDisplayType.Bar }
                },
                Parameters  = new List<IndicatorParameterMetadata>
                {
                    new() { Name = "BinCount", DisplayName = "Bin Count",
                            DefaultValue = 50, DataType = typeof(int),
                            Description = "Number of price level buckets in the histogram." }
                }
            },
            new IndicatorMetadata
            {
                Code        = "VPFR",
                Causality = ComponentCausality.Causal,
                Name        = "Volume Profile (Fixed Range)",
                Category    = "Profile",
                Description = "Volume profile anchored to the range you were viewing when you added it. " +
                              "Pan and zoom freely — it stays put, so you can compare current price " +
                              "against a fixed reference. POC in gold; Value Area (70%) in teal.",
                Components  = new List<IndicatorComponentMetadata>
                {
                    new() { Name = "Profile", DisplayType = ComponentDisplayType.Bar }
                },
                Parameters  = new List<IndicatorParameterMetadata>
                {
                    new() { Name = "BinCount", DisplayName = "Bin Count",
                            DefaultValue = 50, DataType = typeof(int),
                            Description = "Number of price level buckets in the histogram." }
                }
            },
            new IndicatorMetadata
            {
                Code        = "TPO",
                Causality = ComponentCausality.Causal,
                Name        = "Market Profile (TPO)",
                Category    = "Profile",
                Description = "Time-Price Opportunity: counts how many time PERIODS price visited each " +
                              "level, rather than how much volume traded there — so a level price " +
                              "lingered at ranks highly even on thin volume. Covers the visible range " +
                              "and recomputes as you pan or zoom. Highlights acceptance and rejection.",
                Components  = new List<IndicatorComponentMetadata>
                {
                    new() { Name = "Profile", DisplayType = ComponentDisplayType.Bar }
                },
                Parameters  = new List<IndicatorParameterMetadata>
                {
                    new() { Name = "BinCount", DisplayName = "Bin Count",
                            DefaultValue = 50, DataType = typeof(int),
                            Description = "Number of price level buckets in the histogram." }
                }
            }
        };

        // Profile indicators are computed at render time by ProfileRenderLayer — not here.
        public void Calculate(string code, ReadOnlySpan<Ohlcv> data, Dictionary<string, object> parameters, IIndicatorResultBuffer buffer)
        {
        }

        public void UpdateLast(string code, ReadOnlySpan<Ohlcv> data, Dictionary<string, object> parameters, IIndicatorResultBuffer buffer)
        {
        }

        public int GetStabilityWindow(string code, Dictionary<string, object> parameters)
            => 0;

        public string GetDetailFact(string code, ReadOnlySpan<Ohlcv> data, IReadOnlyDictionary<string, double[]> calculatedResults, int index, Dictionary<string, object> parameters)
        {
            // For Profiles, the detail is usually best served by ResolveProfileBins 
            // in the renderer/manager because it's viewport-specific. 
            // We return empty here to allow the manager's default profile speech to win.
            return string.Empty;
        }
    }
}
