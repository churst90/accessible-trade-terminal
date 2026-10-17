using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Audio;
using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Mocks;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json;

namespace AccessibleTrader.Tests;

/// <summary>
/// <b>Cody's saved Cipher SR comes back, narrating, with every part it had.</b>
///
/// <para>
/// The fixture is the Cipher SR series exactly as his BTCUSDT 1d session saved it on 2026-10-17
/// (Fixtures/codys_cipher_sr_series_2026-10-17.json, cut from __last-session__*.json): narration
/// on for the series, Auto-Scale on, Pivot Bars 5, and the components of that day. It is
/// restored through the workspace path (<see cref="SeriesManagementService.MaterializeSaved"/>)
/// and computed on the BTC daily history, so a change to Cipher SR's components or parameters
/// that would drop something from his chart, or leave a restored component with no values, fails
/// here rather than on his desktop.
/// </para>
/// </summary>
public sealed class CipherSrSavedChartTests
{
    private static SeriesConfig Saved()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "codys_cipher_sr_series_2026-10-17.json");
        return JsonConvert.DeserializeObject<SeriesConfig>(File.ReadAllText(path))!;
    }

    private static ChartSeries Restore(SeriesConfig saved)
    {
        var provider = new CipherSrProvider();
        var meta = provider.GetIndicators().Single(m => m.Code == "CIPHER_SR");
        var providers = new List<IIndicatorProvider> { provider };
        var engine = new IndicatorEngine(new IndicatorService(providers, NullLogger<IndicatorService>.Instance),
            new CustomIndicatorRegistry(), providers);
        var styling = new StylingService(new ComponentRoleMapper(), new SonificationProfileProvider(), new PaneAssignmentService());
        var factory = new IndicatorModelFactory(styling, new MockIndicatorPreferencesService());
        return SeriesManagementService.MaterializeSaved(saved, meta, factory,
            Array.Empty<IReadOnlyDictionary<string, object>>(), engine, new MockIndicatorPreferencesService());
    }

    [Fact]
    public void CodysSavedCipherSrRestoresNarratingWithHisSettingsAndEveryComponentHasValues()
    {
        var saved = Saved();
        var restored = Restore(saved);

        Assert.True(restored.Config.IsAutoNarrated, "narration was on in the saved chart and did not come back");
        Assert.Equal("CIPHER_SR", restored.IndicatorCode);
        foreach (var (k, v) in saved.Parameters)
            if (restored.Config.Parameters.ContainsKey(k))
                Assert.Equal(v, restored.Config.Parameters[k]);

        // Every component the saved chart had is either still there or was retired with a reason
        // (SignalCatalog.Retired), never silently gone.
        var names = restored.Components.Select(c => c.Name).ToHashSet();
        var vanished = saved.Components.Select(c => c.Name)
            .Where(n => !names.Contains(n)
                && !Core.Services.Strategies.SignalCatalog.Retired.ContainsKey($"CIPHER_SR.{n}"))
            .ToList();
        Assert.True(vanished.Count == 0, $"saved components gone without a retirement notice: {string.Join(", ", vanished)}");

        // Computed with his settings, every restored component has values on an intraday chart, and
        // on his daily chart the levels the narrator speaks — the zone lines — have values. (Range
        // and VWAP are per UTC session, which is one bar on a daily chart, so they are off there.)
        Dictionary<string, double[]> Compute(List<Ohlcv> bars)
        {
            var results = new Dictionary<string, double[]>();
            new CipherSrProvider().Calculate("CIPHER_SR", System.Runtime.InteropServices.CollectionsMarshal.AsSpan(bars),
                restored.BuildParameterMap(), new IndicatorResultBuffer(results, bars.Count));
            return results;
        }
        var intraday = Compute(CipherSrMarketCipherFidelityTests.Csv("coinbase_BTC_USD_15m_2022-01-20_31.csv"));
        var empty = restored.Components
            .Where(c => !intraday.TryGetValue(c.Name, out var a) || a.All(double.IsNaN))
            .Select(c => c.Name).ToList();
        Assert.True(empty.Count == 0, $"restored components with no values on a 15-minute chart: {string.Join(", ", empty)}");

        var daily = Compute(CipherSrSpeechTests.BtcDaily());
        var zones = restored.Components.Where(c => c.IsZoneLine).Select(c => c.Name).ToList();
        Assert.True(zones.Count == 2, $"expected the two zone lines, got {string.Join(", ", zones)}");
        Assert.All(zones, z => Assert.Contains(daily[z], v => !double.IsNaN(v)));
    }

    /// <summary>
    /// <b>Landing on Cody's Resistance Zone says the level's age and its tests, through the real
    /// speech path.</b> The age comes from the "{zone}_since" companion and the test count from
    /// "Resistance_touches"; neither is a component, so the formatter has to hand them to the
    /// provider — and since the pivot-dot components were retired, nothing else names
    /// "Resistance_touches" for it.
    /// </summary>
    [Fact]
    public void LandingOnHisResistanceZoneSpeaksTheLevelsAgeAndTests()
    {
        var restored = Restore(Saved());
        var bars = CipherSrSpeechTests.BtcDaily();
        var results = new Dictionary<string, double[]>();
        new CipherSrProvider().Calculate("CIPHER_SR", System.Runtime.InteropServices.CollectionsMarshal.AsSpan(bars),
            restored.BuildParameterMap(), new IndicatorResultBuffer(results, bars.Count));
        foreach (var kv in results) restored.Data.ComponentData[kv.Key] = kv.Value;

        // The last bar whose resistance had been tested, so the sentence has both parts to say.
        var touches = results["Resistance_touches"];
        int at = Enumerable.Range(0, bars.Count).Last(i => !double.IsNaN(touches[i]) && touches[i] >= 2);
        var since = results[CipherSrProvider.CompResistanceSince];
        int days = (int)Math.Round((bars[at].Date - DateTime.UnixEpoch.AddSeconds(since[at])).TotalDays);

        var providers = new List<IIndicatorProvider> { new CipherSrProvider() };
        var engine = new IndicatorEngine(new IndicatorService(providers, NullLogger<IndicatorService>.Instance),
            new CustomIndicatorRegistry(), providers);
        int index = restored.Components.ToList().FindIndex(c => c.Name == CipherSrProvider.CompResistanceLine);
        var state = WorkspaceState.Initial with
        {
            Data = new TimeSeriesBuffer<Ohlcv>(bars),
            ActiveSeries = System.Collections.Immutable.ImmutableList.Create(restored),
            PrimarySeriesId = restored.Id,
            FocusedSeriesId = restored.Id,
            FocusedComponentIndex = index,
            CurrentDataIndex = at,
            ViewportStartIndex = Math.Max(0, at - 100),
            ViewportLength = 100,
            LastInteractionContext = InteractionContext.Component,
        };
        string said = new Core.Services.Accessibility.SpeechFormatter(NullLogger<Core.Services.Accessibility.SpeechFormatter>.Instance, engine)
            .FormatPointFeedback(state, isXMove: false, isYMove: true, restored, bars[at], "");

        Assert.True(days >= 2, "the fixture does not reach a level held for days");
        Assert.Contains($"held {days} days", said);
        Assert.Contains($"tested {(int)touches[at]} times", said);
    }
}
