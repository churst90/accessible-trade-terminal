using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Strategies;
using AccessibleTrader.Core.Services;

namespace AccessibleTrader.Core.Services.Strategies
{
    /// <summary>
    /// Result of a library operation — carries a user-facing message plus an ok/error flag.
    /// Keeps the UI from having to reinvent error-formatting each time it touches the library.
    /// </summary>
    public readonly record struct StrategyLibraryResult(bool IsSuccess, string Message)
    {
        public static StrategyLibraryResult Ok(string message) => new(true, message);
        public static StrategyLibraryResult Error(string message) => new(false, message);
    }

    /// <summary>
    /// Save/delete/engine-registration/export/import orchestration for
    /// <see cref="EditableStrategySpec"/>. Consolidates the logic that used to live in
    /// <c>BuildSetupTab.razor</c> so the component only has to wire buttons to method calls
    /// and display the returned message. Validation runs up-front; the facade never calls
    /// <see cref="IStrategyLibrary.Upsert"/> with a spec that
    /// <see cref="StrategySpecValidator.ValidateForSave"/> would reject.
    /// </summary>
    public sealed class StrategyLibraryFacade : IStrategyLibraryFacade
    {
        private readonly IStrategyLibrary _library;
        private readonly IConfigurableStrategyFactory _factory;
        private readonly IStrategyEngine _engine;
        private readonly IPlatformPathService _paths;

        public StrategyLibraryFacade(
            IStrategyLibrary library,
            IConfigurableStrategyFactory factory,
            IStrategyEngine engine,
            IPlatformPathService paths)
        {
            _library = library;
            _factory = factory;
            _engine = engine;
            _paths = paths;
        }

        public StrategyLibraryResult Save(EditableStrategySpec spec)
        {
            var validationError = StrategySpecValidator.ValidateForSave(spec);
            if (validationError != null) return StrategyLibraryResult.Error(validationError);

            try
            {
                var s = spec.ToSpec();
                _library.Upsert(s);
                spec.LoadedId = s.Id;
                var advisory = StrategySpecValidator.BuildPulseOnlyAdvisory(spec);
                return StrategyLibraryResult.Ok(advisory == null
                    ? $"Saved '{s.Name}'."
                    : $"Saved '{s.Name}'. {advisory}");
            }
            catch (Exception ex)
            {
                return StrategyLibraryResult.Error($"Save failed: {ex.Message}");
            }
        }

        public StrategyLibraryResult Delete(string loadedId)
        {
            if (string.IsNullOrEmpty(loadedId)) return StrategyLibraryResult.Error("No spec loaded.");
            _library.Remove(loadedId);
            return StrategyLibraryResult.Ok("Deleted.");
        }

        /// <summary>
        /// Builds a spec with <c>IsAutoActivate=true</c>, upserts it, removes any live
        /// duplicate instance in the engine (the user is editing + re-adding), then registers
        /// a fresh <see cref="AccessibleTrader.Core.Strategies.ConfigurableStrategy"/>.
        /// </summary>
        public StrategyLibraryResult AddToEngine(EditableStrategySpec spec)
        {
            var validationError = StrategySpecValidator.ValidateForSave(spec);
            if (validationError != null) return StrategyLibraryResult.Error(validationError);

            try
            {
                var s = spec.ToSpec() with { IsAutoActivate = true };
                _library.Upsert(s);
                spec.LoadedId = s.Id;

                // Duplicate-add guard: if the engine already has an instance of this spec
                // (the user is editing + re-adding) remove the old instance first so we
                // don't end up running two copies side by side. Match by Strategy.Id == spec.Id.
                var existing = _engine.ActiveStrategies
                    .Where(a => string.Equals(a.Strategy.Id, s.Id, StringComparison.OrdinalIgnoreCase))
                    .Select(a => a.InstanceId)
                    .ToList();
                foreach (var id in existing)
                    _engine.RemoveStrategy(id);

                var strategy = _factory.Create(s);
                _engine.AddStrategy(strategy, new Dictionary<string, object>(), s.ExecutionMode);

                var advisory = StrategySpecValidator.BuildPulseOnlyAdvisory(spec);
                string baseMsg = existing.Count > 0
                    ? $"'{s.Name}' updated in engine ({s.ExecutionMode}). Will auto-activate on restart."
                    : $"'{s.Name}' added to engine ({s.ExecutionMode}). Will auto-activate on restart.";
                return StrategyLibraryResult.Ok(advisory == null ? baseMsg : $"{baseMsg} {advisory}");
            }
            catch (Exception ex)
            {
                return StrategyLibraryResult.Error($"Add failed: {ex.Message}");
            }
        }

        /// <summary>Exports the current spec to a .atstrat file in {AppData}/exports/.</summary>
        public StrategyLibraryResult Export(EditableStrategySpec spec)
        {
            try
            {
                var s = spec.ToSpec();
                string dir = Path.Combine(_paths.AppDataDirectory, "exports");
                Directory.CreateDirectory(dir);
                string safeName = new string(s.Name.Where(c => char.IsLetterOrDigit(c) || c == '_' || c == '-').ToArray());
                if (string.IsNullOrEmpty(safeName)) safeName = s.Id;
                string path = Path.Combine(dir, $"{safeName}.atstrat");

                var options = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
                };
                AtomicFile.WriteAllText(path, JsonSerializer.Serialize(s, options));
                return StrategyLibraryResult.Ok($"Exported to {path}");
            }
            catch (Exception ex)
            {
                return StrategyLibraryResult.Error($"Export failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Imports the most-recently-modified .atstrat file from {AppData}/exports/ and loads
        /// it into the supplied editable spec. Does NOT save the imported spec to the library
        /// automatically — the UI exposes a separate Save button so the user can review before
        /// persisting.
        /// </summary>
        public StrategyLibraryResult Import(EditableStrategySpec spec)
        {
            try
            {
                string dir = Path.Combine(_paths.AppDataDirectory, "exports");
                if (!Directory.Exists(dir))
                    return StrategyLibraryResult.Error("No exports folder yet — nothing to import.");
                var files = Directory.GetFiles(dir, "*.atstrat");
                if (files.Length == 0)
                    return StrategyLibraryResult.Error("No .atstrat files found in exports folder.");

                string latest = files.OrderByDescending(File.GetLastWriteTime).First();
                string json = File.ReadAllText(latest);
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var imported = JsonSerializer.Deserialize<StrategySpec>(json, options)
                    ?? throw new InvalidOperationException("Deserialization returned null.");
                spec.LoadFromSpec(imported);
                return StrategyLibraryResult.Ok(
                    $"Imported from {Path.GetFileName(latest)}. Click Save Spec to add to library.");
            }
            catch (Exception ex)
            {
                return StrategyLibraryResult.Error($"Import failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Loads a spec from the library into the supplied editable instance. Returns a
        /// success message when found, an error when the id doesn't resolve.
        /// </summary>
        public StrategyLibraryResult LoadFromLibrary(EditableStrategySpec spec, string id)
        {
            var source = _library.GetById(id);
            if (source == null) return StrategyLibraryResult.Error($"Spec '{id}' not found in library.");
            spec.LoadFromSpec(source);
            return StrategyLibraryResult.Ok($"Loaded '{source.Name}'.");
        }
    }

    public interface IStrategyLibraryFacade
    {
        StrategyLibraryResult Save(EditableStrategySpec spec);
        StrategyLibraryResult Delete(string loadedId);
        StrategyLibraryResult AddToEngine(EditableStrategySpec spec);
        StrategyLibraryResult Export(EditableStrategySpec spec);
        StrategyLibraryResult Import(EditableStrategySpec spec);
        StrategyLibraryResult LoadFromLibrary(EditableStrategySpec spec, string id);
    }
}
