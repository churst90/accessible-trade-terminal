using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using AccessibleTrader.Sdk.Plugins;
using Xunit;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// One roster of the real provider objects, discovered rather than typed out.
    ///
    /// <para>
    /// Three separate tests used to keep their own hand-written list of providers, and all three
    /// had silently fallen behind the repo — <c>ProviderConformanceTests</c> was missing Gemini and
    /// KrakenFutures while calling itself "the universal contract every trading provider must
    /// satisfy", and <c>ProviderTimeframeContractTests</c> did not construct a provider at all: it
    /// asserted that the strings *in the test file* parsed. That is the failure mode
    /// <see cref="ProviderCapabilityHonestyTests"/> already documents being burned by — a sweep
    /// that does not enumerate everything is a spot check wearing a sweep's name.
    /// </para>
    ///
    /// <para>
    /// So the roster is derived from the build output: every <c>AccessibleTrader.Plugins.*.dll</c>
    /// beside the test assembly is loaded and every concrete <see cref="BaseMarketDataProvider"/>
    /// with a public parameterless constructor is instantiated. Adding a provider adds it here;
    /// there is nothing to remember to update.
    /// </para>
    ///
    /// <para>
    /// Discovery has its own way to go quietly wrong — a plugin project that the test project
    /// never references produces no DLL to find, so the sweep shrinks and stays green.
    /// <see cref="ProviderRosterDriftTests"/> is the cross-check: it reads the plugin directories
    /// on disk and fails if any of them contributed nothing to the roster.
    /// </para>
    ///
    /// <para>
    /// Constructing providers touches the global <c>PluginHostServices.ApiKeys</c> bridge, so every
    /// test class that uses this roster must carry <c>[Collection("ProviderCredentialBridge")]</c>.
    /// </para>
    /// </summary>
    public static class ProviderRoster
    {
        private static readonly Lazy<IReadOnlyList<Type>> _types = new(Discover);

        /// <summary>Every concrete provider type found in the plugin assemblies, name-ordered.</summary>
        public static IReadOnlyList<Type> Types => _types.Value;

        /// <summary>A fresh instance of every provider. Fresh because providers are
        /// <see cref="IDisposable"/> and hold subjects; sharing one across theory rows would let
        /// one test's disposal break the next.</summary>
        public static IEnumerable<BaseMarketDataProvider> All() =>
            Types.Select(t => (BaseMarketDataProvider)Activator.CreateInstance(t)!);

        /// <summary>The subset that declares any trading capability, i.e. the providers the
        /// order path can reach. Read from the interface, not from a list.</summary>
        public static IEnumerable<BaseMarketDataProvider> Trading() =>
            All().Where(p => p is ITradingProvider);

        private static IReadOnlyList<Type> Discover()
        {
            var found = new List<Type>();

            foreach (var dll in Directory.GetFiles(AppContext.BaseDirectory, "AccessibleTrader.Plugins.*.dll"))
            {
                Assembly asm;
                try { asm = Assembly.LoadFrom(dll); }
                catch (BadImageFormatException) { continue; }

                Type[] types;
                // A plugin whose types cannot all be loaded must not silently contribute nothing:
                // take what loaded and let the drift guard notice if the whole assembly vanished.
                try { types = asm.GetTypes(); }
                catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t != null).ToArray()!; }

                foreach (var t in types)
                {
                    if (t.IsAbstract || !typeof(BaseMarketDataProvider).IsAssignableFrom(t)) continue;
                    if (t.GetConstructor(Type.EmptyTypes) == null) continue;
                    found.Add(t);
                }
            }

            return found.DistinctBy(t => t.FullName).OrderBy(t => t.Name, StringComparer.Ordinal).ToList();
        }

        // RepoRoot() and ProviderPluginProjectsOnDisk() moved to RepoPaths: they are pure path
        // arithmetic, and keeping them here meant a test class could "use ProviderRoster" without
        // constructing a provider — which made the enrollment rule this class's doc states
        // unenforceable. Everything that REMAINS on this class constructs providers.
    }

    /// <summary>
    /// The anti-vacuity half of <see cref="ProviderRoster"/>: reflection can only find providers
    /// whose assemblies are actually in the output directory, and a plugin the test project does
    /// not reference produces no DLL. Without this, dropping a ProjectReference would shrink every
    /// sweep in the suite and leave them all green.
    /// </summary>
    [Collection("ProviderCredentialBridge")]
    public class ProviderRosterDriftTests
    {
        [Fact]
        public void Every_plugin_project_on_disk_contributes_a_provider_to_the_roster()
        {
            // A plugin project is named AccessibleTrader.Plugins.<Tag>, and its provider types
            // live in the matching namespace — so the namespace prefix is what ties a discovered
            // type back to the directory it came from.
            var namespaces = ProviderRoster.Types
                .Select(t => t.Namespace ?? "")
                .ToHashSet(StringComparer.Ordinal);

            var missing = RepoPaths.ProviderPluginProjectsOnDisk()
                .Where(project => !namespaces.Any(ns => ns == project || ns.StartsWith(project + ".", StringComparison.Ordinal)))
                .OrderBy(p => p, StringComparer.Ordinal)
                .ToList();

            Assert.True(missing.Count == 0,
                "These plugin projects exist on disk but contributed no provider to the roster, so "
              + "every provider sweep in the suite is blind to them. The usual cause is a missing "
              + "ProjectReference in AccessibleTrader.Tests.csproj: "
              + string.Join(", ", missing));
        }

        [Fact]
        public void The_roster_is_not_empty_and_finds_both_families()
        {
            // If Discover() ever returned nothing — wrong base directory, renamed assemblies —
            // every sweep built on it would pass by iterating an empty list.
            Assert.True(ProviderRoster.Types.Count >= 25,
                $"Roster found only {ProviderRoster.Types.Count} providers; the repo ships far more: "
              + string.Join(", ", ProviderRoster.Types.Select(t => t.Name)));

            Assert.NotEmpty(ProviderRoster.Trading());
        }
    }
}
