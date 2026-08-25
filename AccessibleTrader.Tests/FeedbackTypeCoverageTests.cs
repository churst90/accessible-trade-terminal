using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.Analysis;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Tests.Mocks;

namespace AccessibleTrader.Tests;

/// <summary>
/// <b>No member of <see cref="FeedbackType"/> may mean silence.</b>
///
/// <para>
/// Both routers that dispatch on this enum are a bare <c>switch</c> with no <c>default</c>, and
/// both have shipped with members missing. The earcon router lost <c>Alert</c> until 2026-07-21;
/// the speech router lost it until 2026-08-21, taking every network-retry warning
/// (<c>GlobalErrorCoordinator.ReportNetworkRetry</c> — <i>"Connection lost to {provider}"</i>) and
/// every strategy entry-trigger override (<c>ConfigurableStrategy</c>) with it. Those events were
/// constructed, published, and dropped on the floor: the websocket could die mid-session and the
/// trader was told nothing at all.
/// </para>
///
/// <para>
/// The reason this class of defect survives review is that the switch <i>looks</i> exhaustive.
/// Six arms in a row read as "all of them" unless you go and count the enum. So the test counts
/// the enum. It is written as an enumeration on purpose — adding a twelfth member to
/// <see cref="FeedbackType"/> fails these tests until it is routed, which is the only mechanism
/// that has ever caught this.
/// </para>
///
/// <para>
/// Proven to fail: reverting either <c>default:</c> arm turns the relevant test red, and deleting
/// <c>case FeedbackType.Alert</c> from <c>OnFeedbackRequest</c> reproduces the original bug in
/// <see cref="EveryFeedbackTypeCarryingAMessageIsSpoken"/>.
/// </para>
/// </summary>
public class FeedbackTypeCoverageTests
{
    private static readonly FeedbackType[] AllTypes = Enum.GetValues<FeedbackType>();

    /// <summary>
    /// Navigation is the one member excluded, and only because it does not speak its own message:
    /// it hands off to <c>INavigationFeedbackManager.HandleNavigationFeedback</c>, which composes
    /// the bar reading. The spy below records that hand-off separately, so Navigation is still
    /// proven non-silent — see <see cref="NavigationIsNotSilentEither"/>.
    /// </summary>
    public static TheoryData<FeedbackType> SpeakingTypes()
    {
        var d = new TheoryData<FeedbackType>();
        foreach (var t in AllTypes.Where(t => t != FeedbackType.Navigation)) d.Add(t);
        return d;
    }

    public static TheoryData<FeedbackType> EveryType()
    {
        var d = new TheoryData<FeedbackType>();
        foreach (var t in AllTypes) d.Add(t);
        return d;
    }

    // ── The speech router ────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(SpeakingTypes))]
    public void EveryFeedbackTypeCarryingAMessageIsSpoken(FeedbackType type)
    {
        var h = new Harness();

        // A distinctive message so the assertion cannot be satisfied by some other
        // announcement the coordinator happens to make while wiring up.
        h.Bus.Publish(new FeedbackRequestEvent(type, $"routing probe for {type}"));

        Assert.Contains(h.Speech.SpokenTexts, t => t != null && t.Contains("routing probe"));
    }

    [Fact]
    public void NavigationIsNotSilentEither()
    {
        var h = new Harness();

        h.Bus.Publish(new FeedbackRequestEvent(FeedbackType.Navigation, "bar reading", IsXMove: true));

        Assert.True(h.Nav.HandledCount > 0,
            "Navigation must reach the navigation feedback manager, which composes the spoken bar.");
    }

    /// <summary>
    /// The specific event the missing arm swallowed, end to end through the real coordinator.
    /// </summary>
    [Fact]
    public void ANetworkRetryWarningIsSpoken()
    {
        var h = new Harness();
        var errors = new GlobalErrorCoordinator(
            h.Bus,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<GlobalErrorCoordinator>.Instance,
            new MockAudioRouter());

        errors.ReportNetworkRetry("Kraken", retryCount: 2, nextRetrySeconds: 8);

        Assert.Contains(h.Speech.SpokenTexts, t => t != null && t.Contains("Connection lost to Kraken"));
    }

    /// <summary>
    /// An alert is something that happened TO the user, so it belongs to the ambient tier that
    /// Shift+F2 governs — the same channel <c>AlertFiredEvent</c> uses. Pinned because the
    /// tempting fix (Critical, "it's important") would make network retries unmutable.
    /// </summary>
    [Fact]
    public void AnAlertSpeaksOnTheEventChannel()
    {
        var h = new Harness();

        h.Bus.Publish(new FeedbackRequestEvent(FeedbackType.Alert, "connection lost"));

        Assert.Contains(h.Speech.Channels, c => c == SpeechChannel.Event);
    }

    /// <summary>An alert earcons as well as speaks — the immediate cue, like the Error branch.</summary>
    [Fact]
    public void AnAlertAlsoEarcons()
    {
        var h = new Harness();

        h.Bus.Publish(new FeedbackRequestEvent(FeedbackType.Alert, "connection lost"));

        Assert.Contains(FeedbackType.Alert, h.Audio.Requested);
    }

    // ── The earcon router ────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(EveryType))]
    public void EveryFeedbackTypeProducesAnEarcon(FeedbackType type)
    {
        var earcons = new CountingEarconService();
        var router = new AudioFeedbackRouter(new MockNavigationSonifier(), earcons);

        router.PlayEarcon(type);

        Assert.True(earcons.TotalPlayed > 0,
            $"PlayEarcon({type}) was a silent no-op. A caller that asks for a sound gets a sound — "
            + "a silent earcon is indistinguishable from a broken binding.");
    }

    /// <summary>
    /// The call that motivated the fix. <c>OrderCancelledEvent</c> asks for a StateChange earcon,
    /// and that request was added specifically because cancels "vanished silently" in the
    /// 2026-07-22 audit — then did nothing for the next month because the arm was missing.
    /// </summary>
    [Fact]
    public void AnOrderCancellationEarcons()
    {
        var earcons = new CountingEarconService();
        var router = new AudioFeedbackRouter(new MockNavigationSonifier(), earcons);

        router.PlayEarcon(FeedbackType.StateChange, ErrorSeverity.Low);

        Assert.True(earcons.TotalPlayed > 0);
    }

    /// <summary>
    /// Regression guard on the arms that already worked: a fallback must not swallow the
    /// distinctions. Error, Alert, Boundary and Info each keep their own sound.
    /// </summary>
    [Fact]
    public void TheEstablishedEarconsKeepTheirOwnSounds()
    {
        var earcons = new CountingEarconService();
        var router = new AudioFeedbackRouter(new MockNavigationSonifier(), earcons);

        router.PlayEarcon(FeedbackType.Error, ErrorSeverity.High);
        router.PlayEarcon(FeedbackType.Alert);
        router.PlayEarcon(FeedbackType.Boundary);
        router.PlayEarcon(FeedbackType.Info);

        Assert.Equal(1, earcons.ErrorCount);
        Assert.Equal(1, earcons.AlertCount);
        Assert.Equal(1, earcons.BoundaryCount);
        Assert.Equal(1, earcons.InfoCount);
    }

    /// <summary>
    /// <c>EarconType.Boundary</c> mapped to <c>FeedbackType.Navigation</c> — a boundary asking for,
    /// and had the arm existed, receiving the wrong sound. Invisible while Navigation was silent.
    /// </summary>
    [Fact]
    public void ABoundaryEarconRequestPlaysTheBoundarySound()
    {
        var earcons = new CountingEarconService();
        var router = new AudioFeedbackRouter(new MockNavigationSonifier(), earcons);
        var errors = new GlobalErrorCoordinator(
            new SpyEventBus(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<GlobalErrorCoordinator>.Instance,
            router);

        errors.PlayEarcon(EarconType.Boundary);

        Assert.Equal(1, earcons.BoundaryCount);
    }

    // ── Fixtures ─────────────────────────────────────────────────────────────

    private sealed class CountingEarconService : IEarconService
    {
        public int AlertCount, BoundaryCount, ErrorCount, InfoCount, OtherCount;
        public int TotalPlayed => AlertCount + BoundaryCount + ErrorCount + InfoCount + OtherCount;

        public void PlayAlert(bool breakThroughMutes = false) => AlertCount++;
        public void PlayBoundary() => BoundaryCount++;
        public void PlayError(ErrorSeverity severity) => ErrorCount++;
        public void PlayInfo() => InfoCount++;
        public void PlayRetry() => OtherCount++;
        public void PlaySuccess() => OtherCount++;
        public void PlayConnectionState(ConnectionState state) => OtherCount++;
        public void PlayNewBar() => OtherCount++;
        public void PlaySetupBell(OrderSide side, bool reconfirmation) => OtherCount++;
        public void PlaySetupArmed(OrderSide side) => OtherCount++;
        public void PlaySetupEntryReached(OrderSide side) => OtherCount++;
        public void PlayOrderFill(OrderSide side) => OtherCount++;
        public void PlayStopHit() => OtherCount++;
        public void PlayTakeProfitHit() => OtherCount++;
    }

    private sealed class RecordingSpeechRouter : ISpeechFeedbackRouter
    {
        public List<string> SpokenTexts { get; } = new();
        public List<SpeechChannel> Channels { get; } = new();

        public void Speak(string message, bool interrupt = true, SpeechChannel channel = SpeechChannel.Manual)
        {
            SpokenTexts.Add(message);
            Channels.Add(channel);
        }

        public void SpeakPoint(WorkspaceState state, WorkspaceState? previousState, ChartSeries series, Ohlcv point, string prefix = "") { }
        public void SpeakProfile(WorkspaceState state, WorkspaceState? previousState, ChartSeries series, int binIndex, string prefix = "") { }
        public void SpeakHeatmap(WorkspaceState state, WorkspaceState? previousState, ChartSeries series, int dataIndex, int binIndex, string prefix = "") { }
    }

    private sealed class RecordingAudioRouter : IAudioFeedbackRouter
    {
        public List<FeedbackType> Requested { get; } = new();
        public bool IsSonificationEnabled { get; set; } = true;
        public void PlayEarcon(FeedbackType type, ErrorSeverity severity = ErrorSeverity.Medium) => Requested.Add(type);
        public void Silence() { }
    }

    private sealed class CountingNavManager : INavigationFeedbackManager
    {
        public int HandledCount;
        public bool IsSpeechEnabled { get; set; } = true;

        public void HandleNavigationFeedback(WorkspaceState state, bool isXMove, bool isYMove,
            string prefixMessage, bool isUserInitiated = true, bool isJump = false,
            string? extraContext = null) => HandledCount++;
    }

    /// <summary>
    /// The real coordinator wired to recording spies. Constructing it is what subscribes the
    /// handlers, so the object has to be held even though nothing is called on it directly.
    /// </summary>
    private sealed class Harness
    {
        public SpyEventBus Bus { get; } = new();
        public RecordingSpeechRouter Speech { get; } = new();
        public RecordingAudioRouter Audio { get; } = new();
        public CountingNavManager Nav { get; } = new();
        public AccessibilityFeedbackCoordinator Coordinator { get; }

        public Harness()
        {
            var store = new MockWorkspaceStore();
            store.EmitState(WorkspaceState.Initial);

            Coordinator = new AccessibilityFeedbackCoordinator(
                store,
                Nav,
                Speech,
                Audio,
                new SpeechFormatter(),
                Bus,
                new MockEarconService(),
                new SdkCandlePatternAnalyzer(),
                new ChartPatternCache(new ChartPatternDetector(new SwingStructureAnalyzer())),
                new ChartPatternFocus(),
                new MockAutoNarrationService());
        }
    }
}
