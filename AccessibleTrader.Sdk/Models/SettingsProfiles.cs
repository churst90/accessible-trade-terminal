namespace AccessibleTrader.Sdk.Models
{
    /// <summary>Visual appearance settings — shareable between users without exposing credentials.</summary>
    public class VisualProfile
    {
        public string Name { get; set; } = "My Visual Profile";
        public string ThemeType { get; set; } = "HighContrastDark";
        public string BackgroundColor { get; set; } = "#000000";
        /// <summary>ComponentAppearance overrides keyed by "SeriesFriendlyName.ComponentName".</summary>
        public Dictionary<string, ComponentAppearance> ComponentColors { get; set; } = new();
    }

    /// <summary>Audio/sonification settings — shareable between blind users to exchange tuned soundscapes.</summary>
    public class AudioProfile
    {
        public string Name { get; set; } = "My Audio Profile";
        public bool SonificationEnabled { get; set; } = true;
        public float MasterVolume { get; set; } = 0.7f;
        public int WasapiLatency { get; set; } = 50;
        /// <summary>Per-component audio overrides keyed by "SeriesFriendlyName.ComponentName".</summary>
        public Dictionary<string, ComponentAudioOverride> ComponentAudio { get; set; } = new();
    }

    public class ComponentAppearance
    {
        public string ColorHex { get; set; } = "#FFFFFF";
        public string ColorHexSecondary { get; set; } = "#FF0000";
        public float Thickness { get; set; } = 2f;
        public string DashStyle { get; set; } = "Solid";
    }

    public class ComponentAudioOverride
    {
        public string Waveform { get; set; } = "sine";
        public double FreqMultiplier { get; set; } = 1.0;
        public float Volume { get; set; } = 1.0f;
        public bool IsMuted { get; set; }
    }
}
