using AccessibleTrader.WebHost.Account;
using AccessibleTrader.WebHost.Services;

namespace AccessibleTrader.Tests.WebHost;

/// <summary>
/// Pins the per-user data routing that hosted accounts rely on (see
/// docs/HOSTED_AUTH_PERSISTENCE_DESIGN.md): with accounts on, each user's
/// <c>AppDataDirectory</c> is isolated under <c>users/{id}/</c> while
/// <c>CacheDirectory</c> stays shared (public market data); with accounts off it is the
/// legacy single shared directory, so the local/demo modes are unaffected.
/// </summary>
public class UserScopedPathServiceTests
{
    private sealed class FakeUser : ICurrentUser
    {
        public bool IsAuthenticated { get; init; }
        public string? UserId { get; init; }
        public string DataKey { get; init; } = "anon";
    }

    private static string TempRoot() =>
        Path.Combine(Path.GetTempPath(), "att-paths-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void AppDataDirectory_IsPerUser_WhileCacheIsShared()
    {
        var root = TempRoot();
        var a = new UserScopedPathService(
            new FakeUser { IsAuthenticated = true, UserId = "user-a", DataKey = "user-a" }, accountsEnabled: true, root);
        var b = new UserScopedPathService(
            new FakeUser { IsAuthenticated = true, UserId = "user-b", DataKey = "user-b" }, accountsEnabled: true, root);

        Assert.NotEqual(a.AppDataDirectory, b.AppDataDirectory);   // isolation — the whole point
        Assert.Contains("user-a", a.AppDataDirectory);
        Assert.Contains("user-b", b.AppDataDirectory);
        Assert.Equal(a.CacheDirectory, b.CacheDirectory);         // shared public-data cache
    }

    [Fact]
    public void SameUser_AlwaysResolvesTheSameDirectory()
    {
        // Persistence across logout → login is "by construction": the Identity user id is
        // stable, so a returning user maps to the same dir and finds their saved data.
        var root = TempRoot();
        var first = new UserScopedPathService(
            new FakeUser { IsAuthenticated = true, UserId = "user-a", DataKey = "user-a" }, accountsEnabled: true, root);
        var afterRelogin = new UserScopedPathService(
            new FakeUser { IsAuthenticated = true, UserId = "user-a", DataKey = "user-a" }, accountsEnabled: true, root);

        Assert.Equal(first.AppDataDirectory, afterRelogin.AppDataDirectory);
    }

    [Fact]
    public void AnonUser_RoutesToTheAnonBucket()
    {
        var path = new UserScopedPathService(new FakeUser { DataKey = "anon" }, accountsEnabled: true, TempRoot())
            .AppDataDirectory;
        Assert.Contains("anon", path);
    }

    [Fact]
    public void DataKey_IsSanitised_NoPathTraversal()
    {
        // A hostile/odd id can never escape the users/ root — non-alphanumerics are stripped.
        var path = new UserScopedPathService(
            new FakeUser { IsAuthenticated = true, UserId = "../../etc", DataKey = "../../etc" }, accountsEnabled: true, TempRoot())
            .AppDataDirectory;
        Assert.DoesNotContain("..", path);
        Assert.Contains("users", path);
    }

    [Fact]
    public void AccountsOff_UsesTheLegacySharedDirectory()
    {
        var off = new UserScopedPathService(
            new FakeUser { IsAuthenticated = true, UserId = "user-a", DataKey = "user-a" }, accountsEnabled: false, dataRoot: null);

        // Off == byte-for-byte the legacy single-user WebHostPathService location, so the
        // local single-user and --demo modes are untouched.
        Assert.DoesNotContain(Path.Combine("users", "user-a"), off.AppDataDirectory);
        Assert.EndsWith("AccessibleTrader", off.AppDataDirectory.TrimEnd(Path.DirectorySeparatorChar));
    }
}
