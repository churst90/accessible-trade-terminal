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
    public interface ISpeechFeedbackRouter
    {
        void Speak(string message, bool interrupt = true);
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

        public SpeechFeedbackRouter(ISpeechManager speechManager, ISpeechFormatter speechFormatter, IWorkspaceStore store)
        {
            _speechManager = speechManager;
            _speechFormatter = speechFormatter;
            _store = store;

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

        public void Speak(string message, bool interrupt = true)
        {
            if (string.IsNullOrEmpty(message)) return;
            _speechSubject.OnNext((message, interrupt));
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
