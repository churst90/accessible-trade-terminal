using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Scripting;
using AccessibleTrader.Sdk.Models;
using NSubstitute;

namespace AccessibleTrader.Tests;

/// <summary>
/// <c>AllowCustomScripts</c> was enforced only in Razor <c>@if</c> markup, while
/// <c>DemoPolicy</c> itself calls server-side Roslyn "RCE". These pin the
/// service-layer wall: on a Hosted or Demo head, every compile/execute entry
/// point refuses before touching user code — the hidden modal is presentation,
/// not the boundary.
/// </summary>
[Collection("ScriptWorker")] // spawns a real worker / bwrap — see ScriptWorkerCollection
public class ScriptingPolicyWallTests
{
    private static RoslynScriptingService Roslyn(HostMode mode) =>
        new(Substitute.For<IScriptWorkerLauncher>(), () => "unused-worker-path", new DemoPolicy(mode));

    private const string Code = "public class X { }";

    [Theory]
    [InlineData(HostMode.Hosted)]
    [InlineData(HostMode.Demo)]
    public async Task HostedAndDemo_RefuseEveryRoslynEntryPoint(HostMode mode)
    {
        var svc = Roslyn(mode);

        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.CompileIndicatorAsync(Code));
        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.CompileStrategyAsync(Code));
        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.ExecuteSimpleAsync(Code, new List<Ohlcv>()));
    }

    [Fact]
    public async Task FullMode_ReachesTheCompiler()
    {
        // Vacuity check: the wall must not refuse the desktop. Empty code gets past
        // the policy and comes back as an ordinary compile failure, not a policy throw.
        var svc = Roslyn(HostMode.Full);
        var result = await svc.CompileIndicatorAsync("");
        Assert.False(result.Success);
    }

    [Theory]
    [InlineData(HostMode.Hosted)]
    [InlineData(HostMode.Demo)]
    public async Task HostedAndDemo_RefuseTheInProcessScriptPathToo(HostMode mode)
    {
        // ScriptingService runs CSharpScript IN PROCESS — no sandbox at all —
        // so it gets the same wall.
        var svc = new ScriptingService(new DemoPolicy(mode));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.ExecuteScriptAsync("Close.Select(x => x)", new List<Ohlcv>()));
    }

    [Fact]
    public async Task FullMode_InProcessPath_StaysUsable()
    {
        var svc = new ScriptingService(new DemoPolicy(HostMode.Full));
        var result = await svc.ExecuteScriptAsync("", new List<Ohlcv>());
        Assert.False(result.Success);           // empty code is an ordinary refusal…
        Assert.Contains("empty", result.ErrorMessage, StringComparison.OrdinalIgnoreCase); // …not a policy throw
    }
}
