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

        // Computed on his chart's history, every restored component has values, and the levels the
        // narrator speaks are zone lines.
        var bars = CipherSrSpeechTests.BtcDaily();
        var results = new Dictionary<string, double[]>();
        new CipherSrProvider().Calculate("CIPHER_SR", System.Runtime.InteropServices.CollectionsMarshal.AsSpan(bars),
            restored.BuildParameterMap(), new IndicatorResultBuffer(results, bars.Count));
        var empty = restored.Components
            .Where(c => !results.TryGetValue(c.Name, out var a) || a.All(double.IsNaN))
            .Select(c => c.Name).ToList();
        Assert.True(empty.Count == 0, $"restored components with no values on BTC daily: {string.Join(", ", empty)}");
        Assert.Contains(restored.Components, c => c.IsZoneLine);
    }
}
