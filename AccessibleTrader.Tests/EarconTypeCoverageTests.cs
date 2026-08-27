using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Audio;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Tests.Mocks;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AccessibleTrader.Tests;

/// <summary>
/// <b>No member of <see cref="EarconType"/> may mean silence, and none may reach the router by
/// accident.</b>
///
/// <para>
/// <see cref="FeedbackTypeCoverageTests"/> enumerates the enum one rung further down; this is the
/// same rule for the one above it. <c>EarconType</c> is what CALLERS name — an order fill, a stop
/// hit, a boundary, a series muted — and <c>GlobalErrorCoordinator.PlayEarcon</c> folds its
/// sixteen members onto the six <c>FeedbackType</c> values the audio router understands. Both
/// halves of that fold have shipped broken: <c>Boundary</c> mapped to <c>Navigation</c>, which was
/// the wrong sound AND was silent, because <c>Navigation</c> had no arm in the router at the time.
/// One bug hid the other.
/// </para>
///
/// <para>
/// Two different failures are possible here and they need two different mechanisms, so both are
/// below. A member can be silent, which is a question about behaviour and is answered by driving
/// the real coordinator, the real router and the real <see cref="EarconService"/> and watching for
/// a voice command at the driver. A member can also be UNROUTED — added to the enum and never
/// given an arm — which behaviour cannot answer, because the mapping's <c>_ =&gt;</c> default
/// catches it and produces a plausible-sounding Info blip. Nothing is audibly wrong; the earcon is
/// simply not the one the caller asked for. That one is answered by counting the arms.
/// </para>
/// </summary>
public sealed class EarconTypeCoverageTests
{
    private static readonly EarconType[] AllTypes = Enum.GetValues<EarconType>();

    public static TheoryData<EarconType> EveryType()
    {
        var d = new TheoryData<EarconType>();
        foreach (var t in AllTypes) d.Add(t);
        return d;
    }

    // ── Behaviour: every member makes a sound ───────────────────────────────────────

    /// <summary>
    /// The full production chain, with only the sonifier replaced. Nothing here decides what a
    /// member should sound like — it decides whether it sounds at all.
    /// </summary>
    private static (IGlobalErrorCoordinator Coordinator, ISonificationManager Sonifier) Chain(
        bool earconsEnabled = true)
    {
        var sonifier = Substitute.For<ISonificationManager>();
        sonifier.IsEnabled.Returns(true);

        var lib = Substitute.For<ISoundPatchLibrary>();
        lib.EarconOverrides.Returns(new EarconSettings());

        var store = new MockWorkspaceStore();
        store.EmitState(WorkspaceState.Initial with { IsEarconsEnabled = earconsEnabled });

        var earcons = new EarconService(sonifier, lib, new EventBus(), store);
        var router = new AudioFeedbackRouter(Substitute.For<INavigationSonifier>(), earcons);
        var coordinator = new GlobalErrorCoordinator(new EventBus(),
            NullLogger<GlobalErrorCoordinator>.Instance, router);
        return (coordinator, sonifier);
    }

    private static int SoundsMade(ISonificationManager sonifier)
        => sonifier.ReceivedCalls().Count(c => c.GetMethodInfo().Name is "PlayNote" or "PlayPatch");

    [Theory]
    [MemberData(nameof(EveryType))]
    public void EveryEarconTypeProducesASound(EarconType type)
    {
        var (coordinator, sonifier) = Chain();

        coordinator.PlayEarcon(type);

        Assert.True(SoundsMade(sonifier) > 0,
            $"EarconType.{type} reached the sonifier as nothing at all. A silent earcon is " +
            "indistinguishable from a broken binding — which is precisely how Boundary and the " +
            "five StateChange members survived, one of them for a year.");
    }

    /// <summary>
    /// The vacuity half. If the chain were wired to a service that could not sound anything — or
    /// if the counter were counting something that always fires — every case above would pass
    /// while proving nothing. Muting ambient earcons has to actually mute them.
    /// </summary>
    [Fact]
    public void TheHarnessCanObserveSilence()
    {
        var (coordinator, sonifier) = Chain(earconsEnabled: false);

        coordinator.PlayEarcon(EarconType.ModeSwitch);

        Assert.Equal(0, SoundsMade(sonifier));
    }

    /// <summary>
    /// …and the other side of it: an ERROR earcon must still sound with ambient earcons muted.
    /// Without this pair, "the harness can observe silence" could be satisfied by a harness that
    /// observes nothing but silence.
    /// </summary>
    [Fact]
    public void ErrorsStillSoundThroughTheAmbientMute()
    {
        var (coordinator, sonifier) = Chain(earconsEnabled: false);

        coordinator.PlayEarcon(EarconType.ErrorHigh);

        Assert.True(SoundsMade(sonifier) > 0,
            "error earcons are exempt from the mute tier — the silent-failure rule");
    }

    // ── Structure: every member is routed on purpose ────────────────────────────────

    /// <summary>
    /// Every member must appear by name in the mapping switch.
    ///
    /// <para>
    /// This is a source scan and it is deliberate: the thing being asserted is that a
    /// <c>switch</c> expression is exhaustive, and the compiler will not say so for an enum. The
    /// mapping ends in <c>_ =&gt; FeedbackType.Info</c>, which is the right default — a caller
    /// that asks for a sound gets a sound — and is exactly why the gap is invisible from the
    /// outside. A seventeenth member added tomorrow and never routed would produce Info's neutral
    /// blip for a stop-loss, and every behavioural test in this file would pass.
    /// </para>
    ///
    /// <para>
    /// The path check matters as much as the presence check: assert the arms are in the SWITCH,
    /// not merely somewhere in the file, or a member named only in a comment would satisfy it.
    /// The scan therefore looks for the arm form <c>EarconType.X =&gt;</c> rather than the bare
    /// name.
    /// </para>
    /// </summary>
    [Theory]
    [MemberData(nameof(EveryType))]
    public void EveryEarconTypeIsRoutedExplicitly(EarconType type)
    {
        string source = ReadCoordinatorSource();

        Assert.Contains($"EarconType.{type} =>", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// The scan's own vacuity check. A member that does NOT exist must not be found, or the
    /// assertion above is matching something other than what it claims — a substring of a longer
    /// name, say, or a file it failed to read and quietly treated as empty.
    /// </summary>
    [Fact]
    public void TheScanCanFail()
    {
        string source = ReadCoordinatorSource();

        Assert.False(string.IsNullOrWhiteSpace(source), "the scan read nothing — every match above would be vacuous");
        Assert.DoesNotContain("EarconType.NoSuchMember =>", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// A boundary is not a navigation move. This mapping was wrong once and the wrongness was
    /// inaudible, so it is pinned rather than left to the enumeration — which only asks that
    /// something is there, not that it is right.
    /// </summary>
    [Fact]
    public void ABoundaryDoesNotAskForTheNavigationSound()
    {
        string source = ReadCoordinatorSource();

        Assert.Contains("EarconType.Boundary => FeedbackType.Boundary", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// The severity split is the only information the earcon path carries beyond the type itself,
    /// and a low error and a high one are supposed to sound different.
    /// </summary>
    [Fact]
    public void HighAndLowErrorsAreNotTheSameEarcon()
    {
        Assert.NotEqual(ErrorFrequencies(ErrorSeverity.High), ErrorFrequencies(ErrorSeverity.Low));
    }

    private static List<double> ErrorFrequencies(ErrorSeverity severity)
    {
        var lib = Substitute.For<ISoundPatchLibrary>();
        lib.EarconOverrides.Returns(new EarconSettings());
        var sonifier = Substitute.For<ISonificationManager>();
        sonifier.IsEnabled.Returns(true);

        new AudioFeedbackRouter(Substitute.For<INavigationSonifier>(), new EarconService(sonifier, lib))
            .PlayEarcon(FeedbackType.Error, severity);

        var freqs = sonifier.ReceivedCalls()
            .Where(c => c.GetMethodInfo().Name == "PlayNote")
            .Select(c => (double)c.GetArguments()[0]!)
            .ToList();

        Assert.NotEmpty(freqs);
        return freqs;
    }

    /// <summary>
    /// Returns just the body of <c>PlayEarcon</c>'s mapping switch, so a name that appears
    /// anywhere else in the file — a comment, the severity switch, a doc reference — cannot make
    /// a member look routed when it is not.
    /// </summary>
    private static string ReadCoordinatorSource()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AccessibleTrader.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);

        string path = Path.Combine(dir!.FullName, "AccessibleTrader.Core",
            "Services", "Accessibility", "GlobalErrorCoordinator.cs");
        string source = File.ReadAllText(path);

        const string anchor = "FeedbackType feedbackType = type switch";
        int start = source.IndexOf(anchor, StringComparison.Ordinal);
        Assert.True(start >= 0, $"the mapping switch was renamed or moved — this scan is now measuring nothing ({path})");
        Assert.Equal(start, source.LastIndexOf(anchor, StringComparison.Ordinal));   // the anchor is unique

        int end = source.IndexOf("};", start, StringComparison.Ordinal);
        Assert.True(end > start);
        return source[start..end];
    }
}
