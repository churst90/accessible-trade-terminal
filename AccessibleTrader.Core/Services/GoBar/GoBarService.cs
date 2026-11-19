using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services.Accessibility;
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
    ///
    /// <para><b>Closed, then dispatched.</b> The dispatcher drops every command except a short
    /// allow-list while any modal is open, so a Go bar that dispatched from inside its own box
    /// would be silently swallowed. <see cref="Run"/> asks the box to close and watches the one
    /// ordered <see cref="ModalStack"/>; the command is dispatched only on the change that
    /// leaves the stack empty. If another dialog is still underneath, nothing is dispatched and
    /// its name is said.</para>
    ///
    /// <para><b>It speaks only its own refusals.</b> Commands that handle themselves when
    /// dispatched already speak their own outcome, so a success here adds no second
    /// sentence.</para>
    /// </summary>
    public sealed class GoBarService : IDisposable
    {
        private readonly ICommandDispatcher _dispatcher;
        private readonly IShortcutManager _shortcuts;
        private readonly IEventBus _bus;
        private readonly ModalStack _modalStack;
        private GoBarEntry? _pending;

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
            _modalStack.Changed += OnModalStackChanged;
        }

        /// <summary>The matches plus the polite top-suggestion sentence.</summary>
        public GoBarSuggestion Suggest(string input)
        {
            var matches = GoBarMatcher.Match(input, GoBarRegistry.All);
            if (matches.Count == 0)
            {
                string typed = (input ?? string.Empty).Trim();
                return new GoBarSuggestion(matches,
                    typed.Length == 0 ? string.Empty : $"Nothing matches {typed}.");
            }

            var top = matches[0].Entry;
            string? key = KeyLabel(top.Command);
            string sentence = key == null ? $"{top.Name}." : $"{top.Name}, {key}.";

            int more = matches.Count - 1;
            if (more == 1) sentence += " 1 more.";
            else if (more > 1) sentence += $" {more} more.";

            return new GoBarSuggestion(matches, sentence);
        }

        /// <summary>Run one chosen entry, or say why it cannot run.</summary>
        public void Run(GoBarEntry entry)
        {
            if (entry == null) return;

            if (entry.Kind == GoBarKind.KeyOnly)
            {
                Speak(KeyOnlySentence(entry), interrupt: true);
                return;
            }

            // One command at a time: a second Enter while the first close is in flight is the
            // same intent, not a queue.
            if (Interlocked.CompareExchange(ref _pending, entry, null) != null) return;

            _bus.Publish(new CloseGoBarRequestEvent());

            // No box was actually open (a caller outside the modal). Nothing will publish the
            // close for us, so dispatch now.
            if (!_modalStack.IsAnyOpen)
            {
                var pending = Interlocked.Exchange(ref _pending, null);
                if (pending != null) _dispatcher.DispatchFromGoBar(pending.Command);
            }
        }

        /// <summary>
        /// The live key for a command, in the words the app already uses for speech
        /// ("Control plus Alt plus W"), or null when it has no binding. The registry itself
        /// carries no key, so a rebind is reflected here with no second table to update.
        /// </summary>
        public string? KeyLabel(SystemCommand command)
        {
            // CloseModal has no binding of its own by design: Escape is bound to CancelDrawing
            // and the dispatcher re-routes it to CloseModal while a dialog is open. A key-only
            // sentence for it should say the key the user actually presses.
            if (command == SystemCommand.CloseModal)
                command = SystemCommand.CancelDrawing;

            return ShortcutWords.ForCommand(_shortcuts, command);
        }

        private string KeyOnlySentence(GoBarEntry entry)
        {
            string? key = KeyLabel(entry.Command);
            return key == null
                ? $"{entry.Name} is a key; it does nothing from the Go bar."
                : $"{entry.Name} is a key, {key}; it does nothing from the Go bar.";
        }

        private void OnModalStackChanged(ModalStackChange change)
        {
            if (change.IsOpen) return;

            var pending = Interlocked.Exchange(ref _pending, null);
            if (pending == null) return;

            if (change.Stack.Count > 0)
            {
                string dialog = CommandDispatcher.SpokenModalName(change.Stack[^1]);
                Speak($"{pending.Name} can't run while {dialog} is open.", interrupt: true);
                return;
            }

            _dispatcher.DispatchFromGoBar(pending.Command);
        }

        private void Speak(string text, bool interrupt)
            => _bus.Publish(new AnnouncementEvent(
                text, interrupt, Channel: SpeechChannel.Interface));

        public void Dispose()
        {
            _modalStack.Changed -= OnModalStackChanged;
            _pending = null;
        }
    }
}
