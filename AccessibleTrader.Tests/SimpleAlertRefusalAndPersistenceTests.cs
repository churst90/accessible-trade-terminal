using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Alerts;
using AccessibleTrader.Sdk.Alerts;
using AccessibleTrader.Sdk.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// An alert that cannot fire is refused with a reason, and alerts.json written before
    /// Touches and line targets existed still loads — with the new fields round-tripping.
    /// </summary>
    public class SimpleAlertRefusalAndPersistenceTests : IDisposable
    {
        private readonly string _dir = TestTemp.NewDir("att-alertcompat-");
        public void Dispose() { try { Directory.Delete(_dir, recursive: true); } catch { } }

        private static SeriesConfig Config(string id, string code, string name, params LevelConfig[] levels)
        {
            var c = new SeriesConfig { Id = id, IndicatorCode = code, Name = name, FriendlyName = name };
            c.Components.Add(new ComponentConfig { Name = code, DisplayName = name });
            foreach (var l in levels) c.Levels.Add(l);
            return c;
        }

        private static readonly SeriesConfig[] Chart =
        {
            Config("rsi-1", "Rsi", "RSI", new LevelConfig { Name = "Overbought", Value = 70 }, new LevelConfig { Name = "Oversold", Value = 30 }),
            Config("sma-50", "Sma", "SMA 50"),
            // A user who kept only RSI's top line on a second RSI.
            Config("rsi-2", "Rsi", "RSI 7", new LevelConfig { Name = "Overbought", Value = 80 }),
        };

        private static AlertDefinition Zone(string code, string comp, AlertZone zone, string? seriesId = null) => new()
        {
            Id = "z", Name = "z", Delivery = AlertDelivery.Speech, Target = AlertTarget.Indicator,
            Condition = AlertCondition.EntersZone, IndicatorCode = code, ComponentName = comp, Zone = zone, SeriesId = seriesId,
        };

        [Fact]
        public void A_zone_alert_on_an_SMA_is_refused_because_an_SMA_has_no_zone()
        {
            var why = BackgroundWatchability.WhyUnfireable(Zone("Sma", "Sma", AlertZone.Overbought), Chart);
            Assert.NotNull(why);
            Assert.Contains("SMA 50 has no overbought or oversold line", why);
        }

        [Fact]
        public void A_zone_alert_on_an_RSI_is_accepted()
        {
            // Vacuity check: the refusal above is about SMA, not about zone alerts.
            Assert.Null(BackgroundWatchability.WhyUnfireable(Zone("Rsi", "Rsi", AlertZone.Overbought), Chart));
            Assert.Null(BackgroundWatchability.WhyUnfireable(Zone("Rsi", "Rsi", AlertZone.Oversold), Chart));
        }

        [Fact]
        public void A_zone_the_named_instance_does_not_have_is_refused()
        {
            Assert.Contains("no oversold line",
                BackgroundWatchability.WhyUnfireable(Zone("Rsi", "Rsi", AlertZone.Oversold, seriesId: "rsi-2"), Chart));
        }

        [Theory]
        [InlineData(AlertZone.UpperBand)]
        [InlineData(AlertZone.LowerBand)]
        public void Band_zones_are_refused_even_with_no_chart_to_check(AlertZone zone)
        {
            // These never fired: the analyzer looked for components called "Upper"/"Lower".
            Assert.Contains("band", BackgroundWatchability.WhyUnfireable(Zone("BB", "UpperBand", zone)));
        }

        [Fact]
        public void A_line_alert_whose_line_is_not_on_the_chart_is_refused()
        {
            var a = new AlertDefinition
            {
                Id = "l", Name = "l", Delivery = AlertDelivery.Speech, Target = AlertTarget.Price,
                Condition = AlertCondition.Touches, LineIndicatorCode = "Ema", LineComponentName = "Ema",
            };
            Assert.Contains("not on this chart", BackgroundWatchability.WhyUnfireable(a, Chart));
            Assert.Null(BackgroundWatchability.WhyUnfireable(a with { LineIndicatorCode = "Sma", LineComponentName = "Sma", LineSeriesId = "sma-50" }, Chart));
        }

        [Fact]
        public void The_hosted_monitor_which_has_no_chart_refuses_a_line_alert_with_a_reason()
        {
            var a = new AlertDefinition
            {
                Id = "l", Name = "l", Delivery = AlertDelivery.Speech, Target = AlertTarget.Price,
                Condition = AlertCondition.Touches, LineIndicatorCode = "Sma", LineComponentName = "Sma",
                Symbol = "BTC/USD", Provider = "Bitstamp",
            };
            Assert.Contains("line", BackgroundWatchability.WhyUnwatchableWithoutAChart(a));
            // ...and a plain price touch is watchable there: it needs only the bars.
            Assert.Null(BackgroundWatchability.WhyUnwatchableWithoutAChart(a with { LineIndicatorCode = null, LineComponentName = null, Threshold = 5 }));
        }

        // ── Describing an alert whose instance is gone ───────────────────────────

        private static ChartSeries Live(string id, string code, string name)
        {
            var c = new SeriesConfig { Id = id, IndicatorCode = code, Name = name, FriendlyName = name };
            c.Components.Add(new ComponentConfig { Name = code, DisplayName = code });
            return new ChartSeries(c, new SeriesDataBuffer { SeriesId = id });
        }

        [Fact]
        public void A_description_never_names_another_instance_for_one_that_has_gone()
        {
            // The alert was set on the SMA 50; only the SMA 20 is left. The evaluator will not
            // watch the SMA 20 in its place, so the description must not say it does.
            var chart = new[] { Live("sma-20", "Sma", "SMA 20") };
            var line = new AlertDefinition
            {
                Id = "l", Name = "l", Delivery = AlertDelivery.Speech, Target = AlertTarget.Price,
                Condition = AlertCondition.CrossesAbove, Symbol = "BTC/USD",
                LineIndicatorCode = "Sma", LineComponentName = "Sma", LineSeriesId = "sma-50",
            };
            var subject = new AlertDefinition
            {
                Id = "s", Name = "s", Delivery = AlertDelivery.Speech, Target = AlertTarget.Indicator,
                Condition = AlertCondition.Touches, Threshold = 100, Symbol = "BTC/USD",
                IndicatorCode = "Sma", ComponentName = "Sma", SeriesId = "sma-50",
            };

            Assert.Equal("BTC/USD price crosses above Sma (no longer on this chart)", AlertDescriptions.Describe(line, chart));
            Assert.Equal("BTC/USD Sma (no longer on this chart) touches 100", AlertDescriptions.Describe(subject, chart));
            // An alert that names no instance still reads the first by code, as the evaluator does.
            Assert.Equal("BTC/USD price crosses above SMA 20", AlertDescriptions.Describe(line with { LineSeriesId = null }, chart));
        }

        // ── alerts.json ──────────────────────────────────────────────────────────

        private WorkspaceLibraryService Library() =>
            new(NullLogger<WorkspaceLibraryService>.Instance, new TempWorkspacePaths()) { LibraryDirectoryOverride = _dir };

        [Fact]
        public void An_alerts_file_from_before_touches_and_lines_still_loads_ordinals_and_all()
        {
            // Condition 1 = CrossesBelow and Target 1 = Price by ORDINAL, as files written before
            // the name converter carry them; no SeriesId, no Line* fields.
            File.WriteAllText(Path.Combine(_dir, "alerts.json"), """
                [
                  { "Id": "old-1", "Name": "Old", "Target": 1, "Condition": 1, "Threshold": 50000.0, "Delivery": 2,
                    "IsActive": true, "Symbol": "BTC/USD" },
                  { "Id": "old-2", "Name": "Old zone", "Target": "Indicator", "Condition": "EntersZone",
                    "IndicatorCode": "Rsi", "ComponentName": "Rsi", "Zone": "Overbought", "Delivery": "Speech" }
                ]
                """);

            var loaded = Library().LoadAlerts();

            Assert.Equal(2, loaded.Count);
            Assert.Equal(AlertTarget.Price, loaded[0].Target);
            Assert.Equal(AlertCondition.CrossesBelow, loaded[0].Condition);
            Assert.Equal(50000, loaded[0].Threshold);
            Assert.False(loaded[0].ComparesToLine());
            Assert.Null(loaded[0].SeriesId);
            Assert.Equal(AlertZone.Overbought, loaded[1].Zone);
        }

        [Fact]
        public void A_touch_alert_on_a_line_round_trips_with_every_new_field()
        {
            var lib = Library();
            var a = new AlertDefinition
            {
                Id = "new-1", Name = "Touch the 50", Delivery = AlertDelivery.Both, Target = AlertTarget.Price,
                Condition = AlertCondition.Touches, LineIndicatorCode = "Sma", LineComponentName = "Sma",
                LineSeriesId = "sma-50", Symbol = "BTC/USD",
            };
            var i = a with { Id = "new-2", Target = AlertTarget.Indicator, IndicatorCode = "Rsi", ComponentName = "Rsi", SeriesId = "rsi-1", LineIndicatorCode = null, LineComponentName = null, LineSeriesId = null, Threshold = 70 };
            lib.SaveAlerts(new[] { a, i });

            string json = File.ReadAllText(Path.Combine(_dir, "alerts.json"));
            Assert.Contains("\"Touches\"", json);               // by name, not ordinal 7
            Assert.DoesNotContain("ComparesToLine", json);      // a method, not persisted state

            var back = lib.LoadAlerts();
            Assert.Equal(a, back[0]);
            Assert.Equal(i, back[1]);
        }
    }
}
