using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services.Input;

namespace AccessibleTrader.Core.Services.GoBar
{
    /// <summary>The matches for what has been typed, plus the one sentence that is read.</summary>
    public sealed record GoBarSuggestion(IReadOnlyList<GoBarMatch> Matches, string Sentence)
    {
        public bool HasMatches => Matches.Count > 0;
    }

    /// <summary>
    /// Alt+G's two jobs: say what the typed text could mean, and run the chosen entry after
    /// the box has closed (or say why it cannot).
    /// </summary>
    public sealed class GoBarService : IDisposable
    {
        private readonly ICommandDispatcher _dispatcher;
        private readonly IShortcutManager _shortcuts;
        private readonly IEventBus _bus;
        private readonly ModalStack _modalStack;

        public GoBarService(
            ICommandDispatcher dispatcher,
            IShortcutManager shortcuts,
            IEventBus bus,
            ModalStack modalStack)
        {
            _dispatcher = dispatcher;
            _shortcuts = shortcuts;
            _bus = bus;
            _modalStack = modalStack;
        }

        public GoBarSuggestion Suggest(string input) => throw new NotImplementedException();

        public void Run(GoBarEntry entry) => throw new NotImplementedException();

        public string? KeyLabel(SystemCommand command) => throw new NotImplementedException();

        public void Dispose() { }
    }
}
