using System;

namespace AccessibleTrader.Sdk.Models
{
    /// <summary>
    /// The one way to turn a stored component index into an index you may actually use.
    ///
    /// <para>
    /// The shape this replaces is <c>Math.Clamp(state.FocusedComponentIndex, 0, s.Components.Count - 1)</c>,
    /// which reads as defensive and is the opposite. When the series has no components the upper
    /// bound is <c>-1</c>, and <see cref="Math.Clamp(int,int,int)"/> THROWS when <c>min &gt; max</c>
    /// — an <see cref="ArgumentException"/> out of the line written to prevent one.
    /// </para>
    ///
    /// <para>
    /// That throw was not survivable where it lived. Most of these sites sit inside EventBus
    /// subscribers, and a throwing Rx observer is disposed by <c>AutoDetachObserver</c>: the
    /// subscription is torn down and every later keypress produces silence, with nothing said
    /// about it. For a screen-reader user the terminal simply stops talking, permanently, and
    /// restarting the app is the only cure. One of the sites is a reducer, where the throw
    /// surfaces inside <c>Dispatch</c> instead.
    /// </para>
    ///
    /// <para>
    /// A zero-component series is not exotic: an indicator whose provider returned nothing, or
    /// one focused mid-load, has an empty <c>Components</c> collection while the focus index
    /// still points at 0 from the series before it.
    /// </para>
    /// </summary>
    public static class ComponentIndex
    {
        /// <summary>
        /// Clamps <paramref name="index"/> into <paramref name="series"/>'s component range,
        /// returning <b>-1</b> when the series has no components — the caller must decide what
        /// "there is no component here" means rather than indexing into nothing.
        /// </summary>
        public static int ClampComponent(this ChartSeries series, int index)
            => ClampComponent(series?.Components?.Count ?? 0, index);

        /// <summary>
        /// Count-based overload, for the callers that hold a component collection rather than
        /// the series. Returns -1 when <paramref name="count"/> is zero or negative.
        /// </summary>
        public static int ClampComponent(int count, int index)
            => count <= 0 ? -1 : Math.Clamp(index, 0, count - 1);
    }
}
