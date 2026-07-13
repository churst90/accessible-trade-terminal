using System;
using System.Collections.Generic;
using System.Linq;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Sdk.Interfaces;

namespace AccessibleTrader.Core.Services
{
    /// <summary>
    /// In-memory ring-buffer journal. Subscribes to the event bus so strategy signals,
    /// alerts, and errors are auto-recorded. Speech is logged from <c>BlazorSpeechManager</c>
    /// via <see cref="AddSpeech"/> on the same instance (singleton in DI).
    /// </summary>
    public sealed class JournalService : IJournalService, IDisposable
    {
        private const int MaxEntries = 2000;
        private readonly LinkedList<JournalEntry> _entries = new();
        private readonly object _gate = new();
        private readonly List<IDisposable> _subs = new();
        private readonly IWorkspaceStore _store;

        public int Capacity => MaxEntries;
        public event Action<JournalEntry>? EntryAdded;

        public JournalService(IEventBus bus, IWorkspaceStore store)
        {
            _store = store;

            // Strategy signals — these are the headline review surface for "why did we get an alert".
            _subs.Add(bus.Subscribe<StrategySignalEvent>(e =>
            {
                var sig = e.Signal;
                string conf = double.IsNaN(sig.Confidence) ? "" : $" (confidence {sig.Confidence:F2})";
                string text = $"{sig.Side} setup{conf}: {sig.Rationale}";
                Add(new JournalEntry(
                    DateTime.Now,
                    JournalEntryKind.StrategySignal,
                    e.StrategyName,
                    _store.State.Identity.Symbol,
                    text));
            }));

            // Setup lifecycle beyond the signal itself. A CONFIRMED setup is already
            // journaled via StrategySignalEvent (same rationale, same bar) — but ARMED
            // setups (conditions met, entry trigger pending), the entry-trigger firing,
            // and condition dropouts produce speech with no signal, so without these
            // subscriptions they would only be findable in the raw Speech log.
            _subs.Add(bus.Subscribe<SetupArmedEvent>(e =>
            {
                string targets = string.Join(", ", e.ResolvedPlan.TpPrices.Select(
                    (tp, k) => $"target {k + 1} {tp:F4}"));
                Add(new JournalEntry(
                    DateTime.Now,
                    JournalEntryKind.StrategySignal,
                    e.StrategyName,
                    _store.State.Identity.Symbol,
                    $"{e.Side} setup ARMED (entry trigger pending): {e.TriggerDescription} " +
                    $"Entry {e.ResolvedPlan.EntryPrice:F4}, stop {e.ResolvedPlan.StopPrice:F4}, {targets} " +
                    $"(R:R {e.ResolvedPlan.RewardRiskRatio:F2})."));
            }));

            _subs.Add(bus.Subscribe<SetupEntryReachedEvent>(e =>
            {
                Add(new JournalEntry(
                    DateTime.Now,
                    JournalEntryKind.StrategySignal,
                    e.StrategyName,
                    _store.State.Identity.Symbol,
                    $"{e.Side} setup entry zone reached at {e.TriggerPrice:F4}, {e.BarsArmed} bars after arming."));
            }));

            _subs.Add(bus.Subscribe<SetupDroppedEvent>(e =>
            {
                if (e.DroppedLeafLabels == null || e.DroppedLeafLabels.Count == 0) return;
                Add(new JournalEntry(
                    DateTime.Now,
                    JournalEntryKind.StrategySignal,
                    e.StrategyName,
                    _store.State.Identity.Symbol,
                    $"Setup conditions dropped: {string.Join(", ", e.DroppedLeafLabels)}. " +
                    (e.SetupStillActive ? "Setup still active." : "Setup invalidated.")));
            }));

            // User-defined price/indicator alerts.
            _subs.Add(bus.Subscribe<AlertFiredEvent>(e =>
            {
                var a = e.Alert;
                Add(new JournalEntry(
                    DateTime.Now,
                    JournalEntryKind.Alert,
                    a?.Definition?.Name ?? "Alert",
                    _store.State.Identity.Symbol,
                    a?.SpeechText ?? "Alert fired"));
            }));

            // App errors (so the user can review failures from the same place).
            _subs.Add(bus.Subscribe<AppErrorEvent>(e =>
            {
                if (e.IsDuplicate) return;
                Add(new JournalEntry(
                    DateTime.Now,
                    JournalEntryKind.Error,
                    e.Source,
                    _store.State.Identity.Symbol,
                    $"[{e.Severity}] {e.Message}"));
            }));
        }

        public void Add(JournalEntry entry)
        {
            lock (_gate)
            {
                _entries.AddLast(entry);
                while (_entries.Count > MaxEntries) _entries.RemoveFirst();
            }
            try { EntryAdded?.Invoke(entry); } catch (Exception ex) { System.Diagnostics.Trace.TraceWarning($"JournalService subscriber threw: {ex.Message}"); }
        }

        public void AddSpeech(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            Add(new JournalEntry(
                DateTime.Now,
                JournalEntryKind.Speech,
                "TTS",
                _store.State.Identity.Symbol,
                text));
        }

        public IReadOnlyList<JournalEntry> Snapshot()
        {
            lock (_gate) return _entries.ToList();
        }

        public void Clear()
        {
            lock (_gate) _entries.Clear();
        }

        public void Dispose()
        {
            foreach (var s in _subs) s.Dispose();
            _subs.Clear();
        }
    }
}
