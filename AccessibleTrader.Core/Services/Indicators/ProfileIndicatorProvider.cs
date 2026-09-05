using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Indicators
{
    /// <summary>
    /// Registers the eight profile indicators in the IndicatorService catalogue so they appear
    /// in the AddIndicatorModal under the "Profile" category: four windows (visible range, fixed
    /// range, session, anchored) by two measures (volume, time-at-price). See
    /// <see cref="ProfileAnchoring"/> for the windows — the codes and the slicing rule live there,
    /// and this file only describes them to the user.
    ///
    /// Profile indicators do NOT go through IIndicatorOrchestrator's component pipeline. Their
    /// bins are computed by ProfileService over the slice ProfileAnchoring chooses. When a
    /// profile series is added via SeriesManagementService, IsProfile is set to true and the
    /// renderer handles the rest automatically.
    ///
    /// Calculate and CalculateIncremental return empty dictionaries — the orchestrator skips
    /// profile series (they have no time-indexed component data to populate).
    /// </summary>
    public class ProfileIndicatorProvider : IIndicatorProvider
    {
        public string Name => "ProfileIndicators";

        private const string VolumeLandmarks = "POC in gold; Value Area (70%) in teal.";
        private const string TimeMeasure =
            "Time-Price Opportunity: counts how many time PERIODS price visited each level, rather " +
            "than how much volume traded there — so a level price lingered at ranks highly even on " +
            "thin volume. Highlights acceptance and rejection.";

        public List<IndicatorMetadata> GetIndicators() => new()
        {
            Profile(ProfileAnchoring.VolumeVisible, "Volume Profile (Visible Range)",
                "Volume distribution across price levels for the visible viewport. " +
                "Recomputes as you pan or zoom. " + VolumeLandmarks),
            Profile(ProfileAnchoring.VolumeFixed, "Volume Profile (Fixed Range)",
                "Volume profile anchored to the range you were viewing when you added it. " +
                "Pan and zoom freely — it stays put, so you can compare current price " +
                "against a fixed reference. " + VolumeLandmarks),
            Profile(ProfileAnchoring.VolumeSession, "Volume Profile (Session)",
                "Volume profile of ONE trading day — the day of the last bar on screen — so panning " +
                "from day to day shows each session's own profile. Meant for intraday charts; on a " +
                "daily chart a session is a single bar. " + VolumeLandmarks),
            Profile(ProfileAnchoring.VolumeAnchored, "Volume Profile (Anchored)",
                "Volume profile from the bar you were on when you added it to the newest bar. It " +
                "grows as bars arrive — the anchored-VWAP idea applied to volume, for measuring " +
                "where trade has concentrated since a chosen event. " + VolumeLandmarks),
            Profile(ProfileAnchoring.TimeVisible, "Market Profile (TPO)",
                TimeMeasure + " Covers the visible range and recomputes as you pan or zoom."),
            Profile(ProfileAnchoring.TimeFixed, "Market Profile (TPO, Fixed Range)",
                TimeMeasure + " Anchored to the range you were viewing when you added it; " +
                "pan and zoom freely and it stays put."),
            Profile(ProfileAnchoring.TimeSession, "Market Profile (TPO, Session)",
                TimeMeasure + " Covers ONE trading day — the day of the last bar on screen — which " +
                "is how a market profile is conventionally read. Meant for intraday charts."),
            Profile(ProfileAnchoring.TimeAnchored, "Market Profile (TPO, Anchored)",
                TimeMeasure + " From the bar you were on when you added it to the newest bar, " +
                "growing as bars arrive."),
        };

        private static IndicatorMetadata Profile(string code, string name, string description) => new()
        {
            Code        = code,
            Causality   = ComponentCausality.Causal,
            Name        = name,
            Category    = "Profile",
            Description = description,
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
