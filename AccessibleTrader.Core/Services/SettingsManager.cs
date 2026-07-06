using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using AccessibleTrader.Core.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AccessibleTrader.Core.Services
{
    public interface ISettingsManager
    {
        JToken? GetSetting(string keyPath, JToken? defaultValue = null);
        void SetSetting(string keyPath, JToken value);
        JObject GetEffectiveSettingsForSeries(string seriesId);
        void SaveSettings();
    }

    public class SettingsManager : ISettingsManager
    {
        private readonly IPlatformPathService _pathService;
        private readonly ILogger<SettingsManager> _logger;
        // Optional so the direct-construction tests still compile; null = full app.
        private readonly DemoPolicy? _demo;

        // Loaded lazily on first access (not in the ctor) so that, on the multi-user
        // WebHost, AppDataDirectory resolves to the authenticated user's directory — set
        // per circuit *after* this service is constructed — rather than whatever the path
        // service pointed at during construction. Harmless on the single-user heads, where
        // AppDataDirectory is fixed.
        private readonly object _initLock = new();
        private JObject? _settings;
        private string? _filepath;

        public SettingsManager(IPlatformPathService pathService, ILogger<SettingsManager> logger, DemoPolicy? demo = null)
        {
            _pathService = pathService;
            _logger = logger;
            _demo = demo;
        }

        /// <summary>The settings document, loaded (and its file path resolved) on first access.</summary>
        private JObject Settings
        {
            get
            {
                if (_settings != null) return _settings;
                lock (_initLock)
                {
                    if (_settings != null) return _settings;
                    _filepath = Path.Combine(_pathService.AppDataDirectory, "settings.json");
                    _settings = LoadSettings();
                    return _settings;
                }
            }
        }

        private JObject LoadSettings()
        {
            try
            {
                _logger.LogDebug("Loading settings from {Path}.", _filepath);
                if (File.Exists(_filepath))
                {
                    var json = File.ReadAllText(_filepath);
                    var settings = JsonConvert.DeserializeObject<JObject>(json) ?? new JObject();
                    _logger.LogDebug("Settings loaded successfully from JSON.");
                    return settings;
                }
                else
                {
                    _logger.LogDebug("Settings file not found. Using defaults.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load settings from {Path}.", _filepath);
                // Preserve the corrupt original — the next SaveSettings would
                // otherwise overwrite it with the empty default.
                if (!string.IsNullOrEmpty(_filepath))
                    CorruptFileQuarantine.MoveAside(_filepath, ex);
            }
            return new JObject();
        }

        public JToken? GetSetting(string keyPath, JToken? defaultValue = null)
        {
            var keys = keyPath.Split('.');
            JToken current = Settings;
            try
            {
                foreach (var key in keys)
                {
                    current = current[key]!;
                    if (current == null) return defaultValue;
                }
                return current;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SettingsManager: failed to read key '{keyPath}', returning default: {ex.Message}");
                return defaultValue;
            }
        }

        public void SetSetting(string keyPath, JToken value)
        {
            var keys = keyPath.Split('.');
            JObject current = Settings;

            for (int i = 0; i < keys.Length - 1; i++)
            {
                var key = keys[i];
                if (current[key] is not JObject next)
                {
                    next = new JObject();
                    current[key] = next;
                }
                current = next;
            }

            current[keys.Last()] = value;
        }

        public JObject GetEffectiveSettingsForSeries(string seriesId)
        {
            JObject baseSchema;

            if (seriesId == "price")
                baseSchema = SeriesSchemas.CandlestickSchema;
            else if (seriesId == "volume")
                baseSchema = SeriesSchemas.VolumeSchema;
            else
                return new JObject();

            // Clone to avoid modifying the static base schemas
            baseSchema = (JObject)baseSchema.DeepClone();

            var overrides = GetSetting($"overrides.by_instance_id.{seriesId}") as JObject;
            if (overrides != null)
            {
                MergeJson(baseSchema, overrides);
            }

            return baseSchema;
        }

        private void MergeJson(JObject baseObj, JObject overrideObj)
        {
            foreach (var property in overrideObj.Properties())
            {
                if (property.Value is JObject obj && baseObj[property.Name] is JObject baseSubObj)
                {
                    MergeJson(baseSubObj, obj);
                }
                else
                {
                    baseObj[property.Name] = property.Value;
                }
            }
        }

        private void FormatPlaceholders(JToken node, Dictionary<string, string> mapping)
        {
            if (node is JObject obj)
            {
                foreach (var prop in obj.Properties()) FormatPlaceholders(prop.Value, mapping);
            }
            else if (node is JArray arr)
            {
                foreach (var item in arr) FormatPlaceholders(item, mapping);
            }
            else if (node is JValue val && val.Type == JTokenType.String)
            {
                var str = val.Value<string>();
                if (str != null)
                {
                    foreach (var kvp in mapping)
                    {
                        if (!string.IsNullOrEmpty(kvp.Value))
                        {
                            str = str.Replace("{" + kvp.Key + "}", "{" + kvp.Value + "}")
                                     .Replace("{" + kvp.Key + ":", "{" + kvp.Value + ":");
                        }
                    }
                    val.Value = str;
                }
            }
        }

        public void SaveSettings()
        {
            // In the public demo, never write settings.json — visitors share one
            // server process and must not clobber each other's (or the host's) state.
            if (_demo is { AllowSettingsPersist: false }) return;
            try
            {
                // Touch Settings first so the document is loaded and _filepath is resolved
                // (against the current user's directory) before we write.
                var doc = Settings;
                AtomicFile.WriteAllText(_filepath!, JsonConvert.SerializeObject(doc, Formatting.Indented));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to save settings: {ex.Message}");
            }
        }
    }
}
