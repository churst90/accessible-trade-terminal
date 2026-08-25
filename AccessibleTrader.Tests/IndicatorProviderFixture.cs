using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Sdk.Interfaces;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// The whole set of indicator providers the app can register, built for tests.
    ///
    /// <para>
    /// Lifted out of <c>IndicatorCausalityTests</c> when a second contract — the computability one
    /// in <c>SignalCatalogComputabilityTests</c> — needed exactly the same set. Two guards asking
    /// "every provider" while each enumerating its own idea of what that means is how one of them
    /// ends up silently covering fewer providers than its name claims.
    /// </para>
    /// </summary>
    internal static class IndicatorProviderFixture
    {
        internal static IEnumerable<Type> ProviderTypes() =>
            // Core and StrategyLab. No plugin assembly implements IIndicatorProvider — the plugins
            // are data sources — so an anchor type in each of these two covers every provider that
            // can reach SignalCatalog.
            new[] { typeof(ValueDeviationProvider).Assembly, typeof(AccessibleTrader.StrategyLab.CftcCotProvider).Assembly }
                .SelectMany(a => a.GetTypes())
                .Where(t => typeof(IIndicatorProvider).IsAssignableFrom(t)
                            && !t.IsAbstract && !t.IsInterface)
                // No filter on provider subclasses. There used to be one, to skip the
                // EmaFillProvider name alias (an empty subclass of MACloudProvider, which
                // would have double-counted every MA_CLOUD component). The alias is deleted,
                // and leaving the filter behind would silently exclude a future provider that
                // legitimately derives from another one — a guard with no target that only
                // knows how to hide things.
                .OrderBy(t => t.Name);

        /// <summary>
        /// Builds a provider whatever its constructor asks for, substituting its interface
        /// dependencies. Requiring a parameterless constructor would have quietly excluded the three
        /// StrategyLab providers, which take an <c>ICrossSeriesCache</c> — and a contract that skips
        /// the providers feeding the research tooling is not much of a contract.
        /// </summary>
        internal static IIndicatorProvider Create(Type type)
        {
            var ctor = type.GetConstructors().OrderBy(c => c.GetParameters().Length).First();
            var args = ctor.GetParameters().Select(p =>
                p.ParameterType.IsInterface
                    ? NSubstitute.Substitute.For(new[] { p.ParameterType }, Array.Empty<object>())
                    : p.ParameterType.IsValueType ? Activator.CreateInstance(p.ParameterType) : null)
                .ToArray();
            return (IIndicatorProvider)ctor.Invoke(args);
        }

        internal static List<IIndicatorProvider> AllProviders() =>
            ProviderTypes().Select(Create).ToList();
    }
}
