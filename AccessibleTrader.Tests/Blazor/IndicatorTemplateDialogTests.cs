using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Sdk.Models;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Cmp = AccessibleTrader.BlazorClient.Components;

namespace AccessibleTrader.Tests.Blazor;

/// <summary>
/// Save says what would be saved and refuses an empty chart; Apply lists each template with its
/// description, applies through the Core path, deletes without applying, and says every outcome.
/// </summary>
public sealed class IndicatorTemplateDialogTests
{
    private static IndicatorTemplate TemplateWith(params string[] names)
    {
        var template = new IndicatorTemplate { SavedFrom = "Kraken BTCUSDT 1h" };
        foreach (var name in names)
            template.Series.Add(new SeriesConfig
            {
                Name = name,
                FriendlyName = name,
                IndicatorCode = "Ema",
                Pane = "Main",
            });
        return template;
    }

    private static IReadOnlyList<string> Buttons(IRenderedFragment cut) =>
        cut.FindAll("button").Select(b => b.TextContent.Trim()).ToList();

    // ── Save ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Save_shows_the_description_and_from_which_chart_then_saves_and_says_so()
    {
        using var h = new BlazorTestHarness();
        var capture = TemplateWith("RSI 14", "EMA 20", "EMA 50", "Cipher B");
        h.IndicatorTemplates.GetAvailable().Returns(new List<string>());
        h.IndicatorTemplates.Capture(Arg.Any<IWorkspaceStore>()).Returns(capture);
        h.WorkspaceStore.State.Returns(_ => WorkspaceState.Initial with
        {
            Identity = new ChartIdentity("Crypto", "Kraken", "BTCUSDT", "1h"),
        });
        var spoken = new List<AnnouncementEvent>();
        h.EventBus.Subscribe<AnnouncementEvent>(spoken.Add);

        var cut = h.OpenModal<Cmp.SaveIndicatorTemplateModal>(b => b.Publish(new OpenSaveIndicatorTemplateEvent()));

        Assert.Contains("What will be saved: 4 indicators: RSI 14, EMA 20, EMA 50, Cipher B",
                        cut.Markup);
        Assert.Contains("from Kraken BTCUSDT 1h", cut.Markup);

        cut.Find("#indicator-template-name-input").Change("Oscillators");
        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Save").Click();

        h.IndicatorTemplates.Received(1).Save("Oscillators", capture);
        Assert.Contains("Indicator template saved: Oscillators, 4 indicators.", spoken.Select(e => e.Message));
    }

    [Fact]
    public void Save_on_an_empty_chart_shows_the_refusal_and_writes_nothing()
    {
        using var h = new BlazorTestHarness();
        h.IndicatorTemplates.GetAvailable().Returns(new List<string>());
        h.IndicatorTemplates.Capture(Arg.Any<IWorkspaceStore>()).Returns(new IndicatorTemplate());

        var cut = h.OpenModal<Cmp.SaveIndicatorTemplateModal>(b => b.Publish(new OpenSaveIndicatorTemplateEvent()));

        Assert.Contains("This chart has no indicators to save as a template.", cut.Markup);
        cut.Find("#indicator-template-name-input").Change("Empty");
        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Save").Click();

        h.IndicatorTemplates.DidNotReceiveWithAnyArgs().Save(default!, default!);
    }

    [Fact]
    public void Save_refuses_a_name_the_service_would_refuse()
    {
        using var h = new BlazorTestHarness();
        h.IndicatorTemplates.GetAvailable().Returns(new List<string>());
        h.IndicatorTemplates.Capture(Arg.Any<IWorkspaceStore>()).Returns(TemplateWith("RSI 14"));

        var cut = h.OpenModal<Cmp.SaveIndicatorTemplateModal>(b => b.Publish(new OpenSaveIndicatorTemplateEvent()));
        cut.Find("#indicator-template-name-input").Change("..");
        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Save").Click();

        Assert.Contains("Invalid indicator template name.", cut.Markup);
        h.IndicatorTemplates.DidNotReceiveWithAnyArgs().Save(default!, default!);
    }

    [Fact]
    public void Save_lists_existing_templates_to_overwrite()
    {
        using var h = new BlazorTestHarness();
        h.IndicatorTemplates.GetAvailable().Returns(new List<string> { "Mine", "Trend" });
        h.IndicatorTemplates.Capture(Arg.Any<IWorkspaceStore>()).Returns(TemplateWith("RSI 14"));

        var cut = h.OpenModal<Cmp.SaveIndicatorTemplateModal>(b => b.Publish(new OpenSaveIndicatorTemplateEvent()));

        var options = cut.FindAll("[role='option']").Select(o => o.TextContent.Trim()).ToList();
        Assert.Equal(new[] { "Mine", "Trend" }, options);

        cut.FindAll("[role='option']").First(o => o.TextContent.Trim() == "Trend").Click();
        Assert.Equal("Trend", cut.Find("#indicator-template-name-input").GetAttribute("value"));
    }

    // ── Apply ────────────────────────────────────────────────────────────────

    [Fact]
    public void Apply_lists_each_template_with_its_description()
    {
        using var h = new BlazorTestHarness();
        h.IndicatorTemplates.GetAvailable().Returns(new List<string> { "Oscillators", "Trend" });
        h.IndicatorTemplates.Load("Oscillators").Returns(TemplateWith("RSI 14", "Cipher B"));
        h.IndicatorTemplates.Load("Trend").Returns(TemplateWith("EMA 20", "EMA 50"));

        var cut = h.OpenModal<Cmp.ApplyIndicatorTemplateModal>(b => b.Publish(new OpenApplyIndicatorTemplateEvent()));

        Assert.Contains("Replaces this chart's indicators. Symbol, timeframe and drawings stay. "
            + "Control plus Z undoes it.", cut.Markup);
        Assert.Contains("2 indicators: RSI 14, Cipher B", cut.Markup);
        Assert.Contains("2 indicators: EMA 20, EMA 50", cut.Markup);
        Assert.Equal("Delete indicator template: Oscillators",
                     cut.FindAll("button").First(b => b.TextContent.Trim() == "Delete").GetAttribute("aria-label"));
    }

    [Fact]
    public void Apply_applies_the_selected_template_and_says_what_happened()
    {
        using var h = new BlazorTestHarness();
        var template = TemplateWith("RSI 14", "Cipher B");
        h.IndicatorTemplates.GetAvailable().Returns(new List<string> { "Oscillators" });
        h.IndicatorTemplates.Load("Oscillators").Returns(template);
        h.WorkspaceStore.State.Returns(_ => WorkspaceState.Initial with
        {
            Identity = new ChartIdentity("Crypto", "Kraken", "BTCUSDT", "1h"),
        });
        var spoken = new List<AnnouncementEvent>();
        h.EventBus.Subscribe<AnnouncementEvent>(spoken.Add);

        var cut = h.OpenModal<Cmp.ApplyIndicatorTemplateModal>(b => b.Publish(new OpenApplyIndicatorTemplateEvent()));
        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Apply").Click();

        h.Ctx.Services.GetRequiredService<IWorkspaceInitializer>()
            .Received(1).ApplyIndicatorTemplate(template, "Oscillators");
        Assert.Contains("Indicator template Oscillators applied to Kraken BTCUSDT 1h: 2 indicators.",
                        spoken.Select(e => e.Message));
    }

    [Fact]
    public void Apply_says_when_a_template_could_not_be_read()
    {
        using var h = new BlazorTestHarness();
        h.IndicatorTemplates.GetAvailable().Returns(new List<string> { "Oscillators" });
        h.IndicatorTemplates.Load("Oscillators").Returns((IndicatorTemplate?)null);
        var spoken = new List<AnnouncementEvent>();
        h.EventBus.Subscribe<AnnouncementEvent>(spoken.Add);

        var cut = h.OpenModal<Cmp.ApplyIndicatorTemplateModal>(b => b.Publish(new OpenApplyIndicatorTemplateEvent()));
        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Apply").Click();

        Assert.Contains("Indicator template Oscillators could not be read.", spoken.Select(e => e.Message));
        h.Ctx.Services.GetRequiredService<IWorkspaceInitializer>()
            .DidNotReceiveWithAnyArgs().ApplyIndicatorTemplate(default!, default!);
    }

    [Fact]
    public void Delete_says_so_and_does_not_apply()
    {
        using var h = new BlazorTestHarness();
        h.IndicatorTemplates.GetAvailable().Returns(new List<string> { "Oscillators", "Trend" });
        h.IndicatorTemplates.Load("Oscillators").Returns(TemplateWith("RSI 14"));
        var spoken = new List<AnnouncementEvent>();
        h.EventBus.Subscribe<AnnouncementEvent>(spoken.Add);

        var cut = h.OpenModal<Cmp.ApplyIndicatorTemplateModal>(b => b.Publish(new OpenApplyIndicatorTemplateEvent()));
        cut.FindAll("button").First(b => b.TextContent.Trim() == "Delete").Click();

        h.IndicatorTemplates.Received(1).Delete("Oscillators");
        h.Ctx.Services.GetRequiredService<IWorkspaceInitializer>()
            .DidNotReceiveWithAnyArgs().ApplyIndicatorTemplate(default!, default!);
        Assert.Contains("Indicator template deleted: Oscillators.", spoken.Select(e => e.Message));
    }

    [Fact]
    public void An_empty_apply_list_says_there_are_none()
    {
        using var h = new BlazorTestHarness();
        h.IndicatorTemplates.GetAvailable().Returns(new List<string>());

        var cut = h.OpenModal<Cmp.ApplyIndicatorTemplateModal>(b => b.Publish(new OpenApplyIndicatorTemplateEvent()));

        Assert.Contains("No indicator templates saved yet.", cut.Markup);
        Assert.DoesNotContain("Apply", Buttons(cut));
    }

    // ── Toolbar ──────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void The_template_buttons_are_hidden_exactly_when_the_workspace_buttons_are(
        bool isDemo, bool expected)
    {
        using var h = new BlazorTestHarness();
        h.With(new DemoPolicy(isDemo: isDemo));

        var cut = h.Ctx.RenderComponent<Cmp.Toolbar>();
        var labels = cut.FindAll(".icon-btn-label").Select(l => l.TextContent.Trim()).ToList();

        Assert.Equal(expected, labels.Contains("Save workspace"));
        Assert.Equal(expected, labels.Contains("Load workspace"));
        Assert.Equal(expected, labels.Contains("Save indicator template"));
        Assert.Equal(expected, labels.Contains("Apply indicator template"));
    }
}
