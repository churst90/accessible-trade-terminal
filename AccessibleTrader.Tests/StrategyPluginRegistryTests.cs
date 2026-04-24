using System;
using System.IO;
using System.Linq;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Strategies;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AccessibleTrader.Tests;

/// <summary>
/// End-to-end coverage for the Phase 10-F DLL-plugin loader.
///
/// Every test materialises a temporary scratch directory, copies the fixture-plugin
/// DLL (<c>AccessibleTrader.Plugins.Strategy.Fixture.dll</c>, built as a test
/// project reference) into it, constructs a <see cref="StrategyPluginRegistry"/>
/// pointed at that directory, and verifies load / instantiate / unload behaviour.
/// Using a scratch directory per test isolates each test's AssemblyLoadContext so
/// one test's unload can't race another's load.
/// </summary>
public sealed class StrategyPluginRegistryTests : IDisposable
{
    private readonly string _scratchDir;

    public StrategyPluginRegistryTests()
    {
        _scratchDir = Path.Combine(
            Path.GetTempPath(),
            "AccessibleTrader.StrategyPluginTests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_scratchDir);

        // Fixture DLL lands next to the test assembly's output. Copy it into the
        // scratch dir so the loader scan picks it up without collecting the rest
        // of the test-assembly directory.
        var fixtureSource = Path.Combine(
            AppContext.BaseDirectory,
            "AccessibleTrader.Plugins.Strategy.Fixture.dll");
        if (!File.Exists(fixtureSource))
        {
            throw new FileNotFoundException(
                "Fixture plugin DLL missing from test output directory. " +
                "Ensure AccessibleTrader.Plugins.Strategy.Fixture is built before running tests.",
                fixtureSource);
        }
        File.Copy(fixtureSource,
            Path.Combine(_scratchDir, "AccessibleTrader.Plugins.Strategy.Fixture.dll"));
    }

    private StrategyPluginRegistry BuildRegistry(bool requireTrusted = false)
    {
        var trust = new PluginTrustPolicy { RequireTrusted = requireTrusted };
        if (requireTrusted)
        {
            var hash = PluginTrustPolicy.ComputeSha256(
                Path.Combine(_scratchDir, "AccessibleTrader.Plugins.Strategy.Fixture.dll"));
            if (hash != null) trust.TrustedSha256.Add(hash);
        }
        return new StrategyPluginRegistry(
            NullLogger<StrategyPluginRegistry>.Instance,
            trust,
            new[] { _scratchDir });
    }

    [Fact]
    public void Initialize_loads_fixture_plugin_template()
    {
        using var registry = new Wrap(BuildRegistry());
        registry.Inner.Initialize();

        Assert.Single(registry.Inner.LoadedPluginNames);
        Assert.Single(registry.Inner.Templates);
        var tmpl = registry.Inner.Templates[0];
        Assert.Equal("fixture.plugin.noop.v1", tmpl.Id);
        Assert.Equal("Fixture No-Op", tmpl.Name);
    }

    [Fact]
    public void Initialize_is_idempotent()
    {
        using var registry = new Wrap(BuildRegistry());
        registry.Inner.Initialize();
        registry.Inner.Initialize();
        registry.Inner.Initialize();

        Assert.Single(registry.Inner.Templates);
    }

    [Fact]
    public void UnloadAll_clears_templates_and_permits_reinitialise()
    {
        using var registry = new Wrap(BuildRegistry());
        registry.Inner.Initialize();
        Assert.Single(registry.Inner.Templates);

        registry.Inner.UnloadAll();
        Assert.Empty(registry.Inner.Templates);
        Assert.Empty(registry.Inner.LoadedPluginNames);

        registry.Inner.Initialize();
        Assert.Single(registry.Inner.Templates);
    }

    [Fact]
    public void RequireTrusted_without_manifest_entry_skips_plugin()
    {
        // RequireTrusted=true but the fixture hash is not in the trusted set → skipped.
        var trust = new PluginTrustPolicy { RequireTrusted = true };
        using var registry = new Wrap(new StrategyPluginRegistry(
            NullLogger<StrategyPluginRegistry>.Instance,
            trust,
            new[] { _scratchDir }));

        registry.Inner.Initialize();
        Assert.Empty(registry.Inner.Templates);
    }

    [Fact]
    public void RequireTrusted_with_matching_hash_loads_plugin()
    {
        using var registry = new Wrap(BuildRegistry(requireTrusted: true));
        registry.Inner.Initialize();

        Assert.Single(registry.Inner.Templates);
    }

    [Fact]
    public void Templates_survive_gc_and_finalizers()
    {
        // Cheap smoke test: templates are reference-typed and rooted by the
        // registry, so a GC run shouldn't strip them.
        using var registry = new Wrap(BuildRegistry());
        registry.Inner.Initialize();

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Assert.Single(registry.Inner.Templates);
    }

    [Fact]
    public void MissingDirectory_is_tolerated()
    {
        // A directory path that doesn't exist should never throw — first-run hosts
        // may not have the drop-in folder created yet.
        var trust = new PluginTrustPolicy();
        using var registry = new Wrap(new StrategyPluginRegistry(
            NullLogger<StrategyPluginRegistry>.Instance,
            trust,
            new[] { Path.Combine(_scratchDir, "does-not-exist") }));

        registry.Inner.Initialize();
        Assert.Empty(registry.Inner.Templates);
    }

    public void Dispose()
    {
        try { Directory.Delete(_scratchDir, recursive: true); }
        catch { /* best-effort — locked DLLs from aborted tests are acceptable */ }
    }

    /// <summary>
    /// Narrow helper that disposes the registry on test-teardown without coupling
    /// the tests to <see cref="IDisposable"/> on <see cref="IStrategyPluginRegistry"/>.
    /// </summary>
    private sealed class Wrap : IDisposable
    {
        public StrategyPluginRegistry Inner { get; }
        public Wrap(StrategyPluginRegistry inner) { Inner = inner; }
        public void Dispose() { try { Inner.UnloadAll(); } catch { } }
    }
}
