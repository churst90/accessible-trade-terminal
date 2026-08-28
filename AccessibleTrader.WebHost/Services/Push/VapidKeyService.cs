using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace AccessibleTrader.WebHost.Services.Push
{
    /// <summary>
    /// Instance-wide VAPID keypair for Web Push (RFC 8292): generated once on
    /// first use with P-256 and persisted to the accounts data root so push
    /// subscriptions survive restarts — a new keypair would orphan every
    /// browser subscription in the wild. Keys are base64url per the spec
    /// (public = uncompressed EC point, private = scalar).
    ///
    /// <para>
    /// <b>The private half is encrypted at rest.</b> It used to be written as
    /// plaintext JSON, sitting next to the DataProtection-encrypted <c>secrets/</c>
    /// store that <c>WebHostSecureStorageService</c> maintains under the same data
    /// root — the one secret on the box that skipped the mechanism every other
    /// secret goes through, with the provider already in the container. Anyone who
    /// could read the data root (a backup tarball, a mis-set <c>Accounts__DataRoot</c>,
    /// a co-tenant before the documented <c>chmod 700</c> lands) could then send push
    /// notifications the browser attributes to this origin: an alert-shaped phishing
    /// surface aimed at precisely the users who depend on alerts to know what their
    /// positions are doing.
    /// </para>
    ///
    /// <para>
    /// Belt and braces: the file is also chmod 0600 on Unix. DataProtection is the
    /// control; the mode is what limits the damage if the key ring is ever readable
    /// too. A file written by an older build is read, honoured (so subscriptions are
    /// not orphaned by the upgrade) and immediately rewritten in the protected form.
    /// </para>
    /// </summary>
    public sealed class VapidKeyService
    {
        public const string Subject = "mailto:codythurst@gmail.com";

        /// <summary>DataProtection purpose string. Changing it orphans existing key files.</summary>
        private const string ProtectorPurpose = "AccessibleTrader.WebHost.VapidKeys.v1";

        private readonly string _path;
        private readonly ILogger<VapidKeyService> _logger;
        private readonly IDataProtector? _protector;
        private readonly object _gate = new();
        private (string Public, string Private)? _keys;

        public VapidKeyService(string dataRoot, ILogger<VapidKeyService> logger,
                               IDataProtectionProvider? protection = null)
        {
            _path = Path.Combine(dataRoot, "vapid-keys.json");
            _logger = logger;
            _protector = protection?.CreateProtector(ProtectorPurpose);
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
                    if (!string.IsNullOrEmpty(doc?.PublicKey))
                    {
                        if (!string.IsNullOrEmpty(doc.PrivateKeyProtected) && _protector != null)
                        {
                            var unprotected = Encoding.UTF8.GetString(
                                _protector.Unprotect(Convert.FromBase64String(doc.PrivateKeyProtected)));
                            if (!string.IsNullOrEmpty(unprotected))
                                return (doc.PublicKey, unprotected);
                        }
                        else if (!string.IsNullOrEmpty(doc.PrivateKey))
                        {
                            // Written by a build that stored it in the clear. Honour it —
                            // regenerating would orphan every live subscription — and rewrite
                            // it protected on the way past.
                            _logger.LogInformation(
                                "VAPID private key was stored in plaintext; re-persisting it encrypted.");
                            Persist(doc.PublicKey, doc.PrivateKey);
                            return (doc.PublicKey, doc.PrivateKey);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "VAPID key file unreadable; generating a fresh pair (existing push subscriptions will be orphaned).");
            }

            var (pub, priv) = Generate();
            Persist(pub, priv);
            return (pub, priv);
        }

        private void Persist(string pub, string priv)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);

                var stored = new Stored { PublicKey = pub };
                if (_protector != null)
                    stored.PrivateKeyProtected = Convert.ToBase64String(_protector.Protect(Encoding.UTF8.GetBytes(priv)));
                else
                    // No provider (unit tests, a bare CLI run). Still persist so the public
                    // key is stable, and say so — a plaintext key on a real deployment is
                    // the thing this class exists to prevent.
                    stored.PrivateKey = priv;

                AccessibleTrader.Core.Services.AtomicFile.WriteAllText(_path, JsonSerializer.Serialize(stored));
                RestrictToOwner(_path);

                if (_protector == null)
                    _logger.LogWarning("No IDataProtectionProvider available: the VAPID private key was persisted UNENCRYPTED at {Path}.", _path);
                else
                    _logger.LogInformation("Persisted VAPID keypair for Web Push (private key encrypted at rest).");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "VAPID key persistence failed; keys are session-only.");
            }
        }

        /// <summary>0600 on Unix. No-op on Windows, where the data root's ACL governs.</summary>
        private void RestrictToOwner(string path)
        {
            if (OperatingSystem.IsWindows()) return;
            try
            {
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not restrict {Path} to owner-only permissions.", path);
            }
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

            /// <summary>Legacy plaintext scalar. Read for upgrade; never written when a protector exists.</summary>
            public string? PrivateKey { get; set; }

            /// <summary>DataProtection ciphertext of the base64url scalar, itself base64.</summary>
            public string? PrivateKeyProtected { get; set; }
        }
    }
}
