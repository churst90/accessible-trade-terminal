using System.Linq;
using AccessibleTrader.Sdk.Models;
using Xunit;

namespace AccessibleTrader.Tests;

/// <summary>
/// SoundPatch / OscillatorLayer model — the multi-oscillator additions and their backward
/// compatibility with legacy single-waveform patches (old patches.json / imported JSON).
/// </summary>
public class SoundPatchModelTests
{
    [Fact]
    public void EffectiveLayers_EmptyOscillators_SynthesizesSingleLayerFromLegacyFields()
    {
        var patch = new SoundPatch { Waveform = "square", NoiseAmount = 0.4f }; // no Oscillators

        var layers = patch.EffectiveLayers();

        Assert.Single(layers);
        Assert.Equal("square", layers[0].Waveform);
        Assert.Equal(0.4f, layers[0].NoiseAmount);
        Assert.Equal(1f, layers[0].Gain);
        Assert.Equal(1.0, layers[0].FreqRatio);
    }

    [Fact]
    public void EffectiveLayers_WithOscillators_ReturnsThemVerbatim()
    {
        var patch = new SoundPatch();
        patch.Oscillators.Add(new OscillatorLayer { Waveform = "sine", Gain = 1f, FreqRatio = 1.0 });
        patch.Oscillators.Add(new OscillatorLayer { Waveform = "triangle", Gain = 0.5f, FreqRatio = 2.0 });

        var layers = patch.EffectiveLayers();

        Assert.Equal(2, layers.Count);
        Assert.Equal("triangle", layers[1].Waveform);
        Assert.Equal(2.0, layers[1].FreqRatio);
    }

    [Fact]
    public void Clone_GetsNewId_AndDeepCopiesLayers()
    {
        var patch = new SoundPatch { Name = "Bell" };
        patch.Oscillators.Add(new OscillatorLayer { Waveform = "sine", Gain = 1f });

        var clone = patch.Clone();
        clone.Oscillators[0].Waveform = "square"; // mutate the clone

        Assert.NotEqual(patch.Id, clone.Id);
        Assert.Equal("sine", patch.Oscillators[0].Waveform); // original untouched → deep copy
        Assert.Equal("square", clone.Oscillators[0].Waveform);
        Assert.Contains("copy", clone.Name);
    }

    [Fact]
    public void Clone_PreservesLegacyScalarFields()
    {
        var patch = new SoundPatch
        {
            Waveform = "sawtooth", NoiseAmount = 0.2f, BaseFrequency = 660, FreqMultiplier = 1.5,
            Volume = 0.8f, EnvelopeType = "Ping", DurationSeconds = 0.5, Description = "d",
        };

        var clone = patch.Clone();

        Assert.Equal("sawtooth", clone.Waveform);
        Assert.Equal(0.2f, clone.NoiseAmount);
        Assert.Equal(660, clone.BaseFrequency);
        Assert.Equal("Ping", clone.EnvelopeType);
        Assert.Equal(0.5, clone.DurationSeconds);
    }
}
