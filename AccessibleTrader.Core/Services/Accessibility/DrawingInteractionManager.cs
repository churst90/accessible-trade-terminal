using System;
using System.Collections.Generic;
using System.Linq;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Core.Models;

namespace AccessibleTrader.Core.Services.Accessibility
{
    public interface IDrawingInteractionManager
    {
        void HandleAddDrawing(string type, IReadOnlyList<Ohlcv> chartData);
        void HandleMouseEvent(double x, double y, string type, double width, double height);
    }

    /// <summary>
    /// Manages user interactions for placing drawings on the chart.
    /// Implements a two or three click state machine for multi-point drawings.
    /// Single-point drawings complete immediately.
    /// </summary>
    public class DrawingInteractionManager : IDrawingInteractionManager, IDisposable
    {
        private readonly IEventBus _eventBus;
        private readonly IDrawingService _drawingService;
        private readonly IWorkspaceStore _store;
        private readonly IIndicatorModelFactory _modelFactory;
        private readonly IInputService _inputService;
        private readonly System.Reactive.Disposables.CompositeDisposable _subs = new();

        private DrawingType _pendingDrawingType = DrawingType.None;
        private DateTime? _anchorDate1;
        private double? _anchorPrice1;
        private DateTime? _anchorDate2;
        private double? _anchorPrice2;

        public DrawingInteractionManager(
            IEventBus eventBus,
            IDrawingService drawingService,
            IWorkspaceStore store,
            IIndicatorModelFactory modelFactory,
            IInputService inputService)
        {
            _eventBus = eventBus;
            _drawingService = drawingService;
            _store = store;
            _modelFactory = modelFactory;
            _inputService = inputService;

            _subs.Add(_eventBus.Subscribe<CancelDrawingEvent>(_ => CancelPendingDrawing()));
            _inputService.MouseEvent += HandleMouseEvent;
        }

        public void HandleMouseEvent(double x, double y, string type, double width, double height)
        {
            if (_pendingDrawingType == DrawingType.None && type != "MouseDown") return;

            var state = _store.State;
            if (state.Data == null || state.Data.Count == 0) return;

            // Map screen coordinates to Price/Date using actual viewport dimensions
            double price = MapYToPrice(y, height, state.ViewportRange.Min, state.ViewportRange.Max, state.IsLogScale);
            int dataIndex = MapXToIndex(x, width, state.ViewportStartIndex, state.ViewportLength);

            // Future-space anchor allowance. A drawing anchor may land in the 20-bar right
            // margin (the reserved projection slot) for drawings like trendlines that need a
            // forward anchor point. For indices at or past state.Data.Count we synthesise a
            // date by extrapolating from the last real bar's timeframe cadence — the anchor
            // date is stored on DrawingData and survives persistence via the same record path
            // as any other anchor. Indices beyond Data.Count + RightMarginBars are out of
            // bounds and rejected as before.
            int maxFutureIndex = state.Data.Count + System.Math.Max(0, state.RightMarginBars) - 1;
            if (dataIndex < 0 || dataIndex > maxFutureIndex) return;

            System.DateTime anchorDate;
            if (dataIndex < state.Data.Count)
            {
                anchorDate = state.Data[dataIndex].Date;
            }
            else
            {
                anchorDate = ProjectFutureDate(state.Data, dataIndex - (state.Data.Count - 1));
            }

            if (type == "MouseDown")
            {
                if (_pendingDrawingType != DrawingType.None)
                {
                    HandleDrawingStep(anchorDate, price);
                }
            }
        }

        /// <summary>
        /// Project a synthetic date offset forward from the last real bar by <paramref name="offsetBars"/>
        /// steps, inferring the timeframe from the median inter-bar delta across the tail of
        /// the loaded history. Using a tail-median avoids overreacting to gaps in historical
        /// data (e.g. stock weekends, session boundaries) that would make a naive last-two-bar
        /// delta misleading. Falls back to a 1-minute step when fewer than 3 bars are loaded.
        /// </summary>
        internal static System.DateTime ProjectFutureDate(System.Collections.Generic.IReadOnlyList<Ohlcv> data, int offsetBars)
        {
            if (data == null || data.Count == 0) return System.DateTime.UtcNow;
            if (offsetBars <= 0) return data[^1].Date;

            System.TimeSpan step;
            if (data.Count >= 3)
            {
                // Sample up to 8 most-recent inter-bar deltas; take the median.
                int samples = System.Math.Min(8, data.Count - 1);
                var deltas = new System.TimeSpan[samples];
                for (int i = 0; i < samples; i++)
                {
                    int hi = data.Count - 1 - i;
                    deltas[i] = data[hi].Date - data[hi - 1].Date;
                }
                System.Array.Sort(deltas);
                step = deltas[samples / 2];
            }
            else
            {
                step = data[^1].Date - data[0].Date;
            }
            if (step.Ticks <= 0) step = System.TimeSpan.FromMinutes(1);

            return data[^1].Date + System.TimeSpan.FromTicks(step.Ticks * offsetBars);
        }

        private double MapYToPrice(double y, double height, double min, double max, bool isLog)
        {
            double percent = 1.0 - (y / height);
            if (isLog)
            {
                if (min <= 0) min = 0.01;
                if (max <= min) max = min + 1.0;
                return Math.Exp(Math.Log(min) + (percent * (Math.Log(max) - Math.Log(min))));
            }
            return min + (percent * (max - min));
        }

        private int MapXToIndex(double x, double width, int startIndex, int length)
        {
            double percent = x / width;
            return startIndex + (int)Math.Round(percent * (length - 1));
        }

        private void HandleDrawingStep(DateTime date, double price)
        {
            if (_pendingDrawingType == DrawingType.None) return;
            string label = FriendlyName(_pendingDrawingType);

            if (_anchorDate1 == null)
            {
                _anchorDate1 = date;
                _anchorPrice1 = price;

                if (_pendingDrawingType == DrawingType.AnchoredVwap)
                {
                    CompleteDrawing(date, price);
                }
                else
                {
                    string ts = date.ToString("t");
                    _eventBus.Publish(new AnnouncementEvent(
                        $"{label}: anchor 1 set at {SpeechPriceFormatter.FormatPrice(price)}, {ts}. Navigate to next point and press the shortcut again."));
                }
            }
            else if (_anchorDate2 == null)
            {
                bool needsDifferentBar = _pendingDrawingType == DrawingType.TrendLine ||
                                        _pendingDrawingType == DrawingType.Channel ||
                                        _pendingDrawingType == DrawingType.FibRetracement ||
                                        _pendingDrawingType == DrawingType.AndrewsPitchfork;

                if (needsDifferentBar && date == _anchorDate1)
                {
                    _eventBus.Publish(new AnnouncementEvent("Please select a different bar for the second point."));
                    return;
                }

                _anchorDate2 = date;
                _anchorPrice2 = price;

                bool isThreePoint = _pendingDrawingType == DrawingType.FibExtension ||
                                    _pendingDrawingType == DrawingType.RiskReward ||
                                    _pendingDrawingType == DrawingType.AndrewsPitchfork;

                if (!isThreePoint)
                {
                    CompleteDrawing(date, price);
                }
                else
                {
                    string ts = date.ToString("t");
                    string msg = _pendingDrawingType switch {
                        DrawingType.RiskReward       => $"Risk/reward: entry at {SpeechPriceFormatter.FormatPrice(price)}, {ts}. Navigate to stop loss and press the shortcut again.",
                        DrawingType.AndrewsPitchfork => $"Pitchfork: median line at {SpeechPriceFormatter.FormatPrice(price)}, {ts}. Navigate to swing point and press the shortcut again.",
                        _                            => $"{label}: anchor 2 at {SpeechPriceFormatter.FormatPrice(price)}, {ts}. Navigate to anchor 3 and press the shortcut again."
                    };
                    _eventBus.Publish(new AnnouncementEvent(msg));
                }
            }
            else
            {
                CompleteDrawing(date, price);
            }
        }

        private void CompleteDrawing(DateTime dateFinal, double priceFinal)
        {
            var d = new DrawingData
            {
                Type = _pendingDrawingType,
                AnchorDate1 = _anchorDate1,
                AnchorPrice1 = _anchorPrice1,
                AnchorDate2 = _anchorDate2,
                AnchorPrice2 = _anchorPrice2,
                AnchorDate3 = dateFinal,
                AnchorPrice3 = priceFinal,
                ExtendRight = true
            };

            CreateDrawingSeries(_pendingDrawingType.ToString(), d, _store.State.Data.ToList());

            string label = FriendlyName(_pendingDrawingType);
            double fromPrice = _anchorPrice1 ?? priceFinal;
            string feedback = Math.Abs(priceFinal - fromPrice) > 0.001
                ? $"{label} placed from {SpeechPriceFormatter.FormatPrice(fromPrice)} to {SpeechPriceFormatter.FormatPrice(priceFinal)}."
                : $"{label} placed at {SpeechPriceFormatter.FormatPrice(priceFinal)}.";
            _eventBus.Publish(new AnnouncementEvent(feedback));

            _pendingDrawingType = DrawingType.None;
            _anchorDate1 = null; _anchorPrice1 = null;
            _anchorDate2 = null; _anchorPrice2 = null;
        }

        public void HandleAddDrawing(string type, IReadOnlyList<Ohlcv> chartData)
        {
            if (chartData == null || !chartData.Any()) return;

            var state = _store.State;
            var pt = chartData[Math.Clamp(state.CurrentDataIndex, 0, chartData.Count - 1)];

            var dType = type switch
            {
                "Horizontal" => DrawingType.HorizontalLine,
                "Vertical" => DrawingType.VerticalLine,
                "TrendLine" => DrawingType.TrendLine,
                "Channel" => DrawingType.Channel,
                "FibRetracement" => DrawingType.FibRetracement,
                "FibExtension" => DrawingType.FibExtension,
                "Rectangle" => DrawingType.Rectangle,
                "GannFan" => DrawingType.GannFan,
                "RiskReward" => DrawingType.RiskReward,
                "AnchoredVwap" => DrawingType.AnchoredVwap,
                "Measure" => DrawingType.MeasureTool,
                "GannBox" => DrawingType.GannBox,
                "Pitchfork" => DrawingType.AndrewsPitchfork,
                "AngleFib" => DrawingType.AngleFib,
                "TextLabel" => DrawingType.TextLabel,
                _ => DrawingType.None
            };

            if (dType == DrawingType.HorizontalLine)
            {
                CreateDrawingSeries("Horizontal", new DrawingData { Type = DrawingType.HorizontalLine, AnchorPrice1 = pt.Close }, chartData);
                _eventBus.Publish(new AnnouncementEvent($"Horizontal line added at {SpeechPriceFormatter.FormatPrice(pt.Close)}"));
            }
            else if (dType == DrawingType.VerticalLine)
            {
                CreateDrawingSeries("Vertical", new DrawingData { Type = DrawingType.VerticalLine, AnchorDate1 = pt.Date }, chartData);
                _eventBus.Publish(new AnnouncementEvent($"Vertical line added at {pt.Date:MMMM dd, HH:mm}"));
            }
            else if (dType == DrawingType.TextLabel)
            {
                CreateDrawingSeries("Label", new DrawingData { Type = DrawingType.TextLabel, AnchorDate1 = pt.Date, AnchorPrice1 = pt.Close }, chartData);
                _eventBus.Publish(new AnnouncementEvent($"Text label pinned at {SpeechPriceFormatter.FormatPrice(pt.Close)}"));
            }
            else if (dType != DrawingType.None)
            {
                if (_pendingDrawingType != dType)
                {
                    // If a different drawing was in progress, cancel it silently before starting the new one.
                    if (_pendingDrawingType != DrawingType.None)
                        _eventBus.Publish(new AnnouncementEvent($"{FriendlyName(_pendingDrawingType)} cancelled."));

                    _pendingDrawingType = dType;
                    _anchorDate1 = null;
                    _anchorPrice1 = null;
                    _anchorDate2 = null;
                    _anchorPrice2 = null;
                }

                HandleDrawingStep(pt.Date, pt.Close);
            }
        }

        private static string FriendlyName(DrawingType t) => t switch
        {
            DrawingType.TrendLine        => "Trend line",
            DrawingType.HorizontalLine   => "Horizontal line",
            DrawingType.VerticalLine     => "Vertical line",
            DrawingType.Channel          => "Channel",
            DrawingType.FibRetracement   => "Fibonacci retracement",
            DrawingType.FibExtension     => "Fibonacci extension",
            DrawingType.Rectangle        => "Rectangle",
            DrawingType.GannFan          => "Gann fan",
            DrawingType.RiskReward       => "Risk/reward",
            DrawingType.AnchoredVwap     => "Anchored VWAP",
            DrawingType.MeasureTool      => "Measure",
            DrawingType.GannBox          => "Gann box",
            DrawingType.AndrewsPitchfork => "Andrews pitchfork",
            DrawingType.AngleFib         => "Angle Fibonacci",
            DrawingType.TextLabel        => "Text label",
            _                            => t.ToString()
        };

        private void CancelPendingDrawing()
        {
            if (_pendingDrawingType == DrawingType.None) return;

            string label = FriendlyName(_pendingDrawingType);
            _pendingDrawingType = DrawingType.None;
            _anchorDate1 = null;
            _anchorPrice1 = null;
            _anchorDate2 = null;
            _anchorPrice2 = null;
            _eventBus.Publish(new AnnouncementEvent($"{label} cancelled."));
        }

        private void CreateDrawingSeries(string name, DrawingData drawing, IReadOnlyList<Ohlcv> chartData)
        {
            string seriesId = Guid.NewGuid().ToString();
            var config = new SeriesConfig
            {
                Id = seriesId,
                Name = $"{name} ({_store.State.ActiveSeries.Count(x => x.IsDrawing) + 1})",
                FriendlyName = $"{name} Drawing",
                Pane = "Main"
            };

            var dataBuffer = new SeriesDataBuffer { SeriesId = seriesId };
            var drawingResults = _drawingService.CalculateDrawingData(drawing, chartData);
            
            foreach (var kvp in drawingResults)
            {
                var comp = _modelFactory.CreateComponentConfig(name, kvp.Key);
                config.Components.Add(comp);
                dataBuffer.ComponentData[comp.Name] = kvp.Value;
            }

            var series = new ChartSeries(config, dataBuffer)
            {
                Drawing = drawing
            };

            _store.Dispatch(new AddSeriesAction(series));
            _eventBus.Publish(new RedrawEvent());
        }

        public void Dispose()
        {
            _subs.Dispose();
            _inputService.MouseEvent -= HandleMouseEvent;
        }
    }
}
