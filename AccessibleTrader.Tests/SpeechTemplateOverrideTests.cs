using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Sdk.Models;
using Xunit;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Pins the per-indicator speech-template override contract exposed via the Properties
    /// modal's new Speech tab. The override field is <see cref="ComponentConfig.SpeechTemplate"/>
    /// itself — <see cref="SpeechFormatter"/>'s <c>StandardTemplateStrategy</c> reads it
    /// directly (SpeechFormatter.cs:506) and falls back to the generic <c>"{name}. {type}. {value}."</c>
    /// pattern when the value is null or empty.
    ///
    /// These tests verify:
    /// <list type="number">
    ///   <item>A user-set override is applied verbatim to the rendered speech.</item>
    ///   <item>An empty string falls back to the generic default (so a cleared field
    ///         doesn't accidentally silence narration).</item>
    ///   <item>A marker-type component's <see cref="ComponentConfig.SignalSpeechTemplate"/>
    ///         override takes priority over the continuous template.</item>
    /// </list>
    /// </summary>
    public class SpeechTemplateOverrideTests
    {
        [Fact]
        public void Continuous_override_is_applied_verbatim_with_token_substitution()
        {
            var series = SingleComponent(out _, c =>
            {
                c.Name = "rsi";
                c.DisplayName = "RSI";
                c.DisplayType = ComponentDisplayType.Line;
                c.IsVisible = true;
                // User-authored override — distinct enough from the default to prove it's
                // the override being rendered and not the fallback.
                c.SpeechTemplate = "custom prefix for {name} at {value:F1}.";
            }, values: new[] { 64.5 });

            var msg = Format(series, focusedCompIndex: 0);
            Assert.Equal("custom prefix for RSI at 64.5.", msg);
        }

        [Fact]
        public void Empty_continuous_override_falls_back_to_generic_template()
        {
            // The Reset button writes an empty string when the provider declared no default.
            // An empty SpeechTemplate must route through the generic fallback rather than
            // narrating an empty literal — otherwise Y-navigation would silently stop
            // announcing values the moment the user clicked Reset on a provider-less
            // template.
            var series = SingleComponent(out _, c =>
            {
                c.Name = "custom";
                c.DisplayName = "Custom";
                c.DisplayType = ComponentDisplayType.Line;
                c.IsVisible = true;
                c.SpeechTemplate = "";
            }, values: new[] { 42.0 });

            var msg = Format(series, focusedCompIndex: 0);
            Assert.Equal("Custom. line. 42.00.", msg);
        }

        [Fact]
        public void Signal_override_on_marker_takes_priority_over_continuous_template()
        {
            // MarkerSignalStrategy runs before StandardTemplateStrategy; when a marker
            // component has a non-null SignalSpeechTemplate AND the current bar has a
            // non-NaN value, the signal template wins. The Speech tab's "Signal template"
            // field writes SignalSpeechTemplate directly. Continuous SpeechTemplate on
            // the same component must be IGNORED for these hits.
            var series = SingleComponent(out _, c =>
            {
                c.Name = "dot";
                c.DisplayName = "Blue Dot";
                c.DisplayType = ComponentDisplayType.Dot;
                c.IsVisible = true;
                c.SpeechTemplate          = "continuous narration for {name}.";
                c.SignalSpeechTemplate    = "Buy signal on {name} at {price}.";
            }, values: new[] { 12345.0 });

            var msg = Format(series, focusedCompIndex: 0);
            Assert.Contains("Buy signal on Blue Dot", msg);
            Assert.DoesNotContain("continuous narration", msg);
        }

        [Fact]
        public void Signal_override_set_to_null_falls_through_to_continuous_template()
        {
            // Clearing the signal-template field in the UI stores null. Without a signal
            // template, marker components must fall back to StandardTemplateStrategy —
            // which renders the continuous template. Pins the behaviour so a future
            // refactor of MarkerSignalStrategy doesn't break the Reset-to-default UX.
            var series = SingleComponent(out _, c =>
            {
                c.Name = "dot";
                c.DisplayName = "Blue Dot";
                c.DisplayType = ComponentDisplayType.Dot;
                c.IsVisible = true;
                c.SpeechTemplate       = "fallback narration for {name} at {value:F0}.";
                c.SignalSpeechTemplate = null;
            }, values: new[] { 42.0 });

            var msg = Format(series, focusedCompIndex: 0);
            Assert.Equal("fallback narration for Blue Dot at 42.", msg);
        }

        // ── Harness — mirrors SpeechFormatterDispatchTests ───────────────────

        private static string Format(ChartSeries series, int focusedCompIndex)
        {
            var formatter = new SpeechFormatter();
            var pt = new Ohlcv(
                new DateTime(2026, 04, 24, 0, 0, 0, DateTimeKind.Utc),
                100, 101, 99, 100, 1000);

            var state = WorkspaceState.Initial with
            {
                Data = new TimeSeriesBuffer<Ohlcv>(new List<Ohlcv> { pt }),
                CurrentDataIndex = 0,
                ActiveSeries = ImmutableList.Create(series),
                FocusedSeriesId = series.Id,
                FocusedComponentIndex = focusedCompIndex,
                ReadColumnHeaders = true,
                SpeechOrder = "HeaderValue",
                SpeakTimestamps = false,
                LastInteractionContext = InteractionContext.Component,
            };

            return formatter.FormatPointFeedback(state, isXMove: false, isYMove: true, series, pt, prefixMessage: "");
        }

        private static ChartSeries SingleComponent(out ComponentConfig comp, Action<ComponentConfig> configure, double[] values)
        {
            comp = new ComponentConfig();
            configure(comp);
            var cfg = new SeriesConfig { Id = "s", Name = "s", IndicatorCode = "S", Pane = "Main" };
            cfg.Components.Add(comp);
            var buf = new SeriesDataBuffer { SeriesId = "s" };
            buf.ComponentData[comp.Name] = values;
            return new ChartSeries(cfg, buf);
        }
    }
}
