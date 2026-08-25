using AccessibleTrader.Core.Services.MyData;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Indicators
{
    /// <summary>
    /// Surfaces imported Events datasets (date,label[,value]) as chart-marker
    /// indicators: add "My Events: Trades" from the indicator dialog and each
    /// event lands on the bar covering its date — a dot on the price pane with an
    /// earcon in playback and the event's own label as its speech. The killer use:
    /// a trade journal overlaid on the chart it happened on.
    ///
    /// The indicator list is DYNAMIC (one entry per Events dataset), which is why
    /// this is a separate provider rather than more codes on CoreIndicatorProvider.
    /// </summary>
    public sealed class MyDataEventsProvider : IIndicatorProvider
    {
        public const string CodePrefix = "MYDATA_EV_";
        public const string CompEvent = "Event";

        private readonly IMyDataStore _store;

        public MyDataEventsProvider(IMyDataStore store) => _store = store;

        public string Name => "MyData.Events";

        public List<IndicatorMetadata> GetIndicators() =>
            _store.Datasets
                .Where(d => d.Shape == MyDataShape.Events)
                .Select(d => new IndicatorMetadata
                {
                    Code = CodePrefix + d.Id,
                    Causality = ComponentCausality.Causal,
                    Name = $"My Events: {d.Name}",
                    Category = "My Data",
                    DefaultPane = "Main", // markers belong on the price action
                    Components = new List<IndicatorComponentMetadata>
                    {
                        new()
                        {
                            Name = CompEvent,
                            DisplayName = d.Name,
                            Role = ComponentRole.Signal,
                            DisplayType = ComponentDisplayType.Dot,
                            DefaultColorHex = "#FFD54FE0",
                            DefaultThickness = 4.0f,
                            DefaultEnvelopeType = "Ping",
                            DefaultSoundPatchId = "crystal_bell",
                            DefaultDecayMs = 150,
                            DefaultPitchMapping = PitchMapping.None,
                            DefaultBaseFrequency = 740.0,
                            // Speech comes from GetComponentSpeech (the event's own
                            // label); this template is the fallback only.
                            DefaultSignalSpeechTemplate = "Event: {value}",
                        },
                    },
                })
                .ToList();

        public void Calculate(string code, ReadOnlySpan<Ohlcv> data,
            Dictionary<string, object> parameters, IIndicatorResultBuffer buffer)
        {
            var span = buffer.GetComponentSpan(CompEvent);
            span.Fill(double.NaN);

            var events = EventsFor(code);
            var labelsByBarDate = new Dictionary<DateTime, List<string>>();
            if (events.Count > 0 && data.Length > 0)
            {
                // Each event lands on the LAST bar whose date is <= the event date —
                // the bar in progress when it happened. Events before the first bar
                // are dropped (off-chart); events after the last bar pin to it.
                int bar = 0;
                foreach (var ev in events)
                {
                    while (bar + 1 < data.Length && data[bar + 1].Date <= ev.Date) bar++;
                    if (data[bar].Date > ev.Date) continue;

                    // Marker Y: the event's own value when it has one (a fill price
                    // sits exactly where it filled), else the bar close.
                    span[bar] = ev.Value ?? data[bar].Close;

                    string label = ev.Value.HasValue
                        ? $"{ev.Label}, {Accessibility.SpeechPriceFormatter.FormatPrice(ev.Value.Value)}"
                        : ev.Label;
                    if (!labelsByBarDate.TryGetValue(data[bar].Date, out var list))
                        labelsByBarDate[data[bar].Date] = list = new List<string>();
                    list.Add(label);
                }
            }
            // Memoized for GetComponentSpeech, which receives only the bar — the
            // labels ARE the speech ("Bought 0.5 BTC"), not a generic marker phrase.
            _labelsByBarDate = labelsByBarDate;
        }

        public void UpdateLast(string code, ReadOnlySpan<Ohlcv> data,
            Dictionary<string, object> parameters, IIndicatorResultBuffer buffer)
        {
            // Imported events are historical; live ticks never add one. Leave the
            // final bar as Calculate left it.
        }

        public int GetStabilityWindow(string code, Dictionary<string, object> parameters) => 0;

        public string GetDetailFact(string code, ReadOnlySpan<Ohlcv> data,
            IReadOnlyDictionary<string, double[]> calculatedResults, int index,
            Dictionary<string, object> parameters)
        {
            if (index < 0 || index >= data.Length) return "No event on this bar.";
            var memo = _labelsByBarDate;
            return memo != null && memo.TryGetValue(data[index].Date, out var labels)
                ? string.Join(". ", labels)
                : "No event on this bar.";
        }

        public string? GetComponentSpeech(string componentName, double value, Ohlcv bar,
            IReadOnlyDictionary<string, double[]> allComponentData, int dataIndex)
        {
            if (double.IsNaN(value)) return null;
            var memo = _labelsByBarDate;
            return memo != null && memo.TryGetValue(bar.Date, out var labels)
                ? string.Join(". ", labels)
                : null; // stale memo degrades to the generic template — never wrong text
        }

        private Dictionary<DateTime, List<string>>? _labelsByBarDate;

        private IReadOnlyList<MyDataEvent> EventsFor(string code)
        {
            if (!code.StartsWith(CodePrefix, StringComparison.Ordinal)) return Array.Empty<MyDataEvent>();
            string id = code[CodePrefix.Length..];
            var parsed = _store.GetParsed(id);
            return parsed?.Events ?? (IReadOnlyList<MyDataEvent>)Array.Empty<MyDataEvent>();
        }
    }
}
