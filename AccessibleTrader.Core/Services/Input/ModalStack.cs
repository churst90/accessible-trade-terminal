using AccessibleTrader.Core.Models;

namespace AccessibleTrader.Core.Services.Input
{
    /// <summary>
    /// THE ordered stack of open modals, in open order, bottom first.
    ///
    /// <para>
    /// There used to be two ideas of "which dialog is on top". <see cref="CommandDispatcher"/>
    /// kept a private <c>Stack&lt;string?&gt;</c> in open order and used it to aim Escape;
    /// <c>keyboard.js</c>'s Tab trap took the LAST visible <c>role="dialog"</c> in DOM order, and
    /// DOM order is the constant render order in <c>MainLayout.razor</c>, where Help is rendered
    /// before nineteen other modals. Open Settings, press F1 (Help is allowed while a modal is
    /// open), and Escape closed Help while Tab was trapped in Settings — underneath the dialog
    /// the user was reading, whose <c>aria-modal="true"</c> had already told the screen reader
    /// not to describe anything outside it. Reverse the open order and it worked, so it presented
    /// as intermittent. No test anywhere opened two dialogs until 2026-09-02.
    /// </para>
    ///
    /// <para>
    /// This class is the one stack. It subscribes to <see cref="ModalStateChangedEvent"/> itself,
    /// so its contents cannot depend on the order in which other subscribers happen to run; the
    /// dispatcher reads <see cref="Top"/> for Escape, <c>MainLayout</c> pushes
    /// <see cref="Snapshot"/> to <c>accessibleTrader.setModalStack</c> on every
    /// <see cref="Changed"/>, and the trap resolves the top NAME to the dialog element wearing
    /// it as <c>data-modal-name</c>. One feed, one order, two readers.
    /// </para>
    ///
    /// <para>
    /// Close is by NAME, most-recent-first: the common case is LIFO, but a parent that closes a
    /// child programmatically, or a close event arriving after a newer open, must remove the
    /// right entry rather than whatever is on top. A close for a name that is not open is
    /// ignored rather than corrupting the count — the old counter clamped at zero for the same
    /// reason. An open for a name that is ALREADY open moves that entry to the top instead of
    /// adding a second: F1 is allowed while a modal is open and HelpModal's ShowAsync does not
    /// check its own visibility, so F1, F1 used to push Help twice — after one Escape the old
    /// stack still held a Help that no dialog answered to, and Escape and every chart command
    /// were dead until reload (found by the 2026-09-02 modal-specialist review).
    /// </para>
    /// </summary>
    public sealed class ModalStack : IDisposable
    {
        private readonly List<string?> _open = new();
        private readonly object _lock = new();
        private readonly IDisposable? _sub;

        /// <summary>
        /// Raised after every change, on the publisher's thread, with the event that caused it
        /// and the stack as it now stands. <see cref="ModalStackChange.Stack"/> is a copy.
        /// </summary>
        public event Action<ModalStackChange>? Changed;

        /// <summary>
        /// A stack fed by every <see cref="ModalStateChangedEvent"/> on <paramref name="bus"/>.
        /// This is the only constructor on purpose: with a parameterless one alongside it, the
        /// DI container would silently choose that one if <see cref="IEventBus"/> were ever
        /// missing from a host, and the stack would never see an event. A test that wants an
        /// isolated stack gives it a bus of its own.
        /// </summary>
        public ModalStack(IEventBus bus)
        {
            _sub = bus.Subscribe<ModalStateChangedEvent>(Apply);
        }

        /// <summary>Number of open modals.</summary>
        public int Count { get { lock (_lock) return _open.Count; } }

        /// <summary>True when at least one modal is open.</summary>
        public bool IsAnyOpen => Count > 0;

        /// <summary>The name of the most recently opened modal still open, or null when none is.</summary>
        public string? Top
        {
            get { lock (_lock) return _open.Count > 0 ? _open[^1] : null; }
        }

        /// <summary>The open modals, bottom first, top last. A copy.</summary>
        public IReadOnlyList<string?> Snapshot
        {
            get { lock (_lock) return _open.ToArray(); }
        }

        /// <summary>Apply one open/close event. Public so a test can drive an unattached stack.</summary>
        public void Apply(ModalStateChangedEvent e)
        {
            bool changed;
            string?[] snapshot;
            lock (_lock)
            {
                if (e.IsOpen)
                {
                    // Re-open of an open modal: move to top, never duplicate (see the class doc).
                    int already = _open.LastIndexOf(e.ModalName);
                    if (already >= 0) _open.RemoveAt(already);
                    _open.Add(e.ModalName);
                    changed = true;
                }
                else
                {
                    // Newest matching entry. LastIndexOf handles both the LIFO case and the
                    // out-of-order case with one rule, and -1 is "not open": ignore it.
                    int idx = _open.LastIndexOf(e.ModalName);
                    changed = idx >= 0;
                    if (changed) _open.RemoveAt(idx);
                }
                snapshot = _open.ToArray();
            }
            if (changed) Changed?.Invoke(new ModalStackChange(e.IsOpen, e.ModalName, snapshot));
        }

        public void Dispose() => _sub?.Dispose();
    }

    /// <summary>One change to the <see cref="ModalStack"/>: what happened, and the stack afterwards.</summary>
    /// <param name="IsOpen">True for an open, false for a close.</param>
    /// <param name="ModalName">The modal the event named.</param>
    /// <param name="Stack">The stack after the change, bottom first.</param>
    public sealed record ModalStackChange(bool IsOpen, string? ModalName, IReadOnlyList<string?> Stack);
}
