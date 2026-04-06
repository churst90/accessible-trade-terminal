using System;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services.Audio;

namespace AccessibleTrader.Core.Services.Accessibility
{
    public interface INotificationHub
    {
        void NotifyError(string message, bool interrupt = true);
        void NotifyAlert(string message, bool interrupt = true);
        void NotifyInfo(string message, bool interrupt = true);
    }

    public class NotificationHub : INotificationHub
    {
        private readonly ISpeechFeedbackRouter _speechRouter;
        private readonly IAudioFeedbackRouter _audioRouter;

        public NotificationHub(ISpeechFeedbackRouter speechRouter, IAudioFeedbackRouter audioRouter)
        {
            _speechRouter = speechRouter;
            _audioRouter = audioRouter;
        }

        public void NotifyError(string message, bool interrupt = true)
        {
            _speechRouter.Speak(message, interrupt);
            _audioRouter.PlayEarcon(FeedbackType.Error);
        }

        public void NotifyAlert(string message, bool interrupt = true)
        {
            _speechRouter.Speak(message, interrupt);
            _audioRouter.PlayEarcon(FeedbackType.Alert);
        }

        public void NotifyInfo(string message, bool interrupt = true)
        {
            _speechRouter.Speak(message, interrupt);
            _audioRouter.PlayEarcon(FeedbackType.Info);
        }
    }
}