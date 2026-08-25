using System.Collections.Immutable;
using System.Reflection;
using AccessibleTrader.ScriptSandbox;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Tests;

/// <summary>
/// The guard on the <see cref="WorkspaceState"/> projection that carries a strategy's third
/// <c>OnBar</c> argument into the sandbox worker.
///
/// <para>
/// A hand-maintained projection of a 49-property record is a thing that goes stale silently. Add
/// a property next year, forget this file, and every script strategy sees its default forever
/// with nothing red anywhere — the compile passes, the worker starts, the backtest runs, and the
/// only symptom is a decision made on information the strategy was supposed to have. So the
/// census below fails on a property that is on neither the carried nor the not-carried list, and
/// the round-trip below fails on a property that is on the carried list but does not actually
/// survive the wire. Between them, growing the record forces a decision.
/// </para>
/// </summary>
public class WorkspaceProjectionTests
{
    // ── The census ────────────────────────────────────────────────────────────────

    [Fact]
    public void Every_WorkspaceState_property_is_either_carried_or_declared_not_carried()
    {
        var actual = typeof(WorkspaceState)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .ToHashSet(StringComparer.Ordinal);

        var accounted = WorkspaceProjection.Carried
            .Concat(WorkspaceProjection.NotCarried)
            .Concat(WorkspaceProjection.Derived)
            .ToHashSet(StringComparer.Ordinal);

        var unaccounted = actual.Except(accounted).OrderBy(n => n, StringComparer.Ordinal).ToList();
        Assert.True(unaccounted.Count == 0,
            "WorkspaceState has grown properties the script sandbox projection says nothing about: "
            + string.Join(", ", unaccounted)
            + ". Decide for each one: add it to WorkspaceProjection.Carried and to Write/Read, or add it "
            + "to NotCarried with the reason it stays on the host side. Leaving it out silently means "
            + "every script strategy reads its default and nothing anywhere says so.");

        var stale = accounted.Except(actual).OrderBy(n => n, StringComparer.Ordinal).ToList();
        Assert.True(stale.Count == 0,
            "The script sandbox projection names WorkspaceState properties that no longer exist: "
            + string.Join(", ", stale));
    }

    /// <summary>
    /// The lists have to be disjoint, or "is it carried?" has two answers and the census above
    /// passes on a property that was moved from one list to the other and left in both.
    /// </summary>
    [Fact]
    public void The_carried_and_not_carried_lists_do_not_overlap()
    {
        var overlap = WorkspaceProjection.Carried
            .Intersect(WorkspaceProjection.NotCarried, StringComparer.Ordinal)
            .Concat(WorkspaceProjection.Carried.Intersect(WorkspaceProjection.Derived, StringComparer.Ordinal))
            .ToList();
        Assert.True(overlap.Count == 0, "Listed both ways: " + string.Join(", ", overlap));
    }

    // ── The round trip ────────────────────────────────────────────────────────────

    /// <summary>
    /// Every scalar on the carried list, one at a time: set it to something that is NOT its
    /// default, send it across, and read it back. A property added to the list but forgotten in
    /// <c>Write</c>/<c>Read</c> comes back at its default and fails here by name.
    ///
    /// <para>
    /// Deliberately one property per case rather than one state with everything set: if the codec
    /// writes two fields in an order the reader does not expect, an all-at-once test reports the
    /// first mismatch and hides the rest, while this reports every affected field.
    /// </para>
    /// </summary>
    [Fact]
    public void Every_carried_scalar_survives_the_round_trip()
    {
        var complex = new HashSet<string>(StringComparer.Ordinal)
        {
            nameof(WorkspaceState.Identity),
            nameof(WorkspaceState.Data),
            nameof(WorkspaceState.ActiveSeries),
        };

        var failures = new List<string>();
        foreach (var name in WorkspaceProjection.Carried)
        {
            if (complex.Contains(name)) continue;   // covered by their own tests below

            var property = typeof(WorkspaceState).GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            Assert.NotNull(property);

            var state = WorkspaceState.Initial;
            var current = property!.GetValue(state);
            var mutated = Different(property.PropertyType, current);
            if (mutated == null && Nullable.GetUnderlyingType(property.PropertyType) == null
                                && property.PropertyType != typeof(string))
            {
                failures.Add($"{name}: this test does not know how to produce a distinct value of type " +
                             $"{property.PropertyType.Name}. Extend Different(), or the property is not really covered.");
                continue;
            }

            // init-only setters are a compile-time restriction, not a runtime one.
            property.SetValue(state, mutated);

            var back = RoundTrip(state);
            var got = property.GetValue(back);
            if (!Equals(mutated, got))
                failures.Add($"{name}: sent [{Show(mutated)}], got back [{Show(got)}]");
        }

        Assert.True(failures.Count == 0,
            "Carried WorkspaceState properties that did not survive the script sandbox wire:\n  "
            + string.Join("\n  ", failures)
            + "\nA property on WorkspaceProjection.Carried must be written by Write and read by Read.");
    }

    [Fact]
    public void The_not_carried_properties_come_back_at_their_defaults_rather_than_at_the_hosts_values()
    {
        var state = WorkspaceState.Initial with
        {
            PaneRanges = ImmutableDictionary<string, (double Min, double Max)>.Empty.Add("Pane_RSI", (10, 90)),
            PaneHeightRatios = ImmutableDictionary<string, float>.Empty.Add("Pane_RSI", 0.42f),
            TabSnapshots = ImmutableList<TabSnapshot>.Empty.Add(new TabSnapshot(
                TabIndex: 1,
                Identity: new ChartIdentity("Spot", "Kraken", "ETH/USD", "4h"),
                Data: new TimeSeriesBuffer<Ohlcv>(Bar(1), Bar(2)),
                ActiveSeries: ImmutableList<ChartSeries>.Empty,
                FocusedSeriesIndex: 0, FocusedSeriesId: null, FocusedComponentIndex: 0,
                FocusedBinIndex: -1, CurrentDataIndex: 1, ViewportStartIndex: 0, ViewportLength: 100,
                RightMarginBars: 10, ViewportRange: (0, 0),
                PaneRanges: ImmutableDictionary<string, (double Min, double Max)>.Empty,
                IsHeikinAshi: false, IsLogScale: false,
                LastInteractionContext: InteractionContext.Series,
                PaneHeightRatios: null, IndicatorPaneScrollIndex: 0,
                InitStatus: InitializationStatus.Ready, DataStatus: DataStatus.Ready,
                IsCoordinateEntryMode: false, PendingDrawingTool: null,
                CoordinateEntryAnchorCount: 0, CoordinateEntryAnchor1Index: -1)),
        };

        var back = RoundTrip(state);

        // Not "whatever happened to arrive" — exactly Initial's values, which is what the class
        // doc promises a strategy sees for these three.
        Assert.Empty(back.PaneRanges);
        Assert.Null(back.PaneHeightRatios);
        Assert.Empty(back.TabSnapshots!);

        // And the other tab's data did not follow it across. That is the point of leaving
        // TabSnapshots behind: a question about ETH has no business putting BTC inside the sandbox.
        Assert.Equal(1, back.TabCount);
    }

    [Fact]
    public void The_chart_identity_and_the_whole_bar_buffer_cross()
    {
        var bars = Enumerable.Range(0, 250).Select(i => Bar(i)).ToArray();
        var state = WorkspaceState.Initial with
        {
            Identity = new ChartIdentity("Futures", "Bybit", "BTC/USDT", "15m"),
            Data = new TimeSeriesBuffer<Ohlcv>(bars),
        };

        var back = RoundTrip(state);

        Assert.Equal(state.Identity, back.Identity);
        Assert.Equal(bars.Length, back.Data.Count);
        for (int i = 0; i < bars.Length; i++)
        {
            Assert.Equal(bars[i].Date, back.Data[i].Date);
            Assert.Equal(DateTimeKind.Utc, back.Data[i].Date.Kind);
            Assert.Equal(bars[i].Close, back.Data[i].Close);
            Assert.Equal(bars[i].Volume, back.Data[i].Volume);
        }
    }

    /// <summary>
    /// ActiveSeries is what a strategy's conditions are actually built out of — an indicator's
    /// component arrays, its levels, its friendly name. The config half goes as JSON and the
    /// per-bar half goes binary, and this covers both plus the profile bins a VPVR-reading
    /// strategy needs.
    /// </summary>
    [Fact]
    public void An_indicator_series_crosses_with_its_config_its_arrays_and_its_profile_bins()
    {
        var config = new SeriesConfig
        {
            Name = "RSI(14)",
            FriendlyName = "Relative Strength",
            IndicatorCode = "RSI",
            Pane = "Pane_RSI",
            RangeMin = 0,
            RangeMax = 100,
        };
        config.Parameters["period"] = 14;
        config.StringParameters["maType"] = "EMA";
        config.Components.Add(new ComponentConfig
        {
            Name = "RSI",
            DisplayName = "RSI",
            DisplayType = ComponentDisplayType.Oscillator,
            ColorHex = "#00FF00",
            ReferenceLevel = 50,
        });
        config.Levels.Add(new LevelConfig { Name = "Overbought", Value = 70, ColorHex = "#FF0000" });

        var data = new SeriesDataBuffer
        {
            SeriesId = config.Id,
            FirstBarDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        };
        data.ComponentData["RSI"] = new[] { double.NaN, 41.5, 63.25, 71.75 };
        data.ProfileBins.Add(new ProfileBin
        {
            PriceLow = 100, PriceHigh = 101, TotalVolume = 5150.5, TpoPeriodCount = 3,
            IsPOC = true, IsValueArea = true, TpoLetter = 'C', TpoBinIndex = 7, IsSinglePrint = false,
            TpoLetters = ImmutableList.Create('A', 'B', 'C'),
        });
        data.HeatmapData.Add(new List<ProfileBin>
        {
            new() { PriceLow = 99, PriceHigh = 100, TotalVolume = 12, TpoPeriodCount = 1,
                    IsPOC = false, IsValueArea = false },
        });

        var series = new ChartSeries(config, data)
        {
            IsProfile = true,
            FocusedBinIndex = 4,
            RequiresFullRecalcOnTick = true,
            Drawing = new DrawingData { Type = DrawingType.TrendLine, AnchorPrice1 = 12.5, Text = "support" },
        };

        var back = RoundTrip(WorkspaceState.Initial with
        {
            ActiveSeries = ImmutableList<ChartSeries>.Empty.Add(series),
        });

        var got = Assert.Single(back.ActiveSeries);
        Assert.Equal(config.Id, got.Id);
        Assert.Equal("RSI(14)", got.Name);
        Assert.Equal("Relative Strength", got.FriendlyName);
        Assert.Equal("RSI", got.IndicatorCode);
        Assert.Equal("Pane_RSI", got.Pane);
        Assert.Equal(14, got.Parameters["period"]);
        Assert.Equal("EMA", got.StringParameters["maType"]);
        Assert.Equal(0, got.Config.RangeMin);
        Assert.Equal(100, got.Config.RangeMax);

        var component = Assert.Single(got.Components);
        Assert.Equal("RSI", component.Name);
        Assert.Equal(ComponentDisplayType.Oscillator, component.DisplayType);
        Assert.Equal("#00FF00", component.ColorHex);
        Assert.Equal(50, component.ReferenceLevel);

        var level = Assert.Single(got.Levels);
        Assert.Equal("Overbought", level.Name);
        Assert.Equal(70, level.Value);

        Assert.Equal(data.FirstBarDate, got.Data.FirstBarDate);
        Assert.Equal(new[] { double.NaN, 41.5, 63.25, 71.75 }, got.Data.ComponentData["RSI"]);

        var bin = Assert.Single(got.Data.ProfileBins);
        Assert.Equal(5150.5, bin.TotalVolume);
        Assert.True(bin.IsPOC);
        Assert.Equal('C', bin.TpoLetter);
        Assert.Equal(7, bin.TpoBinIndex);
        Assert.Equal(new[] { 'A', 'B', 'C' }, bin.TpoLetters);
        Assert.Equal(12, Assert.Single(Assert.Single(got.Data.HeatmapData)).TotalVolume);

        Assert.True(got.IsProfile);
        Assert.Equal(4, got.FocusedBinIndex);
        Assert.True(got.RequiresFullRecalcOnTick);
        Assert.Equal(DrawingType.TrendLine, got.Drawing!.Type);
        Assert.Equal(12.5, got.Drawing.AnchorPrice1);
        Assert.Equal("support", got.Drawing.Text);
    }

    /// <summary>
    /// The empty case, which is the one the causality probe actually runs with — a state whose
    /// ActiveSeries is empty and whose Data holds the whole check series.
    /// </summary>
    [Fact]
    public void An_empty_workspace_round_trips_without_a_null_anywhere()
    {
        var back = RoundTrip(WorkspaceState.Initial);

        Assert.Empty(back.ActiveSeries);
        Assert.Equal(0, back.Data.Count);
        Assert.Equal(ChartIdentity.Empty, back.Identity);
        Assert.Null(back.FocusedSeriesId);
        Assert.Null(back.PendingDrawingTool);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────

    private static WorkspaceState RoundTrip(WorkspaceState state)
    {
        using var ms = new MemoryStream();
        WorkspaceProjection.Write(ms, state);
        var bytes = ms.ToArray();

        var reader = new WireReader(bytes);
        var back = WorkspaceProjection.Read(ref reader);

        // A writer and a reader that disagree by one field still "work" until the next field is
        // read as garbage. Insisting the payload is fully consumed catches that at the source.
        Assert.Equal(0, reader.Remaining);
        return back;
    }

    private static Ohlcv Bar(int i) =>
        new(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddHours(i),
            100 + i, 101 + i, 99 + i, 100.5 + i, 1000 + i);

    /// <summary>A value of <paramref name="type"/> that is definitely not <paramref name="current"/>.</summary>
    private static object? Different(Type type, object? current)
    {
        var u = Nullable.GetUnderlyingType(type) ?? type;

        if (u == typeof(bool))   return !(bool)(current ?? false);
        if (u == typeof(int))    return (int)(current ?? 0) + 7;
        if (u == typeof(float))  return (float)(current ?? 0f) + 3.5f;
        if (u == typeof(double)) return (double)(current ?? 0d) + 3.5;
        if (u == typeof(string)) return ((string?)current ?? "") + "_projected";
        if (u == typeof(ValueTuple<double, double>)) return (11.5d, 97.25d);
        if (u.IsEnum)
        {
            foreach (var value in Enum.GetValues(u))
                if (!Equals(value, current)) return value;
        }
        return null;
    }

    private static string Show(object? value) => value?.ToString() ?? "null";
}
