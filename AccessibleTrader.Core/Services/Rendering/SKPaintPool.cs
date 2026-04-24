using System;
using System.Collections.Generic;
using SkiaSharp;

namespace AccessibleTrader.Core.Services.Rendering
{
    /// <summary>
    /// Thread-local pool of reusable <see cref="SKPaint"/> instances for the rendering
    /// hot path. Rendering runs on a single thread (the MAUI main thread drives
    /// SKCanvasView.PaintSurface), so a ThreadStatic stack is race-free without locks.
    ///
    /// Historical context: per-bar renderers were allocating a fresh <c>SKPaint</c>
    /// per wick, body, dot, arrow, diamond, square, cross, and conditional line
    /// segment. At 500 visible bars × 5 panes with busy confluence indicators this
    /// peaked around 2,500 allocations per frame — each carrying a native-handle
    /// finalizer that amplified GC pressure. Pooling drops the steady-state alloc
    /// count to ~10 (one first-use grow per paint shape).
    ///
    /// Usage: always via the <c>using</c> pattern so Dispose() returns the paint to
    /// the pool. After renting, the paint is in a freshly-reset state (no leaked
    /// properties from a prior caller); set only the fields you need.
    /// </summary>
    public static class SKPaintPool
    {
        [ThreadStatic] private static Stack<SKPaint>? _pool;

        // Hard cap so long runs with rare wide-viewport snapshots don't retain
        // a huge pool permanently. Past this size we dispose overflow paints.
        private const int MaxPoolSize = 64;

        /// <summary>
        /// Checks out a fresh, default-state <see cref="SKPaint"/>. Disposing the
        /// returned struct returns the underlying paint to the pool.
        /// </summary>
        public static RentedPaint Rent()
        {
            _pool ??= new Stack<SKPaint>(32);
            SKPaint paint = _pool.Count > 0 ? _pool.Pop() : new SKPaint();
            paint.Reset();
            return new RentedPaint(paint);
        }

        internal static void Return(SKPaint paint)
        {
            if (paint == null) return;
            _pool ??= new Stack<SKPaint>(32);
            if (_pool.Count < MaxPoolSize)
            {
                _pool.Push(paint);
            }
            else
            {
                paint.Dispose();
            }
        }
    }

    /// <summary>
    /// RAII wrapper returned from <see cref="SKPaintPool.Rent"/>. A <c>ref struct</c>-style
    /// normal struct is fine here because callers only hold the lease for the
    /// duration of a <c>using</c> block on the render thread.
    /// </summary>
    public readonly struct RentedPaint : IDisposable
    {
        public SKPaint Paint { get; }
        public RentedPaint(SKPaint paint) { Paint = paint; }
        public void Dispose() => SKPaintPool.Return(Paint);
    }
}
