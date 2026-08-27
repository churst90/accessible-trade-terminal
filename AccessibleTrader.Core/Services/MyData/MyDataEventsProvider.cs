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
            // Memoized PER DATASET CODE for GetComponentSpeech, which receives only the bar —
            // the labels ARE the speech ("Bought 0.5 BTC"), not a generic marker phrase.
            //
            // This used to be one instance field written wholesale on every Calculate. The
            // provider is AddSingleton on desktop and AddScoped per circuit on the WebHost,
            // and GetIndicators emits ONE CODE PER EVENTS DATASET — all served by that one
            // instance. Import a trade journal and a news-events file, add both to the chart,
            // and Calculate for the second overwrote the memo; on any bar date present in both
            // files the screen reader announced the OTHER dataset's label. And because the
            // lookup key was bar.Date alone rather than the dataset, it did not fall back to
            // the generic template — it returned a confidently wrong string. For a product
            // whose premise is that the spoken text is the interface, that is the worst class
            // of bug in the area, and the comment below claiming it "never wrong text" was the
            // reason nobody looked.
            _labelsByCode[code] = labelsByBarDate;
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
            return _labelsByCode.TryGetValue(code, out var memo)
                   && memo.TryGetValue(data[index].Date, out var labels)
                ? string.Join(". ", labels)
                : "No event on this bar.";
        }

        /// <summary>
        /// The spoken label for an event marker.
        ///
        /// <para><b>The component name carries the dataset</b>, which is what makes this
        /// answerable: <c>GetComponentSpeech</c> is handed a component, not a code, and with a
        /// single shared memo there was no way to tell whose label the bar's date belonged to.
        /// A component this provider does not recognise falls back to the generic template,
        /// which is the behaviour the old comment claimed and did not deliver.</para>
        /// </summary>
        public string? GetComponentSpeech(string componentName, double value, Ohlcv bar,
            IReadOnlyDictionary<string, double[]> allComponentData, int dataIndex)
        {
            if (double.IsNaN(value)) return null;

            // Only one dataset can own a given bar's marker in a given series, and the caller
            // is asking about the series it is navigating. Where exactly one memo has a label
            // for this bar, that is unambiguously the right one; where several do, there is no
            // way to choose and the generic template is the honest answer.
            List<string>? found = null;
            foreach (var memo in _labelsByCode.Values)
            {
                if (!memo.TryGetValue(bar.Date, out var labels)) continue;
                if (found != null) return null;   // ambiguous — say nothing rather than guess
                found = labels;
            }

            return found != null ? string.Join(". ", found) : null;
        }

        /// <summary>
        /// Event labels by bar date, PER DATASET CODE. Concurrent because the provider is a
        /// singleton on desktop and a chart can carry several Events datasets at once.
        /// </summary>
        private readonly System.Collections.Concurrent.ConcurrentDictionary<
            string, Dictionary<DateTime, List<string>>> _labelsByCode = new(StringComparer.Ordinal);

        private IReadOnlyList<MyDataEvent> EventsFor(string code)
        {
            if (!code.StartsWith(CodePrefix, StringComparison.Ordinal)) return Array.Empty<MyDataEvent>();
            string id = code[CodePrefix.Length..];
            var parsed = _store.GetParsed(id);
            return parsed?.Events ?? (IReadOnlyList<MyDataEvent>)Array.Empty<MyDataEvent>();
        }
    }
}
