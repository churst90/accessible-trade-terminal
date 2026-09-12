using System.Collections.Immutable;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Audio;
using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Mocks;
using Microsoft.Extensions.Logging.Abstractions;

namespace AccessibleTrader.Tests;

/// <summary>
/// <b>A bounded indicator's pane axis covers its natural bounds at every zoom, so a value is the
/// same pitch on every chart.</b>
///
/// <para>
/// Cody, 2026-09-12, the morning after every oscillator got a pane of its own: <i>"the rsi
/// sonifies correctly but it reports a pane range from 22 to like 80 something, not 0 to
/// 100"</i>. That was the auto-fit: lowest and highest visible value, stretched to the visible
/// levels, plus 10%. It meant RSI 70 was a different note in every window. Cody: <i>"if the
/// declared bounds rule will make the sonification more precise no matter the zoom level, yes,
/// have each indicator declare its bounds, as long as nothing else breaks or changes
/// mechanically or visually."</i>
/// </para>
///
/// <para>
/// So: <see cref="IndicatorMetadata.RangeMin"/>/<c>RangeMax</c> on the indicator, copied onto the
/// series by the factory, read by <see cref="ViewportRangeCalculator"/> as the least the axis
/// covers with no buffer — which is exactly what Cipher B's by-name ±100 floor already did, now
/// one rule. Unbounded indicators keep auto-fit: MACD's range must not move.
/// </para>
/// </summary>
public sealed class DeclaredBoundsTests
{
    private static IndicatorMetadata RealMeta(string code) =>
        IndicatorProviderFixture.AllProviders().SelectMany(p => p.GetIndicators())
            .First(m => string.Equals(m.Code, code, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The indicators whose values are bounded BY DEFINITION, and the bounds. This is the
    /// specification, not a mirror of code: it says which indicators must declare, and what.
    /// </summary>
    public static IEnumerable<object[]> BoundedFamily() => new[]
    {
        new object[] { "Rsi", 0.0, 100.0 },        new object[] { "Stoch", 0.0, 100.0 },
        new object[] { "StochRsi", 0.0, 100.0 },   new object[] { "UltOsc", 0.0, 100.0 },
        new object[] { "WilliamsR", -100.0, 0.0 }, new object[] { "Mfi", 0.0, 100.0 },
        new object[] { "Adx", 0.0, 100.0 },        new object[] { "Chop", 0.0, 100.0 },
        new object[] { "Stc", 0.0, 100.0 },        new object[] { "Cmo", -100.0, 100.0 },
        new object[] { "ConnorsRsi", 0.0, 100.0 }, new object[] { "Aroon", -100.0, 100.0 },
        new object[] { "CIPHER_B", -100.0, 100.0 }, new object[] { "FEAR_GREED", 0.0, 100.0 },
        // 2026-09-12: three more that are bounded by construction and were not saying so.
        new object[] { "Cmf", -1.0, 1.0 },          new object[] { "CIPHER_C", -100.0, 100.0 },
        new object[] { "PULSE", 0.0, 100.0 },
    };

    /// <summary>
    /// The one-sided family: a floor and no ceiling. A true range, a standard deviation, a
    /// volatility and a drawdown depth are all ≥ 0 by construction and unbounded above, so a PAIR
    /// would be a lie at the top — which is why "declare both or neither" left them unable to
    /// state the true half either.
    /// </summary>
    [Theory]
    [InlineData("Atr")] [InlineData("StdDev")] [InlineData("Hv")] [InlineData("UlcerIndex")]
    public void TheFlooredFamily_DeclaresItsFloorAndNoCeiling(string code)
    {
        var meta = RealMeta(code);
        Assert.Equal(0.0, meta.RangeMin);
        Assert.Null(meta.RangeMax);
    }

    [Theory]
    [MemberData(nameof(BoundedFamily))]
    public void TheBoundedFamily_DeclaresItsBounds(string code, double min, double max)
    {
        var meta = RealMeta(code);
        Assert.Equal(min, meta.RangeMin);
        Assert.Equal(max, meta.RangeMax);
    }

    /// <summary>Unbounded indicators stay unbounded — declaring a bound for MACD would be a lie that hides data.</summary>
    [Theory]
    [InlineData("Macd")] [InlineData("Obv")] [InlineData("Cci")] [InlineData("Ema")]
    public void UnboundedIndicators_DeclareNothing(string code)
    {
        var meta = RealMeta(code);
        Assert.Null(meta.RangeMin);
        Assert.Null(meta.RangeMax);
    }

    /// <summary>
    /// An inverted pair, or a ceiling with no floor, is a typo; the fleet may not carry one.
    ///
    /// <para>
    /// A FLOOR with no ceiling is legal (see the one-sided family above). A ceiling with no floor
    /// is not: nothing in this repo has a top and no bottom, so allowing it would make a dropped
    /// <c>RangeMin</c> indistinguishable from a deliberate declaration.
    /// </para>
    /// </summary>
    [Fact]
    public void EveryDeclaredBound_IsAPairInOrderOrAFloorAlone()
    {
        var bad = IndicatorProviderFixture.AllProviders().SelectMany(p => p.GetIndicators())
            .Where(m => (m.RangeMax.HasValue && !m.RangeMin.HasValue)
                     || (m.RangeMin.HasValue && m.RangeMax.HasValue && m.RangeMin.Value >= m.RangeMax.Value))
            .Select(m => $"{m.Code}: RangeMin={m.RangeMin?.ToString() ?? "null"} RangeMax={m.RangeMax?.ToString() ?? "null"}")
            .ToList();
        Assert.True(bad.Count == 0, "A pair in order, or a floor alone:\n  " + string.Join("\n  ", bad));
    }

    /// <summary>A default level outside the declared bounds means one of the two is wrong.</summary>
    [Fact]
    public void EveryDefaultLevel_LiesWithinTheDeclaredBounds()
    {
        var offenders = new List<string>();
        foreach (var provider in IndicatorProviderFixture.AllProviders())
        foreach (var meta in provider.GetIndicators())
        {
            if (meta.RangeMin is not double lo || meta.RangeMax is not double hi) continue;
            IReadOnlyList<LevelDescriptor> levels;
            try { levels = provider.GetDefaultLevels(meta.Code.ToUpperInvariant()); } catch { continue; }
            foreach (var l in levels ?? Array.Empty<LevelDescriptor>())
                if (l.Value < lo || l.Value > hi)
                    offenders.Add($"{meta.Code}: level '{l.Name}'={l.Value} outside [{lo}, {hi}]");
        }
        Assert.True(offenders.Count == 0, string.Join("\n  ", offenders));
    }

    // ── The calculator ───────────────────────────────────────────────────────

    private static readonly IndicatorModelFactory Factory = new(
        new StylingService(new ComponentRoleMapper(), new SonificationProfileProvider(), new PaneAssignmentService()),
        new MockIndicatorPreferencesService());

    /// <summary>A series built the way production builds it, with one component's values filled in.</summary>
    private static ChartSeries Built(string code, double[] values, string? component = null)
    {
        var meta = RealMeta(code);
        var series = Factory.CreateSeriesFromMetadata(meta, meta.Name, PaneAssignmentService.PaneFor(meta), new List<(string, string)>(), null, null);
        series.Data.ComponentData[component ?? series.Components[0].Name] = values;
        return series;
    }

    private static WorkspaceState StateWith(int bars, params ChartSeries[] series) =>
        WorkspaceState.Initial with
        {
            Data = new TimeSeriesBuffer<Ohlcv>(Enumerable.Range(0, bars).Select(i => new Ohlcv(
                new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddHours(i), 100_000, 100_100, 99_900, 100_000, 10)).ToList()),
            ActiveSeries = series.ToImmutableList(),
            ViewportStartIndex = 0,
            ViewportLength = bars,
        };

    private static double[] Ramp(int n, double from, double to) =>
        Enumerable.Range(0, n).Select(i => from + (to - from) * i / Math.Max(1, n - 1)).ToArray();

    /// <summary>
    /// A declared floor holds the axis at zero even when the data dips below it — which is the
    /// case that separates the declaration from the inference it replaced. The old rule clamped to
    /// zero only when the pane's own minimum was already non-negative, so one rounding-noise
    /// negative anywhere in the window turned the clamp off and ATR's axis went below zero: a
    /// distance reading as less than no distance, and a pitch floor that moved with the window.
    /// </summary>
    [Fact]
    public void ADeclaredFloor_HoldsTheAxisAtZero_EvenWhenTheDataDipsBelowIt()
    {
        var atr = Built("Atr", new[] { -0.0001, 40.0, 80.0, 120.0 });
        var range = new ViewportRangeCalculator().Calculate(StateWith(4, atr)).PaneRanges[atr.Pane];
        Assert.Equal(0.0, range.Min);
        Assert.True(range.Max > 120.0, "the top still auto-fits with its buffer");
    }

    /// <summary>
    /// And it is a FLOOR, not a bound: it does not pin the top, and it does not pin the bottom to
    /// zero when the data sits well above it. An ATR window of 40–120 reads 40-ish to 120-ish,
    /// not 0 to 120 — declaring the floor must not flatten every volatility chart.
    /// </summary>
    [Fact]
    public void ADeclaredFloor_DoesNotDragTheAxisDownToIt()
    {
        var atr = Built("Atr", new[] { 40.0, 80.0, 120.0 });
        var range = new ViewportRangeCalculator().Calculate(StateWith(3, atr)).PaneRanges[atr.Pane];
        Assert.True(range.Min > 30.0, $"expected the axis to stay near the data, got {range.Min}");
    }

    /// <summary>Cody's report, as a number: RSI visible 25…78 must read 0 to 100, not 20 to 83.</summary>
    [Fact]
    public void Rsi_ReadsZeroToOneHundred_WhateverIsVisible()
    {
        var rsi = Built("Rsi", Ramp(40, 25, 78));
        var range = new ViewportRangeCalculator().Calculate(StateWith(40, rsi)).PaneRanges[rsi.Pane];
        Assert.Equal((0.0, 100.0), range);
    }

    /// <summary>
    /// The property Cody asked for: the same value is the same pitch at every zoom. Two windows
    /// over different stretches of the same RSI give the identical axis — before, each window
    /// had its own.
    /// </summary>
    [Fact]
    public void TheAxisDoesNotMoveWithTheWindow()
    {
        var calc = new ViewportRangeCalculator();
        var quiet = Built("Rsi", Ramp(40, 45, 55));
        var wild = Built("Rsi", Ramp(40, 12, 91));
        var a = calc.Calculate(StateWith(40, quiet)).PaneRanges[quiet.Pane];
        var b = calc.Calculate(StateWith(40, wild)).PaneRanges[wild.Pane];
        Assert.Equal(a, b);
    }

    [Fact]
    public void WilliamsR_ReadsMinusOneHundredToZero()
    {
        var w = Built("WilliamsR", Ramp(40, -80, -20));
        Assert.Equal((-100.0, 0.0), new ViewportRangeCalculator().Calculate(StateWith(40, w)).PaneRanges[w.Pane]);
    }

    /// <summary>A mis-declared bound can never hide data: a value beyond it still expands the axis.</summary>
    [Fact]
    public void AValueBeyondTheBounds_StillExpandsTheAxis()
    {
        var cmo = Built("Cmo", Ramp(40, -50, 130));
        var range = new ViewportRangeCalculator().Calculate(StateWith(40, cmo)).PaneRanges[cmo.Pane];
        Assert.Equal(-100.0, range.Min);
        Assert.Equal(130.0, range.Max);
    }

    /// <summary>"Nothing else changes": MACD has no bounds and keeps the auto-fit, buffer included.</summary>
    [Fact]
    public void Macd_KeepsAutoFit()
    {
        var macd = Built("Macd", Ramp(40, -800, 800), "Macd");
        var range = new ViewportRangeCalculator().Calculate(StateWith(40, macd)).PaneRanges[macd.Pane];
        // Data span 1600 plus 10% each side, then the zero level is already inside.
        Assert.Equal(-960.0, range.Min, 6);
        Assert.Equal(960.0, range.Max, 6);
    }

    /// <summary>"Nothing else changes": Cipher B's axis is what its by-name floor gave it, from the declaration now.</summary>
    [Fact]
    public void CipherB_KeepsItsPlusMinusHundredFloor()
    {
        var cb = Built("CIPHER_B", Ramp(40, -40, 60));
        var range = new ViewportRangeCalculator().Calculate(StateWith(40, cb)).PaneRanges[cb.Pane];
        Assert.Equal((-100.0, 100.0), range);

        // A hand-built Cipher B series with no declared bounds — a fixture, an old snapshot —
        // still gets the floor, so the old behaviour is a subset of the new.
        var bare = new SeriesConfig { Id = "cb", IndicatorCode = "CIPHER_B", Pane = "Pane_CIPHER_B" };
        bare.Components.Add(new ComponentConfig { Name = "WT1", DisplayType = ComponentDisplayType.Line });
        var buf = new SeriesDataBuffer { SeriesId = "cb" }; buf.ComponentData["WT1"] = Ramp(40, -40, 60);
        var bareSeries = new ChartSeries(bare, buf);
        Assert.Equal((-100.0, 100.0), new ViewportRangeCalculator().Calculate(StateWith(40, bareSeries)).PaneRanges["Pane_CIPHER_B"]);
    }

    /// <summary>A sub-pane strip is its own axis and takes none of its pane's bounds.</summary>
    [Fact]
    public void ASubPane_IsNotBounded()
    {
        var cfg = new SeriesConfig { Id = "b", IndicatorCode = "BOUNDED", Pane = "Pane_BOUNDED", RangeMin = -100, RangeMax = 100 };
        cfg.Components.Add(new ComponentConfig { Name = "Wave", DisplayType = ComponentDisplayType.Line });
        cfg.Components.Add(new ComponentConfig { Name = "Flow", DisplayType = ComponentDisplayType.Line, SubPaneName = "MF" });
        var buf = new SeriesDataBuffer { SeriesId = "b" };
        buf.ComponentData["Wave"] = Ramp(40, -40, 60);
        buf.ComponentData["Flow"] = Ramp(40, -2, 2);
        var series = new ChartSeries(cfg, buf);

        var ranges = new ViewportRangeCalculator().Calculate(StateWith(40, series)).PaneRanges;
        Assert.Equal((-100.0, 100.0), ranges["Pane_BOUNDED"]);
        var strip = ranges["Pane_BOUNDED/MF"];
        Assert.True(strip.Max < 10 && strip.Min > -10, $"the sub-pane took the pane's bounds: {strip}");
    }

    /// <summary>Two RSIs in one pane: same bounds, one axis.</summary>
    [Fact]
    public void TwoBoundedSeriesInOnePane_TakeTheUnion()
    {
        var a = Built("Rsi", Ramp(40, 40, 60));
        var b = Built("Rsi", Ramp(40, 30, 70));
        Assert.Equal(a.Pane, b.Pane);
        Assert.Equal((0.0, 100.0), new ViewportRangeCalculator().Calculate(StateWith(40, a, b)).PaneRanges[a.Pane]);
    }

    // ── The carrier: factory and restore ─────────────────────────────────────

    [Fact]
    public void TheFactory_CopiesTheBoundsOntoTheSeries()
    {
        var rsi = Built("Rsi", Ramp(40, 30, 70));
        Assert.Equal(0.0, rsi.Config.RangeMin);
        Assert.Equal(100.0, rsi.Config.RangeMax);
    }

    /// <summary>A workspace saved before bounds existed gets them from the metadata on restore.</summary>
    [Fact]
    public void ARestoredSeries_GetsTheBoundsFromCurrentMetadata()
    {
        var providers = new List<IIndicatorProvider> { new SkenderBoundedOscillatorProvider() };
        var indicators = new IndicatorService(providers, NullLogger<IndicatorService>.Instance);
        var engine = new IndicatorEngine(indicators, new CustomIndicatorRegistry(), providers);
        var saved = new SeriesConfig { Id = "rsi-1", IndicatorCode = "Rsi", Pane = "Pane_Rsi" }; // RangeMin/Max null, as saved last week

        var restored = SeriesManagementService.MaterializeSaved(saved, RealMeta("Rsi"), Factory,
            Array.Empty<IReadOnlyDictionary<string, object>>(), engine, new MockIndicatorPreferencesService());

        Assert.Equal(0.0, restored.Config.RangeMin);
        Assert.Equal(100.0, restored.Config.RangeMax);
    }
}
