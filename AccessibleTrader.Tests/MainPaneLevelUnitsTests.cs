using System;
using System.Collections.Generic;
using System.Linq;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;
using Xunit;

namespace AccessibleTrader.Tests;

/// <summary>
/// No indicator that lands on the price pane may declare a fixed reference level.
///
/// <para>
/// <b>The invariant, and why it is checkable at all.</b> A default level is a <i>constant in source
/// code</i>. Price is not a constant — BTC trades near 64,000 and a sub-cent token near 0.0001 — so a
/// hard-coded level can never be a price. It follows that any provider whose codes are assigned to
/// the "Main" pane and which declares any default level is, by construction, publishing a number in
/// the wrong units. That is a static property of the source, so it can be enforced statically.
/// </para>
///
/// <para>
/// <b>Where this came from.</b> A maintainer screenshot of BTC 4h showed the y-axis running 0 →
/// 70,000, with all price action squashed into the top tenth of the pane.
/// <c>ViewportRangeCalculator</c> expands the price range to cover any visible level on a main-pane
/// series — right for a bounded metric, catastrophic for a level that is a bar count.
/// <c>LoukasCyclesProvider</c> is the worked example, declaring "DC Floor" at <b>0.0</b>, "DC Window
/// Open" at 35.0 and "DC Overdue" at 90.0: days into a cycle, not dollars.
/// </para>
///
/// <para>
/// <b>What this test is NOT.</b> It cannot catch a level that arrives at runtime — a saved user
/// override, an analytics hint, or a series whose pane assignment is wrong. That is why the range
/// calculator <i>also</i> carries a units guard (see <c>ViewportRangeUnitGuardTests</c>). The two are
/// deliberately different in kind: this one names the offending provider at build time, the other
/// holds regardless of how the level got there.
/// </para>
///
/// <para>
/// Note that pane assignment is decided by <see cref="PaneAssignmentService"/> from the indicator
/// <i>code</i> — <b>not</b> by the provider's own <c>DefaultPane</c> property, which several
/// providers set to a value the assignment service never returns. Checking the property instead of
/// the service would have quietly cleared exactly the provider under suspicion.
/// </para>
/// </summary>
public class MainPaneLevelUnitsTests
{
    private sealed record Scanned(string Provider, string Code, string Pane, IReadOnlyList<LevelDescriptor> Levels);

    /// <summary>
    /// Instantiates every provider it can and asks each one for its codes and levels.
    ///
    /// <para>
    /// Providers have varied constructors, so the ones needing real collaborators are skipped rather
    /// than faked — a null-filled provider could report misleading metadata.
    /// <see cref="TheScanReachesMostProviders"/> keeps that skipping honest.
    /// </para>
    /// </summary>
    private static List<Scanned> Scan(out int total, out List<string> skipped)
    {
        var panes = new PaneAssignmentService();
        var results = new List<Scanned>();
        skipped = new List<string>();

        var types = typeof(AccessibleTrader.Core.Services.Indicators.CipherAProvider).Assembly
            .GetTypes()
            .Where(t => t is { IsAbstract: false, IsInterface: false } && typeof(IIndicatorProvider).IsAssignableFrom(t))
            .OrderBy(t => t.Name)
            .ToList();
        total = types.Count;

        foreach (var t in types)
        {
            IIndicatorProvider? provider = null;
            try { provider = (IIndicatorProvider?)Activator.CreateInstance(t, nonPublic: true); }
            catch { /* needs collaborators */ }

            if (provider == null) { skipped.Add(t.Name); continue; }

            List<IndicatorMetadata> codes;
            try { codes = provider.GetIndicators(); }
            catch { skipped.Add(t.Name); continue; }

            foreach (var meta in codes)
            {
                if (string.IsNullOrWhiteSpace(meta.Code)) continue;
                List<LevelDescriptor> levels;
                try { levels = provider.GetDefaultLevels(meta.Code.ToUpperInvariant()); }
                catch { continue; }

                results.Add(new Scanned(t.Name, meta.Code, panes.GetPane(meta.Code), levels ?? new()));
            }
        }

        return results;
    }

    [Fact]
    public void NoMainPaneIndicatorDeclaresAFixedLevel()
    {
        var offenders = Scan(out _, out _)
            .Where(s => string.Equals(s.Pane, "Main", StringComparison.OrdinalIgnoreCase) && s.Levels.Count > 0)
            .Select(s => $"{s.Provider}/{s.Code} declares [{string.Join(", ", s.Levels.Select(l => $"{l.Name}={l.Value}"))}]")
            .OrderBy(x => x)
            .ToList();

        Assert.True(offenders.Count == 0,
            "These indicators are assigned to the price pane AND declare fixed reference levels. A "
          + "constant in source cannot be a price, so the level is in the wrong units — and "
          + "ViewportRangeCalculator will stretch the price axis to reach it, squashing every candle "
          + "into a sliver of the pane:\n  " + string.Join("\n  ", offenders));
    }

    /// <summary>
    /// The specific provider the screenshot led to. Its levels are bar counts, so it must not be on
    /// the price pane — and if anyone ever moves it there, this says why that is wrong.
    /// </summary>
    [Fact]
    public void TheCyclesIndicatorStaysOffThePricePane()
    {
        var cycles = Scan(out _, out _).Where(s => s.Provider.Contains("Loukas", StringComparison.OrdinalIgnoreCase)).ToList();
        Assert.NotEmpty(cycles);

        foreach (var s in cycles)
            Assert.False(string.Equals(s.Pane, "Main", StringComparison.OrdinalIgnoreCase),
                $"{s.Code} was assigned to the price pane, but its levels (\"DC Floor\" 0, \"DC Window Open\" 35, "
              + "\"DC Overdue\" 90) are BAR COUNTS. On the price pane the zero level drags the y-axis to the "
              + "origin and the chart becomes unreadable.");
    }

    /// <summary>
    /// Guards the guard. If constructors changed and nothing could be instantiated, the check above
    /// would pass by examining an empty list.
    /// </summary>
    [Fact]
    public void TheScanReachesMostProviders()
    {
        var scanned = Scan(out int total, out var skipped);

        Assert.True(scanned.Count > 40,
            $"The scan only reached {scanned.Count} indicator codes — it is probably not checking anything. "
          + $"Skipped providers: {string.Join(", ", skipped)}");

        Assert.True(skipped.Count * 2 < total,
            $"{skipped.Count} of {total} providers could not be constructed, so most of the codebase is "
          + $"unchecked: {string.Join(", ", skipped)}");
    }

    /// <summary>The scan must actually see main-pane indicators, or the filter above matches nothing.</summary>
    [Fact]
    public void TheScanSeesBothPaneKinds()
    {
        var scanned = Scan(out _, out _);
        Assert.Contains(scanned, s => string.Equals(s.Pane, "Main", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(scanned, s => !string.Equals(s.Pane, "Main", StringComparison.OrdinalIgnoreCase));
    }
}
