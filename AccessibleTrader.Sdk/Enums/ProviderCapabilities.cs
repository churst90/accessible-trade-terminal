using System;

namespace AccessibleTrader.Sdk.Enums
{
    /// <summary>
    /// Bitwise flags representing the specific trading and data features supported by a provider.
    /// This allows the UI to dynamically show/hide advanced controls (like OCO or Shorting)
    /// based on the connected exchange or broker's API capabilities.
    /// </summary>
    [Flags]
    public enum ProviderCapabilities
    {
        /// <summary>No advanced capabilities; basic spot trading only.</summary>
        None = 0,

        /// <summary>Level 2 Order Book data is available for depth visualization.</summary>
        L2 = 1 << 0,

        /// <summary>Supports short selling (borrowing assets to sell first).</summary>
        Shorting = 1 << 1,

        /// <summary>Supports One-Cancels-the-Other (OCO) order pairs.</summary>
        OCO = 1 << 2,

        /// <summary>Supports Trailing Stop Loss orders.</summary>
        TrailingStop = 1 << 3,

        /// <summary>Provides full Market Depth beyond standard L2 (e.g., full book snapshots).</summary>
        MarketDepth = 1 << 4,

        /// <summary>Supports Margin/Leverage trading on spot or futures.</summary>
        Leverage = 1 << 5,

        /// <summary>Supports Bracket Orders (automated SL/TP attached to entry).</summary>
        Brackets = 1 << 6
    }
}
