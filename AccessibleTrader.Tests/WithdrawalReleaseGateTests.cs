// Razor components live in this namespace. An "unused using" sweep run before
// BlazorClient.Components has generated its component types will not see them and
// will offer to delete this line; it is used. See the same note in WebHost/Program.cs.
using AccessibleTrader.BlazorClient.Components;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Trading;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Tests.Blazor;
using Bunit;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Withdrawals ship DARK in 2.3.0.
    ///
    /// <para>
    /// The path is built and its controls are pinned in <see cref="WithdrawalServiceTests"/>
    /// and <see cref="WithdrawalReachabilityTests"/> — but no human has ever driven it
    /// against a live venue with a real withdrawal-enabled key, and it is the only path
    /// in the terminal that moves money OFF an exchange. Everything else unverified in
    /// this release costs a wasted click.
    /// </para>
    ///
    /// <para>
    /// These tests pin the gate rather than the feature: that the default is closed, that
    /// a closed gate refuses BEFORE any request could leave the machine, and that both
    /// user-facing surfaces are wired to the same flag. Opening it is a one-line change —
    /// see <see cref="WithdrawalService.Released"/> — and these tests are what tells the
    /// person making it which surfaces they just turned on.
    /// </para>
    /// </summary>
    public class WithdrawalReleaseGateTests
    {
        private const string Provider = "Kraken";

        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AccessibleTrader.slnx")))
                dir = dir.Parent;
            Assert.NotNull(dir);
            return dir!.FullName;
        }

        private static string Read(params string[] parts) =>
            File.ReadAllText(Path.Combine(RepoRoot(), Path.Combine(parts)));

        /// <summary>
        /// A service built the way the DI container builds it — no override — with a
        /// provider and a withdrawal key both present. Everything except the gate says yes.
        /// </summary>
        private static (WithdrawalService Svc, IWithdrawalProvider W) BuildAsShipped()
        {
            var data = Substitute.For<IDataService>();
            var provider = Substitute.For<IMarketDataProvider, IWithdrawalProvider>();
            data.GetProviderAsync(Provider).Returns(Task.FromResult<IMarketDataProvider?>(provider));

            var keys = Substitute.For<IApiKeyService>();
            keys.GetWithdrawalKeyAsync(Provider).Returns(Task.FromResult<ApiKeyConfig?>(
                new ApiKeyConfig(Provider, "withdrawals", "k", "s", AllowsWithdrawal: true)));

            return (new WithdrawalService(data, keys, NullLogger<WithdrawalService>.Instance),
                    (IWithdrawalProvider)provider);
        }

        [Fact]
        public void The_gate_is_closed_by_default()
        {
            // Deliberately asserts the literal shipped value. When this fails, it is
            // because someone opened the gate — which is correct for 2.3.1, and the
            // point of failing here is that they read the checklist in
            // docs/RELEASE_2.3.0_VERIFICATION.md before they do.
            Assert.False(WithdrawalService.Released,
                "Withdrawals were enabled. That is the 2.3.1 change and it requires one real "
              + "withdrawal driven end to end against a live venue first — see "
              + "docs/RELEASE_2.3.0_VERIFICATION.md. If that has happened, update this test.");
        }

        [Fact]
        public async Task Nothing_makes_withdrawal_possible_while_the_gate_is_closed()
        {
            // Provider implements the interface AND a withdrawal key exists — the two
            // conditions that are otherwise sufficient. The toolbar button reads exactly
            // this call, so a false here is the button never rendering.
            var (svc, _) = BuildAsShipped();

            Assert.False(await svc.CanWithdrawAsync(Provider));
        }

        [Fact]
        public async Task A_closed_gate_refuses_before_the_venue_is_ever_called()
        {
            // The property that matters most: not that the UI is hidden, but that no
            // request leaves the machine even if some surface is missed. A UI gate
            // someone forgets is exactly how a dark feature stops being dark.
            var (svc, w) = BuildAsShipped();

            var dest = await svc.GetDestinationsAsync(Provider, "BTC");
            Assert.Equal(ResultKind.NotPermitted, dest.Kind);

            var quote = await svc.GetQuoteAsync(Provider, "BTC", "cold-wallet", 0.5);
            Assert.Equal(ResultKind.NotPermitted, quote.Kind);

            await w.DidNotReceive().GetWithdrawalDestinationsAsync(
                Arg.Any<string>(), Arg.Any<CancellationToken>());
            await w.DidNotReceiveWithAnyArgs().WithdrawAsync(
                default!, default!, default, default);
        }

        // ── The API Keys checkbox, rendered ──────────────────────────────────
        //
        // This used to be `Assert.Contains("WithdrawalService.Released", source)` plus a
        // comparison of two IndexOf results. Both were weaker than they read: the substring is
        // satisfied by `@if (WithdrawalService.Released || _debug)`, and comparing FIRST
        // occurrence indices is textual order, not lexical nesting — it says nothing about
        // whether the checkbox is inside the block. The gate is now injected
        // (WithdrawalReleasePolicy) so the dialog can be rendered with it CLOSED and OPEN, and
        // the assertion is on what a user would actually see.

        private static IRenderedComponent<ApiKeysModal> RenderApiKeys(BlazorTestHarness h, bool? releasedOverride)
        {
            var keys = Substitute.For<IApiKeyService>();
            keys.GetAllKeysAsync().Returns(Task.FromResult(new List<ApiKeyConfig>()));
            h.With(keys);
            h.With(Substitute.For<IDataService>());
            if (releasedOverride is { } released)
                h.With(new WithdrawalReleasePolicy(released));

            return h.OpenModal<ApiKeysModal>(bus => bus.Publish(new OpenApiKeysEvent()));
        }

        [Fact]
        public void The_withdrawal_checkbox_does_not_render_while_the_gate_is_closed()
        {
            using var h = new BlazorTestHarness();
            // No policy registered at all — the shipped fallback, i.e. exactly what a user gets.
            var cut = RenderApiKeys(h, releasedOverride: null);

            Assert.Empty(cut.FindAll("#key-withdrawal"));
            Assert.Empty(cut.FindAll("#key-withdrawal-block"));

            // Anti-vacuity: the dialog really did render, so the absence above is the gate and
            // not a modal that never opened. The API-key field lives in the same fieldset as
            // the withdrawal block.
            Assert.NotNull(cut.Find("#key-apikey"));
            Assert.NotNull(cut.Find("#apikeys-save"));
        }

        [Fact]
        public void The_withdrawal_checkbox_renders_INSIDE_the_guard_when_the_gate_opens()
        {
            using var h = new BlazorTestHarness();
            var cut = RenderApiKeys(h, releasedOverride: true);

            // Present…
            var box = cut.Find("#key-withdrawal");
            Assert.Equal("checkbox", box.GetAttribute("type"));

            // …and genuinely nested inside the guarded block, which is what the old
            // first-occurrence index comparison only appeared to check. An unguarded checkbox
            // would let a user mint a withdrawal profile, go to the venue for a withdrawal key,
            // and find no screen that takes it.
            var block = cut.Find("#key-withdrawal-block");
            Assert.Single(block.QuerySelectorAll("#key-withdrawal"));

            // The explanation is part of the control, and the checkbox points at it.
            Assert.Equal("key-withdrawal-why", box.GetAttribute("aria-describedby"));
            Assert.NotNull(cut.Find("#key-withdrawal-why"));
        }

        [Fact]
        public void The_layout_only_instantiates_the_withdraw_dialog_behind_the_same_gate()
        {
            // MainLayout injects ~25 services and renders the whole app shell, so it is not
            // rendered here; the assertion is structural, but properly scoped rather than the
            // first-occurrence index comparison it replaces. The service half is what actually
            // makes this safe (A_closed_gate_refuses_before_the_venue_is_ever_called) — the
            // layout gate is defence in depth, as its own comment says.
            string layout = Read("AccessibleTrader.BlazorClient.Components", "Layout", "MainLayout.razor");

            var guarded = System.Text.RegularExpressions.Regex.Match(
                layout, @"@if\s*\(\s*Demo\.AllowTrading\s*&&\s*WithdrawalsReleased\s*\)\s*\{([^}]*)\}");

            Assert.True(guarded.Success,
                "MainLayout no longer instantiates <WithdrawModal /> behind the withdrawal release gate.");
            Assert.Contains("<WithdrawModal", guarded.Groups[1].Value);

            // And <WithdrawModal /> appears nowhere else — one gated instantiation, not two.
            Assert.Equal(1, System.Text.RegularExpressions.Regex.Matches(layout, "<WithdrawModal").Count);

            // The gate the layout reads is the injected policy, which falls back to the shipped
            // (closed) value — so a host that forgets to register it cannot open the dialog.
            Assert.Contains("WithdrawalReleasePolicy.From(Services).Released", layout);
        }

        [Fact]
        public void The_toolbar_button_needs_no_gate_of_its_own()
        {
            // Documents WHY there is nothing to assert in Toolbar.razor: it renders on
            // _canWithdraw, which is CanWithdrawAsync, which is gated in the service.
            // If that ever becomes a direct capability check, this test should start
            // failing rather than the button silently appearing.
            string toolbar = Read("AccessibleTrader.BlazorClient.Components", "Toolbar.razor");

            Assert.Contains("CanWithdrawAsync", toolbar);
            Assert.Contains("_canWithdraw", toolbar);
        }
    }
}
