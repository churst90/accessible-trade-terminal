using AccessibleTrader.Core.Services.Accessibility;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Sparse marker components (Cipher B dots etc.) are NaN at most bars. Landing on
    /// one used to announce "no data"; it now reports how many signals are in view so
    /// a blind user knows to jump between them with Ctrl+Left/Right.
    /// </summary>
    public class SparseMarkerCountTests
    {
        private static double N => double.NaN;

        [Fact]
        public void Counts_only_non_nan_within_the_viewport()
        {
            // markers at indices 1, 4, 7; viewport [2, 6) → indices 2..5 → one marker (idx 4).
            var data = new[] { N, 1.0, N, N, 2.0, N, N, 3.0 };
            Assert.Equal(1, MarkerSignalStrategy.CountMarkersInView(data, viewportStart: 2, viewportLength: 4));
        }

        [Fact]
        public void Whole_array_when_viewport_unknown()
        {
            var data = new[] { N, 1.0, N, 2.0, N, 3.0 };
            Assert.Equal(3, MarkerSignalStrategy.CountMarkersInView(data, viewportStart: -1, viewportLength: -1));
        }

        [Fact]
        public void Zero_when_no_markers_in_view()
        {
            var data = new[] { 1.0, N, N, N, 2.0 };
            Assert.Equal(0, MarkerSignalStrategy.CountMarkersInView(data, viewportStart: 1, viewportLength: 3));
        }

        [Fact]
        public void Minus_one_when_no_data_at_all()
        {
            Assert.Equal(-1, MarkerSignalStrategy.CountMarkersInView(null, 0, 10));
            Assert.Equal(-1, MarkerSignalStrategy.CountMarkersInView(new double[0], 0, 10));
        }

        [Fact]
        public void Viewport_past_the_array_end_is_clamped()
        {
            var data = new[] { N, 1.0, N, 2.0 };
            Assert.Equal(2, MarkerSignalStrategy.CountMarkersInView(data, viewportStart: 0, viewportLength: 999));
        }
    }
}
