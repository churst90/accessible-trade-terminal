using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using AccessibleTrader.Sdk.Theming;
using AccessibleTrader.Sdk.Interfaces;
using Microsoft.Extensions.Logging;

namespace AccessibleTrader.Core.Services.Theming
{
    /// <summary>
    /// Stores the user's own themes.
    /// </summary>
    public interface IThemeLibrary
    {
        IReadOnlyList<ThemePreset> All { get; }
        ThemePreset? GetById(string id);

        /// <summary>Inserts or replaces by id, then saves.</summary>
        void Upsert(ThemePreset preset);

        void Remove(string id);
        void Save();
        void Reload();

        /// <summary>Serialises one theme for sharing — the text a user hands to someone else.</summary>
        string Export(ThemePreset preset);

        /// <summary>
        /// Reads a shared theme. Returns null on anything unparseable rather than throwing, because
        /// this is fed by paste and by file picker, and both routinely receive the wrong thing.
        /// </summary>
        ThemePreset? Import(string json);
    }

    /// <summary>
    /// JSON-on-disk theme storage (<c>themes.json</c>), alongside watchlists and screeners.
    ///
    /// <para>
    /// Themes are saved as a base plus the colours the user actually changed. That sparseness is
    /// the point: a theme saved today keeps working when a new themeable colour is added later,
    /// where a full snapshot would freeze the palette at the moment it was written and deliver
    /// every future field as black.
    /// </para>
    /// </summary>
    public class JsonThemeLibrary : IThemeLibrary
    {
        private readonly string _filepath;
        private readonly ILogger<JsonThemeLibrary>? _logger;
        private List<ThemePreset> _presets = new();

        private static readonly JsonSerializerOptions Options = new()
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,   // a null override is meaningful
            Converters = { new JsonStringEnumConverter() },
        };

        public JsonThemeLibrary(IPlatformPathService pathService, ILogger<JsonThemeLibrary>? logger = null)
        {
            _logger = logger;
            _filepath = Path.Combine(pathService.AppDataDirectory, "themes.json");
            Reload();
        }

        public IReadOnlyList<ThemePreset> All => _presets;

        public ThemePreset? GetById(string id) =>
            string.IsNullOrEmpty(id) ? null : _presets.FirstOrDefault(p => p.Id == id);

        public void Upsert(ThemePreset preset)
        {
            ArgumentNullException.ThrowIfNull(preset);
            int i = _presets.FindIndex(p => p.Id == preset.Id);
            if (i >= 0) _presets[i] = preset; else _presets.Add(preset);
            Save();
        }

        public void Remove(string id)
        {
            _presets.RemoveAll(p => p.Id == id);
            Save();
        }

        public void Save()
        {
            try
            {
                File.WriteAllText(_filepath, JsonSerializer.Serialize(_presets, Options));
            }
            catch (Exception ex)
            {
                // A theme is cosmetic. Losing one to a read-only directory must not take the
                // application down with it — but it must not pass silently either.
                _logger?.LogWarning(ex, "Could not save themes to {Path}", _filepath);
            }
        }

        public void Reload()
        {
            try
            {
                _presets = File.Exists(_filepath)
                    ? JsonSerializer.Deserialize<List<ThemePreset>>(File.ReadAllText(_filepath), Options) ?? new()
                    : new();
            }
            catch (Exception ex)
            {
                // A corrupt file must not wedge startup. Start empty and say why; the file is left
                // on disk untouched so it can be inspected rather than silently rewritten.
                _logger?.LogWarning(ex, "themes.json could not be read; starting with no custom themes");
                _presets = new();
            }
        }

        public string Export(ThemePreset preset) => JsonSerializer.Serialize(preset, Options);

        public ThemePreset? Import(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try
            {
                var preset = JsonSerializer.Deserialize<ThemePreset>(json, Options);
                if (preset == null || string.IsNullOrWhiteSpace(preset.Name)) return null;

                // A fresh id on every import, so bringing in a theme never overwrites one of the
                // user's own that happens to share an id.
                return preset with { Id = Guid.NewGuid().ToString("N") };
            }
            catch (Exception ex)
            {
                _logger?.LogInformation(ex, "Rejected an unparseable theme import");
                return null;
            }
        }
    }
}
