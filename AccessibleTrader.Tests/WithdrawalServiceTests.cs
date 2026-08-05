using System;
using System.Collections.Generic;
using System.Linq;
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
    /// The only path in the terminal that moves money OFF a venue.
    ///
    /// <para>
    /// Three controls hold it together and each is tested as a control rather than
    /// as a message: the credential is separate from the trading key, destinations
    /// come only from the venue's own whitelist, and the confirmation is enforced
    /// in the SERVICE rather than only by the screen that draws it.
    /// </para>
    /// </summary>
    public class WithdrawalServiceTests
    {
        private const string Provider = "Kraken";

        private static (WithdrawalService Svc, IWithdrawalProvider W, IApiKeyService Keys) Build(
            bool withdrawalKeyExists = true)
        {
            var data = Substitute.For<IDataService>();
            var provider = Substitute.For<IMarketDataProvider, IWithdrawalProvider>();
            data.GetProviderAsync(Provider).Returns(Task.FromResult<IMarketDataProvider?>(provider));

            var keys = Substitute.For<IApiKeyService>();
            keys.GetWithdrawalKeyAsync(Provider).Returns(Task.FromResult<ApiKeyConfig?>(
                withdrawalKeyExists
                    ? new ApiKeyConfig(Provider, "withdrawals", "k", "s", AllowsWithdrawal: true)
                    : null));

            return (new WithdrawalService(data, keys, NullLogger<WithdrawalService>.Instance),
                    (IWithdrawalProvider)provider, keys);
        }

        private static WithdrawalDestination Dest(string key = "cold-wallet") =>
            new(key, "BTC", "bc1qexample", "Bitcoin");

        // ── The separate credential ──────────────────────────────────────────

        [Fact]
        public async Task Without_a_withdrawal_key_nothing_is_possible()
        {
            // The point of the separation. There is deliberately NO fallback to the
            // trading key: "not configured" is the correct and safe answer.
            var (svc, _, _) = Build(withdrawalKeyExists: false);

            Assert.False(await svc.CanWithdrawAsync(Provider));

            var r = await svc.GetDestinationsAsync(Provider, "BTC");
            Assert.Equal(ResultKind.NotPermitted, r.Kind);
            // Says WHY the separation exists, not merely that something is missing —
            // otherwise the obvious "fix" is to grant withdrawal to the trading key.
            Assert.Contains("withdrawal-enabled", r.Reason);
            Assert.Contains("never move funds", r.Reason);
        }

        [Fact]
        public async Task The_withdrawal_path_asks_only_for_the_withdrawal_key()
        {
            // It must never reach for the active TRADING credential — that is the
            // rejoining of powers this design exists to prevent.
            var (svc, w, keys) = Build();
            w.GetWithdrawalDestinationsAsync("BTC", Arg.Any<CancellationToken>())
             .Returns(Task.FromResult<IReadOnlyList<WithdrawalDestination>>(new[] { Dest() }));

            await svc.GetDestinationsAsync(Provider, "BTC");

            await keys.Received().GetWithdrawalKeyAsync(Provider);
            await keys.DidNotReceive().GetActiveKeyForProviderAsync(Arg.Any<string>(), Arg.Any<string>());
            await keys.DidNotReceive().GetKeyForProviderAsync(Arg.Any<string>(), Arg.Any<string>());
        }

        [Fact]
        public async Task A_provider_without_the_interface_cannot_withdraw()
        {
            var data = Substitute.For<IDataService>();
            var plain = Substitute.For<IMarketDataProvider>();
            plain.GetCapability<IWithdrawalProvider>().Returns((IWithdrawalProvider?)null);
            data.GetProviderAsync("Alpaca").Returns(Task.FromResult<IMarketDataProvider?>(plain));
            var svc = new WithdrawalService(data, Substitute.For<IApiKeyService>(),
                                            NullLogger<WithdrawalService>.Instance);

            Assert.False(await svc.CanWithdrawAsync("Alpaca"));
        }

        // ── Destinations come from the venue ─────────────────────────────────

        [Fact]
        public async Task An_empty_whitelist_says_the_fix_is_at_the_venue()
        {
            // This terminal cannot add a destination, by design — so the message has
            // to send the user to the right place rather than look like a fault.
            var (svc, w, _) = Build();
            w.GetWithdrawalDestinationsAsync("BTC", Arg.Any<CancellationToken>())
             .Returns(Task.FromResult<IReadOnlyList<WithdrawalDestination>>(Array.Empty<WithdrawalDestination>()));

            var r = await svc.GetDestinationsAsync(Provider, "BTC");

            Assert.Equal(ResultKind.NotSupported, r.Kind);
            Assert.Contains("venue's site", r.Reason);
            Assert.Contains("cannot add destinations", r.Reason);
        }

        [Fact]
        public void The_interface_offers_no_way_to_pass_a_raw_address()
        {
            // The strongest property in the design, asserted structurally: even a
            // fully compromised terminal cannot invent a destination, because there
            // is no parameter through which to express one.
            var withdraw = typeof(IWithdrawalProvider).GetMethod(nameof(IWithdrawalProvider.WithdrawAsync))!;
            var names = withdraw.GetParameters().Select(p => p.Name!.ToLowerInvariant()).ToList();

            Assert.Contains("destinationkey", names);
            Assert.DoesNotContain(names, n => n.Contains("address"));
        }

        // ── The quote ────────────────────────────────────────────────────────

        [Fact]
        public async Task A_fee_that_consumes_the_whole_amount_is_refused()
        {
            // Sending it would destroy the funds for nothing, and the venue would
            // happily accept the instruction.
            var (svc, w, _) = Build();
            w.GetWithdrawalQuoteAsync("BTC", "cold-wallet", 0.0001, Arg.Any<CancellationToken>())
             .Returns(Task.FromResult(new WithdrawalQuote("BTC", 0.0001, 0.0005, -0.0004)));

            var r = await svc.GetQuoteAsync(Provider, "BTC", "cold-wallet", 0.0001);

            Assert.False(r.IsOk);
            Assert.Contains("nothing would arrive", r.Reason);
        }

        [Fact]
        public async Task An_amount_below_the_venue_minimum_is_refused_with_the_minimum()
        {
            var (svc, w, _) = Build();
            w.GetWithdrawalQuoteAsync("BTC", "cold-wallet", 0.0001, Arg.Any<CancellationToken>())
             .Returns(Task.FromResult(new WithdrawalQuote("BTC", 0.0001, 0.00001, 0.00009, MinimumAmount: 0.001)));

            var r = await svc.GetQuoteAsync(Provider, "BTC", "cold-wallet", 0.0001);

            Assert.False(r.IsOk);
            Assert.Contains("0.001", r.Reason);
        }

        [Fact]
        public async Task A_zero_or_negative_amount_never_reaches_the_venue()
        {
            var (svc, w, _) = Build();

            var r = await svc.GetQuoteAsync(Provider, "BTC", "cold-wallet", 0);

            Assert.False(r.IsOk);
            await w.DidNotReceive().GetWithdrawalQuoteAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<double>(), Arg.Any<CancellationToken>());
        }

        // ── The confirmation ─────────────────────────────────────────────────

        [Fact]
        public async Task An_unconfirmed_withdrawal_never_reaches_the_venue()
        {
            // Enforced in the SERVICE, not only in the screen that draws the field.
            // A confirmation that lives in the UI is a convention; this is a control.
            var (svc, w, _) = Build();

            foreach (string attempt in new[] { "", "yes", "withdraw", " Withdraw " })
            {
                var r = await svc.WithdrawAsync(Provider, "BTC", "cold-wallet", 0.5, attempt);
                Assert.False(r.IsOk);
                Assert.Contains("not confirmed", r.Reason);
            }

            await w.DidNotReceive().WithdrawAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<double>(), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task The_exact_phrase_lets_it_through()
        {
            var (svc, w, _) = Build();
            w.WithdrawAsync("BTC", "cold-wallet", 0.5, Arg.Any<CancellationToken>())
             .Returns(Task.FromResult(new WithdrawalResult("REF123", "initiated")));

            var r = await svc.WithdrawAsync(Provider, "BTC", "cold-wallet", 0.5,
                                            WithdrawalService.ConfirmationPhrase);

            Assert.True(r.IsOk);
            Assert.Equal("REF123", r.Value!.ReferenceId);
        }

        [Fact]
        public async Task A_missing_withdrawal_key_beats_even_a_correct_confirmation()
        {
            // Order matters: typing the phrase must not be able to substitute for
            // the credential that should not exist on a trading profile.
            var (svc, w, _) = Build(withdrawalKeyExists: false);

            var r = await svc.WithdrawAsync(Provider, "BTC", "cold-wallet", 0.5,
                                            WithdrawalService.ConfirmationPhrase);

            Assert.Equal(ResultKind.NotPermitted, r.Kind);
            await w.DidNotReceive().WithdrawAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<double>(), Arg.Any<CancellationToken>());
        }

        // ── The readback ─────────────────────────────────────────────────────

        [Fact]
        public void The_confirmation_leads_with_what_ARRIVES_not_what_is_sent()
        {
            // The fee is the number people are surprised by afterwards, so the net
            // is stated in the same breath as the amount.
            string said = WithdrawalService.Confirmation(
                new WithdrawalQuote("BTC", 0.5, 0.0005, 0.4995), Dest());

            Assert.Contains("0.5 BTC", said);
            Assert.Contains("Fee 0.0005", said);
            Assert.Contains("0.4995 BTC will ARRIVE", said);
        }

        [Fact]
        public void The_confirmation_names_the_destination_and_the_network()
        {
            string said = WithdrawalService.Confirmation(
                new WithdrawalQuote("BTC", 0.5, 0.0005, 0.4995), Dest());

            Assert.Contains("cold-wallet", said);
            Assert.Contains("Bitcoin network", said);
            Assert.Contains("bc1qexample", said);
        }

        [Fact]
        public void The_confirmation_says_it_cannot_be_undone_and_how_to_cancel()
        {
            // A refusal that will not say how to escape is the silent-feedback bug
            // on the one screen where hesitating is the right instinct.
            string said = WithdrawalService.Confirmation(
                new WithdrawalQuote("BTC", 0.5, 0.0005, 0.4995), Dest());

            Assert.Contains("cannot be undone", said);
            Assert.Contains(WithdrawalService.ConfirmationPhrase, said);
            Assert.Contains("cancel", said);
        }
    }
}
