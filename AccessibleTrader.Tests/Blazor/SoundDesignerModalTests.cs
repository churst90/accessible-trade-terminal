using System.Collections.Generic;
using System.Linq;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Sdk.Models;
using Bunit;
using NSubstitute;
using Xunit;

namespace AccessibleTrader.Tests.Blazor;

/// <summary>bUnit coverage for the multi-oscillator Sound Designer modal.</summary>
public class SoundDesignerModalTests
{
    private static IRenderedComponent<AccessibleTrader.BlazorClient.Components.SoundDesignerModal>
        Open(BlazorTestHarness h) =>
        h.OpenModal<AccessibleTrader.BlazorClient.Components.SoundDesignerModal>(
            bus => bus.Publish(new OpenSoundDesignerEvent()));

    [Fact]
    public void SoundDesigner_Opens_RendersDialog()
    {
        using var h = new BlazorTestHarness();

        var cut = Open(h);

        Assert.NotEmpty(cut.FindAll("[role='dialog']"));
        Assert.Contains("Sound Designer", cut.Find("h2#sound-designer-title").TextContent);
    }

    [Fact]
    public void SoundDesigner_AddOscillator_AddsALayerRow()
    {
        using var h = new BlazorTestHarness();
        // Seed one patch so it opens selected and the Oscillators editor renders.
        var patch = new SoundPatch { Name = "Bell" };
        h.SoundPatchLibrary.GetPatches().Returns(_ => new List<SoundPatch> { patch });
        h.SoundPatchLibrary.GetPatch(patch.Id).Returns(patch);

        var cut = Open(h);

        int before = cut.FindAll("select[id^='sd-wave-']").Count;
        Assert.True(before >= 1, "the selected patch should render an initial oscillator layer");

        cut.FindAll("button").First(b => b.TextContent.Contains("Add Oscillator")).Click();

        Assert.Equal(before + 1, cut.FindAll("select[id^='sd-wave-']").Count);
    }
}
