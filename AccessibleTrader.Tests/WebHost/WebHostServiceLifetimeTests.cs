using AccessibleTrader.BlazorClient.Services;
using AccessibleTrader.Core.Services;
using AccessibleTrader.WebHost;
using Microsoft.Extensions.DependencyInjection;

namespace AccessibleTrader.Tests.WebHost;

/// <summary>
/// Guards the multi-user web-host DI contract (see docs/WEBHOST_MULTI_USER_SCOPING.md).
/// On the public web demo every Blazor circuit is its own DI scope, so per-visitor state
/// services MUST be Scoped — if one is accidentally made Singleton again, one visitor's
/// chart/indicators/navigation leak into everyone else's. These tests fail fast if that
/// regression is reintroduced, and if a captive dependency (Singleton consuming a Scoped
/// service) sneaks in.
/// </summary>
public class WebHostServiceLifetimeTests
{
    private static ServiceLifetime LifetimeOf<T>(IServiceCollection s) =>
        s.Last(d => d.ServiceType == typeof(T)).Lifetime;

    [Fact]
    public void PerUserStateServices_AreScoped()
    {
        var s = new ServiceCollection().AddAccessibleTraderWebHostServices();

        Assert.Equal(ServiceLifetime.Scoped, LifetimeOf<IEventBus>(s));         // per-circuit pub/sub backbone
        Assert.Equal(ServiceLifetime.Scoped, LifetimeOf<IWorkspaceStore>(s));   // the chart state itself
        Assert.Equal(ServiceLifetime.Scoped, LifetimeOf<IMarketOrchestrator>(s));
        Assert.Equal(ServiceLifetime.Scoped, LifetimeOf<IDataManager>(s));
        Assert.Equal(ServiceLifetime.Scoped, LifetimeOf<IDataService>(s));      // per-circuit provider instances
        Assert.Equal(ServiceLifetime.Scoped, LifetimeOf<IIndicatorService>(s));
        Assert.Equal(ServiceLifetime.Scoped, LifetimeOf<GlobalInputService>(s));
        Assert.Equal(ServiceLifetime.Scoped, LifetimeOf<ISpeechManager>(s));
    }

    [Fact]
    public void SharedInfrastructure_StaysSingleton()
    {
        var s = new ServiceCollection().AddAccessibleTraderWebHostServices();

        // Loaded plugin assemblies + trust policy + key store + caches are process-wide;
        // making them per-circuit would reload DLLs per visitor (PluginLoaderService also
        // backs the type cache) or create captive dependencies.
        Assert.Equal(ServiceLifetime.Singleton, LifetimeOf<IPluginLoaderService>(s));
        Assert.Equal(ServiceLifetime.Singleton, LifetimeOf<PluginTrustPolicy>(s));
        Assert.Equal(ServiceLifetime.Singleton, LifetimeOf<IApiKeyService>(s));
    }

    [Fact]
    public void TwoCircuits_GetIndependentState()
    {
        using var provider = new ServiceCollection()
            .AddAccessibleTraderWebHostServices()
            .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        using var circuitA = provider.CreateScope();
        using var circuitB = provider.CreateScope();

        var busA  = circuitA.ServiceProvider.GetRequiredService<IEventBus>();
        var busA2 = circuitA.ServiceProvider.GetRequiredService<IEventBus>();
        var busB  = circuitB.ServiceProvider.GetRequiredService<IEventBus>();

        Assert.Same(busA, busA2);   // same circuit → same instance
        Assert.NotSame(busA, busB); // different circuits → independent instances (isolation)
    }
}
