using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Trading;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Trading;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// The wallet read path. Its job is not merely to fetch an address but to
    /// refuse to show a bad one, and to distinguish the several different ways
    /// "no address" can be true — on the one screen in the terminal where being
    /// wrong costs money directly rather than through a trade.
    /// </summary>
    public class WalletServiceTests
    {
        private const string BtcLegacy = "1A1zP1eP5QGefi2DMPTfTL5SLmv7DivfNa";

        private static (WalletService Svc, IWalletProvider Wallet) Build()
        {
            var data = Substitute.For<IDataService>();
            var provider = Substitute.For<IMarketDataProvider, IWalletProvider>();
            data.GetProviderAsync("Kraken").Returns(Task.FromResult<IMarketDataProvider?>(provider));
            return (new WalletService(data, NullLogger<WalletService>.Instance), (IWalletProvider)provider);
        }

        private static WalletService WithoutWallet()
        {
            var data = Substitute.For<IDataService>();
            // A trading provider that is NOT a wallet provider — Schwab, Alpaca, any
            // equity broker.
            var plain = Substitute.For<IMarketDataProvider>();
            // NSubstitute auto-fills interface-returning methods with substitutes, so
            // GetCapability<IWalletProvider>() would come back non-null and the fake
            // would claim a wallet the real provider does not have. Real providers
            // implement it as `this as T`, which is null here.
            plain.GetCapability<IWalletProvider>().Returns((IWalletProvider?)null);
            data.GetProviderAsync("Schwab").Returns(Task.FromResult<IMarketDataProvider?>(plain));
            return new WalletService(data, NullLogger<WalletService>.Instance);
        }

        // ── Which providers have a wallet at all ─────────────────────────────

        [Fact]
        public async Task A_provider_without_the_interface_has_no_wallet()
        {
            // This is what keeps the Wallet button off equity brokers, and it is a
            // compiler-enforced fact rather than a declared flag — flags in this
            // codebase have been wrong in both directions.
            Assert.False(await WithoutWallet().HasWalletAsync("Schwab"));
        }

        [Fact]
        public async Task A_provider_with_the_interface_has_a_wallet()
        {
            var (svc, _) = Build();

            Assert.True(await svc.HasWalletAsync("Kraken"));
        }

        [Fact]
        public async Task Asking_a_non_wallet_provider_for_an_address_says_it_is_unsupported()
        {
            // NotSupported, not Failed — nothing went wrong, the venue simply has no
            // such thing, and the user should not go looking for a fault.
            var r = await WithoutWallet().GetAddressAsync("Schwab", "BTC", "Bitcoin");

            Assert.Equal(ResultKind.NotSupported, r.Kind);
        }

        // ── Networks ─────────────────────────────────────────────────────────

        [Fact]
        public async Task No_networks_means_the_asset_cannot_be_deposited_not_that_the_call_failed()
        {
            var (svc, wallet) = Build();
            wallet.GetDepositNetworksAsync("XYZ", Arg.Any<CancellationToken>())
                  .Returns(Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>()));

            var r = await svc.GetNetworksAsync("Kraken", "XYZ");

            Assert.Equal(ResultKind.NotSupported, r.Kind);
            Assert.Contains("does not accept XYZ deposits", r.Reason);
        }

        [Fact]
        public async Task A_refused_networks_call_is_reported_as_a_permission_problem()
        {
            // KYC incomplete or a key without the wallet scope — actionable at the
            // venue, so it must not read as a transient failure.
            var (svc, wallet) = Build();
            wallet.GetDepositNetworksAsync("BTC", Arg.Any<CancellationToken>())
                  .Returns<Task<IReadOnlyList<string>>>(_ => throw new UnauthorizedAccessException("403"));

            var r = await svc.GetNetworksAsync("Kraken", "BTC");

            Assert.Equal(ResultKind.NotPermitted, r.Kind);
        }

        // ── The address itself ───────────────────────────────────────────────

        [Fact]
        public async Task A_good_address_comes_back_with_its_validation()
        {
            var (svc, wallet) = Build();
            wallet.GetDepositAddressAsync("BTC", "Bitcoin", Arg.Any<CancellationToken>())
                  .Returns(Task.FromResult(new DepositAddress("BTC", "Bitcoin", BtcLegacy)));

            var r = await svc.GetAddressAsync("Kraken", "BTC", "Bitcoin");

            Assert.True(r.IsOk);
            Assert.Equal(BtcLegacy, r.Value!.Address.Address);
            Assert.Equal(AddressCheck.Verified, r.Value.Validation.Result);
        }

        [Fact]
        public async Task A_malformed_address_is_REFUSED_not_shown_with_a_warning()
        {
            // The whole point of a warning is that it can be ignored, and on this
            // screen ignoring it means the funds are gone. There is no legitimate
            // reason for a venue's own API to return a corrupt address, so it is
            // treated as the alarm it is.
            var (svc, wallet) = Build();
            string corrupt = BtcLegacy[..^1] + (BtcLegacy[^1] == 'a' ? 'b' : 'a');
            wallet.GetDepositAddressAsync("BTC", "Bitcoin", Arg.Any<CancellationToken>())
                  .Returns(Task.FromResult(new DepositAddress("BTC", "Bitcoin", corrupt)));

            var r = await svc.GetAddressAsync("Kraken", "BTC", "Bitcoin");

            Assert.False(r.IsOk);
            Assert.Null(r.Value);                       // never handed to the UI
            Assert.Contains("Do not send funds", r.Reason);
        }

        [Fact]
        public async Task An_unverifiable_but_well_formed_address_is_still_shown()
        {
            // Refusing everything we cannot checksum would leave EVM and Solana
            // deposits unusable. It is shown, with the limit stated.
            var (svc, wallet) = Build();
            wallet.GetDepositAddressAsync("USDC", "ERC20", Arg.Any<CancellationToken>())
                  .Returns(Task.FromResult(new DepositAddress(
                      "USDC", "ERC20", "0x52908400098527886E0F7030069857D2E4169EE7")));

            var r = await svc.GetAddressAsync("Kraken", "USDC", "ERC20");

            Assert.True(r.IsOk);
            Assert.Equal(AddressCheck.StructureOnly, r.Value!.Validation.Result);
        }

        // ── What gets spoken ─────────────────────────────────────────────────

        [Fact]
        public void The_network_is_spoken_before_anything_else()
        {
            // Sending to the right address over the wrong chain destroys the funds,
            // and it is a more common loss than any attack.
            string said = WalletService.Announce(new CheckedDepositAddress(
                new DepositAddress("USDC", "Solana", "11111111111111111111111111111111"),
                new AddressValidation(AddressCheck.StructureOnly, "x")));

            Assert.StartsWith("USDC deposit address on the Solana network.", said);
        }

        [Fact]
        public void A_required_memo_is_announced_in_the_strongest_terms()
        {
            // Omitting a destination tag loses the funds exactly as completely as a
            // wrong address, so it is not a footnote.
            string said = WalletService.Announce(new CheckedDepositAddress(
                new DepositAddress("XRP", "Ripple", "rXXXXXXXXXXXXXXXXXXXXXXXXXXXXXX",
                    Memo: "12345", MemoLabel: "destination tag"),
                new AddressValidation(AddressCheck.Unknown, "x")));

            Assert.Contains("REQUIRES a destination tag", said);
            Assert.Contains("the funds are lost", said);
        }

        [Fact]
        public void The_minimum_and_confirmation_count_are_said_when_known()
        {
            string said = WalletService.Announce(new CheckedDepositAddress(
                new DepositAddress("BTC", "Bitcoin", BtcLegacy, MinimumDeposit: 0.0005, Confirmations: 3),
                new AddressValidation(AddressCheck.Verified, "x")));

            Assert.Contains("Minimum 0.0005 BTC", said);
            Assert.Contains("3 confirmations", said);
        }

        [Fact]
        public void An_unverified_format_tells_the_user_to_compare_capitals()
        {
            // Speech drops case and most address formats are case-sensitive, so this
            // is the sentence that sends the user to braille or the review cursor.
            string said = WalletService.Announce(new CheckedDepositAddress(
                new DepositAddress("USDC", "ERC20", "0x52908400098527886E0F7030069857D2E4169EE7"),
                new AddressValidation(AddressCheck.StructureOnly, "x")));

            Assert.Contains("character by character", said);
            Assert.Contains("capitals", said);
        }

        [Fact]
        public void A_verified_checksum_is_stated_so_the_user_knows_it_was_checked()
        {
            string said = WalletService.Announce(new CheckedDepositAddress(
                new DepositAddress("BTC", "Bitcoin", BtcLegacy),
                new AddressValidation(AddressCheck.Verified, "x")));

            Assert.Contains("checksum was verified", said);
        }
    }
}
