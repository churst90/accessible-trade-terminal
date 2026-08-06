using System;
using System.Linq;
using System.Reflection;
using AccessibleTrader.Plugins.Kraken;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Plugins;
using Xunit;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Kraken's wallet implementation — the first venue behind
    /// <see cref="IWalletProvider"/>.
    ///
    /// <para>
    /// The network calls themselves need a real key and are verified by hand, so
    /// what is pinned here is everything that can be wrong WITHOUT a network: the
    /// asset codes Kraken insists on, its habit of answering HTTP 200 with an error
    /// array, and the fact that it is wired up at all.
    /// </para>
    /// </summary>
    [Collection("ProviderCredentialBridge")]
    public class KrakenWalletTests
    {
        [Fact]
        public void Kraken_is_a_wallet_provider()
        {
            // The Deposit button gates on exactly this — a compiler-enforced fact
            // rather than a declared flag.
            Assert.IsAssignableFrom<IWalletProvider>(new KrakenProvider());
        }

        [Fact]
        public void Kraken_declares_DepositAddresses_now_that_it_implements_them()
        {
            Assert.True(new KrakenProvider().Capabilities.HasFlag(ProviderCapabilities.DepositAddresses));
        }

        [Fact]
        public void Withdrawals_are_a_separate_interface_from_deposits()
        {
            // IWalletProvider is read-only by design, so implementing it says nothing
            // about being able to move funds OUT — the capability audit caught that
            // assumption the day deposits landed. Kraken now implements both, and
            // they remain two distinct interfaces with two distinct credentials.
            var kraken = new KrakenProvider();

            Assert.IsAssignableFrom<IWalletProvider>(kraken);
            Assert.IsAssignableFrom<IWithdrawalProvider>(kraken);
            Assert.True(kraken.Capabilities.HasFlag(ProviderCapabilities.Withdrawals));
        }

        [Fact]
        public void Withdrawing_takes_a_saved_destination_key_and_not_an_address()
        {
            // Kraken's Withdraw endpoint accepts the NAME of an address saved on
            // kraken.com, never an address — which is why a compromised terminal
            // could not send funds anywhere the user had not personally whitelisted
            // behind Kraken's own 2FA.
            var m = typeof(KrakenProvider).GetMethod(nameof(IWithdrawalProvider.WithdrawAsync))!;
            var names = m.GetParameters().Select(p => p.Name!.ToLowerInvariant()).ToList();

            Assert.Contains("destinationkey", names);
            Assert.DoesNotContain(names, n => n.Contains("address"));
        }

        // ── Kraken's own asset codes ─────────────────────────────────────────

        [Theory]
        [InlineData("BTC", "XBT")]      // the one that matters most and the one that breaks
        [InlineData("btc", "XBT")]
        [InlineData("DOGE", "XDG")]
        [InlineData("ETH", "ETH")]      // most assets pass through untouched
        [InlineData("USDT", "USDT")]
        public void Bitcoin_is_translated_to_Krakens_own_code(string input, string expected)
        {
            // Kraken calls bitcoin XBT. Asking it for "BTC" deposit methods returns
            // an error, and bitcoin is the single most likely asset anyone asks for.
            var m = typeof(KrakenProvider).GetMethod("NormaliseAsset",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(m);

            Assert.Equal(expected, (string)m!.Invoke(null, new object[] { input })!);
        }

        // ── Kraken answers 200 with an error array ───────────────────────────

        private static void Throw(string body, string what)
        {
            var m = typeof(KrakenProvider).GetMethod("ThrowOnKrakenError",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(m);
            try { m!.Invoke(null, new object[] { body, what }); }
            catch (TargetInvocationException ex) when (ex.InnerException is not null)
            {
                throw ex.InnerException;
            }
        }

        [Fact]
        public void An_empty_error_array_passes()
        {
            Throw("{\"error\":[],\"result\":[]}", "deposit methods");   // must not throw
        }

        [Fact]
        public void A_permission_error_becomes_an_unauthorised_exception()
        {
            // This is the case the user can actually fix — the key is missing its
            // Funding permission — so it has to reach WalletService as NotPermitted
            // rather than as a generic failure they can do nothing about.
            var ex = Assert.Throws<UnauthorizedAccessException>(() =>
                Throw("{\"error\":[\"EGeneral:Permission denied\"]}", "deposit methods"));

            Assert.Contains("kraken.com", ex.Message);
        }

        [Fact]
        public void An_invalid_key_is_also_treated_as_a_permission_problem()
        {
            var ex = Assert.Throws<UnauthorizedAccessException>(() =>
                Throw("{\"error\":[\"EAPI:Invalid key\"]}", "deposit address"));

            // Observed live 2026-08-05: Kraken answers EAPI:Invalid key for address
            // types it will not issue over the API (Lightning, kBTC L2) even when
            // the key's Funding scope demonstrably works. A message that only said
            // "check permissions" sent the user to re-make a key that was fine.
            Assert.Contains("address types it does not issue over the API", ex.Message);
            Assert.Contains("Funding permissions", ex.Message);
        }

        [Fact]
        public void Any_other_kraken_error_is_surfaced_with_its_own_wording()
        {
            // Kraken returns HTTP 200 for these, so a caller checking only the status
            // code sees success and an empty result — the silent read-path failure
            // this whole line of work exists to remove.
            var ex = Assert.Throws<InvalidOperationException>(() =>
                Throw("{\"error\":[\"EFunding:Unknown asset\"]}", "deposit methods for FOO"));

            Assert.Contains("EFunding:Unknown asset", ex.Message);
            Assert.Contains("deposit methods for FOO", ex.Message);
        }
    }
}
