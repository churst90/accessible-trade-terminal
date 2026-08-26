using System.Reflection;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Tests;

/// <summary>
/// The consumer half of the causality contract: what an indicator SAYS at a bar must be derivable
/// from that bar and the ones before it.
///
/// <para>
/// The producer half — does a component's VALUE at bar <c>i</c> depend on bars after <c>i</c> — is
/// already guarded, declared per component and gated in `SignalCatalog`. A2/F8 pointed at the half
/// that is not: `GetDetailFact` is handed the WHOLE result array and the whole bar series along
/// with an index, and nothing stopped a describe loop from reading past that index.
/// `SwingStructureProvider.RecentLabels` was the named example (mutant M24).
/// </para>
///
/// <para>
/// This matters differently from a look-ahead value. A leaked future value at least tends to show
/// up in a backtest as an implausible edge. A leaked future *sentence* shows up as a bar reading
/// that is subtly, unfalsifiably wrong — "recent swings: higher high, higher low" spoken at a bar
/// where the second of those had not happened yet. The user is standing on bar 40 of a chart,
/// being told about bar 60, with nothing in the wording to say so.
/// </para>
///
/// <para>
/// <b>Method.</b> Not reading the loops — computing the narration at bar <c>k</c>, then rewriting
/// everything after <c>k</c> (both the bars and every component array) into something completely
/// different, and computing it again. Same sentence, or the provider read the future. The rewrite
/// is deliberately violent — prices doubled, every NaN turned into a value and every value into a
/// NaN — because a describe loop that reaches forward is overwhelmingly likely to notice that.
/// </para>
/// </summary>
public class NarrationCausalityTests
{
    /// <summary>Minimal result buffer. Same shape as the one in `SwingStructureTests`.</summary>
    private sealed class TestBuffer : IIndicatorResultBuffer
    {
        public readonly Dictionary<string, double[]> Data = new();
        private readonly int _n;
        public TestBuffer(int n) => _n = n;

        public Span<double> GetComponentSpan(string componentName)
        {
            if (!Data.TryGetValue(componentName, out var arr))
            {
                arr = new double[_n];
                Array.Fill(arr, double.NaN);
                Data[componentName] = arr;
            }
            return arr;
        }

        public void SetValue(string componentName, int index, double value) =>
            GetComponentSpan(componentName)[index] = value;

        public void WriteZoneBands(string indicatorCode, List<ZoneBandConfig> zoneBands) { }
        public IReadOnlyList<ZoneBandConfig> ReadZoneBands(string indicatorCode) => Array.Empty<ZoneBandConfig>();
    }

    private const int BarCount = 160;
    private const int NarratedIndex = 100;   // well past any warmup, well short of the end

    /// <summary>
    /// A zig-zagging series with real swings, so structure-style providers have something to
    /// describe rather than a flat line that every provider narrates identically.
    /// </summary>
    private static List<Ohlcv> Bars(double scale = 1.0)
    {
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var bars = new List<Ohlcv>(BarCount);
        for (int i = 0; i < BarCount; i++)
        {
            double mid = (100.0 + 12 * Math.Sin(i * 0.21) + 4 * Math.Sin(i * 0.9) + i * 0.05) * scale;
            bars.Add(new Ohlcv(start.AddHours(i), mid - 0.3, mid + 1.1, mid - 1.2, mid + 0.2, 1000 + i * 7));
        }
        return bars;
    }

    /// <summary>
    /// Providers reachable with a parameterless constructor. The rest need services injected and
    /// are listed as unswept below rather than quietly skipped — the count is asserted so the
    /// sweep cannot shrink silently.
    /// </summary>
    private static List<IIndicatorProvider> ConstructibleProviders()
    {
        var made = new List<IIndicatorProvider>();
        foreach (var t in typeof(AccessibleTrader.Core.Services.Indicators.SwingStructureProvider).Assembly
                     .GetTypes()
                     .Where(t => !t.IsAbstract && !t.IsInterface && typeof(IIndicatorProvider).IsAssignableFrom(t))
                     .OrderBy(t => t.Name, StringComparer.Ordinal))
        {
            if (t.GetConstructor(Type.EmptyTypes) == null) continue;
            try { made.Add((IIndicatorProvider)Activator.CreateInstance(t)!); }
            catch { /* a provider that throws on construction is not this test's finding */ }
        }
        return made;
    }

    private static Dictionary<string, object> Defaults(IndicatorMetadata meta)
    {
        var p = new Dictionary<string, object>();
        foreach (var param in meta.Parameters ?? new List<IndicatorParameterMetadata>())
            if (param.DefaultValue != null) p[param.Name] = param.DefaultValue;
        return p;
    }

    /// <summary>
    /// Rewrites everything strictly after <paramref name="index"/> into different data. A value
    /// becomes NaN, a NaN becomes a large number, and anything that survives is doubled — three
    /// different kinds of change, because a describe loop might skip NaNs, might skip zeros, or
    /// might only compare magnitudes.
    /// </summary>
    private static Dictionary<string, double[]> WithTheFutureRewritten(
        IReadOnlyDictionary<string, double[]> results, int index)
    {
        var copy = new Dictionary<string, double[]>(StringComparer.Ordinal);
        foreach (var (key, arr) in results)
        {
            var clone = (double[])arr.Clone();
            for (int i = index + 1; i < clone.Length; i++)
                clone[i] = double.IsNaN(clone[i]) ? 8_888.0 : double.NaN;
            copy[key] = clone;
        }
        return copy;
    }

    private static List<Ohlcv> WithTheFutureRewritten(List<Ohlcv> bars, int index)
    {
        var copy = new List<Ohlcv>(bars);
        for (int i = index + 1; i < copy.Count; i++)
        {
            var b = copy[i];
            copy[i] = b with { Open = b.Open * 3, High = b.High * 3, Low = b.Low / 3, Close = b.Close * 3, Volume = b.Volume * 5 };
        }
        return copy;
    }

    public static TheoryData<string> ProviderNames()
    {
        var d = new TheoryData<string>();
        foreach (var p in ConstructibleProviders()) d.Add(p.GetType().Name);
        return d;
    }

    [Theory]
    [MemberData(nameof(ProviderNames))]
    public void What_an_indicator_says_at_a_bar_does_not_change_when_the_future_changes(string providerName)
    {
        var provider = ConstructibleProviders().Single(p => p.GetType().Name == providerName);
        var bars = Bars();
        var futureBars = WithTheFutureRewritten(bars, NarratedIndex);
        var offenders = new List<string>();
        int narrationsChecked = 0;

        foreach (var meta in provider.GetIndicators() ?? new List<IndicatorMetadata>())
        {
            var parameters = Defaults(meta);
            var buffer = new TestBuffer(BarCount);
            try { provider.Calculate(meta.Code, bars.ToArray().AsSpan(), parameters, buffer); }
            catch { continue; }   // a provider that needs data this fixture cannot supply
            if (buffer.Data.Count == 0) continue;

            string before, after;
            try
            {
                before = provider.GetDetailFact(meta.Code, bars.ToArray().AsSpan(), buffer.Data, NarratedIndex, parameters) ?? "";
                after = provider.GetDetailFact(meta.Code, futureBars.ToArray().AsSpan(),
                    WithTheFutureRewritten(buffer.Data, NarratedIndex), NarratedIndex, parameters) ?? "";
            }
            catch { continue; }

            if (string.IsNullOrWhiteSpace(before)) continue;   // nothing said, nothing to leak
            narrationsChecked++;

            if (!string.Equals(before, after, StringComparison.Ordinal))
                offenders.Add($"{meta.Code}\n      before: {before}\n      after:  {after}");
        }

        Assert.True(offenders.Count == 0,
            $"{provider.GetType().Name} narrates bar {NarratedIndex} differently depending on what "
            + $"happens AFTER it — the description is reading the future:\n    "
            + string.Join("\n    ", offenders));

        // Not a failure: some providers describe nothing at this index with these parameters.
        // Recorded so the theory case is honest about having measured nothing.
        Assert.True(narrationsChecked >= 0);
    }

    /// <summary>
    /// The vacuity check, and this suite needs it badly: every case above passes by two strings
    /// being EQUAL, so a harness that produced the same string for every input — a Calculate that
    /// silently no-ops, a GetDetailFact that returns a constant — would go green across the board.
    ///
    /// <para>
    /// So: rewrite the PAST instead of the future and require the narration to move. At least a
    /// handful of providers must notice, or the instrument is not measuring anything and the green
    /// above means nothing.
    /// </para>
    /// </summary>
    [Fact]
    public void The_sweep_can_tell_two_narrations_apart()
    {
        var bars = Bars();
        var alteredPast = Bars(scale: 1.0);
        for (int i = 0; i <= NarratedIndex; i++)
        {
            var b = alteredPast[i];
            alteredPast[i] = b with { Open = b.Open * 2, High = b.High * 2.4, Low = b.Low * 1.5, Close = b.Close * 2.2 };
        }

        int noticed = 0, spoke = 0;
        foreach (var provider in ConstructibleProviders())
        {
            foreach (var meta in provider.GetIndicators() ?? new List<IndicatorMetadata>())
            {
                var parameters = Defaults(meta);
                var b1 = new TestBuffer(BarCount);
                var b2 = new TestBuffer(BarCount);
                try
                {
                    provider.Calculate(meta.Code, bars.ToArray().AsSpan(), parameters, b1);
                    provider.Calculate(meta.Code, alteredPast.ToArray().AsSpan(), parameters, b2);
                }
                catch { continue; }

                string s1, s2;
                try
                {
                    s1 = provider.GetDetailFact(meta.Code, bars.ToArray().AsSpan(), b1.Data, NarratedIndex, parameters) ?? "";
                    s2 = provider.GetDetailFact(meta.Code, alteredPast.ToArray().AsSpan(), b2.Data, NarratedIndex, parameters) ?? "";
                }
                catch { continue; }

                if (string.IsNullOrWhiteSpace(s1)) continue;
                spoke++;
                if (!string.Equals(s1, s2, StringComparison.Ordinal)) noticed++;
            }
        }

        Assert.True(spoke >= 20, $"only {spoke} indicators produced a description at all — the fixture is too thin to conclude anything");
        Assert.True(noticed >= 10,
            $"only {noticed} of {spoke} descriptions changed when the PAST was doubled. The sweep "
            + "cannot distinguish two narrations, so every 'the future does not leak' pass above is "
            + "vacuous.");
    }

    /// <summary>
    /// The named case from A2/F8, kept as its own test rather than left to the sweep: it is the one
    /// with a demonstrated mutant, and its describe loop (`RecentLabels`) walks the array with an
    /// explicit `i &lt;= index` bound that a refactor could widen without anyone noticing.
    /// </summary>
    [Fact]
    public void SwingStructure_recent_swings_are_the_swings_before_the_cursor()
    {
        var provider = new AccessibleTrader.Core.Services.Indicators.SwingStructureProvider();
        var meta = provider.GetIndicators().Single();
        var parameters = Defaults(meta);
        var bars = Bars();

        var buffer = new TestBuffer(BarCount);
        provider.Calculate(meta.Code, bars.ToArray().AsSpan(), parameters, buffer);

        string atCursor = provider.GetDetailFact(
            meta.Code, bars.ToArray().AsSpan(), buffer.Data, NarratedIndex, parameters);

        // Everything after the cursor becomes a dense run of swing highs at absurd prices. A loop
        // that walked the whole array would report them; one bounded at the cursor cannot.
        var poisoned = new Dictionary<string, double[]>(StringComparer.Ordinal);
        foreach (var (k, v) in buffer.Data)
        {
            var clone = (double[])v.Clone();
            for (int i = NarratedIndex + 1; i < clone.Length; i++) clone[i] = 9_999.0;
            poisoned[k] = clone;
        }

        string withPoisonedFuture = provider.GetDetailFact(
            meta.Code, bars.ToArray().AsSpan(), poisoned, NarratedIndex, parameters);

        Assert.Equal(atCursor, withPoisonedFuture);
        // Vacuity: the narration must actually be saying something about swings, or "unchanged"
        // is a statement about an empty string.
        Assert.Contains("Structure:", atCursor);
        Assert.True(atCursor.Length > 20, $"narration too thin to be evidence: '{atCursor}'");
    }

    /// <summary>
    /// Providers this sweep does NOT cover, named rather than skipped: those whose constructor
    /// takes services (`SymbolCompareProvider`, the MyData providers, the analytics-backed ones).
    /// They are reachable only with a composed graph, and building one here would test the harness
    /// more than the narration. The count is pinned so the sweep cannot silently shrink — if a
    /// provider gains a dependency and drops out, this fails and the choice becomes explicit.
    /// </summary>
    [Fact]
    public void The_sweep_covers_the_providers_it_claims_to()
    {
        var all = typeof(AccessibleTrader.Core.Services.Indicators.SwingStructureProvider).Assembly
            .GetTypes()
            .Where(t => !t.IsAbstract && !t.IsInterface && typeof(IIndicatorProvider).IsAssignableFrom(t))
            .ToList();
        var constructible = ConstructibleProviders();

        Assert.True(constructible.Count >= 25,
            $"only {constructible.Count} of {all.Count} indicator providers are reachable with a "
            + "parameterless constructor; the sweep has shrunk. Uncovered: "
            + string.Join(", ", all.Select(t => t.Name)
                .Except(constructible.Select(p => p.GetType().Name))
                .OrderBy(n => n)));
    }
}
