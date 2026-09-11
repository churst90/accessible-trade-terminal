using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Core.Services.Workspace;
using AccessibleTrader.Tests.Mocks;
using AccessibleTrader.WebHost.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace AccessibleTrader.Tests.WebHost;

/// <summary>
/// The hand-off to the headless monitors happens when the CONNECTION drops, not when the
/// circuit is finally disposed.
///
/// <para>
/// Closing a tab does not close a Blazor circuit: the server keeps a disconnected circuit for
/// <c>DisconnectedCircuitRetentionPeriod</c> (three minutes by default) in case the client
/// comes back, and <c>OnCircuitClosedAsync</c> — where coverage used to be released — runs when
/// that expires. For those minutes the circuit claimed its symbols while its speech went into a
/// live region nobody would render, and the headless monitor, correctly, left them alone.
/// </para>
/// </summary>
[Collection("CircuitCoverage")]
public sealed class CircuitCoverageHandoffTests : IDisposable
{
    public CircuitCoverageHandoffTests() => CircuitAlertCoverage.ResetForTests();
    public void Dispose() => CircuitAlertCoverage.ResetForTests();

    private static WebHostBrowserCircuitHandler Handler(string symbol)
    {
        var store = new MockWorkspaceStore();
        store.EmitState(WorkspaceState.Initial with { SymbolDisplayName = symbol });
        var services = new ServiceCollection();
        services.AddSingleton<IWorkspaceStore>(store);
        return new WebHostBrowserCircuitHandler(
            NullLogger<WebHostBrowserCircuitHandler>.Instance, services.BuildServiceProvider());
    }

    [Fact]
    public async Task A_dropped_connection_hands_the_symbols_to_the_headless_side_at_once()
    {
        var handler = Handler("BTCUSDT");
        handler.RegisterCoverage("c1");
        Assert.Contains("BTCUSDT", CircuitAlertCoverage.CoveredSymbols());

        await handler.OnConnectionDownAsync(null!, CancellationToken.None);

        Assert.Empty(CircuitAlertCoverage.CoveredSymbols());
        Assert.False(handler.HoldsCoverage);
    }

    [Fact]
    public async Task A_reconnect_takes_them_back()
    {
        var handler = Handler("BTCUSDT");
        handler.RegisterCoverage("c1");
        await handler.OnConnectionDownAsync(null!, CancellationToken.None);

        // Blazor passes the same circuit on reconnect; the id is remembered from registration,
        // which is what lets the test hand over no Circuit at all (its constructor is internal).
        await handler.OnConnectionUpAsync(null!, CancellationToken.None);

        Assert.Contains("BTCUSDT", CircuitAlertCoverage.CoveredSymbols());
    }

    [Fact]
    public async Task Registering_twice_holds_one_registration_not_two()
    {
        // OnConnectionUpAsync also runs for the FIRST connection, right after
        // OnCircuitOpenedAsync has registered. A second registration under the same id would
        // be harmless in the dictionary but would leak the first IDisposable.
        var handler = Handler("BTCUSDT");
        handler.RegisterCoverage("c1");
        await handler.OnConnectionUpAsync(null!, CancellationToken.None);

        Assert.Equal(1, CircuitAlertCoverage.SourceCount);
        await handler.OnConnectionDownAsync(null!, CancellationToken.None);
        Assert.Equal(0, CircuitAlertCoverage.SourceCount);
    }
}
