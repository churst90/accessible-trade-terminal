using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// <c>GetComponentSpeech</c> is keyed on a component's <see cref="IndicatorComponentMetadata.Name"/>,
    /// never its <c>DisplayName</c>.
    ///
    /// <para>
    /// The two are different strings — Value Deviation's marker components are named
    /// <c>"SupportShallow"</c> and displayed as <c>"Support tier 1-2"</c> — and a provider whose
    /// switch is written against the display names matches nothing. Every case falls through to
    /// <c>null</c>, the speech pipeline quietly moves on to its generic template, and the user
    /// hears a bare number where the provider author wrote a sentence.
    /// </para>
    ///
    /// <para>
    /// Nothing throws. No test failed. The provider looks correct in isolation, and the whole
    /// point of the imperative speech path — that an indicator can explain itself in its own words
    /// — is silently lost. It was found by someone navigating the chart and noticing the speech
    /// was uninformative, which is not a repeatable way to catch it.
    /// </para>
    ///
    /// <para>
    /// This detects it structurally: if a provider will speak for a component's DISPLAY name but
    /// not for its real name, its switch is keyed on the wrong string.
    /// </para>
    /// </summary>
    public class ComponentSpeechKeyTests
    {
        /// <summary>
        /// Every built-in indicator provider, constructed reflectively so a provider added later
        /// is covered without anyone remembering to add it here.
        /// </summary>
        public static IEnumerable<object[]> Providers() =>
            typeof(AccessibleTrader.Core.Services.Indicators.ValueDeviationProvider).Assembly
                .GetTypes()
                .Where(t => typeof(IIndicatorProvider).IsAssignableFrom(t)
                            && !t.IsAbstract && !t.IsInterface
                            && t.GetConstructor(Type.EmptyTypes) != null)
                .OrderBy(t => t.Name)
                .Select(t => new object[] { t });

        [Theory]
        [MemberData(nameof(Providers))]
        public void AProvidersSpeechSwitch_isKeyedOnComponentNameNotDisplayName(Type providerType)
        {
            var provider = (IIndicatorProvider)Activator.CreateInstance(providerType)!;

            var bar = new Ohlcv(new DateTime(2026, 1, 1), 100, 110, 90, 105, 1000);
            var empty = new Dictionary<string, double[]>();
            var offenders = new List<string>();

            foreach (var meta in Safe(() => provider.GetIndicators()) ?? new List<IndicatorMetadata>())
                foreach (var comp in meta.Components)
                {
                    if (string.IsNullOrEmpty(comp.DisplayName) || comp.DisplayName == comp.Name) continue;

                    // A provider is entitled to decline for any component — most do, for most of
                    // them. The bug signature is narrower and unambiguous: it speaks for the
                    // DISPLAY name and stays silent for the name it is actually called with.
                    string? byName    = Safe(() => provider.GetComponentSpeech(comp.Name, 100, bar, empty, 0));
                    string? byDisplay = Safe(() => provider.GetComponentSpeech(comp.DisplayName, 100, bar, empty, 0));

                    if (byName == null && byDisplay != null)
                        offenders.Add($"{meta.Code}.{comp.Name} (display \"{comp.DisplayName}\")");
                }

            Assert.True(offenders.Count == 0,
                $"{providerType.Name} keys its speech switch on DisplayName. SpeechFormatter passes " +
                $"Name, so these components fall through to the generic template and lose the " +
                $"sentence the provider wrote:\n  " + string.Join("\n  ", offenders));
        }

        /// <summary>
        /// Providers may throw on odd inputs — a missing companion array, an index they did not
        /// expect. That is not what this test is about, so a throw counts as "declined".
        /// </summary>
        private static T? Safe<T>(Func<T?> f) where T : class
        {
            try { return f(); } catch { return null; }
        }
    }
}
