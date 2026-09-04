using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using NSubstitute;

namespace AccessibleTrader.Tests;

/// <summary>
/// THE TWO EARCON FAMILIES, and the rule that neither of them reaches the ones that matter most.
///
/// <para>
/// Cody, 2026-09-04: <i>"I'm thinking about differentiating earcons; earcons for indicators and
/// signals and then earcons for the application itself i.e. boundary beeps like when you're at
/// the right edge of a chart … modal openings and closing"</i>. One switch governed both, and
/// they fire at wildly different rates against wildly different value: the boundary tone sounds
/// on every further arrow press at the edge of a chart, a setup bell twice in a session.
/// </para>
///
/// <para>
/// The split is by WHAT THE SOUND IS ABOUT. Market: the market did something. Interface: the
/// terminal did something, and it also said so in words. Errors and order outcomes are in
/// NEITHER — see the tests at the bottom, which are the ones worth breaking the build over.
/// </para>
/// </summary>
public class EarconFamilyTests
{
    private static (EarconService svc, ISonificationManager sonify) Build(
        bool master = true, bool chart = true, bool iface = true, bool muteIncludesOrders = false)
    {
        var sonify = Substitute.For<ISonificationManager>();
        var lib = Substitute.For<ISoundPatchLibrary>();
        lib.EarconOverrides.Returns(new EarconSettings());
        var store = Substitute.For<IWorkspaceStore>();
        store.State.Returns(WorkspaceState.Initial with { IsEarconsEnabled = master });
        var settings = Substitute.For<IAppSettings>();
        settings.ChartEarconsEnabled.Returns(chart);
        settings.InterfaceEarconsEnabled.Returns(iface);
        settings.MuteIncludesOrderEvents.Returns(muteIncludesOrders);
        return (new EarconService(sonify, lib, null, store, settings), sonify);
    }

    private static void AssertPlayed(ISonificationManager sonify, bool played)
    {
        if (played)
            sonify.Received().PlayNote(Arg.Any<double>(), Arg.Any<double>(), Arg.Any<string>(),
                Arg.Any<float>(), Arg.Any<float>(), Arg.Any<double>(), Arg.Any<bool>());
        else
            sonify.DidNotReceive().PlayNote(Arg.Any<double>(), Arg.Any<double>(), Arg.Any<string>(),
                Arg.Any<float>(), Arg.Any<float>(), Arg.Any<double>(), Arg.Any<bool>());
    }

    [Fact]
    public void MarketEarconsOff_SilencesTheMarketOnes_AndLeavesTheInterfaceOnes()
    {
        // The half of the ask that is hardest to get right: switching one family off has to be
        // invisible to the other. A test that only asserted the silence would pass equally
        // against a service that had gone mute altogether.
        var (svc, sonify) = Build(chart: false, iface: true);

        svc.PlayAlert();
        svc.PlayNewBar();
        svc.PlaySetupBell(OrderSide.Buy, reconfirmation: false);
        svc.PlaySetupArmed(OrderSide.Sell);
        svc.PlaySetupEntryReached(OrderSide.Buy);
        AssertPlayed(sonify, played: false);

        svc.PlayBoundary();
        AssertPlayed(sonify, played: true);
    }

    [Fact]
    public void InterfaceEarconsOff_SilencesTheTerminalOnes_AndLeavesTheMarketOnes()
    {
        var (svc, sonify) = Build(chart: true, iface: false);

        svc.PlayBoundary();
        svc.PlayInfo();
        svc.PlaySuccess();
        svc.PlayRetry();
        svc.PlayConnectionState(ConnectionState.Connected);
        AssertPlayed(sonify, played: false);

        svc.PlayAlert();
        AssertPlayed(sonify, played: true);
    }

    [Fact]
    public void ShiftF3_IsStillTheMasterOverBoth()
    {
        // The families sit UNDER the mute, not beside it. If they were an OR, Shift+F3 would
        // have quietly stopped working for anyone who had both switches on — which is everyone,
        // since both default on.
        var (svc, sonify) = Build(master: false, chart: true, iface: true);

        svc.PlayAlert();
        svc.PlayNewBar();
        svc.PlayBoundary();
        svc.PlayInfo();
        AssertPlayed(sonify, played: false);
    }

    [Fact]
    public void BothDefaultOn_SoAnUntouchedInstallSoundsExactlyAsItDid()
    {
        // Opt-out, and the reason is upgrade rather than taste: a user who has never opened the
        // Sonification tab must not lose every earcon to a release note they did not read.
        var settings = new AppSettings(new EmptySettingsManager());

        Assert.True(settings.ChartEarconsEnabled);
        Assert.True(settings.InterfaceEarconsEnabled);
    }

    [Fact]
    public void ErrorEarcons_AreInNeitherFamily_AndSurviveBothSwitchesOff()
    {
        // THE ONE THAT MATTERS. There is no compensating channel for a blind user: an error
        // that makes no sound and no sentence did not happen as far as they are concerned. The
        // silent-failure rule outranks every preference in this file.
        var (svc, sonify) = Build(master: false, chart: false, iface: false);

        svc.PlayError(ErrorSeverity.Critical);
        AssertPlayed(sonify, played: true);
    }

    [Fact]
    public void OrderOutcomeEarcons_AreInNeitherFamily_AndStillBreakThrough()
    {
        // Money moving is not a market observation and not an interface confirmation. A user who
        // muted one family to quieten the terminal must not thereby lose the sound of a stop.
        var (svc, sonify) = Build(master: false, chart: false, iface: false, muteIncludesOrders: false);

        svc.PlayStopHit();
        AssertPlayed(sonify, played: true);
    }

    /// <summary>An empty store, so every AppSettings property answers with its declared default.</summary>
    private sealed class EmptySettingsManager : ISettingsManager
    {
        public Newtonsoft.Json.Linq.JToken? GetSetting(string keyPath, Newtonsoft.Json.Linq.JToken? defaultValue = null)
            => defaultValue;
        public void SetSetting(string keyPath, Newtonsoft.Json.Linq.JToken value) { }
        public Newtonsoft.Json.Linq.JObject GetEffectiveSettingsForSeries(string seriesId) => new();
        public void SaveSettings() { }
        public void ResetToDefaults() { }
    }
}
