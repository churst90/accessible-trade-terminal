using System.Collections.Generic;
using System.Linq;
using AccessibleTrader.Core.Services.Audio;
using AccessibleTrader.Core.Services.Input;
using AccessibleTrader.Sdk.Models;
using Xunit;

namespace AccessibleTrader.Tests;

/// <summary>
/// A reference level a person places must behave like one an indicator declares — visible, audible,
/// styleable, removable, and saved.
///
/// <para>
/// <b>What "half bolted on" meant here.</b> The <c>0</c> key created a level and there the
/// integration stopped. It could not be deleted from anywhere in the application. It made no sound,
/// because the crossing monitor decided which levels to watch by <i>sniffing the name</i> for
/// "Overbought"/"Oversold" and skipped everything else — so the "Play Earcon on Crossing" checkbox
/// in Properties was live UI over a dead code path. Every level was named the same thing, and the
/// audio tracker keys on the name. Restyling one was lost at the next launch, because only the audio
/// fields were saved. And "Reset to defaults" deleted hand-placed levels as collateral.
/// </para>
/// </summary>
public class ReferenceLevelIntegrationTests
{
    // ── Naming: the tracker and the saved preferences both key on it ─────────

    [Fact]
    public void TwoLevelsOnOneSeriesNeverShareAName()
    {
        var levels = new List<LevelConfig>();
        for (int i = 0; i < 4; i++)
        {
            var l = ReferenceLevelPlacement.For("Main", 100 + i, levels, out _);
            Assert.NotNull(l);
            levels.Add(l!);
        }

        Assert.Equal(4, levels.Select(l => l.Name).Distinct(System.StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void UniqueNamingAlsoAvoidsProviderLevelNames()
    {
        var existing = new List<LevelConfig> { new() { Name = "Zero", Value = 0 } };
        var added = ReferenceLevelPlacement.For("Pane_MACD", 0, existing, out _);

        Assert.NotNull(added);
        Assert.NotEqual("Zero", added!.Name);
    }

    // ── Audibility: the point of placing one on an audio-first terminal ──────

    [Fact]
    public void AHandPlacedLevelIsAudibleWithoutTouchingSettings()
    {
        var level = ReferenceLevelPlacement.For("Main", 63_920, null, out _);

        Assert.NotNull(level);
        Assert.True(level!.PlayEarcon, "A level placed deliberately must announce itself being reached.");
        Assert.True(level.IsUserDefined);
        Assert.Equal(LevelCrossDirection.Both, level.CrossDirection);
    }

    /// <summary>
    /// The exact defect: a level whose name is not "Overbought"/"Oversold" was skipped outright.
    /// </summary>
    [Theory]
    [InlineData("Level")]
    [InlineData("Zero")]
    [InlineData("My target")]
    public void AnyNameResolvesToATwoSidedWatchRatherThanSilence(string name)
        => Assert.Equal(LevelCrossDirection.Both,
                        new LevelConfig { Name = name }.EffectiveCrossDirection);

    /// <summary>Provider levels must keep the behaviour they have always had.</summary>
    [Theory]
    [InlineData("Overbought", LevelCrossDirection.Above)]
    [InlineData("Extreme OB", LevelCrossDirection.Above)]
    [InlineData("Oversold", LevelCrossDirection.Below)]
    [InlineData("Extreme OS", LevelCrossDirection.Below)]
    public void ProviderLevelsKeepTheirInferredDirection(string name, LevelCrossDirection expected)
        => Assert.Equal(expected, new LevelConfig { Name = name }.EffectiveCrossDirection);

    [Fact]
    public void AnExplicitDirectionBeatsTheNameInference()
        => Assert.Equal(LevelCrossDirection.Below,
                        new LevelConfig { Name = "Overbought", CrossDirection = LevelCrossDirection.Below }
                            .EffectiveCrossDirection);

    // ── Removal: pressing the key again takes it back ────────────────────────

    [Fact]
    public void PressingTheKeyWhereYourLevelSitsFindsItForRemoval()
    {
        var levels = new List<LevelConfig>();
        var placed = ReferenceLevelPlacement.For("Main", 63_920, levels, out _)!;
        levels.Add(placed);

        var found = ReferenceLevelPlacement.FindRemovable(levels, "Main", 63_920);
        Assert.Same(placed, found);
    }

    /// <summary>
    /// Tolerance is proportional, because this terminal charts both BTC and sub-cent tokens and a
    /// fixed band would be absurd on one of them.
    /// </summary>
    [Fact]
    public void ALevelOnADifferentBarIsLeftAlone()
    {
        var levels = new List<LevelConfig> { new() { Name = "Level", Value = 63_920, IsUserDefined = true } };

        Assert.NotNull(ReferenceLevelPlacement.FindRemovable(levels, "Main", 63_930));   // ~0.016% — same bar
        Assert.Null(ReferenceLevelPlacement.FindRemovable(levels, "Main", 61_000));      // ~4.6%  — elsewhere
    }

    [Fact]
    public void ASubCentLevelUsesTheSameProportionalTolerance()
    {
        var levels = new List<LevelConfig> { new() { Name = "Level", Value = 0.083, IsUserDefined = true } };

        Assert.NotNull(ReferenceLevelPlacement.FindRemovable(levels, "Main", 0.0831));
        Assert.Null(ReferenceLevelPlacement.FindRemovable(levels, "Main", 0.090));
    }

    /// <summary>
    /// A provider's overbought line is part of what the indicator IS. Deleting it because the cursor
    /// happened to sit at 70 would be a considerable surprise.
    /// </summary>
    [Fact]
    public void ProviderLevelsAreNeverRemovedByTheKey()
    {
        var levels = new List<LevelConfig> { new() { Name = "Overbought", Value = 70, IsUserDefined = false } };
        Assert.Null(ReferenceLevelPlacement.FindRemovable(levels, "Pane_RSI", 0));
    }

}
