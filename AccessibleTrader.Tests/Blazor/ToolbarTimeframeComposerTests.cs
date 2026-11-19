using System.Reactive.Subjects;
using AccessibleTrader.Core.Services;
using Bunit;
using NSubstitute;
using Cmp = AccessibleTrader.BlazorClient.Components;

namespace AccessibleTrader.Tests.Blazor;

/// <summary>
/// The toolbar's custom timeframe composer follows the chart, not only its own edits.
///
/// <para>The Go bar's "TF 4h" changes the orchestrator's selected timeframe from outside the
/// toolbar. Until 6b the PipelineUpdated subscription only re-rendered, so the multiplier and
/// unit stayed at their init values (1 / hr) and the next edit of the multiplier wrote a 1h-based
/// timeframe back over the chart. This pins the re-parse.</para>
/// </summary>
public sealed class ToolbarTimeframeComposerTests
{
    [Fact]
    public void TheComposerShowsTheNewTimeframeWhenItChangesFromOutside()
    {
        using var h = new BlazorTestHarness();
        var updated = new Subject<MarketOrchestrator.Unit>();
        h.MarketOrchestrator.SelectedTimeframe.Returns("1h");
        h.MarketOrchestrator.AvailableTimeframes.Returns(new List<string> { "1h", "4h" });
        h.MarketOrchestrator.PipelineUpdated.Returns(updated);

        var cut = h.Ctx.RenderComponent<Cmp.Toolbar>();
        cut.WaitForAssertion(() =>
        {
            Assert.Equal("1", cut.Find("#tf-mult").GetAttribute("value"));
            Assert.Equal("h", cut.Find("#tf-unit").GetAttribute("value"));
        });

        h.MarketOrchestrator.SelectedTimeframe.Returns("4h");
        updated.OnNext(MarketOrchestrator.Unit.Default);

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("4", cut.Find("#tf-mult").GetAttribute("value"));
            Assert.Equal("h", cut.Find("#tf-unit").GetAttribute("value"));
        });
    }
}
