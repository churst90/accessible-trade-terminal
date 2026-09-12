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
            public void ResetToDefaults() { Store.Clear(); SaveCount++; }
            public void Reload() { }
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
                object value = Distinct(prop);
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
                // Distinct() rather than a fall-through to "x": a silent string default here is
                // what turned "cannot convert String to Single" into the failure mode instead of
                // a clear one, and a type this test cannot represent must say so.
                prop.SetValue(app, Distinct(prop));
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
        /// <summary>
        /// A value distinct from any default, for whatever type the property is.
        ///
        /// <para>
        /// Throws on a type it does not know, which is what made a <c>float</c> preference added
        /// on 2026-09-12 turn this test red rather than slip through it. A clamped property needs
        /// a value INSIDE its range — <c>PlaybackSpeed</c> is clamped to 0.1–10, so 1234 would be
        /// written, clamped on the way out, and read back as something else entirely.
        /// </para>
        /// </summary>
        private static object Distinct(System.Reflection.PropertyInfo prop)
        {
            if (prop.PropertyType == typeof(bool)) return true;
            if (prop.PropertyType == typeof(int)) return 1234;
            if (prop.PropertyType == typeof(float)) return 2.5f;
            if (prop.PropertyType == typeof(double)) return 2.5d;
            if (prop.PropertyType == typeof(string)) return $"val_{prop.Name}";
            throw new InvalidOperationException($"Unhandled type {prop.PropertyType} on {prop.Name}");
        }

    }
}
