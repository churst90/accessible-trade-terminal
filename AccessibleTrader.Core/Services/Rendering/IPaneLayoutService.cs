using System;
using System.Collections.Generic;

namespace AccessibleTrader.Core.Services.Rendering
{
    /// <summary>
    /// Shared service that ChartRenderer populates after each render pass and
    /// ChartArea.razor reads to position transparent pane-divider drag handles.
    /// All positions are expressed as fractions (0.0–1.0) of the total canvas height.
    /// </summary>
    public interface IPaneLayoutService
    {
        /// <summary>
        /// Ordered list of dividers.  Each entry carries the pane that sits BELOW
        /// the divider so that a drag dispatches <see cref="ResizePaneAction"/> for
        /// the correct pane name.
        /// </summary>
        IReadOnlyList<(string BelowPaneName, float DividerFraction)> Dividers { get; }

        /// <summary>Fraction of the total canvas height consumed by the x-axis strip.</summary>
        float AxisHeightFraction { get; }

        /// <summary>Called by ChartRenderer after pane heights are calculated.</summary>
        void Update(
            IReadOnlyList<(string BelowPaneName, float DividerFraction)> dividers,
            float axisHeightFraction);

        /// <summary>Fires whenever <see cref="Update"/> is called with changed values.</summary>
        event Action? LayoutChanged;
    }

    public class PaneLayoutService : IPaneLayoutService
    {
        private IReadOnlyList<(string, float)> _dividers = Array.Empty<(string, float)>();
        private float _axisHeightFraction;

        public IReadOnlyList<(string BelowPaneName, float DividerFraction)> Dividers => _dividers;
        public float AxisHeightFraction => _axisHeightFraction;

        public event Action? LayoutChanged;

        public void Update(
            IReadOnlyList<(string BelowPaneName, float DividerFraction)> dividers,
            float axisHeightFraction)
        {
            _dividers = dividers;
            _axisHeightFraction = axisHeightFraction;
            LayoutChanged?.Invoke();
        }
    }
}
