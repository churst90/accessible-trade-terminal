using System.Collections.Concurrent;
using System.Collections.Generic;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Indicators
{
    /// <summary>
    /// Stores ICustomIndicator instances compiled at runtime (via Roslyn or Pine transpiler).
    /// Keyed by indicator ID so IndicatorEngine can route CalculateAsync to the compiled instance
    /// instead of falling through to IIndicatorService.
    /// Thread-safe: ConcurrentDictionary handles concurrent compiles.
    ///
    /// <para>
    /// Registration is also where a script's causality is established. Every scripted indicator in
    /// the app arrives through <see cref="Register"/> — there is no other door — so it is the one
    /// place that can guarantee no script reaches a chart without having been asked whether its
    /// values move when the data around them changes. See
    /// <see cref="CustomIndicatorCausalityProbe"/> for what is asked and why a self-declaration is
    /// not enough on its own.
    /// </para>
    /// </summary>
    public interface ICustomIndicatorRegistry
    {
        void Register(ICustomIndicator indicator);
        bool TryGet(string indicatorId, out ICustomIndicator? indicator);
        void Unregister(string indicatorId);
        IReadOnlyCollection<ICustomIndicator> GetAll();

        /// <summary>
        /// What the probe concluded about this indicator when it was registered. Null when the id
        /// is unknown. Callers that intend to TRADE a scripted component must consult this —
        /// drawing an unproven component is a display decision, trading one is not.
        /// </summary>
        CustomIndicatorCausalityReport? Causality(string indicatorId);

        /// <summary>
        /// Whether <paramref name="componentName"/> of <paramref name="indicatorId"/> may be
        /// offered to the strategy builder. False for anything unknown, unproven, or refused —
        /// the same "silence is refusal" default the built-in catalog uses, for the same reason.
        /// </summary>
        bool IsPublishable(string indicatorId, string componentName);
    }

    public class CustomIndicatorRegistry : ICustomIndicatorRegistry
    {
        private readonly ConcurrentDictionary<string, ICustomIndicator> _indicators =
            new(System.StringComparer.OrdinalIgnoreCase);

        private readonly ConcurrentDictionary<string, CustomIndicatorCausalityReport> _causality =
            new(System.StringComparer.OrdinalIgnoreCase);

        public void Register(ICustomIndicator indicator)
        {
            _indicators[indicator.Id] = indicator;
            _causality[indicator.Id] = CustomIndicatorCausalityProbe.Probe(indicator);
        }

        public bool TryGet(string indicatorId, out ICustomIndicator? indicator)
            => _indicators.TryGetValue(indicatorId, out indicator);

        public void Unregister(string indicatorId)
        {
            _indicators.TryRemove(indicatorId, out _);
            _causality.TryRemove(indicatorId, out _);
        }

        public IReadOnlyCollection<ICustomIndicator> GetAll()
            => (IReadOnlyCollection<ICustomIndicator>)_indicators.Values;

        public CustomIndicatorCausalityReport? Causality(string indicatorId)
            => _causality.TryGetValue(indicatorId, out var report) ? report : null;

        public bool IsPublishable(string indicatorId, string componentName)
            => Causality(indicatorId)?.For(componentName)?.Publishable ?? false;
    }
}
