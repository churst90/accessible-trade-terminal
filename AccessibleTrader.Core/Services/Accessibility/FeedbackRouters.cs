using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading.Tasks;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Core.Services.Audio;

namespace AccessibleTrader.Core.Services.Accessibility
{
    /// <summary>
    /// Which mute tier a spoken message belongs to. The 2026-07-21 redesign: the
    /// gate lives HERE, at the router, not at call sites — per-call-site
    /// IsSpeechEnabled checks are exactly how the F2 bypasses crept in.
    /// </summary>
    public enum SpeechChannel
    {
        /// <summary>Response to something the user just did (navigation values,
        /// zoom/pan, context summary, status). Muted by F2.</summary>
        Manual,
        /// <summary>Something that happened to the user (alerts, monitoring,
        /// new bars, auto-narration). Muted by Shift+F2.</summary>
        Event,
        /// <summary>Order-execution outcomes (fills, stops, take-profits).
        /// Break through BOTH mutes by default — the manual's "the one feedback
        /// you never miss" promise — unless speech.muteIncludesOrderEvents opts
        /// them into the Event tier.</summary>
        OrderEvent,
        /// <summary>Errors and the mute-toggle confirmations themselves.
        /// Never muted: "Speech off" must be heard, and silent failures are
        /// forbidden by the feedback contract.</summary>
        Critical,
    }

    public interface ISpeechFeedbackRouter
    {
        void Speak(string message, bool interrupt = true, SpeechChannel channel = SpeechChannel.Manual);
        void SpeakPoint(WorkspaceState state, WorkspaceState? previousState, ChartSeries series, Ohlcv point, string prefix = "");
        void SpeakProfile(WorkspaceState state, WorkspaceState? previousState, ChartSeries series, int binIndex, string prefix = "");
        void SpeakHeatmap(WorkspaceState state, WorkspaceState? previousState, ChartSeries series, int dataIndex, int binIndex, string prefix = "");
    }

    public class SpeechFeedbackRouter : ISpeechFeedbackRouter, IDisposable
    {
        private readonly ISpeechManager _speechManager;
        private readonly ISpeechFormatter _speechFormatter;
        private readonly IWorkspaceStore _store;
        private readonly Subject<(string Message, bool Interrupt)> _speechSubject = new();
        private readonly IDisposable _subscription;

        // Optional so existing three-arg construction (tests) keeps working; DI
        // supplies the settings facade, enabling the order-event opt-in.
        private readonly IAppSettings? _appSettings;

        public SpeechFeedbackRouter(ISpeechManager speechManager, ISpeechFormatter speechFormatter, IWorkspaceStore store,
            IAppSettings? appSettings = null)
        {
            _speechManager = speechManager;
            _speechFormatter = speechFormatter;
            _store = store;
            _appSettings = appSettings;

            _subscription = _speechSubject
                .Subscribe(x =>
                {
                    if (x.Interrupt)
                    {
                        _speechManager.Silence();
                        _speechManager.Speak(x.Message, true);
                    }
                    else
                    {
                        _speechManager.Speak(x.Message, false);
                    }
                });
        }

        public void Speak(string message, bool interrupt = true, SpeechChannel channel = SpeechChannel.Manual)
        {
            if (string.IsNullOrEmpty(message)) return;
            if (!IsChannelAudible(channel)) return;
            _speechSubject.OnNext((message, interrupt));
        }

        private bool IsChannelAudible(SpeechChannel channel)
        {
            var state = _store.State;
            return channel switch
            {
                SpeechChannel.Manual => state.IsSpeechEnabled,
                SpeechChannel.Event => state.IsEventSpeechEnabled,
                // Order outcomes break through unless the user explicitly opted
                // them into the event mute (Settings → speech.muteIncludesOrderEvents).
                SpeechChannel.OrderEvent =>
                    state.IsEventSpeechEnabled || !(_appSettings?.MuteIncludesOrderEvents ?? false),
                _ => true, // Critical
            };
        }

        public void SpeakPoint(WorkspaceState state, WorkspaceState? previousState, ChartSeries series, Ohlcv point, string prefix = "")
        {
            bool isXMove = previousState == null || state.CurrentDataIndex != previousState.CurrentDataIndex;
            bool isYMove = previousState != null && (state.FocusedComponentIndex != previousState.FocusedComponentIndex || state.FocusedBinIndex != previousState.FocusedBinIndex);
            
            string speechText = _speechFormatter.FormatPointFeedback(state, isXMove, isYMove, series, point, prefix);
            Speak(speechText, true);
        }

        public void SpeakProfile(WorkspaceState state, WorkspaceState? previousState, ChartSeries series, int binIndex, string prefix = "")
        {
            bool isXMove = previousState == null || state.CurrentDataIndex != previousState.CurrentDataIndex;
            bool isYMove = previousState != null && state.FocusedBinIndex != previousState.FocusedBinIndex;

            string speechText = _speechFormatter.FormatProfileFeedback(state, isXMove, isYMove, series, binIndex, prefix);
            Speak(speechText, true);
        }

        public void SpeakHeatmap(WorkspaceState state, WorkspaceState? previousState, ChartSeries series, int dataIndex, int binIndex, string prefix = "")
        {
            bool isXMove = previousState == null || state.CurrentDataIndex != previousState.CurrentDataIndex;
            bool isYMove = previousState != null && state.FocusedBinIndex != previousState.FocusedBinIndex;

            string speechText = _speechFormatter.FormatHeatmapFeedback(state, isXMove, isYMove, series, dataIndex, binIndex, prefix);
            Speak(speechText, true);
        }

        public void Dispose()
        {
            _subscription.Dispose();
        }
    }

    public interface IAudioFeedbackRouter
    {
        bool IsSonificationEnabled { get; set; }
        void PlayEarcon(FeedbackType type, ErrorSeverity severity = ErrorSeverity.Medium);
        void SonifySeries(ChartSeries series, Ohlcv point, int relativeIndex, int viewportWidth, (double Min, double Max) viewportRange, int dataIndex, float masterVolume = 1.0f);
        void SonifyComponent(ChartSeries series, int componentIndex, Ohlcv point, int relativeIndex, int viewportWidth, (double Min, double Max) viewportRange, int dataIndex, float masterVolume = 1.0f);
        void SonifyProfile(ChartSeries series, int binIndex, float masterVolume = 1.0f);
        void SonifyHeatmap(ChartSeries series, int dataIndex, int binIndex, float masterVolume = 1.0f);
        void Silence();
    }

    public class AudioFeedbackRouter : IAudioFeedbackRouter
    {
        private readonly INavigationSonifier _sonifier;
        private readonly IEarconService _earcons;

        public bool IsSonificationEnabled { get; set; } = true;

        public AudioFeedbackRouter(INavigationSonifier sonifier, IEarconService earcons)
        {
            _sonifier = sonifier;
            _earcons = earcons;
        }

        public void PlayEarcon(FeedbackType type, ErrorSeverity severity = ErrorSeverity.Medium)
        {
            switch (type)
            {
                case FeedbackType.Error: _earcons.PlayError(severity); break;
                case FeedbackType.Info: _earcons.PlayInfo(); break;
                // FOUND 2026-07-21: Alert had no case — every PlayEarcon(Alert)
                // call (fired alerts with Delivery=Earcon, NotificationHub,
                // the sandbox advisory) was SILENT. Now a real alert sound.
                case FeedbackType.Alert: _earcons.PlayAlert(); break;
                case FeedbackType.Boundary: _earcons.PlayBoundary(); break;
            }
        }

        public void SonifySeries(ChartSeries series, Ohlcv point, int relativeIndex, int viewportWidth, (double Min, double Max) viewportRange, int dataIndex, float masterVolume = 1.0f)
        {
            if (!IsSonificationEnabled) return;
            _sonifier.SonifySeries(series, point, relativeIndex, viewportWidth, viewportRange, dataIndex, masterVolume);
        }

        public void SonifyComponent(ChartSeries series, int componentIndex, Ohlcv point, int relativeIndex, int viewportWidth, (double Min, double Max) viewportRange, int dataIndex, float masterVolume = 1.0f)
        {
            if (!IsSonificationEnabled) return;
            _sonifier.SonifyComponent(series, componentIndex, point, relativeIndex, viewportWidth, viewportRange, dataIndex, masterVolume);
        }

        public void SonifyProfile(ChartSeries series, int binIndex, float masterVolume = 1.0f)
        {
            if (!IsSonificationEnabled) return;
            _sonifier.SonifyProfile(series, binIndex, masterVolume);
        }

        public void SonifyHeatmap(ChartSeries series, int dataIndex, int binIndex, float masterVolume = 1.0f)
        {
            if (!IsSonificationEnabled) return;
            _sonifier.SonifyHeatmap(series, dataIndex, binIndex, masterVolume);
        }

        public void Silence() => _sonifier.Silence();
    }
}
