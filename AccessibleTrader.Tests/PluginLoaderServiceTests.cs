using AccessibleTrader.Core.Services;
using AccessibleTrader.Sdk.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;

namespace AccessibleTrader.Tests;

/// <summary>
/// Pins the type-cache contract that makes per-circuit data isolation cheap (see
/// docs/WEBHOST_MULTI_USER_SCOPING.md): plugin DLLs/types are discovered ONCE, but each
/// LoadPlugins call returns FRESH instances. A per-circuit (Scoped) DataService relies on
/// this to give every web visitor its own provider objects (own connections/subscriptions)
/// without re-loading assemblies into a new AssemblyLoadContext per connection.
/// </summary>
public class PluginLoaderServiceTests
{
    [Fact]
    public void LoadPlugins_ReturnsFreshInstancesOfTheSameTypes()
    {
        // The test output directory carries the referenced provider plugin DLLs.
        var dir = AppContext.BaseDirectory;
        var loader = new PluginLoaderService(
            NullLogger<PluginLoaderService>.Instance,
            new PluginTrustPolicy { RequireTrusted = false }); // load unverified in tests

        var first  = loader.LoadPlugins<IProviderPlugin>(dir).ToList();
        var second = loader.LoadPlugins<IProviderPlugin>(dir).ToList();

        Assert.NotEmpty(first);

        // Same set of plugin TYPES both calls (assemblies loaded + cached once)...
        var firstTypes  = first.Select(p => p.GetType().FullName).OrderBy(x => x).ToList();
        var secondTypes = second.Select(p => p.GetType().FullName).OrderBy(x => x).ToList();
        Assert.Equal(firstTypes, secondTypes);

        // ...but every instance is distinct, so two circuits never share a provider object.
        foreach (var a in first)
            Assert.DoesNotContain(second, b => ReferenceEquals(a, b));
    }
}
