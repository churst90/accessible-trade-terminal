using AccessibleTrader.Core.Services;
using AccessibleTrader.Sdk.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging;

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

/// <summary>
/// <b>An install that refuses every plugin must say so, not merely behave as though none are
/// installed.</b>
///
/// <para>
/// Reported from a Windows VM on 2026-09-21, the first time the desktop head had been exercised
/// there: the market dropdown offered only the built-in providers and nothing anywhere said why.
/// That is what an empty trusted allow-list looks like from the outside. The refusal itself is
/// correct and deliberate — <c>plugins_trusted.manifest</c> is generated against the build output
/// and an install whose manifest did not travel with it, or whose DLLs were rebuilt after it,
/// SHOULD refuse the fleet. What was missing is that the only account of it was one
/// <c>LogWarning</c> per DLL, and the MAUI Release head registers no logging providers at all.
/// </para>
///
/// <para>
/// The distinction the code now draws is between SOME refused and ALL refused, and no single
/// iteration of the discovery loop can draw it — the interesting quantity is the ratio.
/// </para>
/// </summary>
public class PluginTrustRefusalReportingTests
{
    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message)> Lines { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                                Func<TState, Exception?, string> formatter)
            => Lines.Add((logLevel, formatter(state, exception)));
    }

    [Fact]
    public void WhenEveryPluginIsRefusedForTrust_ThatIsReportedAsItsOwnFact()
    {
        var log = new CapturingLogger<PluginLoaderService>();
        // RequireTrusted with an allow-list nothing matches — the shape of a missing manifest.
        var loader = new PluginLoaderService(log, new PluginTrustPolicy { RequireTrusted = true });

        var loaded = loader.LoadPlugins<IProviderPlugin>(AppContext.BaseDirectory).ToList();

        Assert.Empty(loaded);

        var errors = log.Lines.Where(l => l.Level == LogLevel.Error).ToList();
        Assert.True(errors.Count > 0,
            "every plugin was refused and the only record was per-DLL warnings — the user sees an empty "
          + "provider list with no account of why");
        Assert.Contains(errors, e => e.Message.Contains("refused", StringComparison.OrdinalIgnoreCase)
                                  && e.Message.Contains("plugins_trusted.manifest", StringComparison.Ordinal));
    }

    /// <summary>
    /// And the report is about the WHOLESALE case only. A fleet that loads fine must not log an
    /// error, or the signal is worth nothing the first time it matters.
    /// </summary>
    [Fact]
    public void WhenPluginsLoadNormally_NothingIsReportedAsAnError()
    {
        var log = new CapturingLogger<PluginLoaderService>();
        var loader = new PluginLoaderService(log, new PluginTrustPolicy { RequireTrusted = false });

        var loaded = loader.LoadPlugins<IProviderPlugin>(AppContext.BaseDirectory).ToList();

        Assert.NotEmpty(loaded);
        Assert.DoesNotContain(log.Lines, l => l.Level == LogLevel.Error);
    }
}
