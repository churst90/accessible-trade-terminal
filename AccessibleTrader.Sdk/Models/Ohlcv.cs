using System;
using System.Collections.Generic;

namespace AccessibleTrader.Sdk.Models
{
    /// <summary>
    /// Represents a single, immutable unit of market data (Open, High, Low, Close, Volume).
    /// Designed as a 'readonly record struct' to minimize heap allocations and Garbage Collection 
    /// pressure when handling large historical datasets in high-frequency trading contexts.
    /// </summary>
    public readonly record struct Ohlcv(DateTime Date, double Open, double High, double Low, double Close, double Volume)
    {
        /// <summary>
        /// Returns a new instance of this OHLCV bar updated with the data from a subsequent tick.
        /// Expands the High/Low bounds and accumulates the volume while updating the Close price.
        /// </summary>
        /// <param name="tick">The new incoming tick data.</param>
        /// <returns>A new aggregated Ohlcv struct.</returns>
        public Ohlcv UpdateWith(Ohlcv tick)
        {
            return this with
            {
                Open   = this.Open  == 0 ? tick.Open  : this.Open,   // heal zero Open from malformed first tick
                High   = Math.Max(this.High, tick.High),
                Low    = this.Low   == 0 ? tick.Low   : Math.Min(this.Low, tick.Low),
                Close  = tick.Close,
                Volume = this.Volume + tick.Volume
            };
        }
    }

    /// <summary>
    /// Represents a single price/quantity pair in an order book depth chart.
    /// </summary>
    public readonly record struct OrderBookEntry(double Price, double Quantity);

    /// <summary>
    /// Defines a standardized request for historical market data from a plugin provider.
    /// </summary>
    public record MarketDataRequest(string Market, string Symbol, string Timeframe, int Limit = 200, long? Since = null, long? Until = null);

    /// <summary>
    /// Defines a specific point in a sonification sequence, mapping audio characteristics to data.
    /// </summary>
    /// <param name="PatchLayers">When a multi-oscillator user Sound Designer patch is assigned to the
    /// component, the full ordered list of oscillator layers to render (each as its own voice). Null for
    /// single-oscillator patches / no patch — those are carried by <c>Waveform</c>/<c>NoiseAmount</c>.</param>
    /// <param name="CrossDirection">Set when the value crossed a reference / OB / OS level on this bar,
    /// for firing a directional cross earcon: +1 = crossed upward, -1 = crossed downward, 0 = no cross.
    /// Inherits the same PlayEarcon / subscription gating as <c>TriggerClick</c>.</param>
    public readonly record struct AudioPoint(double Frequency, float Volume, string Waveform, double Pan, string EnvelopeType = "Sustain", bool TriggerClick = false, double ReferenceLevel = 0, float NoiseAmount = 0f, string? PatchId = null, string NoiseType = "pink", IReadOnlyList<OscillatorLayer>? PatchLayers = null, int CrossDirection = 0);
}
