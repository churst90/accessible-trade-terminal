using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace AccessibleTrader.Tests.WebHost;

/// <summary>
/// Full mode refuses a non-loopback bind — asserted on a REAL Kestrel socket, through
/// <c>Program.cs</c>.
///
/// <para>
/// <b>What nothing checked (A2o, survivor O21).</b> <see cref="FullModeBindPolicyTests"/> pins
/// the address classifier thoroughly — wildcards, 0.0.0.0, [::], the first offender among
/// safe ones — and every one of those tests stayed green when the CALL SITE in
/// <c>Program.cs</c> was inverted to <c>args.Contains("--unsafe-remote-full")</c>: the guard
/// then ran only for operators who had asked to skip it, and a plain start on a public bind
/// handed every anonymous visitor live trading, the API-keys modal and server-side scripts.
/// Every other WebHost integration test runs on <c>TestServer</c>, whose address list is
/// empty, so the guard had nothing to refuse there even when wired correctly.
/// </para>
///
/// <para>
/// .NET 10's <see cref="WebApplicationFactory{TEntryPoint}.UseKestrel()"/> boots the real
/// server. The endpoint comes from the same <c>Kestrel:Endpoints:Http:Url</c> key
/// <c>appsettings.json</c> uses, overridden to port 0 so parallel runs never collide on 5145.
/// The refusal is observed the way an operator sees it: the host stops. A loopback bind is
/// the positive control — a guard that stopped EVERY start would pass the first test alone.
/// </para>
///
/// <para>
/// The refusing test binds all interfaces for as long as it takes <c>Program.cs</c> to read
/// its own address list and stop — that is the defect being guarded, reproduced on an
/// ephemeral port.
/// </para>
/// </summary>
[Collection("ProviderCredentialBridge")]
public sealed class FullModeBindRefusalIntegrationTests : IDisposable
{
    private readonly PluginBridgeScope _bridges = new();
    public void Dispose() => _bridges.Dispose();

    private static WebApplicationFactory<WebHostDemoMode> FullOnKestrel(string url)
    {
        var factory = WebHostIntegration.FullFactory()
            .WithWebHostBuilder(b => b.UseSetting("Kestrel:Endpoints:Http:Url", url));
        factory.UseKestrel();
        return factory;
    }

    private static async Task<bool> StopsWithin(IHostApplicationLifetime lifetime, TimeSpan window)
    {
        var deadline = DateTime.UtcNow + window;
        while (DateTime.UtcNow < deadline)
        {
            if (lifetime.ApplicationStopping.IsCancellationRequested) return true;
            await Task.Delay(50);
        }
        return lifetime.ApplicationStopping.IsCancellationRequested;
    }

    [Fact]
    public async Task Full_mode_on_an_all_interfaces_bind_stops_instead_of_serving()
    {
        using var factory = FullOnKestrel("http://0.0.0.0:0");
        try { factory.StartServer(); }
        catch (InvalidOperationException ex) when (ex.Message.Contains("Refusing to serve Full mode"))
        {
            return;   // refused before the factory even finished starting: the property holds
        }

        var lifetime = factory.Services.GetRequiredService<IHostApplicationLifetime>();
        Assert.True(await StopsWithin(lifetime, TimeSpan.FromSeconds(20)),
            "Full mode kept serving on 0.0.0.0 — the unauthenticated terminal (live trading, API keys, "
            + "scripts) is reachable from every interface. Program.cs must refuse a non-loopback bind "
            + "unless --unsafe-remote-full was passed.");
    }

    [Fact]
    public async Task Full_mode_on_a_loopback_bind_keeps_serving()
    {
        using var factory = FullOnKestrel("http://127.0.0.1:0");
        factory.StartServer();

        var lifetime = factory.Services.GetRequiredService<IHostApplicationLifetime>();
        Assert.False(await StopsWithin(lifetime, TimeSpan.FromSeconds(3)),
            "a loopback-only Full-mode terminal was stopped — the bind guard refuses the safe case too");
    }
}
