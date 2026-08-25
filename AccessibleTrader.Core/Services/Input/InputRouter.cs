using AccessibleTrader.Core.Models;

namespace AccessibleTrader.Core.Services.Input
{
    public interface IInputRouter
    {
        void HandleKeyPress(string key, bool shift, bool ctrl, bool alt);
    }

    public class InputRouter : IInputRouter, IDisposable
    {
        private readonly IInputService _inputService;
        private readonly IShortcutManager _shortcutManager;
        private readonly ICommandDispatcher _dispatcher;
        private readonly IKeyNormalizationService _normalizer;

        public InputRouter(
            IInputService inputService,
            IShortcutManager shortcutManager,
            ICommandDispatcher dispatcher,
            IKeyNormalizationService normalizer)
        {
            _inputService = inputService;
            _shortcutManager = shortcutManager;
            _dispatcher = dispatcher;
            _normalizer = normalizer;

            _inputService.KeyPressed += HandleKeyPress;
        }

        public void HandleKeyPress(string key, bool shift, bool ctrl, bool alt)
        {
            string normalizedKey = _normalizer.Normalize(key);
            SystemCommand command = _shortcutManager.GetCommand(normalizedKey, shift, ctrl, alt);
            if (command != SystemCommand.None)
            {
                _dispatcher.Dispatch(command);
            }
        }

        public void Dispose()
        {
            _inputService.KeyPressed -= HandleKeyPress;
        }
    }
}