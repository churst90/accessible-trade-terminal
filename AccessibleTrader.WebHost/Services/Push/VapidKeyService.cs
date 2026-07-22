using System.Security.Cryptography;
using System.Text.Json;

namespace AccessibleTrader.WebHost.Services.Push
{
    /// <summary>
    /// Instance-wide VAPID keypair for Web Push (RFC 8292): generated once on
    /// first use with P-256 and persisted to the accounts data root so push
    /// subscriptions survive restarts — a new keypair would orphan every
    /// browser subscription in the wild. Keys are base64url per the spec
    /// (public = uncompressed EC point, private = scalar).
    /// </summary>
    public sealed class VapidKeyService
    {
        public const string Subject = "mailto:codythurst@gmail.com";

        private readonly string _path;
        private readonly ILogger<VapidKeyService> _logger;
        private readonly object _gate = new();
        private (string Public, string Private)? _keys;

        public VapidKeyService(string dataRoot, ILogger<VapidKeyService> logger)
        {
            _path = Path.Combine(dataRoot, "vapid-keys.json");
            _logger = logger;
        }

        public string PublicKey => Keys.Public;
        public string PrivateKey => Keys.Private;

        private (string Public, string Private) Keys
        {
            get
            {
                lock (_gate)
                {
                    if (_keys != null) return _keys.Value;
                    _keys = LoadOrCreate();
                    return _keys.Value;
                }
            }
        }

        private (string Public, string Private) LoadOrCreate()
        {
            try
            {
                if (File.Exists(_path))
                {
                    var doc = JsonSerializer.Deserialize<Stored>(File.ReadAllText(_path));
                    if (!string.IsNullOrEmpty(doc?.PublicKey) && !string.IsNullOrEmpty(doc.PrivateKey))
                        return (doc.PublicKey, doc.PrivateKey);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "VAPID key file unreadable; generating a fresh pair (existing push subscriptions will be orphaned).");
            }

            var (pub, priv) = Generate();
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                AccessibleTrader.Core.Services.AtomicFile.WriteAllText(_path,
                    JsonSerializer.Serialize(new Stored { PublicKey = pub, PrivateKey = priv }));
                _logger.LogInformation("Generated new VAPID keypair for Web Push.");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "VAPID key persistence failed; keys are session-only.");
            }
            return (pub, priv);
        }

        internal static (string PublicKey, string PrivateKey) Generate()
        {
            using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var p = ecdsa.ExportParameters(includePrivateParameters: true);
            var point = new byte[65];
            point[0] = 0x04; // uncompressed EC point marker
            Buffer.BlockCopy(p.Q.X!, 0, point, 1, 32);
            Buffer.BlockCopy(p.Q.Y!, 0, point, 33, 32);
            return (Base64Url(point), Base64Url(p.D!));
        }

        internal static string Base64Url(byte[] bytes) =>
            Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');

        private sealed class Stored
        {
            public string PublicKey { get; set; } = "";
            public string PrivateKey { get; set; } = "";
        }
    }
}
