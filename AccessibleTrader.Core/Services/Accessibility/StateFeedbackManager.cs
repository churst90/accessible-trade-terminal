using System;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services.Audio;

namespace AccessibleTrader.Core.Services.Accessibility
{
    public interface IStateFeedbackManager
    {
        void HandleStateChange(string message, bool interrupt = true);
        void HandleVolumeChange(string message, float volume);
    }

    public class StateFeedbackManager : IStateFeedbackManager
    {
        private readonly ISpeechFeedbackRouter _speechRouter;
        private readonly IAudioFeedbackRouter _audioRouter;

        public StateFeedbackManager(ISpeechFeedbackRouter speechRouter, IAudioFeedbackRouter audioRouter)
        {
            _speechRouter = speechRouter;
            _audioRouter = audioRouter;
        }

        public void HandleStateChange(string message, bool interrupt = true)
        {
            _speechRouter.Speak(message, interrupt);
            // Optional: Play a "toggle" earcon if needed
        }

        public void HandleVolumeChange(string message, float volume)
        {
            _speechRouter.Speak(message, true);
        }
    }
}