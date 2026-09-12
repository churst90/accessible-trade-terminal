using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Audio;
using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Mocks;

namespace AccessibleTrader.Tests;

/// <summary>
/// <b>An indicator's neutral line is DECLARED, the way its bounds are — it is not inferred from
/// the indicator's name.</b>
///
/// <para>
/// <c>ComponentConfig.ReferenceLevel</c> is the value a component swings about. Three things read
/// it: the audio layer splits its above/below waveforms there, the amplitude mapping measures
/// deviation from it, and since 2026-09-06 the <c>0</c> key marks it. Until this pass it was
/// resolved by a four-step chain whose second step was a substring match on the indicator code
/// (RSI→50, MACD→0, STOCH→50, WILLIAMS→−50) — so the four bounded oscillators whose names contain
/// none of those words fell through to step 3, the display-type default of <b>0.0</b>, and got a
/// neutral sitting on the floor of a 0–100 pane.
/// </para>
///
/// <para>
/// MFI, the Ultimate Oscillator, Choppiness and STC all did. Their waveform never flipped — every
/// value is "above" a line at 0 that they never visit — and pressing <c>0</c> on the three of them
/// with no declared midline added a level named "Zero" at the very bottom of the pane, which is
/// precisely the defect <see cref="Core.Services.Input.ReferenceLevelPlacement"/> was created to
/// retire. MFI escaped the second half only because it happens to declare "Midpoint 50".
/// </para>
///
/// <para>
/// The fix is the same shape as the bounds fix that preceded it: state the property once, on the
/// component, and delete the by-name guessing. These tests pin the property, not the spelling.
/// </para>
/// </summary>
public sealed class DeclaredNeutralTests
{
    private static readonly IndicatorModelFactory Factory = new(
        new StylingService(new ComponentRoleMapper(), new SonificationProfileProvider(), new PaneAssignmentService()),
        new MockIndicatorPreferencesService());

    private static IEnumerable<(IIndicatorProvider Provider, IndicatorMetadata Meta)> Fleet() =>
        IndicatorProviderFixture.AllProviders().SelectMany(p => p.GetIndicators().Select(m => (p, m)));

    private static ChartSeries Build(IndicatorMetadata meta) =>
        Factory.CreateSeriesFromMetadata(meta, meta.Name, PaneAssignmentService.PaneFor(meta),
            new List<(string, string)>(), null, null);

    /// <summary>
    /// The defect, as a property: on a pane whose axis is pinned to declared bounds, a neutral
    /// sitting ON a bound is a line the value can never cross from both sides. It is never right,
    /// and it is what "fell through to 0.0" always produces.
    /// </summary>
    [Fact]
    public void NoComponentsNeutral_SitsOnADeclaredBound()
    {
        var offenders = new List<string>();
        foreach (var (_, meta) in Fleet())
        {
            if (meta.RangeMin is not double lo || meta.RangeMax is not double hi) continue;
            foreach (var c in Build(meta).Components)
            {
                if (c.ReferenceLevel is not double n) continue;
                if (n <= lo || n >= hi)
                    offenders.Add($"{meta.Code}.{c.Name}: neutral {n} on/outside the bounds [{lo}, {hi}]");
            }
        }
        Assert.True(offenders.Count == 0,
            "A neutral on a bound can never be crossed:\n  " + string.Join("\n  ", offenders));
    }

    /// <summary>
    /// The same defect one layer out: a DECLARED level whose role is Neutral must not sit on a
    /// bound either. Connors RSI declared "Zero" at 0 on a 0–100 pane — a midline at the floor,
    /// and the one the <c>0</c> key protects as "already marked".
    /// </summary>
    [Fact]
    public void NoDeclaredNeutralLevel_SitsOnADeclaredBound()
    {
        var offenders = new List<string>();
        foreach (var (provider, meta) in Fleet())
        {
            if (meta.RangeMin is not double lo || meta.RangeMax is not double hi) continue;
            IReadOnlyList<LevelDescriptor> levels;
            try { levels = provider.GetDefaultLevels(meta.Code.ToUpperInvariant()); } catch { continue; }
            foreach (var l in levels ?? Array.Empty<LevelDescriptor>())
            {
                var asConfig = new LevelConfig { Name = l.Name, Value = l.Value };
                if (asConfig.EffectiveRole != LevelRole.Neutral) continue;
                if (l.Value <= lo || l.Value >= hi)
                    offenders.Add($"{meta.Code}: neutral level '{l.Name}'={l.Value} on the bound of [{lo}, {hi}]");
            }
        }
        Assert.True(offenders.Count == 0,
            "A midline on a bound is a midline of nothing:\n  " + string.Join("\n  ", offenders));
    }

    /// <summary>
    /// Every component that swings about something says so. Oscillator and ZeroArea are the two
    /// display types whose whole sound is the above/below split, so a component of either type
    /// that declares no neutral is one the chain would answer for — with 0.0, right for MACD and
    /// wrong for MFI, and indistinguishable at the call site. Declaring it is how the next bounded
    /// oscillator added to this repo cannot repeat MFI.
    /// </summary>
    [Fact]
    public void EverySwingingComponent_DeclaresItsNeutral()
    {
        var silent = new List<string>();
        foreach (var (_, meta) in Fleet())
        foreach (var c in meta.Components)
        {
            if (c.DisplayType is not (ComponentDisplayType.Oscillator or ComponentDisplayType.ZeroArea)) continue;
            if (c.DefaultReferenceLevel == null)
                silent.Add($"{meta.Code}.{c.Name} ({c.DisplayType})");
        }
        Assert.True(silent.Count == 0,
            "Declare DefaultReferenceLevel on every Oscillator/ZeroArea component:\n  " + string.Join("\n  ", silent));
    }

    /// <summary>
    /// The four that were wrong, named, with the value each must carry. A fleet-wide property can
    /// be satisfied by declaring something plausible; this says what the right answer is.
    /// </summary>
    [Theory]
    [InlineData("Rsi", "Rsi", 50.0)]
    [InlineData("Mfi", "Mfi", 50.0)]
    [InlineData("UltOsc", "Ultimate", 50.0)]
    [InlineData("Chop", "Chop", 50.0)]
    [InlineData("Stc", "Stc", 50.0)]
    [InlineData("WilliamsR", "WilliamsR", -50.0)]
    [InlineData("ConnorsRsi", "ConnorsRsi", 50.0)]
    [InlineData("Macd", "Macd", 0.0)]
    [InlineData("Cci", "Cci", 0.0)]
    public void TheNeutral_IsWhatTheIndicatorActuallySwingsAbout(string code, string component, double expected)
    {
        var meta = Fleet().First(f => string.Equals(f.Meta.Code, code, StringComparison.OrdinalIgnoreCase)).Meta;
        var built = Build(meta).Components.First(c => c.Name == component);
        Assert.Equal(expected, built.ReferenceLevel);
    }

    /// <summary>
    /// And the source is the declaration. <c>StylingService</c> used to answer for RSI by name;
    /// a styling service that knows nothing about any indicator must now produce the same chart,
    /// because every value comes off the component metadata.
    /// </summary>
    [Fact]
    public void TheNeutral_SurvivesAStylingServiceThatKnowsNoIndicatorByName()
    {
        var blind = new IndicatorModelFactory(new MockStylingService(), new MockIndicatorPreferencesService());
        foreach (var (_, meta) in Fleet())
        {
            if (meta.RangeMin is not double lo || meta.RangeMax is not double hi) continue;
            var series = blind.CreateSeriesFromMetadata(meta, meta.Name, PaneAssignmentService.PaneFor(meta),
                new List<(string, string)>(), null, null);
            foreach (var c in series.Components)
            {
                if (c.ReferenceLevel is not double n) continue;
                Assert.True(n > lo && n < hi,
                    $"{meta.Code}.{c.Name}: neutral {n} came from somewhere other than the declaration (bounds [{lo}, {hi}])");
            }
        }
    }
}
