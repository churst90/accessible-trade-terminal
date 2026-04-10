using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using AccessibleTrader.Core.Configuration;
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
        private readonly string _filepath;
        private JObject _settings;

        public SettingsManager(IPlatformPathService pathService)
        {
            _filepath = Path.Combine(pathService.AppDataDirectory, "settings.json");
            _settings = LoadSettings();
        }

        private JObject LoadSettings()
        {
            try
            {
                Console.WriteLine($"SettingsManager: Loading settings from {_filepath}...");
                if (File.Exists(_filepath))
                {
                    var json = File.ReadAllText(_filepath);
                    var settings = JsonConvert.DeserializeObject<JObject>(json) ?? new JObject();
                    Console.WriteLine("SettingsManager: Settings loaded successfully from JSON.");
                    return settings;
                }
                else
                {
                    Console.WriteLine("SettingsManager: Settings file not found. Using defaults.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"SettingsManager ERROR: {ex.Message}");
            }
            return new JObject();
        }

        public JToken? GetSetting(string keyPath, JToken? defaultValue = null)
        {
            var keys = keyPath.Split('.');
            JToken current = _settings;
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
            JObject current = _settings;
            
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
            try
            {
                var dir = Path.GetDirectoryName(_filepath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(_filepath, JsonConvert.SerializeObject(_settings, Formatting.Indented));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to save settings: {ex.Message}");
            }
        }
    }
}
