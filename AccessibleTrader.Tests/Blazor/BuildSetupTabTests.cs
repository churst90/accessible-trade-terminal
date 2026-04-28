// BuildSetupTab — bUnit coverage. Thin coordinator that owns one
// EditableStrategySpec instance and composes ConditionTreeEditor +
// RiskPlanEditor + SummaryExport. Tests focus on:
//   - The composition surface (children present, ARIA structure correct).
//   - The Spec metadata edit path (name, description, side round-trip).
// Per-child tests live in their own files when written; this file covers
// only what BuildSetupTab itself owns.

using AccessibleTrader.Sdk.Plugins;
using Bunit;

namespace AccessibleTrader.Tests.Blazor;

public class BuildSetupTabTests
{
    private static IRenderedComponent<AccessibleTrader.BlazorClient.Components.BuildSetupTab>
        Render(BlazorTestHarness h) =>
        h.Ctx.RenderComponent<AccessibleTrader.BlazorClient.Components.BuildSetupTab>();

    [Fact]
    public void BuildSetupTab_RendersWithoutError()
    {
        using var h = new BlazorTestHarness();

        var cut = Render(h);

        // No throw means the composition + DI seam works end-to-end. Spot-check:
        // the tabpanel role is present.
        var panel = cut.Find("[role='tabpanel']");
        Assert.Equal("tab-build", panel.GetAttribute("aria-labelledby"));
    }

    [Fact]
    public void BuildSetupTab_StrategyIdentityFieldset_Present()
    {
        using var h = new BlazorTestHarness();

        var cut = Render(h);

        var legend = cut.FindAll("legend").FirstOrDefault(l => l.TextContent.Contains("Strategy Identity"));
        Assert.NotNull(legend);
    }

    [Fact]
    public void BuildSetupTab_NameInput_Present()
    {
        using var h = new BlazorTestHarness();

        var cut = Render(h);

        var input = cut.Find("input#bs-name");
        Assert.Equal("text", input.GetAttribute("type"));
    }

    [Fact]
    public void BuildSetupTab_DescriptionInput_Present()
    {
        using var h = new BlazorTestHarness();

        var cut = Render(h);

        var input = cut.Find("input#bs-desc");
        Assert.Equal("text", input.GetAttribute("type"));
    }

    [Fact]
    public void BuildSetupTab_SideSelect_DefaultsToBuy()
    {
        using var h = new BlazorTestHarness();

        var cut = Render(h);

        var select = cut.Find("select#bs-side");
        // The bound spec's Side defaults to Buy → the rendered <select> reflects
        // that via the @value="@_spec.Side" binding.
        Assert.Equal(OrderSide.Buy.ToString(), select.GetAttribute("value"));
    }

    [Fact]
    public void BuildSetupTab_NameInput_RoundTrips()
    {
        using var h = new BlazorTestHarness();

        var cut = Render(h);

        cut.Find("input#bs-name").Change("My Long Setup");

        // After the change, the input's value reflects the spec mutation.
        Assert.Equal("My Long Setup", cut.Find("input#bs-name").GetAttribute("value"));
    }

    [Fact]
    public void BuildSetupTab_DescriptionInput_RoundTrips()
    {
        using var h = new BlazorTestHarness();

        var cut = Render(h);

        cut.Find("input#bs-desc").Change("Free-form description");

        Assert.Equal("Free-form description", cut.Find("input#bs-desc").GetAttribute("value"));
    }

    [Fact]
    public void BuildSetupTab_SideSelect_ChangesToShort()
    {
        using var h = new BlazorTestHarness();

        var cut = Render(h);

        cut.Find("select#bs-side").Change(OrderSide.Sell.ToString());

        Assert.Equal(OrderSide.Sell.ToString(), cut.Find("select#bs-side").GetAttribute("value"));
    }

    [Fact]
    public void BuildSetupTab_TwoSideOptions_LongAndShort()
    {
        using var h = new BlazorTestHarness();

        var cut = Render(h);

        var options = cut.FindAll("select#bs-side option");
        Assert.Equal(2, options.Count);
        Assert.Equal(OrderSide.Buy.ToString(), options[0].GetAttribute("value"));
        Assert.Equal(OrderSide.Sell.ToString(), options[1].GetAttribute("value"));
    }

    [Fact]
    public void BuildSetupTab_PlaceholderHints_Present()
    {
        using var h = new BlazorTestHarness();

        var cut = Render(h);

        Assert.Equal("My Long Setup", cut.Find("input#bs-name").GetAttribute("placeholder"));
        Assert.Equal("Optional free-form description", cut.Find("input#bs-desc").GetAttribute("placeholder"));
    }
}
