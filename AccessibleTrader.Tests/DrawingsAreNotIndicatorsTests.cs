using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Tests;

/// <summary>
/// <b>A drawing is not an indicator, and the Add Indicator dialog must not offer one.</b>
///
/// <para>
/// Cody, 2026-09-13: <i>"I don't think drawing tools should be in the indicators add dialog,
/// that's why the alt d modal exists. Because inserting the measure tool on the chart just inserts
/// a series with 0 components."</i> Both halves were true, and the second explains the first.
/// </para>
///
/// <para>
/// Fifteen drawing types were registered as <c>IndicatorMetadata</c> with an EMPTY component list,
/// and nothing in the app ever looked one up. What they did do was appear in the Add Indicator
/// dialog, which offers whatever the registry returns — so choosing one built a series with no
/// components: no anchors to place, nothing to draw, nothing to navigate, nothing to hear.
/// <b>A menu entry that produces an inert object is worse than a missing one</b>, because the user
/// cannot tell it apart from a feature that has broken.
/// </para>
///
/// <para>
/// The real route is the Drawing Tools dialog (Alt+D) and the shortcut chords, both of which run
/// the anchor state machine and ask for each point by name. That was always the whole route; these
/// entries were a second, broken one.
/// </para>
/// </summary>
public sealed class DrawingsAreNotIndicatorsTests
{
    private static IEnumerable<IndicatorMetadata> Fleet() =>
        IndicatorProviderFixture.AllProviders().SelectMany(p => p.GetIndicators());

    /// <summary>
    /// The general property, so a new drawing cannot be added to the registry "just so it has a
    /// name": nothing the Add Indicator dialog offers may be componentless.
    /// </summary>
    [Fact]
    public void NoOfferedIndicatorHasZeroComponents()
    {
        var empty = Fleet()
            .Where(m => !m.IsDeprecated)
            .Where(m => m.Components.Count == 0)
            .Select(m => $"{m.Code} ({m.Category})")
            .ToList();

        Assert.True(empty.Count == 0,
            "These would appear in Add Indicator and build a series with nothing in it:\n  "
          + string.Join("\n  ", empty));
    }

    /// <summary>The category itself is gone — a drawing has no business in the indicator registry.</summary>
    [Fact]
    public void TheRegistryOffersNoDrawingsCategory()
    {
        var drawings = Fleet()
            .Where(m => m.Category.Contains("Drawing", StringComparison.OrdinalIgnoreCase))
            .Select(m => m.Code)
            .ToList();

        Assert.True(drawings.Count == 0,
            "Drawings are placed through Alt+D or their shortcut chords, not through Add "
          + "Indicator: " + string.Join(", ", drawings));
    }

    /// <summary>
    /// Named, because these are the ones that were there. A regression that re-adds "MEASURE"
    /// should fail on the name a person would search for.
    /// </summary>
    [Theory]
    [InlineData("MEASURE")] [InlineData("RISKREWARD")] [InlineData("TREND")]
    [InlineData("FIB")] [InlineData("FIBEXT")] [InlineData("CHANNEL")]
    [InlineData("RECT")] [InlineData("LABEL")] [InlineData("HORIZONTAL")]
    [InlineData("VERTICAL")] [InlineData("GANNFAN")] [InlineData("GANNBOX")]
    [InlineData("PITCHFORK")] [InlineData("ANGLEFIB")] [InlineData("AVWAP")]
    public void TheDeletedPlaceholdersStayDeleted(string code)
    {
        Assert.DoesNotContain(Fleet(), m => m.Code.Equals(code, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// And the real Anchored VWAP — a genuine indicator with components — is still offered. The
    /// deleted "AVWAP" was a second, empty entry for the same name, which is why removing it must
    /// not remove the working one.
    /// </summary>
    [Fact]
    public void TheRealAnchoredVwapIsStillOffered()
    {
        var real = Fleet().FirstOrDefault(m =>
            m.Name.Contains("Anchored VWAP", StringComparison.OrdinalIgnoreCase));

        Assert.NotNull(real);
        Assert.NotEmpty(real!.Components);
    }
}
