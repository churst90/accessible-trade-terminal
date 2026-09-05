using System.Reactive.Linq;
using System.Reactive.Subjects;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.Audio;
using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Logging;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using DynamicData;

namespace AccessibleTrader.Tests.Mocks
{
    public class SpyEventBus : IEventBus
    {
        public List<object> Log = new();
        private Dictionary<Type, object> _subjects = new();
        public void Publish<T>(T eventItem)
        {
            if (eventItem == null) return;
            Log.Add(eventItem);
            if (_subjects.TryGetValue(typeof(T), out var subject)) ((Subject<T>)subject).OnNext(eventItem);
        }
        public IDisposable Subscribe<T>(Action<T> action) => AsObservable<T>().Subscribe(action);
        public IObservable<T> AsObservable<T>()
        {
            if (!_subjects.ContainsKey(typeof(T))) _subjects[typeof(T)] = new Subject<T>();
            return (Subject<T>)_subjects[typeof(T)];
        }
        public IDisposable SubscribeCoalesced<T>(Action<T> handler, TimeSpan quietWindow)
            => AsObservable<T>().Throttle(quietWindow).Subscribe(handler);
        public IDisposable SubscribeSampled<T>(Action<T> handler, TimeSpan window)
            => AsObservable<T>().Sample(window).Subscribe(handler);
    }

    public class MockMainThreadService : IMainThreadService
    {
        public List<Action> PendingActions = new();
        public void InvokeOnMainThread(Action action) => PendingActions.Add(action);
        public void RunAll() { var copy = PendingActions.ToList(); PendingActions.Clear(); foreach (var a in copy) a(); }
    }

    public class CounterSpeechManager : ISpeechManager
    {
        public int SpeakCalls = 0;
        public string LastSpokenText = "";
        public bool IsActive => true;
        public bool IsSpeechEnabled { get; set; } = true;
        public string SpeechMode => "Test";
        public Action<string>? OnSpeak { get; set; }

        // The interrupt flag used to be discarded here, which is why nothing in the
        // suite could observe it: A2/F2 found the only assertion on `interrupt:`
        // anywhere was a grep over .razor source. Whether an utterance cuts off the
        // one before it is not a detail — a fill that queues behind a bar reading is
        // a fill the trader hears seconds late, and a cancel that cuts one off is a
        // routine event stamping on something the user asked for.
        public readonly List<(string Text, bool Interrupt)> Utterances = new();
        public int SilenceCalls = 0;

        /// <summary>The interrupt flag of the most recent utterance.</summary>
        public bool LastInterrupt => Utterances.Count > 0 ? Utterances[^1].Interrupt : false;

        public void Silence() { SilenceCalls++; }
        public void Speak(string text, bool interrupt = false)
        {
            SpeakCalls++;
            LastSpokenText = text;
            Utterances.Add((text, interrupt));
            OnSpeak?.Invoke(text);
        }
    }

    public class CounterSonificationManager : ISonificationManager
    {
        public bool IsEnabled { get; set; } = true;
        public bool IsPlaying => false;
#pragma warning disable CS0067
        public event Action? PlaybackFinished;
        public event Action<int>? PlaybackPointReached;
#pragma warning restore CS0067
        public void PlayNote(double frequency, double durationSeconds, string waveformType, float volume, float pan = 0, double delayMilliseconds = 0, bool force = false) { }
        public void PlayPatch(AccessibleTrader.Sdk.Models.SoundPatch patch, float volumeScale = 1f, float pan = 0f, bool force = false) { }
        public AudioPoint CreateAudioPoint(ChartSeries series, int componentIndex, Ohlcv point, int relativeIndex, int viewportWidth, (double Min, double Max) viewportRange, int dataIndex, float masterVolume = 1.0f, double? overrideValue = null) => new AudioPoint(440, 1, "sine", 0, "Sustain");
        public void Stop() { }
        public void Silence() { }
        public void SetMasterVolume(float volume) { }
    }

    public class MockDataManager : IDataManager 
    { 
        public TimeSeriesBuffer<Ohlcv> Data { get; } = TimeSeriesBuffer<Ohlcv>.Empty; 
        public ChartIdentity Identity { get; set; } = ChartIdentity.Empty; 
        public IObservable<TimeSeriesBuffer<Ohlcv>> DataStream => Observable.Never<TimeSeriesBuffer<Ohlcv>>(); 
        public IObservable<TimeSeriesBuffer<Ohlcv>> InitialLoadStream => Observable.Never<TimeSeriesBuffer<Ohlcv>>(); 
        public Task RefreshDataAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task PrependOlderDataAsync() => Task.CompletedTask; 
        public Task StartLiveUpdates() => Task.CompletedTask; 
        public Task StopLiveUpdatesAsync() => Task.CompletedTask;
        public Task CatchUpFromSnapshotAsync(TimeSeriesBuffer<Ohlcv> snapshotData, CancellationToken ct = default) => Task.CompletedTask;
        public Task<(List<OrderBookEntry> Bids, List<OrderBookEntry> Asks)> GetOrderBookAsync() => Task.FromResult((new List<OrderBookEntry>(), new List<OrderBookEntry>()));
#pragma warning disable CS0067
        public event Action? DataUpdated;
        public event Action<string>? ErrorOccurred;
#pragma warning restore CS0067
    }

    public class MockEarconService : IEarconService
    {
        public void PlayAlert(bool breakThroughMutes = false) { }
        public void PlayBoundary() { }
        public void PlayError(ErrorSeverity severity) { }
        public void PlayRetry() { }
        public void PlaySuccess() { }
        public void PlayConnectionState(ConnectionState state) { }
        public void PlayInfo() { }
        public void PlayNewBar() { }
        public void PlaySetupBell(OrderSide side, bool isLeg) { }
        public void PlaySetupArmed(OrderSide side) { }
        public void PlaySetupEntryReached(OrderSide side) { }

        public int OrderFillCount;
        public OrderSide? LastOrderFillSide;
        public int StopHitCount;
        public int TakeProfitHitCount;
        public void PlayOrderFill(OrderSide side) { OrderFillCount++; LastOrderFillSide = side; }
        public void PlayStopHit() { StopHitCount++; }
        public void PlayTakeProfitHit() { TakeProfitHitCount++; }
    }

    public class MockStylingService : IStylingService
    {
        public SkiaSharp.SKPaint GetPaint(ComponentConfig component, float density) => new SkiaSharp.SKPaint { Color = SkiaSharp.SKColors.White };
        public string GetDefaultColor(string indicatorCode, string componentName = "", ComponentDisplayType type = ComponentDisplayType.Line) => "#FFF";
        public string GetSecondaryColor(string indicatorCode, string componentName = "", ComponentDisplayType type = ComponentDisplayType.Line) => "#000"; 
        public string GetWaveform(string indicatorCode, string componentName = "", ComponentDisplayType displayType = ComponentDisplayType.Line) => "sine"; 
        public float GetDefaultThickness(ComponentDisplayType type) => 1.0f; 
        public ComponentDisplayType GetDisplayType(string indicatorCode, string componentName = "") => ComponentDisplayType.Line; 
        public string GetPane(string indicatorCode) => "Main"; 
        public string GetCategory(string indicatorCode) => "Other"; 
        public double GetBullishFrequency(string indicatorCode) => 440; 
        public double GetBearishFrequency(string indicatorCode) => 220; 
        public ComponentRole GetComponentRole(string indicatorCode, string componentName) => ComponentRole.PriceAction;
        public SonificationProfile GetSonificationProfile(ComponentDisplayType type, ComponentRole role, string strategy) => new SonificationProfile("sine", "sine", "sine", AmplitudeMapping.None, PitchMapping.Value, 440, 1.0, false, "Sustain");
        public ColorSource GetColorSource(string indicatorCode, string componentName) => ColorSource.Value;
        public double? GetReferenceLevel(string indicatorCode, string componentName, ComponentDisplayType type) => null;
        public List<(string Name, double Value)> GetLevelComponents(string indicatorCode) => new List<(string Name, double Value)>();
        public bool GetIsAreaFill(string indicatorCode, string componentName, ComponentDisplayType type) => false;
        public bool GetUsePolarityColoring(string indicatorCode, string componentName, ComponentDisplayType type) => false;
        public string GetSpeechTemplate(string indicatorCode, string componentName, ComponentDisplayType type) => "";
        public double GetColorBaseline(string indicatorCode, string componentName) => 0.0;
    }

    public class MockSpeechFormatter : ISpeechFormatter
    {
        public string FormatPointFeedback(WorkspaceState state, bool isXMove, bool isYMove, ChartSeries series, Ohlcv pt, string prefixMessage) => $"Price {pt.Close}";
        public string FormatProfileFeedback(WorkspaceState state, bool isXMove, bool isYMove, ChartSeries series, int binIndex, string prefixMessage) => "Profile";
        public string FormatHeatmapFeedback(WorkspaceState state, bool isXMove, bool isYMove, ChartSeries series, int dataIndex, int binIndex, string prefixMessage, int cursorDataIndex = -1) => "Heatmap";
        public string FormatViewportDescription(int count, DateTime start, DateTime end) => $"Viewing {count} bars";
        public void RegisterTemplate(string indicatorCode, string componentName, string template) { }
    }

    public class MockViewportManager : IViewportManager
    {
        public void HandleZoom(string direction, IReadOnlyList<Ohlcv> data) { }
        public void HandlePan(int direction, IReadOnlyList<Ohlcv> data) { }
        public void EnsureVisible(int index) { }
        public void AnnounceViewport(IReadOnlyList<Ohlcv> data) { }
        public string GetRichViewportDescription(IReadOnlyList<Ohlcv> data, int startIndex, int length) => "Viewport";
    }

    public class MockWorkspaceStore : IWorkspaceStore
    {
        private readonly BehaviorSubject<WorkspaceState> _stateSubject = new(WorkspaceState.Initial);
        private WorkspaceState _state = WorkspaceState.Initial;
        public List<WorkspaceAction> DispatchedActions = new();

        public WorkspaceState State => _state;
        public IObservable<WorkspaceState> StateStream => _stateSubject.AsObservable();
        public void Dispatch(WorkspaceAction action) => DispatchedActions.Add(action);
        public IObservable<IChangeSet<Ohlcv, DateTime>> DataStream => Observable.Never<IChangeSet<Ohlcv, DateTime>>();
        public IObservable<IChangeSet<ChartSeries, string>> SeriesStream => Observable.Never<IChangeSet<ChartSeries, string>>();

        public void EmitState(WorkspaceState state)
        {
            _state = state;
            _stateSubject.OnNext(state);
        }
    }

    public class MockSpeechRouter : ISpeechFeedbackRouter
    {
        public void Speak(string text, bool interrupt = false, SpeechChannel channel = SpeechChannel.Manual) { }
        public void SpeakPoint(WorkspaceState state, WorkspaceState? previousState, ChartSeries series, Ohlcv point, string prefix = "") { }
        public void SpeakProfile(WorkspaceState state, WorkspaceState? previousState, ChartSeries series, int binIndex, string prefix = "") { }
        public void SpeakHeatmap(WorkspaceState state, WorkspaceState? previousState, ChartSeries series, int dataIndex, int binIndex, string prefix = "") { }
    }

    public class MockAudioRouter : IAudioFeedbackRouter
    {
        public bool IsSonificationEnabled { get; set; } = true;
        public void PlayEarcon(FeedbackType type, ErrorSeverity severity = ErrorSeverity.Medium) { }
        public void Silence() { }
    }

    public class MockNotificationHub : INotificationHub 
    { 
        public void NotifyAlert(string message, bool interrupt = true) { } 
        public void NotifyError(string message, bool interrupt = true) { } 
        public void NotifyInfo(string message, bool interrupt = true) { } 
    }

    public class MockNavManager : INavigationFeedbackManager
    {
        public bool IsSpeechEnabled { get; set; } = true;
        public int HandleNavigationCalls { get; private set; }

        /// <summary>The chart-formation clause handed in on the most recent call, if any.</summary>
        public string? LastExtraContext { get; private set; }

        public void HandleNavigationFeedback(WorkspaceState state, bool isXMove, bool isYMove, string prefixMessage, bool isUserInitiated = true, bool isJump = false, string? extraContext = null)
        {
            HandleNavigationCalls++;
            LastExtraContext = extraContext;
        }
    }

    public class MockIndicatorEngine : IIndicatorEngine
    {
        public Task<Dictionary<string, double[]>> CalculateAsync(string code, IReadOnlyList<Ohlcv> data, Dictionary<string, object> parameters, System.Threading.CancellationToken ct)
            => Task.FromResult(new Dictionary<string, double[]>());
        public Task<Dictionary<string, double>> CalculateIncrementalAsync(string code, IReadOnlyList<Ohlcv> data, Dictionary<string, object> parameters, Dictionary<string, double[]> previousResults, System.Threading.CancellationToken ct)
            => Task.FromResult(new Dictionary<string, double>());
        public IIndicatorProvider? GetProvider(string indicatorCode) => null;
        public Task<(Dictionary<string, double[]> Results, IReadOnlyList<AccessibleTrader.Sdk.Models.ZoneBandConfig> ZoneBands)>
            CalculateWithBandsAsync(string code, IReadOnlyList<Ohlcv> data, Dictionary<string, object> parameters, System.Threading.CancellationToken ct)
            => Task.FromResult<(Dictionary<string, double[]>, IReadOnlyList<AccessibleTrader.Sdk.Models.ZoneBandConfig>)>((new Dictionary<string, double[]>(), System.Array.Empty<AccessibleTrader.Sdk.Models.ZoneBandConfig>()));
    }

    public class MockIndicatorService : IIndicatorService
    {
        public void LoadIndicatorPlugins(IPluginLoaderService pluginLoader) { }
        public List<IndicatorMetadata> GetAvailableIndicators() => new();
        public void CalculateIndicator(string code, ReadOnlySpan<Ohlcv> data, Dictionary<string, object> parameters, IIndicatorResultBuffer buffer) { }
        public void UpdateLast(string code, ReadOnlySpan<Ohlcv> data, Dictionary<string, object> parameters, IIndicatorResultBuffer buffer) { }
        public int GetStabilityWindow(string code, Dictionary<string, object> parameters) => 0;
        public string GetDetailFact(string code, ReadOnlySpan<Ohlcv> data, IReadOnlyDictionary<string, double[]> results, int index, Dictionary<string, object> parameters) => "Mock Fact";
    }

    public class MockNavigationSonifier : INavigationSonifier
    {
        public void PlayNote(double frequency, double durationSeconds, string waveformType, float volume, float pan = 0, double delayMilliseconds = 0) { }
        public void PlayPatch(AccessibleTrader.Sdk.Models.SoundPatch patch, float volumeScale = 1f, float pan = 0f) { }
        public AudioPoint CreateAudioPoint(ChartSeries series, int componentIndex, Ohlcv point, int relativeIndex, int viewportWidth, (double Min, double Max) viewportRange, int dataIndex, float masterVolume = 1.0f, double? overrideValue = null) => new AudioPoint(440, 1, "sine", 0, "Sustain");
        public void StopNavigationVoice() { }
        public void SetMasterGain(float gain) { }
        public void Silence() { }
        public void SyncNavigationSlots(WorkspaceState state) { }
        public void SonifyProfile(ChartSeries series, int binIndex, float masterVolume = 1.0f) { }
        public void SonifyHeatmap(ChartSeries series, int dataIndex, int binIndex, float masterVolume = 1.0f) { }
        public Task FireClusterTicksAsync(WorkspaceState state, int dataIndex, string excludeSeriesId, int excludeComponentIndex, bool crossSeriesMode = false) => Task.CompletedTask;
    }

    public class MockAppLogger : IAppLogger
    {
        public void Log(ErrorSeverity severity, ErrorCategory category, string message, string source, Exception? exception = null) { }
        public void LogDebug(string message, string source) { }
        public void LogInfo(string message, string source) { }
        public void LogWarning(string message, string source, Exception? exception = null) { }
        public void LogError(string message, string source, Exception? exception = null) { }
        public void LogCritical(string message, string source, Exception? exception = null) { }
    }

    public class MockGlobalErrorCoordinator : IGlobalErrorCoordinator
    {
        public void ReportError(string message, ErrorSeverity severity = ErrorSeverity.Medium, ErrorCategory category = ErrorCategory.Systemic) { }
        public void ReportNetworkRetry(string message, int attempt, int maxAttempts) { }
        public void ReportSuccess(string message) { }
        public void PlayEarcon(EarconType type) { }
    }

    public class MockIndicatorPreferencesService : IIndicatorPreferencesService
    {
        public List<ComponentPreference>? GetPreferences(string indicatorCode) => null;
        public void SavePreferences(string indicatorCode, List<ComponentPreference> prefs) { }
        public void ClearPreferences(string indicatorCode) { }
        public List<LevelPreference> GetLevelPreferences(string indicatorCode) => new();
        public void SaveLevelPreference(string indicatorCode, LevelPreference pref) { }
        public void ClearAllPreferences() { }
    }

    /// <summary>Narrates nothing, so the coordinator speaks the new-bar sentence itself —
    /// which is what every test that does not wire the real narrator wants.</summary>
    public class MockAutoNarrationService : IAutoNarrationService
    {
        public bool WillNarrateBarClose() => false;
        public void DeferBarCloseSentence(string sentence) { }
    }
}




