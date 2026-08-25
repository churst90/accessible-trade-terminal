using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Sdk.Enums;

namespace AccessibleTrader.Core.Services.Accessibility
{
    public interface IGlobalErrorCoordinator
    {
        void ReportError(string message, ErrorSeverity severity = ErrorSeverity.Medium, ErrorCategory category = ErrorCategory.Systemic);
        void ReportNetworkRetry(string provider, int retryCount, int nextRetrySeconds);
        void ReportSuccess(string message);
        void PlayEarcon(EarconType type);
    }

    /// <summary>
    /// Central hub for error reporting across the application.
    /// Maps high-level error states to semantic events for the Feedback Coordinator.
    /// Subscribes to AppErrorEvent from EventBus; deduplicates announcements within 3 seconds.
    /// </summary>
    public class GlobalErrorCoordinator : IGlobalErrorCoordinator, IDisposable
    {
        private readonly IEventBus _eventBus;
        private readonly ILogger<GlobalErrorCoordinator> _logger;
        private readonly IAudioFeedbackRouter _audioRouter;
        private readonly IDisposable _subscription;

        // Dedup: track last announced message per source, suppress within 3 seconds
        private readonly ConcurrentDictionary<string, DateTime> _announcedCache = new(StringComparer.Ordinal);
        private static readonly TimeSpan DedupWindow = TimeSpan.FromSeconds(3);

        public GlobalErrorCoordinator(
            IEventBus eventBus,
            ILogger<GlobalErrorCoordinator> logger,
            IAudioFeedbackRouter audioRouter)
        {
            _eventBus = eventBus;
            _logger = logger;
            _audioRouter = audioRouter;

            // Subscribe to AppErrorEvent from the EventBus
            _subscription = _eventBus.Subscribe<AppErrorEvent>(OnAppError);
        }

        private void OnAppError(AppErrorEvent ev)
        {
            if (ev.IsDuplicate) return;

            string dedupKey = $"{ev.Source}|{ev.Message}";
            var now = DateTime.UtcNow;
            if (_announcedCache.TryGetValue(dedupKey, out var lastAnnounced) && (now - lastAnnounced) < DedupWindow)
                return;

            _announcedCache[dedupKey] = now;

            string speechText = ev.Severity >= ErrorSeverity.Critical
                ? $"{ev.Category} - CRITICAL: {ev.Message}"
                : $"{ev.Category}: {ev.Message}";

            bool interrupt = ev.Severity >= ErrorSeverity.High;
            _eventBus.Publish(new FeedbackRequestEvent(FeedbackType.Error, speechText, interrupt, IsUserInitiated: false));
        }

        public void ReportError(string message, ErrorSeverity severity = ErrorSeverity.Medium, ErrorCategory category = ErrorCategory.Systemic)
        {
            _logger.LogError("[{Category}:{Severity}] {Message}", category, severity, message);

            FeedbackType feedbackType = severity switch {
                ErrorSeverity.Low => FeedbackType.Info,
                ErrorSeverity.Medium => FeedbackType.Error,
                _ => FeedbackType.Error
            };

            bool interrupt = severity >= ErrorSeverity.High;
            string prefix = (severity >= ErrorSeverity.High) ? "Error: " : "";

            _eventBus.Publish(new FeedbackRequestEvent(feedbackType, prefix + message, interrupt, IsUserInitiated: false));
        }

        public void ReportNetworkRetry(string provider, int retryCount, int nextRetrySeconds)
        {
            string msg = $"Connection lost to {provider}. Retry {retryCount}. Next attempt in {nextRetrySeconds} seconds.";
            _eventBus.Publish(new FeedbackRequestEvent(FeedbackType.Alert, msg, true, IsUserInitiated: false));
        }

        public void ReportSuccess(string message)
        {
            _eventBus.Publish(new FeedbackRequestEvent(FeedbackType.Info, message, false, IsUserInitiated: false, IncludeSonification: true));
        }

        public void PlayEarcon(EarconType type)
        {
            FeedbackType feedbackType = type switch
            {
                EarconType.ErrorLow => FeedbackType.Error,
                EarconType.ErrorHigh => FeedbackType.Error,
                EarconType.NetworkDown => FeedbackType.Alert,
                EarconType.NetworkUp => FeedbackType.Info,
                EarconType.OrderFill => FeedbackType.Info,
                EarconType.StopHit => FeedbackType.Alert,
                EarconType.TakeProfit => FeedbackType.Info,
                EarconType.Alert => FeedbackType.Alert,
                EarconType.Confirmation => FeedbackType.Info,
                // Was FeedbackType.Navigation — a boundary asked for, and would have got, the
                // wrong sound. Doubly invisible while Navigation had no arm in the earcon router
                // at all, so the mismap produced silence rather than an audible mistake.
                EarconType.Boundary => FeedbackType.Boundary,
                EarconType.ModeSwitch => FeedbackType.StateChange,
                EarconType.Navigation => FeedbackType.Navigation,
                EarconType.PlaybackStart => FeedbackType.Info,
                EarconType.PlaybackStop => FeedbackType.Info,
                EarconType.SeriesHidden => FeedbackType.StateChange,
                EarconType.SeriesMuted => FeedbackType.StateChange,
                _ => FeedbackType.Info
            };

            ErrorSeverity severity = type switch
            {
                EarconType.ErrorHigh => ErrorSeverity.High,
                EarconType.ErrorLow => ErrorSeverity.Low,
                _ => ErrorSeverity.Medium
            };

            _audioRouter.PlayEarcon(feedbackType, severity);
        }

        public void Dispose() => _subscription?.Dispose();
    }
}
