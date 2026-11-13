using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Analysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;

namespace AccessibleTrader.Tests;

/// <summary>
/// "if I close the terminal and reopen it, that choice should stay selected" (Cody, 2026-11-13).
/// The semicolon choice is written to settings.json per chart and read back by a fresh
/// <see cref="ChartPatternFocus"/> — through the real <see cref="SettingsManager"/> and the real
/// file, because the date round trip is where a saved choice would quietly stop matching.
/// </summary>
public sealed class ChartFormationChoicePersistenceTests : IDisposable
{
    private readonly string _dir;

    public ChartFormationChoicePersistenceTests()
    {
        _dir = TestTemp.NewPath("atc-formation-choice-");
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best-effort */ }
    }

    private sealed class FakePathService : IPlatformPathService
    {
        public FakePathService(string dir) { AppDataDirectory = dir; CacheDirectory = dir; }
        public string AppDataDirectory { get; }
        public string CacheDirectory { get; }
    }

    private SettingsManager Settings() => new(new FakePathService(_dir), NullLogger<SettingsManager>.Instance);

    // Real dates, one UTC and one Unspecified (bars arrive both ways), so a reader that shifted
    // either to local time on the way back would no longer match.
    private static readonly ChartPattern Ihs = new(
        Kind: ChartPatternKind.InverseHeadAndShoulders, State: ChartPatternState.Forming,
        StartBarIndex: 343, EndBarIndex: 419, KnownAtIndex: 424, TriggerLevel: 225.59,
        StartTime: new DateTime(2026, 11, 6, 23, 0, 0, DateTimeKind.Utc),
        EndTime: new DateTime(2026, 11, 10, 3, 0, 0, DateTimeKind.Utc), ExpiresAtIndex: 500);

    private static readonly ChartPattern Top = new(
        Kind: ChartPatternKind.DoubleTop, State: ChartPatternState.Forming,
        StartBarIndex: 351, EndBarIndex: 411, KnownAtIndex: 416, TriggerLevel: 212,
        StartTime: new DateTime(2026, 11, 7, 7, 0, 0, DateTimeKind.Unspecified),
        EndTime: new DateTime(2026, 11, 9, 19, 0, 0, DateTimeKind.Unspecified), ExpiresAtIndex: 475,
        BreaksBelow: true);

    private static IReadOnlyList<ChartPattern> Ranked => ChartPatternNarrator.ByDominance(new[] { Ihs, Top }).ToList();

    [Fact]
    public void TheChoiceIsStillChosenAfterARestart()
    {
        var before = new ChartPatternFocus(Settings());
        Assert.Equal(ChartPatternKind.InverseHeadAndShoulders, before.Apply("mexc|TAOUSDT|1h", Ranked)[0].Kind);
        Assert.Equal(ChartPatternKind.DoubleTop, before.CycleAt("mexc|TAOUSDT|1h", Ranked)!.Kind);

        var after = new ChartPatternFocus(Settings());                   // a fresh process: a new manager reads the file
        Assert.Equal(ChartPatternKind.DoubleTop, after.Apply("mexc|TAOUSDT|1h", Ranked)[0].Kind);
        Assert.Equal(ChartPatternKind.InverseHeadAndShoulders, after.Apply("other|BTCUSD|1h", Ranked)[0].Kind);   // per chart
    }

    /// <summary>
    /// Both heads register the focus through DI, and the in-memory constructor would also satisfy
    /// it. The container must take the one with the settings, or nothing is ever saved.
    /// </summary>
    [Fact]
    public void DependencyInjectionHandsTheFocusTheSettings()
    {
        var sp = new ServiceCollection()
            .AddSingleton<ISettingsManager>(Settings())
            .AddSingleton<IChartPatternFocus, ChartPatternFocus>()
            .BuildServiceProvider();
        sp.GetRequiredService<IChartPatternFocus>().CycleAt("mexc|TAOUSDT|1h", Ranked);

        Assert.Equal(ChartPatternKind.DoubleTop, new ChartPatternFocus(Settings()).Apply("mexc|TAOUSDT|1h", Ranked)[0].Kind);
    }

    [Fact]
    public void ADamagedSavedChoiceIsSkippedNotThrown()
    {
        var mgr = Settings();
        mgr.SetSetting(SettingsKeys.ChartFormationChoices, new JObject
        {
            ["bad|kind|1h"] = new JObject { ["kind"] = "NoSuchShape", ["start"] = 1L, ["end"] = 2L },
            ["bad|date|1h"] = new JObject { ["kind"] = "DoubleTop", ["start"] = "yesterday", ["end"] = 2L },
            ["bad|shape|1h"] = "DoubleTop",
            ["mexc|TAOUSDT|1h"] = new JObject { ["kind"] = "DoubleTop", ["start"] = Top.StartTime.Ticks, ["end"] = Top.EndTime.Ticks },
        });
        mgr.SaveSettings();

        var focus = new ChartPatternFocus(Settings());
        Assert.False(focus.IsPinned("bad|kind|1h"));
        Assert.False(focus.IsPinned("bad|date|1h"));
        Assert.False(focus.IsPinned("bad|shape|1h"));
        Assert.Equal(ChartPatternKind.DoubleTop, focus.Apply("mexc|TAOUSDT|1h", Ranked)[0].Kind);
    }
}
