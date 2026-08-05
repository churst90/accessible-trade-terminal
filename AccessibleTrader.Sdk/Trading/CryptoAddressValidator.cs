using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;

namespace AccessibleTrader.Sdk.Trading
{
    /// <summary>How far an address could be checked before it was shown.</summary>
    public enum AddressCheck
    {
        /// <summary>A checksum built into the format was computed and matched.</summary>
        Verified,

        /// <summary>
        /// The shape is right — length, prefix, alphabet — but the format's
        /// integrity check could not be run here. Reported distinctly from
        /// <see cref="Verified"/> on purpose: claiming verification we did not do
        /// is worse than admitting the limit, because the user would trust it.
        /// </summary>
        StructureOnly,

        /// <summary>Definitely wrong for this network. Do not display it.</summary>
        Malformed,

        /// <summary>The network is not one this validator knows how to check at all.</summary>
        Unknown,
    }

    public sealed record AddressValidation(AddressCheck Result, string Detail)
    {
        /// <summary>Whether the address is safe to put in front of a user.</summary>
        public bool IsDisplayable => Result != AddressCheck.Malformed;
    }

    /// <summary>
    /// Checks a deposit address locally before it is shown.
    ///
    /// <para>
    /// This runs offline and costs nothing, and it is the one defence available on
    /// our side of the wire: if the venue's API hands back something malformed, that
    /// is the alarm. It does NOT prove an address belongs to your account — nothing
    /// local can — which is why the wallet also refuses to cache or persist an
    /// address, so there is never a stored copy to substitute.
    /// </para>
    ///
    /// <para>
    /// **Case matters here more than anywhere else in the terminal.** Base58 and
    /// checksummed hex are case-sensitive, and speech drops case entirely — which is
    /// why the UI reads addresses through the review cursor and braille, and offers
    /// a character read that announces capitals.
    /// </para>
    /// </summary>
    public static class CryptoAddressValidator
    {
        public static AddressValidation Validate(string? address, string? network)
        {
            if (string.IsNullOrWhiteSpace(address))
                return new(AddressCheck.Malformed, "the venue returned an empty address");

            address = address.Trim();
            string net = (network ?? "").Trim().ToUpperInvariant();

            // Whitespace inside an address is always a transcription accident and
            // never valid; catching it here stops a silent truncation later.
            if (address.Any(char.IsWhiteSpace))
                return new(AddressCheck.Malformed, "the address contains a space");

            if (Looks(net, "BITCOIN", "BTC", "XBT")) return Bitcoin(address);
            if (Looks(net, "LITECOIN", "LTC"))       return Bech32Or58(address, "ltc", "the Litecoin network");

            if (Looks(net, "ETH", "ERC20", "ETHEREUM", "BASE", "ARBITRUM", "OPTIMISM",
                           "POLYGON", "MATIC", "BSC", "BEP20", "AVAX", "AVALANCHE"))
                return EvmHex(address, net);

            if (Looks(net, "TRON", "TRC20"))         return Base58Check(address, "a Tron address", expectPrefix: "T");
            if (Looks(net, "SOL", "SOLANA"))         return Solana(address);

            return new(AddressCheck.Unknown,
                $"no local format check exists for {(network is null or "" ? "this network" : network)} — "
              + "verify the address against the venue itself");
        }

        private static bool Looks(string net, params string[] names) =>
            names.Any(n => net == n || net.Contains(n, StringComparison.Ordinal));

        // ── Per-network ──────────────────────────────────────────────────────

        private static AddressValidation Bitcoin(string a)
        {
            if (a.StartsWith("bc1", StringComparison.OrdinalIgnoreCase))
                return Bech32(a, "bc");
            if (a.StartsWith('1') || a.StartsWith('3'))
                return Base58Check(a, "a Bitcoin address");
            return new(AddressCheck.Malformed,
                "not a Bitcoin address — it should start with 1, 3 or bc1");
        }

        private static AddressValidation Bech32Or58(string a, string hrp, string what) =>
            a.StartsWith(hrp + "1", StringComparison.OrdinalIgnoreCase)
                ? Bech32(a, hrp)
                : Base58Check(a, what);

        private static AddressValidation EvmHex(string a, string net)
        {
            if (!a.StartsWith("0x", StringComparison.Ordinal))
                return new(AddressCheck.Malformed, "an EVM address must start with 0x");
            if (a.Length != 42)
                return new(AddressCheck.Malformed, $"an EVM address is 42 characters; this one is {a.Length}");
            if (!a[2..].All(Uri.IsHexDigit))
                return new(AddressCheck.Malformed, "an EVM address must be hexadecimal after 0x");

            bool mixedCase = a[2..].Any(char.IsUpper) && a[2..].Any(char.IsLower);

            // EIP-55 encodes its checksum in the LETTER CASE, verified with
            // keccak-256 — which is not SHA3-256 and is not in .NET. Rather than
            // ship a hand-rolled hash on the money path, the limit is stated.
            return new(AddressCheck.StructureOnly,
                mixedCase
                    ? "length and hex are correct; the EIP-55 capitalisation checksum is not verified here, "
                    + "so compare the address character by character including capitals"
                    : "length and hex are correct; this address carries no capitalisation checksum to verify");
        }

        private static AddressValidation Solana(string a)
        {
            // Base58 with no checksum layer — a 32-byte public key. Length and
            // alphabet are all there is to check, and that is worth saying.
            if (a.Length is < 32 or > 44)
                return new(AddressCheck.Malformed, $"a Solana address is 32–44 characters; this one is {a.Length}");
            if (a.Any(c => !Base58Alphabet.Contains(c)))
                return new(AddressCheck.Malformed, "contains characters that are not valid base58");
            return new(AddressCheck.StructureOnly,
                "length and alphabet are correct; Solana addresses carry no checksum, so compare "
              + "character by character including capitals");
        }

        // ── bech32 / bech32m ─────────────────────────────────────────────────

        private const string Bech32Charset = "qpzry9x8gf2tvdw0s3jn54khce6mua7l";

        internal static AddressValidation Bech32(string address, string expectedHrp)
        {
            // Mixed case is invalid in bech32 by specification — it exists so the
            // address can be written either way without ambiguity.
            if (address.Any(char.IsUpper) && address.Any(char.IsLower))
                return new(AddressCheck.Malformed, "bech32 addresses must not mix upper and lower case");

            string lower = address.ToLowerInvariant();
            int split = lower.LastIndexOf('1');
            if (split < 1 || split + 7 > lower.Length)
                return new(AddressCheck.Malformed, "bech32 separator is missing or misplaced");

            string hrp = lower[..split];
            if (!string.Equals(hrp, expectedHrp, StringComparison.Ordinal))
                return new(AddressCheck.Malformed,
                    $"this address is for '{hrp}', not '{expectedHrp}' — check you picked the right network");

            var data = new List<int>();
            foreach (char c in lower[(split + 1)..])
            {
                int v = Bech32Charset.IndexOf(c);
                if (v < 0) return new(AddressCheck.Malformed, $"'{c}' is not a bech32 character");
                data.Add(v);
            }

            int checksum = Polymod(Expand(hrp).Concat(data).ToArray());
            // 1 is bech32 (segwit v0); 0x2bc830a3 is bech32m (v1+, taproot).
            if (checksum != 1 && checksum != 0x2bc830a3)
                return new(AddressCheck.Malformed, "the bech32 checksum does not match — this address is corrupt");

            return new(AddressCheck.Verified,
                $"bech32 checksum verified for {expectedHrp}");
        }

        private static IEnumerable<int> Expand(string hrp) =>
            hrp.Select(c => c >> 5).Concat(new[] { 0 }).Concat(hrp.Select(c => c & 31));

        private static int Polymod(IReadOnlyList<int> values)
        {
            int[] gen = { 0x3b6a57b2, 0x26508e6d, 0x1ea119fa, 0x3d4233dd, 0x2a1462b3 };
            int chk = 1;
            foreach (int v in values)
            {
                int top = chk >> 25;
                chk = ((chk & 0x1ffffff) << 5) ^ v;
                for (int i = 0; i < 5; i++)
                    if (((top >> i) & 1) != 0) chk ^= gen[i];
            }
            return chk;
        }

        // ── base58check ──────────────────────────────────────────────────────

        private const string Base58Alphabet = "123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz";

        internal static AddressValidation Base58Check(string address, string what, string? expectPrefix = null)
        {
            if (expectPrefix != null && !address.StartsWith(expectPrefix, StringComparison.Ordinal))
                return new(AddressCheck.Malformed, $"{what} should start with '{expectPrefix}'");

            if (address.Any(c => !Base58Alphabet.Contains(c)))
                return new(AddressCheck.Malformed,
                    "contains characters that are not valid base58 — note 0, O, I and l are excluded on purpose");

            byte[]? decoded = DecodeBase58(address);
            if (decoded is null || decoded.Length < 5)
                return new(AddressCheck.Malformed, $"{what} is too short to carry a checksum");

            byte[] payload = decoded[..^4];
            byte[] given = decoded[^4..];
            byte[] want = SHA256.HashData(SHA256.HashData(payload))[..4];

            return given.SequenceEqual(want)
                ? new(AddressCheck.Verified, $"base58check checksum verified for {what}")
                : new(AddressCheck.Malformed,
                    "the base58 checksum does not match — this address is corrupt or was mistyped");
        }

        private static byte[]? DecodeBase58(string s)
        {
            var num = new List<byte> { 0 };
            foreach (char c in s)
            {
                int digit = Base58Alphabet.IndexOf(c);
                if (digit < 0) return null;

                int carry = digit;
                for (int i = 0; i < num.Count; i++)
                {
                    carry += num[i] * 58;
                    num[i] = (byte)(carry & 0xff);
                    carry >>= 8;
                }
                while (carry > 0) { num.Add((byte)(carry & 0xff)); carry >>= 8; }
            }

            // Each leading '1' is a leading zero byte.
            foreach (char c in s)
            {
                if (c != '1') break;
                num.Add(0);
            }

            num.Reverse();
            return num.ToArray();
        }
    }
}
