using AccessibleTrader.Core.Services;
using Newtonsoft.Json.Linq;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Typed settings facade (debt item 3, stage a). The contract worth pinning:
    /// every property round-trips through the underlying key-value store, defaults
    /// match the documented values when the store is empty, and every property maps
    /// to a distinct SettingsKeys constant (two properties silently sharing a key
    /// would be the new version of the typo bug this facade exists to kill).
    /// </summary>
    public class AppSettingsTests
    {
        /// <summary>In-memory ISettingsManager: just a dictionary.</summary>
        private sealed class FakeSettings : ISettingsManager
        {
            public readonly Dictionary<string, JToken> Store = new();
            public int SaveCount;
            public JToken? GetSetting(string keyPath, JToken? defaultValue = null)
                => Store.TryGetValue(keyPath, out var v) ? v : defaultValue;
            public void SetSetting(string keyPath, JToken value) => Store[keyPath] = value;
            public JObject GetEffectiveSettingsForSeries(string seriesId) => new();
            public void SaveSettings() => SaveCount++;
        }

        [Fact]
        public void Defaults_WhenStoreIsEmpty()
        {
            var app = new AppSettings(new FakeSettings());
            Assert.False(app.BrailleEnabled);
            Assert.False(app.PaperTradingMode);
            Assert.False(app.BackgroundMonitoring);
            Assert.Equal(30, app.MonitorPollSeconds);
            Assert.Equal(100, app.UiScale);
            Assert.Equal(587, app.EmailPort);
            Assert.True(app.EmailUseTls);
            Assert.Equal(Core.Services.Audio.SoundThemes.ClassicId, app.SoundTheme);
            Assert.Equal(string.Empty, app.EmailHost);
            Assert.Equal(string.Empty, app.SetupWebhookTarget);
        }

        [Fact]
        public void EveryProperty_RoundTrips()
        {
            var fake = new FakeSettings();
            var app = new AppSettings(fake);

            // Write a distinct value through every settable property, read it back.
            foreach (var prop in typeof(IAppSettings).GetProperties().Where(p => p.CanWrite))
            {
                object value = prop.PropertyType == typeof(bool) ? true
                             : prop.PropertyType == typeof(int) ? 1234
                             : prop.PropertyType == typeof(string) ? $"val_{prop.Name}"
                             : throw new InvalidOperationException($"Unhandled type {prop.PropertyType} on {prop.Name}");
                prop.SetValue(app, value);
                Assert.Equal(value, prop.GetValue(app));
            }
        }

        [Fact]
        public void EveryProperty_UsesItsOwnKey()
        {
            var fake = new FakeSettings();
            var app = new AppSettings(fake);
            var props = typeof(IAppSettings).GetProperties().Where(p => p.CanWrite).ToList();

            // Setting each property must add exactly one NEW key to the store —
            // if two properties shared a key, the count would fall short.
            foreach (var prop in props)
            {
                object value = prop.PropertyType == typeof(bool) ? true
                             : prop.PropertyType == typeof(int) ? 7
                             : (object)"x";
                prop.SetValue(app, value);
            }
            Assert.Equal(props.Count, fake.Store.Count);

            // And every key written is a declared SettingsKeys constant.
            var declared = typeof(SettingsKeys).GetFields()
                .Where(f => f.IsLiteral)
                .Select(f => (string)f.GetRawConstantValue()!)
                .ToHashSet(StringComparer.Ordinal);
            foreach (var key in fake.Store.Keys)
                Assert.Contains(key, declared);
        }

        [Fact]
        public void Save_DelegatesToSettingsManager()
        {
            var fake = new FakeSettings();
            var app = new AppSettings(fake);
            app.PaperTradingMode = true;
            Assert.Equal(0, fake.SaveCount); // setters never save implicitly
            app.Save();
            Assert.Equal(1, fake.SaveCount);
        }

        [Fact]
        public void SettingsKeys_AreAllDistinct()
        {
            var keys = typeof(SettingsKeys).GetFields()
                .Where(f => f.IsLiteral)
                .Select(f => (string)f.GetRawConstantValue()!)
                .ToList();
            Assert.Equal(keys.Count, keys.Distinct(StringComparer.Ordinal).Count());
        }
    }
}
