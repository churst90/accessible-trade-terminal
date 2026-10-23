using AccessibleTrader.Core.Services;
using Newtonsoft.Json.Linq;

namespace AccessibleTrader.Tests
{
    /// <summary>An <see cref="IAppSettings"/> over an in-memory store, for tests that need one
    /// opt-in switched on (a null settings object reads every opt-in as off).</summary>
    internal static class TestAppSettings
    {
        private sealed class MemorySettings : ISettingsManager
        {
            private readonly Dictionary<string, JToken> _store = new();
            public JToken? GetSetting(string keyPath, JToken? defaultValue = null)
                => _store.TryGetValue(keyPath, out var v) ? v : defaultValue;
            public void SetSetting(string keyPath, JToken value) => _store[keyPath] = value;
            public JObject GetEffectiveSettingsForSeries(string seriesId) => new();
            public void SaveSettings() { }
            public void ResetToDefaults() => _store.Clear();
            public void Reload() { }
        }

        /// <summary>Settings with "Speak other signals while arrowing" set as given.</summary>
        public static IAppSettings SpeakingOtherSignals(bool on = true)
            => new AppSettings(new MemorySettings()) { SpeakOtherSignalsWhileArrowing = on };
    }
}
