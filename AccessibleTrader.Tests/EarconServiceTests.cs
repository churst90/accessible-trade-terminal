using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Sdk.Models;
using NSubstitute;
using Xunit;

namespace AccessibleTrader.Tests;

/// <summary>
/// EarconService patch routing: an earcon with an assigned Sound Designer patch plays through
/// PlayPatch (so multi-oscillator earcons render fully); with no assignment it falls back to
/// the built-in default via PlayNote.
/// </summary>
public class EarconServiceTests
{
    private static (ISonificationManager Sonify, ISoundPatchLibrary Lib) Deps(EarconSettings overrides)
    {
        var sonify = Substitute.For<ISonificationManager>();
        sonify.IsEnabled.Returns(true);
        var lib = Substitute.For<ISoundPatchLibrary>();
        lib.EarconOverrides.Returns(overrides);
        return (sonify, lib);
    }

    [Fact]
    public void PlayBoundary_WithAssignedPatch_RoutesThroughPlayPatch()
    {
        var patch = new SoundPatch { Name = "custom" };
        var overrides = new EarconSettings();
        overrides.EarconPatchIds["Boundary"] = patch.Id;
        var (sonify, lib) = Deps(overrides);
        lib.GetPatch(patch.Id).Returns(patch);

        new EarconService(sonify, lib).PlayBoundary();

        sonify.Received(1).PlayPatch(patch, Arg.Any<float>(), Arg.Any<float>(), Arg.Any<bool>());
        sonify.DidNotReceive().PlayNote(Arg.Any<double>(), Arg.Any<double>(), Arg.Any<string>(),
            Arg.Any<float>(), Arg.Any<float>(), Arg.Any<double>(), Arg.Any<bool>());
    }

    [Fact]
    public void PlayBoundary_NoAssignment_FallsBackToPlayNote()
    {
        var (sonify, lib) = Deps(new EarconSettings()); // no overrides

        new EarconService(sonify, lib).PlayBoundary();

        sonify.Received().PlayNote(Arg.Any<double>(), Arg.Any<double>(), Arg.Any<string>(),
            Arg.Any<float>(), Arg.Any<float>(), Arg.Any<double>(), Arg.Any<bool>());
        sonify.DidNotReceive().PlayPatch(Arg.Any<SoundPatch>(), Arg.Any<float>(), Arg.Any<float>(), Arg.Any<bool>());
    }

    [Fact]
    public void PlayNewBar_WithAssignedPatch_RoutesThroughPlayPatch()
    {
        var patch = new SoundPatch { Name = "bar" };
        var overrides = new EarconSettings();
        overrides.EarconPatchIds["NewBar"] = patch.Id;
        var (sonify, lib) = Deps(overrides);
        lib.GetPatch(patch.Id).Returns(patch);

        new EarconService(sonify, lib).PlayNewBar();

        sonify.Received(1).PlayPatch(patch, Arg.Any<float>(), Arg.Any<float>(), Arg.Any<bool>());
    }
}
