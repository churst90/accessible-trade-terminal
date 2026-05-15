using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Core.Services;

namespace AccessibleTrader.Core.Services.Accessibility
{
    public interface ISpeechTemplateService
    {
        string GetTemplate(string indicatorCode, string componentName);
        void SetTemplate(string indicatorCode, string componentName, string template);
        void LoadFromDisk();
        void SaveToDisk();
    }

    /// <summary>
    /// Manages user-customizable speech templates for all series and indicators.
    /// This allows the platform to be "100% customizable" in its verbal feedback.
    /// </summary>
    public class SpeechTemplateService : ISpeechTemplateService
    {
        private readonly Dictionary<string, string> _templates = new(StringComparer.OrdinalIgnoreCase);
        private readonly string _filePath;

        public SpeechTemplateService(IPlatformPathService pathService)
        {
            _filePath = Path.Combine(pathService.AppDataDirectory, "speech_templates.json");
            InitializeDefaults();
            LoadFromDisk();
        }

        private void InitializeDefaults()
        {
            // Core Templates
            SetTemplate("CANDLES", "Body", "{trend}{type}. Close {value}. Open {open}. High {high}. Low {low}. {body}% body.");
            SetTemplate("PRICE", "Body", "{name}. {type}. {value}.");
            SetTemplate("VOLUME", "Volume", "{name}. {type}. {value}.");

            // Indicator Defaults
            SetTemplate("RSI", "Rsi", "{name}. {type}. {value}. {zone}.");
            SetTemplate("BB", "UpperBand", "{name}. {type}. {value}. At upper band.");
            SetTemplate("BB", "LowerBand", "{name}. {type}. {value}. At lower band.");
            SetTemplate("MACD", "Histogram", "{name}. {type}. {value}. {trend}.");
            
            // Profile Defaults
            SetTemplate("VPVR", "Profile", "Price {price}. Volume {value}. {zone}.");
            SetTemplate("TPO", "Profile", "Price {price}. Periods {value}. {letters}. {zone}.");
            
            // Heatmap Defaults
            SetTemplate("HEATMAP", "Liquidity", "Price {price}. Liquidity {intensity}. {value}.");
        }

        public string GetTemplate(string indicatorCode, string componentName)
        {
            string key = $"{indicatorCode.ToUpperInvariant()}|{componentName.ToUpperInvariant()}";
            return _templates.TryGetValue(key, out var t) ? t : "{name}. {value}.";
        }

        public void SetTemplate(string indicatorCode, string componentName, string template)
        {
            string key = $"{indicatorCode.ToUpperInvariant()}|{componentName.ToUpperInvariant()}";
            _templates[key] = template;
        }

        public void LoadFromDisk()
        {
            try
            {
                if (File.Exists(_filePath))
                {
                    string json = File.ReadAllText(_filePath);
                    var dict = JsonConvert.DeserializeObject<Dictionary<string, string>>(json);
                    if (dict != null)
                    {
                        foreach (var kvp in dict) _templates[kvp.Key] = kvp.Value;
                    }
                }
            }
            catch (Exception ex) { System.Diagnostics.Trace.TraceWarning($"SpeechTemplateService: Failed to load templates: {ex.Message}"); }
        }

        public void SaveToDisk()
        {
            try
            {
                string json = JsonConvert.SerializeObject(_templates, Formatting.Indented);
                AtomicFile.WriteAllText(_filePath, json);
            }
            catch (Exception ex) { System.Diagnostics.Trace.TraceWarning($"SpeechTemplateService: Failed to save templates: {ex.Message}"); }
        }
    }
}
