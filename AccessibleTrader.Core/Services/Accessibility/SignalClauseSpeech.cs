using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Accessibility
{
    /// <summary>
    /// How a SIGNAL clause — one built from a component's <c>SignalSpeechTemplate</c> — is
    /// introduced when it is spoken. One rule, used by the live narrator
    /// (<c>AutoNarrationService.ScanUtterance</c>) and by playback (<c>PlaybackNarration</c>),
    /// because the two had drifted once already: the live path named the series, playback named
    /// it only sometimes, and neither named the component.
    ///
    /// <para>
    /// Cody, 2026-09-05: <i>"hearing only the component name before the signal is all that is
    /// needed, not the series name as the user probably knows what they enabled for narration"</i>.
    /// Narration is opt-in per series — the listener chose which series speak — so the series name
    /// is a fact they already hold, and the component name is the one they are waiting for: WHICH
    /// of Cipher B's eleven markers just fired.
    /// </para>
    ///
    /// <para>
    /// The component leads only when the clause has not already said it. Most shipped templates
    /// are the component's own name in a sentence — "Bullish divergence", "Triple confluence buy,
    /// strong confirmation" — and "Bullish Divergence: Bullish divergence" is a stutter, not an
    /// introduction. Matched case-insensitively anywhere in the clause: "{name} at {price}"
    /// templates put the name first, hand-written ones put it wherever the sentence wants it.
    /// </para>
    /// </summary>
    public static class SignalClauseSpeech
    {
        /// <summary>The spoken name of a component: its display name, or its machine name.</summary>
        public static string ComponentName(ComponentConfig comp)
            => string.IsNullOrEmpty(comp.DisplayName) ? comp.Name : comp.DisplayName;

        /// <summary>
        /// A marker template's <c>{label}</c>: the number printed beside the marker on bar
        /// <paramref name="index"/> (its <see cref="ComponentCompanions.Label"/> companion), said as
        /// a whole number, or nothing when the bar has none. So "Buy setup {label}." says "Buy setup
        /// 7." on the bar the 7 is printed on. A template without the word is returned unchanged.
        /// </summary>
        public static string FillLabel(string template, ChartSeries series, ComponentConfig comp, int index)
        {
            if (!template.Contains("{label}")) return template;
            var labels = series.GetComponentData(comp.Name + ComponentCompanions.Label);
            string said = index >= 0 && index < labels.Length ? Rendering.ValueLabelRenderer.Spoken(labels[index]) : "";
            return template.Replace("{label}", said);
        }

        /// <summary>
        /// The clause with its component named in front — unless the clause already names it, or
        /// there is no component to name.
        /// </summary>
        public static string WithComponentName(string clause, string? componentName)
        {
            if (string.IsNullOrWhiteSpace(componentName)) return clause;
            if (clause.Contains(componentName, StringComparison.OrdinalIgnoreCase)) return clause;
            return $"{componentName}: {clause}";
        }
    }
}
