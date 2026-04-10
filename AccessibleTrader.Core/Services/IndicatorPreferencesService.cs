using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AccessibleTrader.Sdk.Models;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace AccessibleTrader.Core.Services
{
    /// <summary>
    /// Stores per-component appearance and sonification preferences for each indicator code,
    /// keyed by indicator code (e.g. "CIPHER_B", "RSI"). Preferences persist across workspace
    /// reloads and are applied by IndicatorModelFactory as the top-priority layer over metadata
    /// defaults. Users set preferences via the Properties dialog → Appearance / Sonification tabs.
    /// </summary>
    public interface IIndicatorPreferencesService
    {
        /// <summary>Returns saved component preferences for an indicator, or null if none exist.</summary>
        List<ComponentPreference>? GetPreferences(string indicatorCode);
        /// <summary>Saves component preferences for an indicator. Replaces any existing entry.</summary>
        void SavePreferences(string indicatorCode, List<ComponentPreference> prefs);
        /// <summary>Removes saved preferences for an indicator (resets to metadata defaults).</summary>
        void ClearPreferences(string indicatorCode);
        /// <summary>Returns saved level preferences for an indicator, or an empty list if none exist.</summary>
        List<LevelPreference> GetLevelPreferences(string indicatorCode);
        /// <summary>Saves or updates a single level preference (matched by Name).</summary>
        void SaveLevelPreference(string indicatorCode, LevelPreference pref);
    }

    /// <summary>
    /// Per-component preference snapshot. Only non-null fields are applied as overrides so
    /// a sparse record can override just colour without touching audio, or vice-versa.
    /// </summary>
    public class ComponentPreference
    {
        public string Name                { get; set; } = "";
        public string? ColorHex           { get; set; }
        public string? ColorHexSecondary  { get; set; }
        public float?  Thickness          { get; set; }
        public DashStyle? DashStyle       { get; set; }
        public string? Waveform           { get; set; }
        public string? EnvelopeType       { get; set; }
        public float?  Volume             { get; set; }
        public double? FreqMultiplier     { get; set; }
        public double? BaseFrequency      { get; set; }
        public float?  NoiseAmount        { get; set; }
        public bool?   IsVisible          { get; set; }
    }

    /// <summary>
    /// Per-level preference snapshot. Only non-null fields are applied as overrides.
    /// </summary>
    public class LevelPreference
    {
        public string  Name            { get; set; } = "";
        public double? Value           { get; set; }
        public bool?   IsVisible       { get; set; }
        public bool?   PlayEarcon      { get; set; }
        public float?  EarconVolume    { get; set; }
        public float?  ZoneNoiseAmount { get; set; }
        public string? ZoneNoiseType   { get; set; }
    }

    /// <summary>
    /// On-disk payload for a single indicator's preferences file.
    /// Wraps both component and level preferences in a single JSON file.
    /// </summary>
    internal class IndicatorPrefsFile
    {
        public List<ComponentPreference> Components { get; set; } = new();
        public List<LevelPreference>     Levels     { get; set; } = new();
    }

    public class IndicatorPreferencesService : IIndicatorPreferencesService
    {
        private readonly string _prefsDir;
        private readonly ILogger<IndicatorPreferencesService> _logger;

        public IndicatorPreferencesService(ILogger<IndicatorPreferencesService> logger)
        {
            _logger = logger;
            _prefsDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AccessibleTrader", "IndicatorPrefs");
            if (!Directory.Exists(_prefsDir)) Directory.CreateDirectory(_prefsDir);
        }

        // ── Component preferences (backward-compatible with legacy list-only JSON) ──

        public List<ComponentPreference>? GetPreferences(string indicatorCode)
        {
            try
            {
                var path = FilePath(indicatorCode);
                if (!File.Exists(path)) return null;
                var json = File.ReadAllText(path);
                // Try new envelope format first; fall back to legacy bare list.
                var file = TryDeserializeFile(json);
                return file?.Components ?? JsonConvert.DeserializeObject<List<ComponentPreference>>(json);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "IndicatorPrefs: failed to load {IndicatorCode}.", indicatorCode);
                return null;
            }
        }

        public void SavePreferences(string indicatorCode, List<ComponentPreference> prefs)
        {
            try
            {
                var file = LoadFile(indicatorCode) ?? new IndicatorPrefsFile();
                file.Components = prefs;
                WriteFile(indicatorCode, file);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "IndicatorPrefs: failed to save {IndicatorCode}.", indicatorCode);
            }
        }

        public void ClearPreferences(string indicatorCode)
        {
            try
            {
                var path = FilePath(indicatorCode);
                if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "IndicatorPrefs: failed to clear {IndicatorCode}.", indicatorCode);
            }
        }

        // ── Level preferences ──────────────────────────────────────────────────────

        public List<LevelPreference> GetLevelPreferences(string indicatorCode)
        {
            try
            {
                return LoadFile(indicatorCode)?.Levels ?? new();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "IndicatorPrefs: failed to load level prefs for {IndicatorCode}.", indicatorCode);
                return new();
            }
        }

        public void SaveLevelPreference(string indicatorCode, LevelPreference pref)
        {
            try
            {
                var file = LoadFile(indicatorCode) ?? new IndicatorPrefsFile();
                var existing = file.Levels.FirstOrDefault(l => l.Name == pref.Name);
                if (existing != null) file.Levels.Remove(existing);
                file.Levels.Add(pref);
                WriteFile(indicatorCode, file);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "IndicatorPrefs: failed to save level pref for {IndicatorCode}.", indicatorCode);
            }
        }

        // ── Helpers ────────────────────────────────────────────────────────────────

        private IndicatorPrefsFile? LoadFile(string indicatorCode)
        {
            var path = FilePath(indicatorCode);
            if (!File.Exists(path)) return null;
            var json = File.ReadAllText(path);
            return TryDeserializeFile(json);
        }

        private static IndicatorPrefsFile? TryDeserializeFile(string json)
        {
            try
            {
                // Detect envelope format: starts with '{'.
                var trimmed = json.TrimStart();
                if (trimmed.StartsWith('{'))
                    return JsonConvert.DeserializeObject<IndicatorPrefsFile>(json);
            }
            catch { /* fall through */ }
            return null;
        }

        private void WriteFile(string indicatorCode, IndicatorPrefsFile file)
        {
            var json = JsonConvert.SerializeObject(file, Formatting.Indented);
            File.WriteAllText(FilePath(indicatorCode), json);
        }

        private string FilePath(string indicatorCode)
        {
            // Sanitise code to a safe filename (strip non-alphanumeric/underscore chars)
            var safe = System.Text.RegularExpressions.Regex.Replace(indicatorCode, @"[^\w]", "_");
            return Path.Combine(_prefsDir, $"{safe}.json");
        }
    }
}
