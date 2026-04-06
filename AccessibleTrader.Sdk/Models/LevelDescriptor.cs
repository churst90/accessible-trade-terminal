namespace AccessibleTrader.Sdk.Models
{
    /// <summary>
    /// Describes a reference level line declared by an indicator provider.
    /// All audio fields default to off so providers only need to declare what they use.
    /// </summary>
    public record LevelDescriptor(
        string Name,
        double Value,
        string ColorHex,
        DashStyle Dash,
        bool PlayEarcon       = false,
        float EarconVolume    = 0.7f,
        float ZoneNoiseAmount = 0f,
        string ZoneNoiseType  = "pink"
    );
}
