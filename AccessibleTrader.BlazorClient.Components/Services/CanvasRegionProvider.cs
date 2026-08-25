using System.Reactive.Linq;
using System.Reactive.Subjects;

namespace AccessibleTrader.BlazorClient.Services
{
    /// <summary>
    /// Viewport-space bounds reported from the Blazor ChartArea to the native host.
    /// Values are in CSS pixels (== DIPs at 100% DPI; MAUI Margin is also DIPs, so
    /// they can be used directly). TopDip is the chart region's top edge measured
    /// from the WebView top; BottomDip is its distance from the WebView bottom.
    /// </summary>
    public readonly record struct CanvasBounds(double TopDip, double BottomDip)
    {
        public static readonly CanvasBounds Fallback = new(185, 100);
    }

    /// <summary>
    /// Bridge between the Blazor chart-area DOM element and the native SKCanvasView
    /// margin. ChartArea.razor publishes via JS interop on mount and on resize;
    /// MainPage.xaml.cs subscribes and re-applies the margin on the main thread.
    /// Falls back to the original hardcoded 185/100 values if no report arrives.
    /// </summary>
    public interface ICanvasRegionProvider
    {
        CanvasBounds Current { get; }
        IObservable<CanvasBounds> BoundsChanged { get; }
        void Report(double topDip, double bottomDip);
    }

    public sealed class CanvasRegionProvider : ICanvasRegionProvider, IDisposable
    {
        private readonly BehaviorSubject<CanvasBounds> _subject = new(CanvasBounds.Fallback);

        public CanvasBounds Current => _subject.Value;
        public IObservable<CanvasBounds> BoundsChanged => _subject.DistinctUntilChanged();

        public void Report(double topDip, double bottomDip)
        {
            // Discard implausible values (JS timing can report zero/negatives during
            // initial layout). Keep the last valid bounds rather than snapping to 0.
            if (topDip < 0 || bottomDip < 0 || double.IsNaN(topDip) || double.IsNaN(bottomDip))
                return;
            _subject.OnNext(new CanvasBounds(topDip, bottomDip));
        }

        public void Dispose() => _subject.Dispose();
    }
}
