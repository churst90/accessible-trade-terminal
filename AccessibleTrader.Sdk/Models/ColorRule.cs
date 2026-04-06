using System.Collections.Generic;

namespace AccessibleTrader.Sdk.Models
{
    /// <summary>
    /// Condition that triggers a per-bar color override.
    /// </summary>
    public enum ColorCondition
    {
        /// <summary>Value is greater than or equal to zero.</summary>
        AboveZero,
        /// <summary>Value is less than zero.</summary>
        BelowZero,
        /// <summary>Value increased compared to the previous bar.</summary>
        Rising,
        /// <summary>Value decreased compared to the previous bar.</summary>
        Falling,
        /// <summary>Value is greater than the specified Level threshold.</summary>
        AboveLevel,
        /// <summary>Value is less than or equal to the specified Level threshold.</summary>
        BelowLevel,
    }

    /// <summary>
    /// Maps a <see cref="ColorCondition"/> to a hex color string for per-bar coloring.
    /// Rules are evaluated in order; the first match wins.
    /// </summary>
    public record ColorRule
    {
        public required ColorCondition Condition { get; init; }
        public required string ColorHex { get; init; }

        /// <summary>
        /// Threshold used by <see cref="ColorCondition.AboveLevel"/> and
        /// <see cref="ColorCondition.BelowLevel"/>. Ignored for other conditions.
        /// </summary>
        public double Level { get; init; } = 0.0;
    }
}
