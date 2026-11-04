using System.Text;
using Org.BouncyCastle.Asn1.Sec;
using Org.BouncyCastle.Asn1.X9;
using Org.BouncyCastle.Crypto.Digests;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Math.EC;

namespace AccessibleTrader.Plugins.Hyperliquid
{
    /// <summary>A recoverable secp256k1 signature, spelled the way the exchange wants it.</summary>
    internal readonly record struct HlSignature(string R, string S, int V);

    /// <summary>
    /// Hyperliquid "L1 action" signing, a line-for-line port of <c>sign_l1_action</c> in
    /// the Python SDK (<c>hyperliquid/utils/signing.py</c>):
    /// <list type="number">
    ///   <item>msgpack the action, append the nonce (8 bytes big-endian), then the vault
    ///   address (<c>0x00</c> when none, else <c>0x01</c> + 20 bytes), then, only when
    ///   set, <c>0x00</c> + expiry (8 bytes);</item>
    ///   <item>keccak256 of that is the <c>connectionId</c>;</item>
    ///   <item>sign the EIP-712 "phantom agent" <c>Agent(string source, bytes32
    ///   connectionId)</c> under the domain <c>Exchange / 1 / 1337 / 0x0</c>, with
    ///   <c>source</c> "a" on mainnet and "b" on testnet.</item>
    /// </list>
    /// The curve and hash come from BouncyCastle; the nonce is RFC 6979, like the SDK's, so
    /// the same key and message give the same bytes, which the signing tests pin.
    /// </summary>
    internal static class HyperliquidSigner
    {
        private static readonly X9ECParameters Curve = SecNamedCurves.GetByName("secp256k1");
        private static readonly ECDomainParameters Domain =
            new(Curve.Curve, Curve.G, Curve.N, Curve.H);

        public static byte[] Keccak256(byte[] data)
        {
            var d = new KeccakDigest(256);
            d.BlockUpdate(data, 0, data.Length);
            var o = new byte[32];
            d.DoFinal(o, 0);
            return o;
        }

        private static byte[] Keccak256(string s) => Keccak256(Encoding.UTF8.GetBytes(s));

        public static byte[] AddressBytes(string address)
        {
            string h = address.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? address[2..] : address;
            return Convert.FromHexString(h);
        }

        /// <summary>The connectionId: keccak256(msgpack(action) || nonce || vault || expiry).</summary>
        public static byte[] ActionHash(object action, string? vaultAddress, ulong nonce, ulong? expiresAfter = null)
        {
            var data = new List<byte>(MsgPack.Pack(action));
            for (int i = 7; i >= 0; i--) data.Add((byte)(nonce >> (8 * i)));
            if (vaultAddress is null) data.Add(0x00);
            else { data.Add(0x01); data.AddRange(AddressBytes(vaultAddress)); }
            if (expiresAfter.HasValue)
            {
                data.Add(0x00);
                for (int i = 7; i >= 0; i--) data.Add((byte)(expiresAfter.Value >> (8 * i)));
            }
            return Keccak256(data.ToArray());
        }

        /// <summary>The EIP-712 digest of the phantom agent for this connectionId.</summary>
        public static byte[] PhantomAgentDigest(byte[] connectionId, bool isMainnet)
        {
            byte[] domainType = Keccak256("EIP712Domain(string name,string version,uint256 chainId,address verifyingContract)");
            var domain = new List<byte>();
            domain.AddRange(domainType);
            domain.AddRange(Keccak256("Exchange"));
            domain.AddRange(Keccak256("1"));
            domain.AddRange(Pad32(new byte[] { 0x05, 0x39 }));   // chainId 1337
            domain.AddRange(new byte[32]);                        // verifyingContract = 0x0
            byte[] domainSeparator = Keccak256(domain.ToArray());

            var agent = new List<byte>();
            agent.AddRange(Keccak256("Agent(string source,bytes32 connectionId)"));
            agent.AddRange(Keccak256(isMainnet ? "a" : "b"));
            agent.AddRange(connectionId);
            byte[] structHash = Keccak256(agent.ToArray());

            var msg = new List<byte> { 0x19, 0x01 };
            msg.AddRange(domainSeparator);
            msg.AddRange(structHash);
            return Keccak256(msg.ToArray());
        }

        private static byte[] Pad32(byte[] b)
        {
            var o = new byte[32];
            Buffer.BlockCopy(b, 0, o, 32 - b.Length, b.Length);
            return o;
        }

        public static HlSignature SignL1Action(string privateKeyHex, object action, string? vaultAddress,
            ulong nonce, bool isMainnet, ulong? expiresAfter = null)
        {
            var connectionId = ActionHash(action, vaultAddress, nonce, expiresAfter);
            return SignDigest(privateKeyHex, PhantomAgentDigest(connectionId, isMainnet));
        }

        public static HlSignature SignDigest(string privateKeyHex, byte[] digest)
        {
            string h = privateKeyHex.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? privateKeyHex[2..] : privateKeyHex;
            var d = new Org.BouncyCastle.Math.BigInteger(1, Convert.FromHexString(h));
            var signer = new ECDsaSigner(new HMacDsaKCalculator(new Sha256Digest()));
            signer.Init(true, new ECPrivateKeyParameters(d, Domain));
            var rs = signer.GenerateSignature(digest);
            var r = rs[0];
            var s = rs[1];
            var n = Domain.N;
            if (s.CompareTo(n.ShiftRight(1)) > 0) s = n.Subtract(s);   // canonical low-s, as libsecp256k1 does

            var pub = Domain.G.Multiply(d).Normalize();
            int recId = -1;
            for (int id = 0; id < 2; id++)
            {
                var q = Recover(digest, r, s, id);
                if (q != null && q.Equals(pub)) { recId = id; break; }
            }
            if (recId < 0) throw new InvalidOperationException("Could not derive the signature recovery id.");

            return new HlSignature("0x" + r.ToString(16), "0x" + s.ToString(16), 27 + recId);
        }

        private static ECPoint? Recover(byte[] digest, Org.BouncyCastle.Math.BigInteger r,
            Org.BouncyCastle.Math.BigInteger s, int recId)
        {
            var n = Domain.N;
            var e = new Org.BouncyCastle.Math.BigInteger(1, digest);
            // Only the two low recovery ids matter: r < n always holds for our nonces in practice.
            var x = r;
            var curve = (Org.BouncyCastle.Math.EC.FpCurve)Domain.Curve;
            if (x.CompareTo(curve.Q) >= 0) return null;
            var compressed = new byte[33];
            compressed[0] = (byte)(0x02 + (recId & 1));
            var xb = x.ToByteArrayUnsigned();
            Buffer.BlockCopy(xb, 0, compressed, 33 - xb.Length, xb.Length);
            ECPoint rPoint;
            try { rPoint = Domain.Curve.DecodePoint(compressed); }
            catch (ArgumentException) { return null; }
            var rInv = r.ModInverse(n);
            var srInv = rInv.Multiply(s).Mod(n);
            var eInvrInv = rInv.Multiply(e).Mod(n);   // = r^-1 * e
            var q = Org.BouncyCastle.Math.EC.ECAlgorithms.SumOfTwoMultiplies(
                Domain.G, n.Subtract(eInvrInv).Mod(n), rPoint, srInv);
            return q.Normalize();
        }

        /// <summary>The 0x-prefixed lowercase address a private key controls (for diagnostics and tests).</summary>
        public static string AddressOf(string privateKeyHex)
        {
            string h = privateKeyHex.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? privateKeyHex[2..] : privateKeyHex;
            var d = new Org.BouncyCastle.Math.BigInteger(1, Convert.FromHexString(h));
            var pub = Domain.G.Multiply(d).Normalize();
            var xy = new byte[64];
            Buffer.BlockCopy(Pad32Big(pub.AffineXCoord.ToBigInteger()), 0, xy, 0, 32);
            Buffer.BlockCopy(Pad32Big(pub.AffineYCoord.ToBigInteger()), 0, xy, 32, 32);
            var hash = Keccak256(xy);
            return "0x" + Convert.ToHexString(hash, 12, 20).ToLowerInvariant();
        }

        private static byte[] Pad32Big(Org.BouncyCastle.Math.BigInteger b)
        {
            var u = b.ToByteArrayUnsigned();
            return Pad32(u);
        }
    }
}
