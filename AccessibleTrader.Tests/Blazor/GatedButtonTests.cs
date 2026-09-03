// GatedButton — a button that is unavailable rather than absent.
//
// The defect it replaces was demonstrated on 2026-09-02 with a bUnit probe against the
// real order ticket: type 0 into Quantity and "Submit Buy Order" came back
// `disabled=True` — out of the tab order, out of the screen reader's button list, with
// no aria-invalid on the field that caused it and nothing said. For a sighted user a
// disabled button is greyed out and still on screen, and its position says which field
// to go back and fix. For this product's user it is deletion.
//
// The tests below are about the three things that make the swap safe rather than merely
// louder: the handler is still unreachable while blocked; the gate is re-read AT CLICK
// TIME rather than trusted from the last render; and the reason is a real sentence
// reachable the way a screen reader reaches it.

using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Accessibility;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Cmp = AccessibleTrader.BlazorClient.Components;

namespace AccessibleTrader.Tests.Blazor;

public sealed class GatedButtonTests
{
    private static (TestContext Ctx, IEventBus Bus, List<FeedbackRequestEvent> Spoken) NewContext()
    {
        var ctx = new TestContext();
        var bus = new EventBus();
        ctx.Services.AddSingleton<IEventBus>(bus);
        var spoken = new List<FeedbackRequestEvent>();
        bus.Subscribe<FeedbackRequestEvent>(spoken.Add);
        return (ctx, bus, spoken);
    }

    [Fact]
    public void Blocked_it_stays_reachable_announces_unavailable_and_carries_the_reason()
    {
        var (ctx, _, spoken) = NewContext();
        using var _c = ctx;
        int clicks = 0;

        var cut = ctx.RenderComponent<Cmp.GatedButton>(p => p
            .Add(x => x.Gate, () => "Enter a quantity above zero.")
            .Add(x => x.OnClick, () => { clicks++; })
            .AddChildContent("Submit Buy Order"));

        var btn = cut.Find("button");

        // Reachable. This is the whole point — a natively disabled button is not in the
        // tab order and does not appear in NVDA's or JAWS's button list at all.
        Assert.False(btn.HasAttribute("disabled"));
        Assert.Equal("true", btn.GetAttribute("aria-disabled"));
        Assert.Equal("Enter a quantity above zero.", GatedButtonAssert.ReasonOf(cut, btn));

        btn.Click();
        Assert.Equal(0, clicks);
        var e = Assert.Single(spoken);
        Assert.Equal(FeedbackType.Boundary, e.Type);
        Assert.Equal("Enter a quantity above zero.", e.Message);
    }

    [Fact]
    public void Available_it_says_nothing_extra_and_runs_the_handler()
    {
        var (ctx, _, spoken) = NewContext();
        using var _c = ctx;
        int clicks = 0;

        var cut = ctx.RenderComponent<Cmp.GatedButton>(p => p
            .Add(x => x.Gate, () => null)
            .Add(x => x.OnClick, () => { clicks++; })
            .AddChildContent("Submit"));

        var btn = cut.Find("button");
        GatedButtonAssert.IsAvailable(cut, btn);

        btn.Click();
        Assert.Equal(1, clicks);
        Assert.Empty(spoken);
    }

    [Fact]
    public void The_reason_span_is_present_but_empty_when_the_button_works()
    {
        // Two rules pulling in opposite directions, and both matter. The span cannot be
        // removed when the gate opens — aria-describedby would then point at nothing, and
        // a dangling IDREF is handled differently by every screen reader. Its TEXT must
        // go, or an enabled Confirm would still announce "Enter a quantity above zero."
        var (ctx, _, _) = NewContext();
        using var _c = ctx;

        var cut = ctx.RenderComponent<Cmp.GatedButton>(p => p
            .Add(x => x.Gate, () => null)
            .AddChildContent("Submit"));

        var btn = cut.Find("button");
        var id = btn.GetAttribute("aria-describedby");
        Assert.False(string.IsNullOrWhiteSpace(id));
        var span = cut.Find("#" + id);
        Assert.Equal("", span.TextContent.Trim());
    }

    [Fact]
    public void The_gate_is_read_at_CLICK_time_not_from_the_last_render()
    {
        // This is the case that decides whether aria-disabled can replace `disabled` at
        // all. The browser blocks a click on a natively disabled button; it delivers one
        // to an aria-disabled button quite happily. So a second Enter arriving after the
        // in-flight latch is set but BEFORE the re-render — which is exactly the
        // double-Enter the order ticket's latch exists because of, having once placed two
        // live orders — would reach the handler if Blocked were a cached bool parameter.
        //
        // Here the gate closes without the component being re-rendered at all: the
        // captured flag flips, no parameter changes, no StateHasChanged. If the click
        // path trusted the last render, `ran` would be 1.
        var (ctx, _, spoken) = NewContext();
        using var _c = ctx;

        bool inFlight = false;
        int ran = 0;

        var cut = ctx.RenderComponent<Cmp.GatedButton>(p => p
            .Add(x => x.Gate, () => inFlight ? "An order is already being sent." : null)
            .Add(x => x.OnClick, () => { ran++; })
            .AddChildContent("Submit"));

        var btn = cut.Find("button");
        GatedButtonAssert.IsAvailable(cut, btn);   // it was open when it was drawn

        inFlight = true;                           // …and shut underneath the rendered DOM
        btn.Click();

        Assert.Equal(0, ran);
        Assert.Equal("An order is already being sent.", Assert.Single(spoken).Message);
    }

    [Fact]
    public void A_gate_with_two_causes_reports_the_one_that_applies()
    {
        // A reason that names the wrong cause is worse than no reason: it sends the user
        // to edit a field that was never the problem. The gate returns the sentence, so
        // the state and the explanation cannot disagree.
        var (ctx, _, _) = NewContext();
        using var _c = ctx;

        double qty = 0;
        bool inFlight = false;

        var cut = ctx.RenderComponent<Cmp.GatedButton>(p => p
            .Add(x => x.Gate, () => inFlight ? "An order is already being sent."
                                  : qty <= 0  ? "Enter a quantity above zero."
                                  : null)
            .AddChildContent("Submit"));

        Assert.Equal("Enter a quantity above zero.", GatedButtonAssert.ReasonOf(cut, cut.Find("button")));

        qty = 1; inFlight = true;
        cut.Render();
        Assert.Equal("An order is already being sent.", GatedButtonAssert.ReasonOf(cut, cut.Find("button")));
    }

    [Fact]
    public void A_money_button_can_raise_the_refusal_above_the_speech_mute()
    {
        // FeedbackType.Boundary speaks on SpeechChannel.Manual, which F2 silences. That is
        // right for "no more signals in this direction" and wrong for a refused order: a
        // muted terminal would answer the money button with an earcon and no account of
        // why, on a screen with no visual channel to fall back on. Same shape as the
        // live-order readback F2 silenced until 2026-09-01.
        var (ctx, _, spoken) = NewContext();
        using var _c = ctx;

        var cut = ctx.RenderComponent<Cmp.GatedButton>(p => p
            .Add(x => x.Gate, () => "Enter a quantity above zero.")
            .Add(x => x.Channel, SpeechChannel.Critical)
            .AddChildContent("Submit"));

        cut.Find("button").Click();
        Assert.Equal(SpeechChannel.Critical, Assert.Single(spoken).Channel);
    }

    [Fact]
    public void An_ordinary_refusal_keeps_the_default_channel()
    {
        // The control for the test above: without this, a component that hard-coded
        // Critical everywhere would pass it, and every "choose a list first" would
        // shout through a mute the user asked for.
        var (ctx, _, spoken) = NewContext();
        using var _c = ctx;

        var cut = ctx.RenderComponent<Cmp.GatedButton>(p => p
            .Add(x => x.Gate, () => "Choose a watchlist first.")
            .AddChildContent("Delete list"));

        cut.Find("button").Click();
        Assert.Null(Assert.Single(spoken).Channel);
    }

    [Fact]
    public void The_call_sites_own_description_is_kept_and_the_reason_is_read_first()
    {
        var (ctx, _, _) = NewContext();
        using var _c = ctx;

        var cut = ctx.RenderComponent<Cmp.GatedButton>(p => p
            .Add(x => x.Gate, () => "Enter a quantity above zero.")
            .AddUnmatched("aria-describedby", "standing-hint")
            .AddChildContent("Submit"));

        var described = cut.Find("button").GetAttribute("aria-describedby")!.Split(' ');
        Assert.Equal(2, described.Length);
        Assert.Equal("standing-hint", described[1]);
        // The reason leads: a description is read in listed order, and the cause of a
        // refusal must not arrive behind a paragraph of standing hint text.
        Assert.StartsWith("gated-", described[0]);
    }

    [Fact]
    public void The_call_sites_class_and_label_survive_the_splat()
    {
        var (ctx, _, _) = NewContext();
        using var _c = ctx;

        var cut = ctx.RenderComponent<Cmp.GatedButton>(p => p
            .Add(x => x.Gate, () => null)
            .AddUnmatched("class", "submit-btn buy")
            .AddUnmatched("aria-label", "Confirm Buy 0.5 BTC/USD")
            .AddChildContent("Confirm"));

        var btn = cut.Find("button");
        Assert.Equal("submit-btn buy", btn.GetAttribute("class"));
        Assert.Equal("Confirm Buy 0.5 BTC/USD", btn.GetAttribute("aria-label"));
        Assert.Equal("button", btn.GetAttribute("type"));
    }
}
