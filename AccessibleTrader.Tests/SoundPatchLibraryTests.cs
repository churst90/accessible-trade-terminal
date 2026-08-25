using AccessibleTrader.Core.Services;
using AccessibleTrader.Sdk.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace AccessibleTrader.Tests;

/// <summary>
/// SoundPatchLibrary — CRUD, JSON export/import (must preserve multi-oscillator layers),
/// on-disk persistence across instances, and earcon-override round-tripping.
/// </summary>
public class SoundPatchLibraryTests : IDisposable
{
    private readonly string _dir;
    private readonly SoundPatchLibrary _lib;

    public SoundPatchLibraryTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "atc-patchlib-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _lib = NewLibrary();
    }

    private SoundPatchLibrary NewLibrary() =>
        new(new FakePathService(_dir), NullLogger<SoundPatchLibrary>.Instance);

    public void Dispose() { try { Directory.Delete(_dir, true); } catch { /* best effort */ } }

    private static SoundPatch MultiLayer()
    {
        var p = new SoundPatch { Name = "Bell" };
        p.Oscillators.Add(new OscillatorLayer { Waveform = "sine", Gain = 1f, FreqRatio = 1.0, NoiseAmount = 0.1f, NoiseType = "white" });
        p.Oscillators.Add(new OscillatorLayer { Waveform = "triangle", Gain = 0.5f, FreqRatio = 2.0 });
        return p;
    }

    [Fact]
    public void AddGetUpdateRemove_Roundtrips()
    {
        var p = new SoundPatch { Name = "A" };
        _lib.AddPatch(p);
        Assert.Equal("A", _lib.GetPatch(p.Id)?.Name);

        var updated = new SoundPatch { Id = p.Id, Name = "B" };
        _lib.UpdatePatch(updated);
        Assert.Equal("B", _lib.GetPatch(p.Id)?.Name);

        _lib.RemovePatch(p.Id);
        Assert.Null(_lib.GetPatch(p.Id));
    }

    [Fact]
    public void GetPatch_NullOrUnknownId_ReturnsNull()
    {
        Assert.Null(_lib.GetPatch(null));
        Assert.Null(_lib.GetPatch("does-not-exist"));
    }

    [Fact]
    public void ExportImport_PreservesOscillatorLayers()
    {
        var patch = MultiLayer();
        var imported = _lib.ImportPatchJson(_lib.ExportPatchJson(patch));

        Assert.NotNull(imported);
        Assert.Equal(2, imported!.Oscillators.Count);
        Assert.Equal("triangle", imported.Oscillators[1].Waveform);
        Assert.Equal(2.0, imported.Oscillators[1].FreqRatio);
        Assert.Equal("white", imported.Oscillators[0].NoiseType);
    }

    [Fact]
    public void ImportPatchJson_AssignsFreshId()
    {
        var patch = MultiLayer();
        var imported = _lib.ImportPatchJson(_lib.ExportPatchJson(patch));
        Assert.NotEqual(patch.Id, imported!.Id);
    }

    [Fact]
    public void ImportPatchJson_InvalidJson_ReturnsNull()
        => Assert.Null(_lib.ImportPatchJson("{ not valid json"));

    [Fact]
    public void Persistence_SurvivesReload_WithLayers()
    {
        var patch = MultiLayer();
        _lib.AddPatch(patch); // AddPatch persists via SavePatches

        var got = NewLibrary().GetPatch(patch.Id); // fresh instance re-reads patches.json
        Assert.NotNull(got);
        Assert.Equal(2, got!.Oscillators.Count);
        Assert.Equal("triangle", got.Oscillators[1].Waveform);
    }

    [Fact]
    public void LegacyPatch_WithoutOscillators_StillLoads()
    {
        // A pre-multi-oscillator patch (no Oscillators array) must round-trip and yield a single
        // effective layer from the legacy Waveform field.
        var legacy = new SoundPatch { Name = "old", Waveform = "sawtooth" }; // Oscillators empty
        _lib.AddPatch(legacy);

        var got = NewLibrary().GetPatch(legacy.Id);
        Assert.NotNull(got);
        Assert.Empty(got!.Oscillators);
        Assert.Single(got.EffectiveLayers());
        Assert.Equal("sawtooth", got.EffectiveLayers()[0].Waveform);
    }

    [Fact]
    public void EarconOverrides_SaveAndReload()
    {
        _lib.EarconOverrides.EarconPatchIds["Boundary"] = "patch-id-1";
        _lib.SaveEarconOverrides();

        var reloaded = NewLibrary();
        Assert.Equal("patch-id-1", reloaded.EarconOverrides.EarconPatchIds.GetValueOrDefault("Boundary"));
    }

    private sealed class FakePathService : IPlatformPathService
    {
        public FakePathService(string dir) { AppDataDirectory = dir; CacheDirectory = dir; }
        public string AppDataDirectory { get; }
        public string CacheDirectory { get; }
    }
}
