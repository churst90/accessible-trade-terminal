using System;
using System.Collections.Generic;
using System.Linq;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Accessibility
{
    /// <summary>
    /// Classifies a ProfileBin's structural role using TradingView-style volume thresholds.
    /// Used by both the speech formatter and the sonification engine to ensure consistency.
    /// </summary>
    public enum ProfileNodeType { LVN, Normal, ValueArea, VAL, VAH, HVN, POC }

    internal static class ProfileBinClassifier
    {
        // TradingView defaults: Value Area = 70% of session volume (tracked via IsValueArea flag).
        // HVN: bin volume > 130% of session mean AND inside value area.
        // LVN: bin volume < 40% of session mean OR is a TPO single print.
        private const double HvnThreshold = 1.3;
        private const double LvnThreshold = 0.4;

        /// <summary>
        /// Classifies a single bin given the full bin list for context.
        /// Priority order: POC > VAH > VAL > HVN > LVN > ValueArea > Normal
        /// </summary>
        public static ProfileNodeType Classify(ProfileBin bin, IList<ProfileBin> allBins)
        {
            if (bin.IsPOC) return ProfileNodeType.POC;

            // Identify VAH (highest priced value area bin) and VAL (lowest priced).
            var vaBins = allBins.Where(b => b.IsValueArea).ToList();
            if (vaBins.Count > 0)
            {
                double vahPrice = vaBins.Max(b => b.PriceHigh);
                double valPrice = vaBins.Min(b => b.PriceLow);
                if (bin.IsValueArea && Math.Abs(bin.PriceHigh - vahPrice) < 1e-9)
                    return ProfileNodeType.VAH;
                if (bin.IsValueArea && Math.Abs(bin.PriceLow - valPrice) < 1e-9)
                    return ProfileNodeType.VAL;
            }

            // LVN: single print or volume well below the session mean.
            if (bin.IsSinglePrint) return ProfileNodeType.LVN;

            int count = allBins.Count;
            if (count > 0)
            {
                double mean = allBins.Sum(b => b.TotalVolume) / count;
                if (mean > 0)
                {
                    if (bin.TotalVolume < mean * LvnThreshold)  return ProfileNodeType.LVN;
                    if (bin.IsValueArea && bin.TotalVolume > mean * HvnThreshold) return ProfileNodeType.HVN;
                    if (bin.IsValueArea) return ProfileNodeType.ValueArea;
                }
            }

            return ProfileNodeType.Normal;
        }

        // ── Sonification properties ──────────────────────────────────────────────

        /// <summary>Base frequency for this node type. No Y-axis offset is applied for profiles.</summary>
        public static double GetBasePitch(ProfileNodeType t) => t switch
        {
            ProfileNodeType.POC       => 880.0,
            ProfileNodeType.HVN       => 660.0,
            ProfileNodeType.VAH       => 550.0,
            ProfileNodeType.VAL       => 440.0,
            ProfileNodeType.ValueArea => 440.0,
            ProfileNodeType.Normal    => 330.0,
            ProfileNodeType.LVN       => 220.0,
            _                         => 330.0,
        };

        /// <summary>Waveform for this node type.</summary>
        public static string GetWaveform(ProfileNodeType t) => t switch
        {
            ProfileNodeType.POC => "sine",      // pure tone — maximally distinctive
            ProfileNodeType.HVN => "triangle",  // softer harmonic richness
            _                   => "sine",
        };

        /// <summary>Whether to trigger a click transient (used for POC to make it pop).</summary>
        public static bool ShouldTriggerClick(ProfileNodeType t) => t == ProfileNodeType.POC;

        /// <summary>Sustain duration in seconds.</summary>
        public static double GetDuration(ProfileNodeType t) => t switch
        {
            ProfileNodeType.POC  => 0.25,
            ProfileNodeType.HVN  => 0.20,
            ProfileNodeType.LVN  => 0.10,
            _                    => 0.15,
        };

        // ── Speech properties ────────────────────────────────────────────────────

        /// <summary>Human-readable label for speech output. Returns empty string for unlabeled Normal bins.</summary>
        public static string GetLabel(ProfileNodeType t) => t switch
        {
            ProfileNodeType.POC       => "Point of Control",
            ProfileNodeType.HVN       => "High Volume Node",
            ProfileNodeType.VAH       => "Value Area High",
            ProfileNodeType.VAL       => "Value Area Low",
            ProfileNodeType.ValueArea => "Value Area",
            ProfileNodeType.LVN       => "Low Volume Node",
            ProfileNodeType.Normal    => "",
            _                         => "",
        };

        // ── Heatmap Y-position pitch shift ──────────────────────────────────────

        /// <summary>
        /// Computes a frequency multiplier from normalised Y position [0=bottom, 1=top].
        /// Scales across two octaves (0.5× at bottom → 2.0× at top).
        /// Applied on top of the node-type base pitch to give heatmaps their ascending pitch feel.
        /// </summary>
        public static double GetYMultiplier(double normalizedY)
            => 0.5 + Math.Clamp(normalizedY, 0.0, 1.0) * 1.5;
    }
}
