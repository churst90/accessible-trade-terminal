using AccessibleTrader.Core.Services;
using AccessibleTrader.Tests.Mocks;
using AccessibleTrader.WebHost.Account;
using AccessibleTrader.WebHost.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AccessibleTrader.Tests.WebHost;

/// <summary>
/// The hosted paper account must never be resolved before the circuit user is known.
///
/// <para>
/// <c>PaperTradingProvider</c> reads <c>AppDataDirectory</c> in its constructor, and
/// <c>UserScopedPathService</c>'s contract is "computed on access, AFTER the circuit handler has
/// set <c>ICurrentUser</c>". Today that ordering holds only because App.razor disables
/// prerendering. If prerendering came back — or anything resolved the broker from a pre-circuit
/// scope — every hosted user's paper account would silently become
/// <c>users/anon/paper_account.json</c>: one shared account for the whole site. These tests pin
/// that the failure is now LOUD (a thrown exception naming the contract) instead of silent
/// sharing of money state.
/// </para>
/// </summary>
public sealed class PaperAccountAttachmentGuardTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("att-guard-").FullName;
    private readonly PaperAccountHub _hub = new();

    public void Dispose()
    {
        _hub.Dispose();
        try { Directory.Delete(_dir, recursive: true); } catch { /* temp */ }
    }

    private PaperAccountAttachment Build(ICurrentUser? user, DemoPolicy? demo)
    {
        var paths = Substitute.For<IPlatformPathService>();
        paths.AppDataDirectory.Returns(_dir);
        return new PaperAccountAttachment(
            _hub, new MockWorkspaceStore(), paths,
            NullLogger<PaperTradingProvider>.Instance,
            Substitute.For<IEventBus>(), Substitute.For<IDataService>(),
            user, demo);
    }

    [Fact]
    public void Hosted_with_no_circuit_user_refuses_instead_of_binding_everyone_to_anon()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => Build(user: null, demo: new DemoPolicy(HostMode.Hosted)));
        Assert.Contains("anon", ex.Message);

        // And crucially: nothing was created — the shared account must not exist as a side effect.
        Assert.Empty(_hub.ActiveUsers);
    }

    [Fact]
    public void Hosted_with_an_unauthenticated_user_refuses_too()
    {
        var current = new CurrentUser();   // Set() never called → IsAuthenticated false, DataKey "anon"
        Assert.Throws<InvalidOperationException>(
            () => Build(current, new DemoPolicy(HostMode.Hosted)));
    }

    [Fact]
    public void Hosted_with_the_circuit_user_set_builds_that_users_account()
    {
        var current = new CurrentUser();
        current.Set("user-42");

        using var attachment = Build(current, new DemoPolicy(HostMode.Hosted));

        Assert.NotNull(attachment.Account);
        Assert.Contains("user-42", _hub.ActiveUsers);
    }

    [Fact]
    public void Full_mode_stays_single_user_and_anonymous_on_purpose()
    {
        // The local terminal has no accounts; "anon" IS the user. The guard is
        // hosted-only — Full mode must keep working with no ICurrentUser at all.
        using var attachment = Build(user: null, demo: new DemoPolicy(HostMode.Full));

        Assert.NotNull(attachment.Account);
        Assert.Contains("anon", _hub.ActiveUsers);
    }
}
