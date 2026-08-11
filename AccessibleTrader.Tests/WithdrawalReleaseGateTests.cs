using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Trading;
using AccessibleTrader.Sdk.Plugins;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

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

        [Fact]
        public void Both_user_facing_surfaces_are_wired_to_the_same_flag()
        {
            // The markup stays in the tree — reverting would throw away work that is
            // probably right — so what has to be pinned is that it cannot RENDER.
            // Two surfaces expose withdrawals: the API Keys checkbox that mints the
            // profile, and the dialog itself.
            string keys   = Read("AccessibleTrader.BlazorClient.Components", "ApiKeysModal.razor");
            string layout = Read("AccessibleTrader.BlazorClient.Components", "Layout", "MainLayout.razor");

            Assert.Contains("WithdrawalService.Released", keys);
            Assert.Contains("WithdrawalService.Released", layout);

            // The checkbox must sit INSIDE the guard, not merely near it: an unguarded
            // checkbox would let a user mint a withdrawal profile, go to the venue for a
            // withdrawal key, and find no screen that takes it.
            int guard = keys.IndexOf("WithdrawalService.Released", StringComparison.Ordinal);
            int box   = keys.IndexOf("id=\"key-withdrawal\"", StringComparison.Ordinal);
            Assert.True(box > guard,
                "The withdrawal checkbox is no longer inside the release guard.");
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
