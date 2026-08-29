// ApiKeysModal — the removal flow, in bUnit.
//
// THE DEFECT THESE WERE WRITTEN AGAINST. Until 2026-08-29 the ✕ on a credential profile row
// called RemoveKeyAsync straight from the click: nothing asked whether the user meant it,
// nothing was spoken, and the button that took the click lived inside the row that then
// disappeared — so focus fell to <body>. A screen-reader user pressed a control, was returned
// to the top of the document, and could not tell whether the profile had been deleted or the
// button had done nothing at all. The profile is not recoverable: the secret lives in
// SecureStorage and this was the last copy of it.
//
// The confirmation is deliberately two-step IN PLACE rather than a nested dialog — see the
// comment on the markup. What is asserted here is the behaviour a user experiences: the first
// click removes nothing, the question is spoken, and focus is somewhere real after every one
// of the three outcomes (armed, confirmed, cancelled).
//
// Each of these goes red against the pre-fix handler, which is the whole of its value: the
// old one-line Remove() fails the "removes nothing yet" assertion, the "says so" assertion and
// the focus assertion separately.

using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace AccessibleTrader.Tests.Blazor;

public class ApiKeysModalTests
{
    private static ApiKeyConfig Key(string nickname, string provider = "Alpaca") =>
        new(provider, nickname, "key", "secret");

    private static (TestContext ctx, List<ApiKeyConfig> store, IApiKeyService svc, List<string> spoken)
        BuildContext(params string[] nicknames)
    {
        var ctx = new TestContext();

        // The service's own store, so a removal is visible to the next GetAllKeysAsync — the
        // modal re-reads the list after removing and renders from what it gets back.
        var store = nicknames.Select(n => Key(n)).ToList();
        var svc = Substitute.For<IApiKeyService>();
        svc.GetAllKeysAsync().Returns(_ => Task.FromResult(store.ToList()));
        svc.RemoveKeyAsync(Arg.Any<string>()).Returns(ci =>
        {
            store.RemoveAll(k => k.Nickname == ci.Arg<string>());
            return Task.CompletedTask;
        });

        IEventBus bus = new EventBus();
        var spoken = new List<string>();
        bus.Subscribe<FeedbackRequestEvent>(e => { if (e.Message != null) spoken.Add(e.Message); });

        ctx.Services.AddSingleton(svc);
        ctx.Services.AddSingleton(Substitute.For<IDataService>());
        ctx.Services.AddSingleton(bus);

        // SetVoidResult() is load-bearing: without it a planned void handler records the
        // invocation and never completes it, so every line after `await focusElement` is dead
        // and the test silently measures a shorter method than the app runs.
        ctx.JSInterop.SetupVoid("accessibleTrader.focusElement", _ => true).SetVoidResult();

        return (ctx, store, svc, spoken);
    }

    private static IRenderedComponent<AccessibleTrader.BlazorClient.Components.ApiKeysModal>
        OpenModal(TestContext ctx)
    {
        var bus = ctx.Services.GetRequiredService<IEventBus>();
        var cut = ctx.RenderComponent<AccessibleTrader.BlazorClient.Components.ApiKeysModal>();
        cut.InvokeAsync(() => bus.Publish(new OpenApiKeysEvent())).GetAwaiter().GetResult();
        cut.WaitForElement("h2#apikeys-title");
        return cut;
    }

    /// <summary>The ids passed to accessibleTrader.focusElement, in order.</summary>
    private static List<string> FocusTargets(TestContext ctx) =>
        ctx.JSInterop.Invocations["accessibleTrader.focusElement"]
           .Select(i => i.Arguments[0]?.ToString() ?? "")
           .ToList();

    [Fact]
    public void ClickingRemove_DeletesNothingYet_AsksTheQuestion_AndPutsFocusOnConfirm()
    {
        var (ctx, store, svc, spoken) = BuildContext("Alpaca Paper", "Alpaca Live");
        var cut = OpenModal(ctx);

        cut.Find("button[aria-label='Remove Alpaca Paper']").Click();

        cut.WaitForAssertion(() =>
        {
            // Nothing has been removed — the whole point of arming.
            svc.DidNotReceive().RemoveKeyAsync(Arg.Any<string>());
            Assert.Equal(2, store.Count);

            // The question is spoken, and it says what is at stake before it says which keys
            // answer it. A confirmation nobody hears is the same silence as before.
            Assert.Contains(spoken, m => m.Contains("Remove Alpaca Paper?", StringComparison.Ordinal)
                                      && m.Contains("cannot be undone", StringComparison.OrdinalIgnoreCase));

            // And focus is on the answer, not left on a button that no longer exists.
            Assert.Contains("apikey-remove-confirm", FocusTargets(ctx));
            Assert.NotNull(cut.Find("button#apikey-remove-confirm"));
            Assert.NotNull(cut.Find("button#apikey-remove-cancel"));
        });
    }

    [Fact]
    public void ConfirmingTheRemoval_RemovesIt_SaysSo_AndLandsOnTheRowThatTookItsPlace()
    {
        var (ctx, store, svc, spoken) = BuildContext("Alpaca Paper", "Alpaca Live");
        var cut = OpenModal(ctx);

        cut.Find("button[aria-label='Remove Alpaca Paper']").Click();
        cut.WaitForElement("button#apikey-remove-confirm").Click();

        cut.WaitForAssertion(() =>
        {
            svc.Received(1).RemoveKeyAsync("Alpaca Paper");
            Assert.Single(store);
            Assert.Contains(spoken, m => m.Contains("Alpaca Paper removed", StringComparison.Ordinal));

            // Row 0 was removed, so the profile that was row 1 is now row 0 — that is where a
            // user working down the list expects to be, and it is an element that exists.
            Assert.Equal("apikey-remove-0", FocusTargets(ctx).Last());
        });
    }

    [Fact]
    public void CancellingTheRemoval_KeepsTheProfile_SaysSo_AndReturnsFocusToItsOwnButton()
    {
        var (ctx, store, svc, spoken) = BuildContext("Alpaca Paper", "Alpaca Live");
        var cut = OpenModal(ctx);

        // The SECOND row, so a cancel that merely focused "the first remove button" would
        // pass by accident.
        cut.Find("button[aria-label='Remove Alpaca Live']").Click();
        cut.WaitForElement("button#apikey-remove-cancel").Click();

        cut.WaitForAssertion(() =>
        {
            svc.DidNotReceive().RemoveKeyAsync(Arg.Any<string>());
            Assert.Equal(2, store.Count);
            Assert.Contains(spoken, m => m.Contains("Cancelled", StringComparison.OrdinalIgnoreCase)
                                      && m.Contains("Alpaca Live", StringComparison.Ordinal));
            Assert.Equal("apikey-remove-1", FocusTargets(ctx).Last());
        });
    }

    [Fact]
    public void RemovingTheOnlyProfile_LandsOnTheAddForm_BecauseTheListIsGone()
    {
        var (ctx, store, _, spoken) = BuildContext("Alpaca Paper");
        var cut = OpenModal(ctx);

        cut.Find("button[aria-label='Remove Alpaca Paper']").Click();
        cut.WaitForElement("button#apikey-remove-confirm").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Empty(store);
            // "No profiles remain" is the fact a sighted user reads off the empty state.
            Assert.Contains(spoken, m => m.Contains("No profiles remain", StringComparison.Ordinal));
            // There is no row left to focus; the add form is the nearest thing on screen.
            Assert.Equal("key-provider", FocusTargets(ctx).Last());
        });
    }

    [Fact]
    public void EscapeWithARemovalArmed_BacksOutOfTheQuestion_NotTheDialog()
    {
        // Escape is how a screen-reader user leaves things, and it arrives at a dialog that is
        // now asking a question. Closing the whole dialog on it would put the user outside with
        // no word on what happened to the profile they were asked about. It cancels the
        // question; a second Escape closes the dialog.
        var (ctx, store, svc, spoken) = BuildContext("Alpaca Paper");
        var cut = OpenModal(ctx);
        var bus = ctx.Services.GetRequiredService<IEventBus>();

        cut.Find("button[aria-label='Remove Alpaca Paper']").Click();
        cut.WaitForElement("button#apikey-remove-confirm");

        cut.InvokeAsync(() => bus.Publish(new CloseTopModalEvent("API keys"))).GetAwaiter().GetResult();

        cut.WaitForAssertion(() =>
        {
            svc.DidNotReceive().RemoveKeyAsync(Arg.Any<string>());
            Assert.Single(store);
            Assert.Empty(cut.FindAll("button#apikey-remove-confirm"));
            // Still open — the dialog's own heading is still rendered.
            Assert.NotNull(cut.Find("h2#apikeys-title"));
            Assert.Contains(spoken, m => m.Contains("Cancelled", StringComparison.OrdinalIgnoreCase));
        });

        // Second Escape: nothing armed any more, so it closes as usual.
        cut.InvokeAsync(() => bus.Publish(new CloseTopModalEvent("API keys"))).GetAwaiter().GetResult();
        cut.WaitForAssertion(() => Assert.Equal(string.Empty, cut.Markup.Trim()));
    }

    [Fact]
    public void ReopeningTheDialogDisarmsAPendingConfirmation()
    {
        // Belt and braces on top of the Escape path: whatever left a confirmation armed, the
        // next visit must not open with "Confirm remove" on screen for a decision the user was
        // asked about a session ago.
        var (ctx, _, _, _) = BuildContext("Alpaca Paper");
        var cut = OpenModal(ctx);
        var bus = ctx.Services.GetRequiredService<IEventBus>();

        cut.Find("button[aria-label='Remove Alpaca Paper']").Click();
        cut.WaitForElement("button#apikey-remove-confirm");

        cut.InvokeAsync(() => bus.Publish(new OpenApiKeysEvent())).GetAwaiter().GetResult();

        cut.WaitForAssertion(() =>
        {
            Assert.Empty(cut.FindAll("button#apikey-remove-confirm"));
            Assert.NotNull(cut.Find("button[aria-label='Remove Alpaca Paper']"));
        });
    }
}
