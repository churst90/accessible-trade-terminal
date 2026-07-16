using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Logging;

namespace AccessibleTrader.Core.Services.Audio
{
    public interface IWavetableLibrary
    {
        /// <summary>Ids of imported single-cycle wavetables (usable as waveform "wavetable:{id}").</summary>
        IReadOnlyList<string> WavetableIds { get; }
        /// <summary>Ids of imported one-shot samples (usable as waveform "sample:{id}").</summary>
        IReadOnlyList<string> SampleIds { get; }

        /// <summary>
        /// Imports a WAV file. Short files (≤ <see cref="WavetableLibraryService.WavetableMaxFrames"/>
        /// frames — AKWF single cycles are ~600) register as WAVETABLES; longer files as one-shot
        /// SAMPLES. Returns a user-speakable result message; success also persists the file so the
        /// import survives restarts.
        /// </summary>
        (bool Ok, string Message, string? Id) Import(string fileName, byte[] bytes);

        /// <summary>Removes an imported wavetable or sample (file + registration).</summary>
        void Remove(string id);
    }

    /// <summary>
    /// Persistence + startup loading for user audio material. Imported WAVs are
    /// copied verbatim into AppData/wavetables/ or AppData/samples/ and re-parsed
    /// into <see cref="WavetableBank"/> on every launch, so patches referencing
    /// "wavetable:{id}" / "sample:{id}" keep working across restarts.
    /// </summary>
    public sealed class WavetableLibraryService : IWavetableLibrary
    {
        /// <summary>Files at or under this many frames import as single-cycle wavetables.
        /// AKWF tables are 600 frames; anything under ~1/10 s is clearly not a clip.</summary>
        public const int WavetableMaxFrames = 4096;

        private readonly string _wavetableDir;
        private readonly string _sampleDir;
        private readonly ILogger<WavetableLibraryService> _logger;
        private readonly List<string> _wavetableIds = new();
        private readonly List<string> _sampleIds = new();
        private readonly object _gate = new();

        public WavetableLibraryService(IPlatformPathService paths, ILogger<WavetableLibraryService> logger)
        {
            _logger = logger;
            _wavetableDir = Path.Combine(paths.AppDataDirectory, "wavetables");
            _sampleDir = Path.Combine(paths.AppDataDirectory, "samples");
            LoadAll();
        }

        public IReadOnlyList<string> WavetableIds { get { lock (_gate) return _wavetableIds.ToList(); } }
        public IReadOnlyList<string> SampleIds { get { lock (_gate) return _sampleIds.ToList(); } }

        private void LoadAll()
        {
            LoadDir(_wavetableDir, asWavetable: true);
            LoadDir(_sampleDir, asWavetable: false);
        }

        private void LoadDir(string dir, bool asWavetable)
        {
            try
            {
                if (!Directory.Exists(dir)) return;
                foreach (var file in Directory.EnumerateFiles(dir, "*.wav"))
                {
                    try
                    {
                        var bytes = File.ReadAllBytes(file);
                        if (!WavFileReader.TryParse(bytes, out var mono, out int rate, out var err))
                        {
                            _logger.LogWarning("Skipping unreadable {Kind} {File}: {Error}",
                                asWavetable ? "wavetable" : "sample", file, err);
                            continue;
                        }
                        string id = Path.GetFileNameWithoutExtension(file);
                        Register(id, mono, rate, asWavetable);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to load {File}", file);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to scan {Dir}", dir);
            }
        }

        private void Register(string id, float[] mono, int rate, bool asWavetable)
        {
            lock (_gate)
            {
                if (asWavetable)
                {
                    WavetableBank.RegisterWavetable(id, mono);
                    if (!_wavetableIds.Contains(id, StringComparer.OrdinalIgnoreCase)) _wavetableIds.Add(id);
                }
                else
                {
                    WavetableBank.RegisterSample(id, mono, rate);
                    if (!_sampleIds.Contains(id, StringComparer.OrdinalIgnoreCase)) _sampleIds.Add(id);
                }
            }
        }

        public (bool Ok, string Message, string? Id) Import(string fileName, byte[] bytes)
        {
            if (!WavFileReader.TryParse(bytes, out var mono, out int rate, out var error))
                return (false, error, null);

            bool asWavetable = mono.Length <= WavetableMaxFrames;
            string id = SanitizeId(Path.GetFileNameWithoutExtension(fileName));
            if (string.IsNullOrEmpty(id)) id = asWavetable ? "wavetable" : "sample";

            try
            {
                string dir = asWavetable ? _wavetableDir : _sampleDir;
                Directory.CreateDirectory(dir);
                // De-dupe id against existing files ("bell", "bell_2", "bell_3", …).
                string finalId = id;
                int n = 2;
                while (File.Exists(Path.Combine(dir, finalId + ".wav"))) finalId = $"{id}_{n++}";
                File.WriteAllBytes(Path.Combine(dir, finalId + ".wav"), bytes);
                Register(finalId, mono, rate, asWavetable);

                double seconds = (double)mono.Length / Math.Max(1, rate);
                string msg = asWavetable
                    ? $"Imported wavetable {finalId}: {mono.Length} samples, one cycle. Use it as an oscillator waveform."
                    : $"Imported sample {finalId}: {seconds:F1} seconds. Use it as a one-shot layer or earcon.";
                return (true, msg, finalId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Import failed for {File}", fileName);
                return (false, $"Import failed: {ex.Message}", null);
            }
        }

        public void Remove(string id)
        {
            lock (_gate)
            {
                _wavetableIds.RemoveAll(x => string.Equals(x, id, StringComparison.OrdinalIgnoreCase));
                _sampleIds.RemoveAll(x => string.Equals(x, id, StringComparison.OrdinalIgnoreCase));
            }
            WavetableBank.Remove(id);
            foreach (var dir in new[] { _wavetableDir, _sampleDir })
            {
                try
                {
                    string path = Path.Combine(dir, id + ".wav");
                    if (File.Exists(path)) File.Delete(path);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to delete {Id} from {Dir}", id, dir);
                }
            }
        }

        private static string SanitizeId(string name)
        {
            var chars = name.Where(c => char.IsLetterOrDigit(c) || c is '_' or '-').ToArray();
            return new string(chars);
        }
    }
}
