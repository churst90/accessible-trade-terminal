using System.Collections.Immutable;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;
using NSubstitute;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Debt item 4: provider contextual speech is now strategy #2 of the single
    /// utterance precedence list in SpeechFormatter (it used to be a separate
    /// "path 1" in NavigationFeedbackManager). These tests pin the contract that
    /// moved: provider speech wins in Component context, declines fall through to
    /// the template chain, Y-moves prefix the component identity, and the
    /// companion data (__live_close) reaches the provider.
    /// </summary>
    public class ProviderSpeechStrategyTests
    {
        private static ChartSeries MakeIndicatorSeries(out ComponentConfig comp)
        {
            comp = new ComponentConfig
            {
                Name = "Rsi",
                DisplayName = "RSI",
                DisplayType = ComponentDisplayType.Oscillator,
                IsVisible = true,
            };
            var config = new SeriesConfig { Id = "rsi-1", IndicatorCode = "RSI", Name = "RSI (14)" };
            config.Components.Add(comp);
            var data = new SeriesDataBuffer { SeriesId = config.Id };
            data.ComponentData[comp.Name] = new double[] { 72.5 };
            return new ChartSeries(config, data);
        }

        private static WorkspaceState State(ChartSeries s, InteractionContext ctx) =>
            WorkspaceState.Initial with
            {
                Data = new TimeSeriesBuffer<Ohlcv>(new Ohlcv(DateTime.UtcNow, 100, 110, 95, 105, 1000)),
                ActiveSeries = ImmutableList.Create(s),
                FocusedSeriesId = s.Id,
                FocusedComponentIndex = 0,
                CurrentDataIndex = 0,
                LastInteractionContext = ctx,
                SpeakTimestamps = false,
                TimestampReadLocation = "None",
                ReadColumnHeaders = true,
                SpeechOrder = "HeaderValue",
            };

        private static (SpeechFormatter formatter, IIndicatorProvider provider) Build(string? providerSpeech)
        {
            var provider = Substitute.For<IIndicatorProvider>();
            provider.GetComponentSpeech(Arg.Any<string>(), Arg.Any<double>(), Arg.Any<Ohlcv>(),
                    Arg.Any<Dictionary<string, double[]>>(), Arg.Any<int>())
                .Returns(providerSpeech);
            var engine = Substitute.For<IIndicatorEngine>();
            engine.GetProvider("RSI").Returns(provider);
            return (new SpeechFormatter(
                Microsoft.Extensions.Logging.Abstractions.NullLogger<SpeechFormatter>.Instance, engine), provider);
        }

        private static Ohlcv Bar() => new(DateTime.UtcNow, 100, 110, 95, 105, 1000);

        [Fact]
        public void ProviderSpeech_WinsOverTemplate_InComponentContext()
        {
            var s = MakeIndicatorSeries(out _);
            var (formatter, _) = Build("Overbought and cooling.");

            string result = formatter.FormatPointFeedback(State(s, InteractionContext.Component),
                isXMove: true, isYMove: false, s, Bar(), "");

            Assert.Equal("Overbought and cooling.", result);
        }

        [Fact]
        public void ProviderDecline_FallsThroughToTemplateChain()
        {
            var s = MakeIndicatorSeries(out _);
            var (formatter, _) = Build(providerSpeech: null);

            string result = formatter.FormatPointFeedback(State(s, InteractionContext.Component),
                isXMove: true, isYMove: false, s, Bar(), "");

            // StandardTemplateStrategy default: "{name}. {type}. {value}."
            Assert.Contains("RSI", result);
            Assert.Contains("72.5", result);
        }

        [Fact]
        public void YMove_PrefixesComponentIdentity_AndHiddenState()
        {
            var s = MakeIndicatorSeries(out var comp);
            comp.IsVisible = false;
            var (formatter, _) = Build("Overbought.");

            string result = formatter.FormatPointFeedback(State(s, InteractionContext.Component),
                isXMove: false, isYMove: true, s, Bar(), "");

            Assert.Equal("RSI. Oscillator. Hidden. Overbought.", result);
        }

        [Fact]
        public void XMove_SpeaksValueOnly_NoIdentityRepeat()
        {
            var s = MakeIndicatorSeries(out _);
            var (formatter, _) = Build("Overbought.");

            string result = formatter.FormatPointFeedback(State(s, InteractionContext.Component),
                isXMove: true, isYMove: false, s, Bar(), "");

            Assert.Equal("Overbought.", result);
        }

        [Fact]
        public void Provider_ReceivesLiveClose_ForDistanceSpeech()
        {
            var s = MakeIndicatorSeries(out _);
            Dictionary<string, double[]>? seen = null;
            var provider = Substitute.For<IIndicatorProvider>();
            provider.GetComponentSpeech(Arg.Any<string>(), Arg.Any<double>(), Arg.Any<Ohlcv>(),
                    Arg.Do<Dictionary<string, double[]>>(d => seen = d), Arg.Any<int>())
                .Returns("x");
            var engine = Substitute.For<IIndicatorEngine>();
            engine.GetProvider("RSI").Returns(provider);
            var formatter = new SpeechFormatter(
                Microsoft.Extensions.Logging.Abstractions.NullLogger<SpeechFormatter>.Instance, engine);

            formatter.FormatPointFeedback(State(s, InteractionContext.Component),
                true, false, s, Bar(), "");

            Assert.NotNull(seen);
            Assert.True(seen!.ContainsKey("__live_close"));
            Assert.Equal(105, seen["__live_close"][0]); // the LIVE bar's close, not the navigated bar's
        }

        [Fact]
        public void SeriesContext_NeverConsultsTheProvider()
        {
            var s = MakeIndicatorSeries(out _);
            var (formatter, provider) = Build("should not be spoken");

            formatter.FormatPointFeedback(State(s, InteractionContext.Series),
                true, false, s, Bar(), "");

            provider.DidNotReceive().GetComponentSpeech(Arg.Any<string>(), Arg.Any<double>(),
                Arg.Any<Ohlcv>(), Arg.Any<Dictionary<string, double[]>>(), Arg.Any<int>());
        }

        [Fact]
        public void NoEngine_BehavesExactlyAsBefore()
        {
            // Minimal construction (tests, tools) — provider strategy simply never matches.
            var s = MakeIndicatorSeries(out _);
            var formatter = new SpeechFormatter();

            string result = formatter.FormatPointFeedback(State(s, InteractionContext.Component),
                true, false, s, Bar(), "");

            Assert.Contains("72.5", result);
        }
    }
}
