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
    /// A user's saved overrides for one reference level, applied on top of the provider's defaults
    /// when a series is created.
    ///
    /// <para>
    /// Every field is nullable and every null means "leave the provider's value alone", so adding a
    /// field here never disturbs a preference file written before it existed.
    /// </para>
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

        // ── Appearance ──────────────────────────────────────────────────────
        // The renderer has always honoured all three; only the audio settings were ever saved, so a
        // restyled level reverted to grey dashes at the next launch.
        public string?  ColorHex  { get; set; }
        public float?   Thickness { get; set; }
        public DashStyle? DashStyle { get; set; }

        /// <summary>Which crossings this level reports. Null keeps <c>Auto</c> name inference.</summary>
        public LevelCrossDirection? CrossDirection { get; set; }
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

        /// <summary>
        /// Preferences live under <see cref="IPlatformPathService.AppDataDirectory"/>, which is the
        /// PER-USER directory when hosted accounts are enabled.
        ///
        /// <para>
        /// This class used to build its own path from
        /// <c>Environment.GetFolderPath(LocalApplicationData)</c> — the same two defects
        /// <see cref="WorkspaceLibraryService"/> had. Every hosted user shared one set of indicator
        /// preferences, so restyling an indicator changed it for everyone; and on Unix the path
        /// could resolve RELATIVE (see <see cref="PlatformPaths"/>), landing inside the deployment
        /// directory that a redeploy replaces.
        /// </para>
        ///
        /// <para>Desktop/local is unaffected: there <c>AppDataDirectory</c> is
        /// <c>~/.local/share/AccessibleTrader</c>, so the directory is exactly where it was.</para>
        /// </summary>
        public IndicatorPreferencesService(ILogger<IndicatorPreferencesService> logger,
            IPlatformPathService paths)
        {
            _logger = logger;
            _prefsDir = Path.Combine(paths.AppDataDirectory, "IndicatorPrefs");
            Directory.CreateDirectory(_prefsDir);
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
                // Preserve the corrupt original; the next SavePreferences for this
                // indicator would otherwise overwrite it with fresh defaults.
                CorruptFileQuarantine.MoveAside(FilePath(indicatorCode), ex);
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
            AtomicFile.WriteAllText(FilePath(indicatorCode), json);
        }

        private string FilePath(string indicatorCode)
        {
            // Sanitise code to a safe filename (strip non-alphanumeric/underscore chars)
            var safe = System.Text.RegularExpressions.Regex.Replace(indicatorCode, @"[^\w]", "_");
            return Path.Combine(_prefsDir, $"{safe}.json");
        }
    }
}
