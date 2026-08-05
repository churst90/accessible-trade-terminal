using AccessibleTrader.Sdk.Trading;
using Xunit;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// The one defence available on our side of the wire before a deposit address
    /// is shown. It cannot prove an address belongs to your account — nothing local
    /// can — but if the venue's own API returns something corrupt, this is what
    /// notices.
    ///
    /// <para>
    /// Real published vectors are used rather than invented strings: a checksum test
    /// that passes on made-up data is testing nothing. The mutations below flip a
    /// single character of a KNOWN-GOOD address, which is exactly the failure the
    /// checksum exists to catch.
    /// </para>
    /// </summary>
    public class CryptoAddressValidatorTests
    {
        // Genesis-block coinbase address, the most-published base58 address there is.
        private const string BtcLegacy = "1A1zP1eP5QGefi2DMPTfTL5SLmv7DivfNa";
        // BIP-173's own bech32 example.
        private const string BtcSegwit = "bc1qw508d6qejxtdg4y5r3zarvary0c5xw7kv8f3t4";

        // ── Verified ─────────────────────────────────────────────────────────

        [Fact]
        public void A_real_legacy_bitcoin_address_verifies()
        {
            var r = CryptoAddressValidator.Validate(BtcLegacy, "Bitcoin");

            Assert.Equal(AddressCheck.Verified, r.Result);
        }

        [Fact]
        public void A_real_segwit_address_verifies()
        {
            var r = CryptoAddressValidator.Validate(BtcSegwit, "Bitcoin");

            Assert.Equal(AddressCheck.Verified, r.Result);
        }

        [Fact]
        public void Bech32_is_case_insensitive_as_a_whole()
        {
            // Upper case is valid bech32; only MIXING is not.
            var r = CryptoAddressValidator.Validate(BtcSegwit.ToUpperInvariant(), "Bitcoin");

            Assert.Equal(AddressCheck.Verified, r.Result);
        }

        // ── Caught corruption ────────────────────────────────────────────────

        [Fact]
        public void One_flipped_character_breaks_the_base58_checksum()
        {
            // The single most likely real corruption, and the whole reason the
            // checksum exists.
            string mutated = BtcLegacy[..^1] + (BtcLegacy[^1] == 'a' ? 'b' : 'a');

            var r = CryptoAddressValidator.Validate(mutated, "Bitcoin");

            Assert.Equal(AddressCheck.Malformed, r.Result);
            Assert.False(r.IsDisplayable);
        }

        [Fact]
        public void One_flipped_character_breaks_the_bech32_checksum()
        {
            string mutated = BtcSegwit[..^1] + (BtcSegwit[^1] == 'q' ? 'p' : 'q');

            var r = CryptoAddressValidator.Validate(mutated, "Bitcoin");

            Assert.Equal(AddressCheck.Malformed, r.Result);
        }

        [Fact]
        public void Mixed_case_bech32_is_rejected()
        {
            // Invalid by specification — and a strong sign of mangling in transit.
            var r = CryptoAddressValidator.Validate("bc1QW508d6qejxtdg4y5r3zarvary0c5xw7kv8f3t4", "Bitcoin");

            Assert.Equal(AddressCheck.Malformed, r.Result);
            Assert.Contains("mix", r.Detail);
        }

        [Fact]
        public void A_testnet_address_is_rejected_for_mainnet()
        {
            // tb1 is testnet. Sending real funds to it loses them, and the human
            // prefix difference is one character.
            var r = CryptoAddressValidator.Validate("tb1qw508d6qejxtdg4y5r3zarvary0c5xw7kxpjzsx", "Bitcoin");

            Assert.Equal(AddressCheck.Malformed, r.Result);
        }

        [Fact]
        public void An_empty_address_is_malformed_not_merely_unknown()
        {
            var r = CryptoAddressValidator.Validate("", "Bitcoin");

            Assert.Equal(AddressCheck.Malformed, r.Result);
        }

        [Fact]
        public void An_address_with_a_space_is_rejected()
        {
            // Always a transcription accident, and catching it here stops a silent
            // truncation further down.
            var r = CryptoAddressValidator.Validate(BtcLegacy[..10] + " " + BtcLegacy[10..], "Bitcoin");

            Assert.Equal(AddressCheck.Malformed, r.Result);
        }

        [Fact]
        public void An_ethereum_address_offered_as_bitcoin_is_rejected()
        {
            // The wrong-network mistake, which loses more deposits than anything else.
            var r = CryptoAddressValidator.Validate("0x52908400098527886E0F7030069857D2E4169EE7", "Bitcoin");

            Assert.Equal(AddressCheck.Malformed, r.Result);
        }

        // ── Honest about limits ──────────────────────────────────────────────

        [Theory]
        [InlineData("ERC20")]
        [InlineData("Polygon")]
        [InlineData("BEP20")]
        public void A_well_formed_evm_address_is_structure_only_never_claimed_verified(string network)
        {
            // EIP-55 hides its checksum in the LETTER CASE and needs keccak-256,
            // which is not SHA3-256 and is not in .NET. Claiming a verification we
            // did not perform is worse than admitting the limit, because the user
            // would rely on it.
            var r = CryptoAddressValidator.Validate("0x52908400098527886E0F7030069857D2E4169EE7", network);

            Assert.Equal(AddressCheck.StructureOnly, r.Result);
            Assert.True(r.IsDisplayable);
            Assert.Contains("capital", r.Detail);
        }

        [Theory]
        [InlineData("0x123", "42 characters")]
        [InlineData("52908400098527886E0F7030069857D2E4169EE7", "0x")]
        [InlineData("0xZZ908400098527886E0F7030069857D2E4169EE7", "hexadecimal")]
        public void Malformed_evm_addresses_are_caught_by_shape(string address, string expected)
        {
            var r = CryptoAddressValidator.Validate(address, "ERC20");

            Assert.Equal(AddressCheck.Malformed, r.Result);
            Assert.Contains(expected, r.Detail);
        }

        [Fact]
        public void A_solana_address_is_structure_only_because_the_format_has_no_checksum()
        {
            var r = CryptoAddressValidator.Validate("11111111111111111111111111111111", "Solana");

            Assert.Equal(AddressCheck.StructureOnly, r.Result);
            Assert.Contains("no checksum", r.Detail);
        }

        [Fact]
        public void An_unknown_network_says_so_rather_than_passing_or_failing()
        {
            // Neither "fine" nor "corrupt" — we do not know, and the user is told to
            // check the venue. Guessing either way would be a lie.
            var r = CryptoAddressValidator.Validate("someaddress123", "Cardano");

            Assert.Equal(AddressCheck.Unknown, r.Result);
            Assert.True(r.IsDisplayable);
            Assert.Contains("verify the address", r.Detail);
        }

        [Fact]
        public void Every_outcome_says_something_a_user_can_act_on()
        {
            // The silent-feedback rule applied here: a refusal that will not say why
            // leaves the user with nothing to do about it.
            foreach (var (addr, net) in new[]
                     {
                         (BtcLegacy, "Bitcoin"), ("", "Bitcoin"), ("0x123", "ERC20"),
                         ("someaddress123", "Cardano"),
                     })
            {
                var r = CryptoAddressValidator.Validate(addr, net);
                Assert.False(string.IsNullOrWhiteSpace(r.Detail));
            }
        }
    }
}
