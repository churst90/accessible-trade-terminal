using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.MarketPulse
{
    /// <summary>Where a crypto change starts. Defaults to midnight UTC.</summary>
    public enum PulseCryptoStart
    {
        MidnightUtc,
        Last24Hours,
    }

    /// <summary>
    /// The pulse setting as the workspace mirror carries it. An unknown value reads as midnight,
    /// which is the shipped default and the less surprising of the two.
    /// </summary>
    public static class MarketPulseSettings
    {
        public const string MidnightUtc = "midnightUtc";
        public const string Last24Hours = "last24h";

        public static PulseCryptoStart CryptoStart(WorkspaceState state)
            => Parse(state.MarketPulseCryptoStart);

        public static PulseCryptoStart Parse(string? value) => value switch
        {
            Last24Hours => PulseCryptoStart.Last24Hours,
            _ => PulseCryptoStart.MidnightUtc,
        };

        public static string ToSetting(PulseCryptoStart start)
            => start == PulseCryptoStart.Last24Hours ? Last24Hours : MidnightUtc;
    }
}
