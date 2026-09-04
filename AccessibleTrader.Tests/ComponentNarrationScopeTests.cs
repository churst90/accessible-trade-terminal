using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Tests;

/// <summary>
/// NARRATION PER COMPONENT, and the one decision in it that is not obvious.
///
/// <para>
/// Cody, 2026-09-04: <i>"I thought I could enable narration per component too… why not change
/// the command to simply N, the same as H and M, and allow for enabling narration per component
/// too"</i>. Narration was a per-series flag and nothing else, so a Cipher B with eleven
/// components was all-or-nothing: switch it on for the divergence you care about and you also
/// get every cross, dot and band it prints.
/// </para>
///
/// <para>
/// THE DECISION: the component flags are a SELECTION under the series flag, and an EMPTY
/// selection means ALL, not NONE. An AND would have silenced every series that exists today —
/// all of them narrate with no component flagged — which is a feature deleting itself in a
/// release nobody would connect to the change.
/// </para>
/// </summary>
public class ComponentNarrationScopeTests
{
    [Fact]
    public void NoComponentSelected_MeansTheWholeSeries()
    {
        // The upgrade case, and the reason the rule is not an AND. Every narrating series on
        // every existing chart looks exactly like this.
        var s = Series(narrated: true, componentFlags: new[] { false, false, false });

        Assert.All(s.Components, c => Assert.True(SeriesNarrationScope.ComponentNarrates(s, c)));
    }

    [Fact]
    public void OneComponentSelected_NarrowsToIt()
    {
        var s = Series(narrated: true, componentFlags: new[] { false, true, false });

        Assert.False(SeriesNarrationScope.ComponentNarrates(s, s.Components[0]));
        Assert.True(SeriesNarrationScope.ComponentNarrates(s, s.Components[1]));
        Assert.False(SeriesNarrationScope.ComponentNarrates(s, s.Components[2]));
    }

    [Fact]
    public void DeselectingTheLastOne_WidensBackOut_RatherThanGoingQuiet()
    {
        // N on a component composes with N on the series the way a user expects: on, narrow,
        // widen. Nothing lands in a state where narration is "on" and silent, which is the
        // state a user cannot diagnose by ear.
        var s = Series(narrated: true, componentFlags: new[] { false, true, false });
        s.Components[1].IsAutoNarrated = false;

        Assert.All(s.Components, c => Assert.True(SeriesNarrationScope.ComponentNarrates(s, c)));
    }

    [Fact]
    public void TheSeriesFlagIsTheMaster_ASelectedComponentOfASilentSeriesStaysSilent()
    {
        // Which is why the confirmation for that keypress says so out loud — see
        // SeriesReducer.ToggleNarration. Setting a component flag on a series that is not
        // narrating changes nothing audible, and announcing "narrating" there would send the
        // user off to wait for speech that never arrives.
        var s = Series(narrated: false, componentFlags: new[] { false, true, false });

        Assert.All(s.Components, c => Assert.False(SeriesNarrationScope.ComponentNarrates(s, c)));
    }

    [Theory]
    [InlineData(false, true)]   // hidden
    [InlineData(true, false)]   // muted
    public void AHiddenOrMutedSeriesNarratesNothing_HoweverItIsFlagged(bool visible, bool unmuted)
    {
        // The same rule the cross-series signal scan applies: a series producing no tone must
        // not be the only thing that speaks.
        var s = Series(narrated: true, componentFlags: new[] { false, false, false });
        s.IsVisible = visible;
        s.IsMuted = !unmuted;

        Assert.False(SeriesNarrationScope.SeriesNarrates(s));
        Assert.All(s.Components, c => Assert.False(SeriesNarrationScope.ComponentNarrates(s, c)));
    }

    private static ChartSeries Series(bool narrated, bool[] componentFlags)
    {
        var cfg = new SeriesConfig
        {
            Id = "cipher", Name = "Cipher B", FriendlyName = "Cipher B", IndicatorCode = "CIPHER_B",
            IsAutoNarrated = narrated, IsVisible = true, IsMuted = false,
        };
        var buf = new SeriesDataBuffer { SeriesId = "cipher" };
        for (int i = 0; i < componentFlags.Length; i++)
        {
            cfg.Components.Add(new ComponentConfig
            {
                Name = $"c{i}", DisplayName = $"c{i}", IsVisible = true,
                DisplayType = ComponentDisplayType.Dot,
                IsAutoNarrated = componentFlags[i],
            });
            buf.ComponentData[$"c{i}"] = new double[10];
        }
        return new ChartSeries(cfg, buf);
    }
}
