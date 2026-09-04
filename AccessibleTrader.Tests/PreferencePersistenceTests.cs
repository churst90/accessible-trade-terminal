using System.Reactive.Subjects;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Sdk.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;
using NSubstitute;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Debt item 3 stage (b): the store's global preferences (speech verbosity,
    /// WASAPI latency, panning granularity) were session-only — every launch reset
    /// them. These tests pin the new durable path: settings seed the store at
    /// startup, and store changes write back to settings.
    /// </summary>
    public class PreferencePersistenceTests
    {
        private sealed class FakeSettings : ISettingsManager
        {
            public readonly Dictionary<string, JToken> Store = new();
            public int SaveCount;
            public JToken? GetSetting(string keyPath, JToken? defaultValue = null)
                => Store.TryGetValue(keyPath, out var v) ? v : defaultValue;
            public void SetSetting(string keyPath, JToken value) => Store[keyPath] = value;
            public JObject GetEffectiveSettingsForSeries(string seriesId) => new();
            public void SaveSettings() => SaveCount++;
            public void ResetToDefaults() { SaveCount++; }
        }

        private static (PreferencePersistenceService svc, IWorkspaceStore store, FakeSettings fake,
            BehaviorSubject<WorkspaceState> stream, List<UpdateSettingsAction> dispatched,
            System.Reactive.Concurrency.HistoricalScheduler clock) Build()
        {
            var fake = new FakeSettings();
            var app = new AppSettings(fake);
            var store = Substitute.For<IWorkspaceStore>();
            var stream = new BehaviorSubject<WorkspaceState>(WorkspaceState.Initial);
            store.StateStream.Returns(stream);
            store.State.Returns(_ => stream.Value);
            var dispatched = new List<UpdateSettingsAction>();
            store.When(s => s.Dispatch(Arg.Any<WorkspaceAction>()))
                 .Do(ci =>
                 {
                     if (ci.Arg<WorkspaceAction>() is UpdateSettingsAction u)
                     {
                         dispatched.Add(u);
                         stream.OnNext(u.Updater(stream.Value)); // apply like the real reducer
                     }
                 });
            // Virtual time: the 1 s write-back throttle fires only when the test
            // advances the clock, so both directions are deterministic — "the save
            // happened" needs no real wait, and "no save happened" is a fact about
            // an exhausted virtual timeline instead of a race against a 1.4 s nap.
            var clock = new System.Reactive.Concurrency.HistoricalScheduler();
            var svc = new PreferencePersistenceService(store, app,
                NullLogger<PreferencePersistenceService>.Instance, clock);
            return (svc, store, fake, stream, dispatched, clock);
        }

        [Fact]
        public void Initialize_SeedsStoreFromPersistedSettings()
        {
            var (svc, _, fake, stream, dispatched, _) = Build();
            fake.Store[SettingsKeys.SpeakTimestamps] = JToken.FromObject(false);
            fake.Store[SettingsKeys.WasapiLatency] = JToken.FromObject(250);
            fake.Store[SettingsKeys.PanningGranularity] = JToken.FromObject(25);

            svc.Initialize();

            Assert.Single(dispatched);
            Assert.False(stream.Value.SpeakTimestamps);
            Assert.Equal(250, stream.Value.WasapiLatency);
            Assert.Equal(25, stream.Value.PanningGranularity);
            // Unset keys keep Initial defaults.
            Assert.True(stream.Value.ReadColumnHeaders);
            Assert.Equal("HeaderValue", stream.Value.SpeechOrder);
        }

        [Fact]
        public void StoreChange_WritesBackToSettings()
        {
            var (svc, _, fake, stream, _, clock) = Build();
            svc.Initialize();
            int savesAfterSeed = fake.SaveCount;

            // A user edit lands in the store (e.g. Settings dialog Close).
            stream.OnNext(stream.Value with { SpeakTimestamps = false, WasapiLatency = 321 });

            // Write-back is throttled (1 s) to coalesce key-repeat bursts: just
            // short of the window nothing may be written yet...
            clock.AdvanceBy(TimeSpan.FromMilliseconds(999));
            Assert.Equal(savesAfterSeed, fake.SaveCount);

            // ...and crossing it flushes exactly once.
            clock.AdvanceBy(TimeSpan.FromMilliseconds(2));
            Assert.Equal(JToken.FromObject(false).ToString(),
                fake.Store[SettingsKeys.SpeakTimestamps].ToString());
            Assert.Equal("321", fake.Store[SettingsKeys.WasapiLatency].ToString());
            Assert.Equal(savesAfterSeed + 1, fake.SaveCount);
        }

        [Fact]
        public void NonPreferenceChanges_DoNotTriggerWrites()
        {
            var (svc, _, fake, stream, _, clock) = Build();
            svc.Initialize();
            int savesAfterSeed = fake.SaveCount;

            // Navigation and other churn must not thrash settings.json. Advancing
            // virtual time far past the throttle window proves "never", not
            // "not yet" — the old 1.4 s real-time nap could go green simply
            // because a loaded runner hadn't scheduled the write when it looked.
            stream.OnNext(stream.Value with { CurrentDataIndex = 42 });
            stream.OnNext(stream.Value with { CurrentDataIndex = 43 });
            clock.AdvanceBy(TimeSpan.FromMinutes(5));

            Assert.Equal(savesAfterSeed, fake.SaveCount);
        }

        [Fact]
        public void Initialize_IsIdempotent()
        {
            var (svc, _, _, _, dispatched, _) = Build();
            svc.Initialize();
            svc.Initialize();
            Assert.Single(dispatched);
        }
    }
}
