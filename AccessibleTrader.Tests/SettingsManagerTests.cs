using System;
using System.IO;
using System.Linq;
using AccessibleTrader.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Phase E test-debt for SettingsManager: missing-file defaults, nested keyPath
    /// round-trips, save/reload persistence through the injectable path service, the
    /// corrupt-file quarantine (2026-06-12 audit fix 6 — corrupt originals are moved
    /// aside, never overwritten by the empty default), and the demo-mode write block.
    /// </summary>
    public class SettingsManagerTests : IDisposable
    {
        private sealed class TempPathService : IPlatformPathService
        {
            public TempPathService(string root) { AppDataDirectory = root; CacheDirectory = root; }
            public string AppDataDirectory { get; }
            public string CacheDirectory { get; }
        }

        private readonly string _dir;
        private readonly TempPathService _paths;

        public SettingsManagerTests()
        {
            _dir = Directory.CreateTempSubdirectory("att-settings-tests-").FullName;
            _paths = new TempPathService(_dir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
        }

        private SettingsManager NewManager(DemoPolicy? demo = null) =>
            new(_paths, NullLogger<SettingsManager>.Instance, demo);

        private string SettingsFile => Path.Combine(_dir, "settings.json");

        [Fact]
        public void MissingFile_LoadsEmptyDefaults_AndGetSettingReturnsFallback()
        {
            var mgr = NewManager();

            Assert.Null(mgr.GetSetting("audio.master.volume"));
            Assert.Equal(0.7, mgr.GetSetting("audio.master.volume", new JValue(0.7))!.Value<double>());
        }

        [Fact]
        public void SetThenGet_RoundTrips_NestedKeyPath()
        {
            var mgr = NewManager();

            mgr.SetSetting("audio.master.volume", new JValue(0.35));

            // Intermediate objects are auto-created; the leaf is retrievable both via
            // the full dotted path and by walking a partial path.
            Assert.Equal(0.35, mgr.GetSetting("audio.master.volume")!.Value<double>());
            var master = mgr.GetSetting("audio.master") as JObject;
            Assert.NotNull(master);
            Assert.Equal(0.35, master!["volume"]!.Value<double>());
        }

        [Fact]
        public void SetSetting_ReplacesScalarIntermediate_WithObject()
        {
            // Writing a deeper path through an existing scalar must not throw — the
            // scalar is replaced by an object so the new leaf can be stored.
            var mgr = NewManager();
            mgr.SetSetting("theme", new JValue("dark"));

            mgr.SetSetting("theme.accent", new JValue("#00FF00"));

            Assert.Equal("#00FF00", mgr.GetSetting("theme.accent")!.Value<string>());
        }

        [Fact]
        public void GetSetting_ThroughAScalar_ReturnsDefaultWithoutThrowing()
        {
            var mgr = NewManager();
            mgr.SetSetting("speech.rate", new JValue(1.5));

            // "speech.rate.fast" walks INTO the scalar 1.5 — must fall back, not throw.
            Assert.Equal("fallback",
                mgr.GetSetting("speech.rate.fast", new JValue("fallback"))!.Value<string>());
        }

        [Fact]
        public void SaveSettings_ThenReloadWithFreshInstance_RoundTrips()
        {
            var writer = NewManager();
            writer.SetSetting("sonification.waveform", new JValue("triangle"));
            writer.SetSetting("overrides.by_instance_id.price.color", new JValue("#123456"));
            writer.SaveSettings();

            Assert.True(File.Exists(SettingsFile), "SaveSettings must write settings.json");

            var reader = NewManager(); // fresh instance = fresh lazy load from disk
            Assert.Equal("triangle", reader.GetSetting("sonification.waveform")!.Value<string>());
            Assert.Equal("#123456",
                reader.GetSetting("overrides.by_instance_id.price.color")!.Value<string>());
        }

        [Fact]
        public void CorruptSettingsFile_RecoversToDefaults_AndQuarantinesTheOriginal()
        {
            const string garbage = "{ this is not valid json !!!";
            File.WriteAllText(SettingsFile, garbage);

            var mgr = NewManager();

            // First access triggers the lazy load; it must not throw and must serve defaults.
            Assert.Equal(42, mgr.GetSetting("anything", new JValue(42))!.Value<int>());

            // The corrupt original is MOVED aside (settings.json.corrupt-<stamp>), so a
            // subsequent SaveSettings can't permanently destroy the recoverable data.
            Assert.False(File.Exists(SettingsFile),
                "corrupt settings.json should have been moved aside");
            var quarantined = Directory.GetFiles(_dir, "settings.json.corrupt-*");
            var q = Assert.Single(quarantined);
            Assert.Equal(garbage, File.ReadAllText(q)); // original bytes preserved
        }

        [Fact]
        public void SaveAfterCorruptLoad_WritesCleanDefaults_WithoutTouchingQuarantine()
        {
            File.WriteAllText(SettingsFile, "not json");
            var mgr = NewManager();
            mgr.SetSetting("fresh.key", new JValue(true));

            mgr.SaveSettings();

            Assert.True(File.Exists(SettingsFile));
            var reloaded = JObject.Parse(File.ReadAllText(SettingsFile));
            Assert.True(reloaded["fresh"]!["key"]!.Value<bool>());
            Assert.Single(Directory.GetFiles(_dir, "settings.json.corrupt-*"));
        }

        [Fact]
        public void DemoPolicy_BlocksSettingsPersistence_SoVisitorsCannotClobberSharedState()
        {
            var mgr = NewManager(new DemoPolicy(HostMode.Demo));
            mgr.SetSetting("theme", new JValue("dark"));

            mgr.SaveSettings();

            // The public demo shares one server process across anonymous visitors —
            // settings.json must never be written in that mode.
            Assert.False(File.Exists(SettingsFile));
        }
    }
}
