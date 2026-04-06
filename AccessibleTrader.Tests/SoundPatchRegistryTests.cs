using System;
using Xunit;
using AccessibleTrader.Core.Services.Audio;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Tests for <see cref="SoundPatchRegistry"/> — built-in patch presence,
    /// custom registration, replacement, and key patch property invariants.
    /// </summary>
    public class SoundPatchRegistryTests
    {
        // ── 1. Built-in patches are pre-registered on construction ───────────

        [Theory]
        [InlineData("sine_bell")]
        [InlineData("triangle_bell")]
        [InlineData("crystal_bell")]
        [InlineData("detuned_pair_bell")]
        [InlineData("gradient_blend")]
        [InlineData("dual_tone_bell")]
        public void TryGetPatch_BuiltIn_ReturnsTrue(string patchId)
        {
            var registry = new SoundPatchRegistry();
            bool found = registry.TryGetPatch(patchId, out _);
            Assert.True(found, $"Built-in patch '{patchId}' should be pre-registered.");
        }

        // ── 2. Unknown patch returns false ────────────────────────────────────

        [Fact]
        public void TryGetPatch_UnknownId_ReturnsFalse()
        {
            var registry = new SoundPatchRegistry();
            bool found = registry.TryGetPatch("nonexistent_patch_xyz", out var patch);
            Assert.False(found);
            Assert.Null(patch);
        }

        // ── 3. Custom patch can be registered and retrieved ───────────────────

        [Fact]
        public void Register_CustomPatch_CanBeRetrieved()
        {
            var registry = new SoundPatchRegistry();
            var custom = new SoundPatch(
                BaseWaveform: "square",
                HarmonicAmount: 0.5f,
                HarmonicFreqMultiplier: 3.0f,
                DefaultDecayMs: 400,
                IsDetuned: false,
                DetuneIntervalHz: 0f,
                DetunedOffsetMs: 0,
                GradientWaveformA: null,
                GradientWaveformB: null);

            registry.Register("custom_test", custom);
            bool found = registry.TryGetPatch("custom_test", out var retrieved);

            Assert.True(found);
            Assert.NotNull(retrieved);
            Assert.Equal("square", retrieved!.BaseWaveform);
            Assert.Equal(400, retrieved.DefaultDecayMs);
        }

        // ── 4. Registered patch replaces existing ────────────────────────────

        [Fact]
        public void Register_ReplacesExistingPatch()
        {
            var registry = new SoundPatchRegistry();

            // Verify original sine_bell exists and read original decay
            registry.TryGetPatch("sine_bell", out var original);
            Assert.NotNull(original);
            int originalDecay = original!.DefaultDecayMs;

            // Replace with custom values
            var replacement = new SoundPatch(
                BaseWaveform: "sawtooth",
                HarmonicAmount: 0.0f,
                HarmonicFreqMultiplier: 2.0f,
                DefaultDecayMs: originalDecay + 1000,  // guaranteed different
                IsDetuned: false,
                DetuneIntervalHz: 0f,
                DetunedOffsetMs: 0,
                GradientWaveformA: null,
                GradientWaveformB: null);

            registry.Register("sine_bell", replacement);
            registry.TryGetPatch("sine_bell", out var updated);

            Assert.NotNull(updated);
            Assert.Equal("sawtooth", updated!.BaseWaveform);
            Assert.Equal(originalDecay + 1000, updated.DefaultDecayMs);
        }

        // ── 5. detuned_pair_bell is IsDetuned=true with non-zero DetuneIntervalHz ──

        [Fact]
        public void DetunedPairBell_HasIsDetunedTrue_AndNonZeroDetuneInterval()
        {
            var registry = new SoundPatchRegistry();
            registry.TryGetPatch("detuned_pair_bell", out var patch);

            Assert.NotNull(patch);
            Assert.True(patch!.IsDetuned, "detuned_pair_bell must have IsDetuned=true.");
            Assert.True(patch.DetuneIntervalHz > 0f,
                $"detuned_pair_bell must have DetuneIntervalHz > 0 (got {patch.DetuneIntervalHz}).");
        }

        // ── 6. gradient_blend has non-null GradientWaveformA and GradientWaveformB ──

        [Fact]
        public void GradientBlend_HasNonNullGradientWaveforms()
        {
            var registry = new SoundPatchRegistry();
            registry.TryGetPatch("gradient_blend", out var patch);

            Assert.NotNull(patch);
            Assert.NotNull(patch!.GradientWaveformA);
            Assert.NotNull(patch.GradientWaveformB);
            Assert.False(string.IsNullOrEmpty(patch.GradientWaveformA));
            Assert.False(string.IsNullOrEmpty(patch.GradientWaveformB));
        }

        // ── 7. dual_tone_bell has DetunedOffsetMs=0 (simultaneous) and IsDetuned=true ──

        [Fact]
        public void DualToneBell_HasDetunedOffsetMsZero_AndIsDetunedTrue()
        {
            var registry = new SoundPatchRegistry();
            registry.TryGetPatch("dual_tone_bell", out var patch);

            Assert.NotNull(patch);
            Assert.True(patch!.IsDetuned, "dual_tone_bell must have IsDetuned=true.");
            Assert.Equal(0, patch.DetunedOffsetMs);
        }
    }
}
