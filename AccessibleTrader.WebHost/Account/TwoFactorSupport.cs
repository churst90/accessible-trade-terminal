using System.Text;

namespace AccessibleTrader.WebHost.Account
{
    /// <summary>
    /// Pure helpers for the TOTP enrollment pages. Kept static and free of
    /// Identity types so the formatting/URI contracts are directly unit-testable:
    /// what the user READS (the grouped key), what the authenticator SCANS (the
    /// otpauth URI), and what we ACCEPT back (code normalization) are the three
    /// places an enrollment quietly breaks.
    /// </summary>
    public static class TwoFactorSupport
    {
        public const string Issuer = "Accessible Trade Terminal";

        /// <summary>
        /// Groups the base32 authenticator key in blocks of four, lowercase —
        /// "gmju 6fk2 …". Blocks of four read cleanly under a screen reader and
        /// match what authenticator apps show for manual entry.
        /// </summary>
        public static string FormatSharedKey(string unformattedKey)
        {
            if (string.IsNullOrEmpty(unformattedKey)) return string.Empty;
            var sb = new StringBuilder();
            for (int i = 0; i < unformattedKey.Length; i += 4)
            {
                if (i > 0) sb.Append(' ');
                sb.Append(unformattedKey, i, Math.Min(4, unformattedKey.Length - i));
            }
            return sb.ToString().ToLowerInvariant();
        }

        /// <summary>
        /// The otpauth:// provisioning URI encoded into the QR code. Issuer and
        /// account are URI-escaped (the issuer contains spaces); the secret is
        /// raw base32 per the spec.
        /// </summary>
        public static string BuildOtpauthUri(string email, string unformattedKey) =>
            $"otpauth://totp/{Uri.EscapeDataString(Issuer)}:{Uri.EscapeDataString(email)}"
            + $"?secret={unformattedKey}&issuer={Uri.EscapeDataString(Issuer)}&digits=6";

        /// <summary>
        /// Accepts an AUTHENTICATOR code the way people type it: "123 456",
        /// "123-456", or "123456" all normalize to "123456". Not for recovery
        /// codes — see <see cref="NormalizeRecoveryCode"/>.
        /// </summary>
        public static string NormalizeCode(string? code) =>
            (code ?? string.Empty).Replace(" ", string.Empty).Replace("-", string.Empty).Trim();

        /// <summary>
        /// Recovery codes are stored hyphenated ("XXXXX-XXXXX") and Identity
        /// redeems them by exact string comparison, so — unlike authenticator
        /// codes — the hyphen must be PRESERVED. Running them through
        /// <see cref="NormalizeCode"/> stripped it and made every recovery
        /// code unredeemable through the sign-in page (found by the WebHost
        /// integration tests, 2026-08-22). Accept the code the way people
        /// type it: strip spaces, uppercase, and re-insert a missing hyphen
        /// in the canonical 5-5 position.
        /// </summary>
        public static string NormalizeRecoveryCode(string? code)
        {
            var c = (code ?? string.Empty).Replace(" ", string.Empty).Trim().ToUpperInvariant();
            if (!c.Contains('-') && c.Length == 10)
                c = c.Insert(5, "-");
            return c;
        }

        /// <summary>
        /// Renders <paramref name="text"/> as a QR PNG data: URI for an inline
        /// &lt;img&gt; (the CSP allows img-src data:). The QR is a convenience
        /// for phone-scanning; the copyable text key is the primary,
        /// screen-reader-first path and must always be shown alongside.
        /// </summary>
        public static string QrPngDataUri(string text)
        {
            using var generator = new QRCoder.QRCodeGenerator();
            using var data = generator.CreateQrCode(text, QRCoder.QRCodeGenerator.ECCLevel.Q);
            var png = new QRCoder.PngByteQRCode(data).GetGraphic(pixelsPerModule: 6);
            return "data:image/png;base64," + Convert.ToBase64String(png);
        }
    }
}
