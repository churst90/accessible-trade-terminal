using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Security;

namespace AccessibleTrader.Plugins.Kalshi
{
    /// <summary>
    /// Kalshi's request signature (docs read 2026-11-14): the text signed is the time in
    /// milliseconds, the method in capitals and the path from <c>/trade-api/v2</c> on, with no
    /// query string and no host. Kalshi hands out two kinds of key: Ed25519 (its default, a PKCS#8
    /// "BEGIN PRIVATE KEY" file, signed as is) and RSA (a PKCS#1 "BEGIN RSA PRIVATE KEY" file,
    /// signed RSA-PSS with SHA-256 and a 32-byte salt). The signature travels base64.
    /// </summary>
    internal sealed class KalshiSigner
    {
        private readonly Ed25519PrivateKeyParameters? _ed;
        private readonly RSA? _rsa;

        private KalshiSigner(Ed25519PrivateKeyParameters? ed, RSA? rsa) { _ed = ed; _rsa = rsa; }

        public bool IsEd25519 => _ed != null;

        /// <summary>The words a person sees when the private key cannot be read.</summary>
        internal const string UnreadableKey =
            "The Kalshi private key could not be read. Paste the whole key file Kalshi gave you when the "
          + "key was made, from the BEGIN line to the END line; the API key box takes the Key ID.";

        /// <summary>
        /// Reads the private key as pasted. A key pasted into a one-line box loses its line breaks,
        /// or arrives with them written as the two characters \n; both still read, because only the
        /// label and the base64 between the BEGIN and END lines matter.
        /// </summary>
        public static KalshiSigner Parse(string pem)
        {
            var m = Regex.Match(pem ?? "", @"-----BEGIN ([A-Z ]+)-----(.*?)-----END \1-----", RegexOptions.Singleline);
            if (!m.Success) throw new FormatException(UnreadableKey);
            string label = m.Groups[1].Value.Trim();
            string body = Regex.Replace(m.Groups[2].Value.Replace("\\n", "").Replace("\\r", ""), @"\s+", "");
            byte[] der;
            try { der = Convert.FromBase64String(body); }
            catch (FormatException) { throw new FormatException(UnreadableKey); }

            try
            {
                if (label == "RSA PRIVATE KEY")
                {
                    var rsa = RSA.Create();
                    rsa.ImportRSAPrivateKey(der, out _);
                    return new KalshiSigner(null, rsa);
                }
                if (label == "PRIVATE KEY")
                {
                    AsymmetricKeyParameter key = PrivateKeyFactory.CreateKey(der);
                    if (key is Ed25519PrivateKeyParameters ed) return new KalshiSigner(ed, null);
                    var rsa = RSA.Create();
                    rsa.ImportPkcs8PrivateKey(der, out _);
                    return new KalshiSigner(null, rsa);
                }
            }
            catch (Exception ex) when (ex is CryptographicException or ArgumentException or InvalidOperationException
                                          or SecurityUtilityException or IOException)
            {
                throw new FormatException(UnreadableKey);
            }
            throw new FormatException(UnreadableKey);
        }

        /// <summary>The text Kalshi signs: milliseconds, METHOD, and the path without its query.</summary>
        public static string Message(string timestampMs, string method, string pathAndQuery)
        {
            int q = pathAndQuery.IndexOf('?');
            return timestampMs + method.ToUpperInvariant() + (q >= 0 ? pathAndQuery[..q] : pathAndQuery);
        }

        public string Sign(string message)
        {
            byte[] data = Encoding.UTF8.GetBytes(message);
            if (_ed != null)
            {
                var signer = new Ed25519Signer();
                signer.Init(true, _ed);
                signer.BlockUpdate(data, 0, data.Length);
                return Convert.ToBase64String(signer.GenerateSignature());
            }
            // .NET's PSS uses a salt as long as the digest — 32 bytes for SHA-256, Kalshi's setting.
            return Convert.ToBase64String(_rsa!.SignData(data, HashAlgorithmName.SHA256, RSASignaturePadding.Pss));
        }
    }
}
