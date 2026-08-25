using AccessibleTrader.Core.Services;
using AccessibleTrader.WebHost.Services;
using Microsoft.AspNetCore.DataProtection;
using NSubstitute;

namespace AccessibleTrader.Tests.WebHost;

/// <summary>
/// Pins <see cref="WebHostSecureStorageService"/> behaviour: roundtrip
/// preserves the value, missing keys return null, corrupt blobs return
/// null instead of throwing, and keys are stored under hashed filenames
/// (so user-supplied keys with path separators or unusual chars can't
/// escape the secrets directory).
///
/// Uses an <see cref="EphemeralDataProtectionProvider"/> so we don't
/// touch the real DataProtection key ring on disk. Each test gets a
/// fresh temp directory for the secrets folder.
/// </summary>
public class WebHostSecureStorageServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly WebHostSecureStorageService _sut;

    public WebHostSecureStorageServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"atst_securestore_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        var paths = Substitute.For<IPlatformPathService>();
        paths.AppDataDirectory.Returns(_tempDir);

        _sut = new WebHostSecureStorageService(new EphemeralDataProtectionProvider(), paths);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best-effort cleanup */ }
    }

    [Fact]
    public async Task SetThenGetRoundtripsTheValue()
    {
        await _sut.SetAsync("api-key/binance", "very-secret-token");

        var read = await _sut.GetAsync("api-key/binance");

        Assert.Equal("very-secret-token", read);
    }

    [Fact]
    public async Task GetReturnsNullForUnknownKey()
    {
        var read = await _sut.GetAsync("never-set");
        Assert.Null(read);
    }

    [Fact]
    public async Task RemoveDeletesValueSoSubsequentGetReturnsNull()
    {
        await _sut.SetAsync("k", "v");
        Assert.Equal("v", await _sut.GetAsync("k"));

        _sut.Remove("k");

        Assert.Null(await _sut.GetAsync("k"));
    }

    [Fact]
    public async Task CorruptBlobReturnsNullInsteadOfThrowing()
    {
        await _sut.SetAsync("provider/secret", "real-value");

        // Locate the per-key file and overwrite with garbage that won't
        // Unprotect cleanly. The service must not throw — secrets that
        // can't be decrypted are presented to the caller as "no value"
        // so the user can re-enter them.
        var secretsDir = Path.Combine(_tempDir, "secrets");
        var files = Directory.GetFiles(secretsDir);
        Assert.Single(files);
        File.WriteAllBytes(files[0], new byte[] { 0xDE, 0xAD, 0xBE, 0xEF });

        var read = await _sut.GetAsync("provider/secret");
        Assert.Null(read);
    }

    [Fact]
    public async Task KeysWithPathSeparatorsAndUnusualCharsStayInSecretsDir()
    {
        // SHA-256 hashing of the key name is what keeps user-supplied
        // strings from being interpreted as a relative path.
        await _sut.SetAsync("../../etc/passwd", "rabbit-hole");
        await _sut.SetAsync("api:key with spaces & symbols", "ok");

        // Both blobs live under {tempDir}/secrets/<hex>.bin — never
        // outside the secrets directory.
        var secretsDir = Path.Combine(_tempDir, "secrets");
        var files = Directory.GetFiles(secretsDir);
        Assert.Equal(2, files.Length);
        foreach (var f in files)
        {
            Assert.Equal(secretsDir, Path.GetDirectoryName(f));
            Assert.EndsWith(".bin", f);
        }

        // And the values are still individually retrievable.
        Assert.Equal("rabbit-hole", await _sut.GetAsync("../../etc/passwd"));
        Assert.Equal("ok",          await _sut.GetAsync("api:key with spaces & symbols"));
    }

    [Fact]
    public async Task SetOverwritesPreviousValueForSameKey()
    {
        await _sut.SetAsync("rotating-secret", "v1");
        await _sut.SetAsync("rotating-secret", "v2");

        Assert.Equal("v2", await _sut.GetAsync("rotating-secret"));
    }
}
