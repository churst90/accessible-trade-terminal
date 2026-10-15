using System.Collections.Immutable;
using System.Reactive.Subjects;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Workspace;
using AccessibleTrader.Sdk.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;
using NSubstitute;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Session autosave + resume (2026-07-22): closing without saving must not
    /// lose the workspace, startup must offer the last session back, and a blank
    /// session must never clobber the saved one.
    /// </summary>
    public class SessionAutosaveTests
    {
        private sealed class Harness
        {
            public readonly IWorkspaceStore Store = Substitute.For<IWorkspaceStore>();
            public readonly IWorkspaceLibraryService Library = Substitute.For<IWorkspaceLibraryService>();
            public readonly IWorkspaceInitializer Initializer = Substitute.For<IWorkspaceInitializer>();
            public readonly ISettingsManager Settings = Substitute.For<ISettingsManager>();
            public readonly IEventBus EventBus = Substitute.For<IEventBus>();
            public readonly Subject<WorkspaceState> StateSubject = new();
            public WorkspaceState State = WorkspaceState.Initial;

            public SessionAutosaveService Build(bool demo = false, bool? settingValue = null, TimeSpan? interval = null)
            {
                Store.State.Returns(_ => State);
                Store.StateStream.Returns(StateSubject);
                // What the library would write for the current state. A fresh object per call,
                // keyed on the symbol, so "changed" and "unchanged" mean what they would on disk.
                Library.CaptureWorkspace(Store).Returns(_ => new WorkspaceConfiguration { Symbol = State.Identity.Symbol });
                // A written slot is then on the fake "disk", as it would be on the real one.
                Library.GetAllProfilesWithTimes().Returns(_ => _slots);
                Library.When(l => l.SaveProfile(Arg.Any<string>(), Arg.Any<WorkspaceConfiguration>()))
                    .Do(ci =>
                    {
                        var name = ci.ArgAt<string>(0);
                        _slots.RemoveAll(x => x.Name == name);
                        _slots.Add((name, DateTime.UtcNow));
                    });
                Settings.GetSetting(SettingsKeys.ResumeLastSession)
                    .Returns(settingValue.HasValue ? JToken.FromObject(settingValue.Value) : null);
                return new SessionAutosaveService(Store, Library, Initializer, Settings,
                    new DemoPolicy(demo), EventBus, NullLogger<SessionAutosaveService>.Instance, interval);
            }

            private readonly List<(string Name, DateTime LastWriteUtc)> _slots = new();

            /// <summary>Puts a session slot on the fake library's "disk" with a write time,
            /// so the most-recent-slot resume path has something to choose between.</summary>
            public void SeedSlot(string name, DateTime writtenUtc, WorkspaceConfiguration config)
            {
                _slots.Add((name, writtenUtc));
                Library.GetAllProfilesWithTimes().Returns(_ => _slots);
                Library.LoadProfile(name).Returns(config);
            }

            public void MakeMeaningful() =>
                State = WorkspaceState.Initial with
                {
                    Identity = new ChartIdentity("Spot", "Bitstamp", "BTC/USD", "1h"),
                };
        }

        [Fact]
        public void SaveNow_writes_the_reserved_profile_for_a_meaningful_session()
        {
            var h = new Harness();
            using var svc = h.Build();
            h.MakeMeaningful();

            svc.SaveNow();

            // The name is now this SESSION's own slot, not one fixed name shared by every
            // circuit — see SessionAutosaveService._slotName. What must hold is that exactly
            // one save happened and it went to a session slot.
            h.Library.Received(1).SaveProfile(
                Arg.Is<string>(n => n.StartsWith(SessionAutosaveService.LastSessionProfileName,
                                                 StringComparison.Ordinal)),
                Arg.Any<WorkspaceConfiguration>());
        }

        /// <summary>Neither write path was taken — the capture-and-write the autosave uses, nor
        /// the one-call save it used to use.</summary>
        private static void AssertNothingWritten(Harness h)
        {
            h.Library.DidNotReceiveWithAnyArgs().SaveProfile(default!, default!);
            h.Library.DidNotReceiveWithAnyArgs().SaveWorkspaceProfile(default!, default!);
        }

        [Fact]
        public void Blank_sessions_never_clobber_the_saved_one()
        {
            var h = new Harness();
            using var svc = h.Build();

            svc.SaveNow(); // store is WorkspaceState.Initial — blank

            AssertNothingWritten(h);
        }

        [Fact]
        public void Demo_sessions_never_autosave()
        {
            var h = new Harness();
            using var svc = h.Build(demo: true);
            h.MakeMeaningful();

            svc.SaveNow();

            AssertNothingWritten(h);
        }

        [Fact]
        public void Opt_out_setting_disables_autosave()
        {
            var h = new Harness();
            using var svc = h.Build(settingValue: false);
            h.MakeMeaningful();

            svc.SaveNow();

            AssertNothingWritten(h);
        }

        [Fact]
        public async Task State_changes_are_sampled_into_periodic_saves()
        {
            var h = new Harness();
            using var svc = h.Build(interval: TimeSpan.FromMilliseconds(50));
            h.MakeMeaningful();

            h.StateSubject.OnNext(h.State); // activity within the sample window

            var deadline = Environment.TickCount64 + 3000;
            while (!h.Library.ReceivedCalls().Any(c => c.GetMethodInfo().Name == nameof(IWorkspaceLibraryService.SaveProfile))
                   && Environment.TickCount64 < deadline)
                await Task.Delay(20);
            h.Library.Received().SaveProfile(
                Arg.Is<string>(n => n.StartsWith(SessionAutosaveService.LastSessionProfileName,
                                                 StringComparison.Ordinal)),
                Arg.Any<WorkspaceConfiguration>());
        }

        [Fact]
        public void Resume_restores_the_saved_config_and_announces()
        {
            var h = new Harness();
            using var svc = h.Build();
            var config = new WorkspaceConfiguration
            {
                Tabs = new List<TabConfiguration> { new(), new(), new() },
            };
            h.SeedSlot("__last-session__aaa", DateTime.UtcNow, config);

            Assert.True(svc.TryResumeAtStartup());

            h.Initializer.Received(1).RestoreWorkspace(config);
            h.EventBus.Received(1).Publish(Arg.Is<AnnouncementEvent>(e => e.Message.Contains("3 tabs")));
        }

        [Fact]
        public void A_chart_that_cannot_be_described_still_announces_the_resume()
        {
            // The sentence names the active chart (SessionResumeAnnouncementTests). Describing it
            // must never cost the user the resume or its announcement: a restore that leaves a
            // state the describer cannot read falls back to the bare sentence.
            var h = new Harness();
            using var svc = h.Build();
            var config = new WorkspaceConfiguration
            {
                Tabs = new List<TabConfiguration> { new(), new(), new() },
            };
            h.SeedSlot("__last-session__aaa", DateTime.UtcNow, config);
            bool restored = false;
            h.Store.State.Returns(_ => restored
                ? throw new InvalidOperationException("state unreadable after restore")
                : h.State);
            h.Initializer.When(i => i.RestoreWorkspace(config)).Do(_ => restored = true);

            Assert.True(svc.TryResumeAtStartup());

            h.EventBus.Received(1).Publish(Arg.Is<AnnouncementEvent>(e =>
                e.Message == "Resumed your last session: 3 tabs."
                && e.Channel == AccessibleTrader.Core.Services.Accessibility.SpeechChannel.Interface));
        }

        [Fact]
        public void Resume_skips_when_nothing_saved_or_workspace_already_loaded()
        {
            var h = new Harness();
            using var svc = h.Build();
            Assert.False(svc.TryResumeAtStartup()); // no profile on disk

            h.SeedSlot("__last-session__aaa", DateTime.UtcNow,
                new WorkspaceConfiguration { Tabs = new List<TabConfiguration> { new() } });
            h.MakeMeaningful(); // user already loaded something
            Assert.False(svc.TryResumeAtStartup());
            h.Initializer.DidNotReceiveWithAnyArgs().RestoreWorkspace(default!);
        }

        [Fact]
        public void Failed_resume_lands_on_the_blank_chart_not_a_crash()
        {
            var h = new Harness();
            using var svc = h.Build();
            h.SeedSlot("__last-session__aaa", DateTime.UtcNow,
                new WorkspaceConfiguration { Tabs = new List<TabConfiguration> { new() } });
            h.Initializer.When(i => i.RestoreWorkspace(Arg.Any<WorkspaceConfiguration>()))
                .Do(_ => throw new InvalidOperationException("corrupt series"));

            Assert.False(svc.TryResumeAtStartup());
        }

        // ── Two circuits, two slots ──────────────────────────────────────────
        //
        // ISessionAutosaveService is AddScoped on the WebHost — one per Blazor circuit, each
        // with its own IWorkspaceStore. Every one of them used to sample its own state and
        // write SaveWorkspaceProfile("__last-session__", …) into the same directory, so two
        // browser tabs on different charts made that one file flip between them every sample
        // interval, and OnCircuitClosedAsync made the LAST TAB CLOSED the winner regardless of
        // which one the user had been working in.

        [Fact]
        public void Two_concurrent_sessions_do_not_write_the_same_slot()
        {
            var a = new Harness();
            var b = new Harness();
            using var svcA = a.Build();
            using var svcB = b.Build();
            a.MakeMeaningful();
            b.MakeMeaningful();

            svcA.SaveNow();
            svcB.SaveNow();

            string nameA = (string)a.Library.ReceivedCalls()
                .First(c => c.GetMethodInfo().Name == nameof(IWorkspaceLibraryService.SaveProfile))
                .GetArguments()[0]!;
            string nameB = (string)b.Library.ReceivedCalls()
                .First(c => c.GetMethodInfo().Name == nameof(IWorkspaceLibraryService.SaveProfile))
                .GetArguments()[0]!;

            Assert.StartsWith(SessionAutosaveService.LastSessionProfileName, nameA);
            Assert.StartsWith(SessionAutosaveService.LastSessionProfileName, nameB);
            Assert.NotEqual(nameA, nameB);
        }

        [Fact]
        public void Resume_takes_the_most_recently_written_slot_not_an_arbitrary_one()
        {
            // The tab the user was last working in is the one whose slot was written last,
            // and that is the one they mean by "my last session".
            var h = new Harness();
            using var svc = h.Build();

            var stale = new WorkspaceConfiguration { Tabs = new List<TabConfiguration> { new() } };
            var fresh = new WorkspaceConfiguration
            {
                Tabs = new List<TabConfiguration> { new(), new(), new() },
            };

            // Seeded stale-first on purpose: a fixture whose newest slot is also its first
            // cannot tell "most recent" from "whichever came back first".
            h.SeedSlot("__last-session__stale", DateTime.UtcNow.AddMinutes(-30), stale);
            h.SeedSlot("__last-session__fresh", DateTime.UtcNow, fresh);

            Assert.True(svc.TryResumeAtStartup());

            h.Initializer.Received(1).RestoreWorkspace(fresh);
            h.Initializer.DidNotReceive().RestoreWorkspace(stale);
        }

        // ── Market data is not the user working ──────────────────────────────
        //
        // Resume picks the slot written most recently, on the reasoning that it is the tab the
        // user was last working in. But a live chart's state changes on EVERY TICK —
        // DataManager dispatches UpdateDataAction and LiveTickObservedAction for each one — and
        // the 30-second Sample saved whenever the state had changed, so any terminal tab left
        // open on a live market rewrote its slot every 30 seconds whether or not anyone touched
        // it. With a second tab open, "most recently written" meant "whichever ticked last":
        // close the tab you were working in, bring the terminal back from the tray, and a tab
        // you had not touched for an hour won. Found investigating Cody's 2026-10-15 report
        // (Market Structure and Volume Profile missing after a restore); his own incident had
        // one tab at a time, so this was not its cause — see SessionRestoreBrowserTests.

        private static WorkspaceStore LiveStore(string symbol)
        {
            var store = new WorkspaceStore(new EventBus(), new ViewportRangeCalculator(),
                new ViewportNavigationService(), new VolumeStateService());
            store.Dispatch(new SetIdentityAction(new ChartIdentity("Spot", "Bitstamp", symbol, "1m")));
            var bars = Enumerable.Range(0, 120)
                .Select(i => new Ohlcv(new DateTime(2026, 10, 15, 0, 0, 0, DateTimeKind.Utc).AddMinutes(i),
                                       100, 101, 99, 100, 1000))
                .ToList();
            store.Dispatch(new UpdateDataAction(new TimeSeriesBuffer<Ohlcv>(bars), IsInitialLoad: true));
            return store;
        }

        /// <summary>One live tick the way DataManager delivers it — the WHOLE buffer, with the
        /// forming bar updated or a new bar opened — and the feed stamped alive. A new bar on a
        /// chart at the live edge also scrolls the viewport (ViewportReducer.UpdateData), which
        /// is a change to the saved ViewportStartIndex. None of it is anything a resume gives back.</summary>
        private static void Tick(WorkspaceStore store, int minute, double close)
        {
            var bar = new Ohlcv(new DateTime(2026, 10, 15, 0, 0, 0, DateTimeKind.Utc).AddMinutes(minute),
                                100, Math.Max(101, close), 99, close, 1000);
            var bars = store.State.Data.ToList();
            if (bars.Count > 0 && bars[^1].Date == bar.Date) bars[^1] = bar; else bars.Add(bar);
            store.Dispatch(new UpdateDataAction(new TimeSeriesBuffer<Ohlcv>(bars), IsInitialLoad: false));
            store.Dispatch(new LiveTickObservedAction(DateTime.UtcNow));
        }

        private static SessionAutosaveService RealAutosave(IWorkspaceStore store, IWorkspaceLibraryService lib,
                                                           IWorkspaceInitializer? init = null) =>
            new(store, lib, init ?? Substitute.For<IWorkspaceInitializer>(), Substitute.For<ISettingsManager>(),
                new DemoPolicy(false), Substitute.For<IEventBus>(), NullLogger<SessionAutosaveService>.Instance,
                TimeSpan.FromMilliseconds(40));

        [Fact]
        public async Task A_tab_that_only_receives_ticks_does_not_displace_the_tab_the_user_closed()
        {
            var dir = TestTemp.NewDir("att-autosave-ticks-");
            try
            {
                var lib = new WorkspaceLibraryService(NullLogger<WorkspaceLibraryService>.Instance, new TempWorkspacePaths())
                    { LibraryDirectoryOverride = dir };

                // Two terminal tabs on live markets. BTC is the one the user is working in; ETH
                // is open in the background.
                var btc = LiveStore("BTC/USD");
                var eth = LiveStore("ETH/USD");
                var working = RealAutosave(btc, lib);
                using var background = RealAutosave(eth, lib);

                // Both tabs take their first sample, so both slots exist.
                Tick(btc, 119, 100.5);
                Tick(eth, 119, 100.5);
                var deadline = Environment.TickCount64 + 3000;
                while (lib.GetAllProfilesWithTimes().Count(p => p.Name.StartsWith(
                           SessionAutosaveService.LastSessionProfileName, StringComparison.Ordinal)) < 2
                       && Environment.TickCount64 < deadline)
                    await Task.Delay(20);
                Assert.True(lib.GetAllProfilesWithTimes().Count(p => p.Name.StartsWith(
                                SessionAutosaveService.LastSessionProfileName, StringComparison.Ordinal)) == 2,
                    "precondition: both tabs' first samples should have written their slots");

                // The user closes the BTC tab: the shutdown hook's final save, then the circuit goes.
                working.SaveNow();
                working.Dispose();

                // The ETH tab sits untouched while its market trades through several sample
                // intervals: the forming bar moves, then a new one opens.
                int startBefore = eth.State.ViewportStartIndex;
                for (int i = 0; i < 12; i++)
                {
                    Tick(eth, i < 6 ? 119 : 120, 100 + i * 0.1);
                    await Task.Delay(25);
                }
                await Task.Delay(120);   // past the last sample
                Assert.True(eth.State.ViewportStartIndex != startBefore,
                    "vacuity: the new bar did not scroll the background chart, so this test no longer "
                  + "covers the one tick-driven change that reaches the saved configuration");

                // The tray's "Restore workspaces to browser" opens a new tab, which resumes.
                var init = Substitute.For<IWorkspaceInitializer>();
                var fresh = new WorkspaceStore(new EventBus(), new ViewportRangeCalculator(),
                    new ViewportNavigationService(), new VolumeStateService());
                using var reopened = RealAutosave(fresh, lib, init);
                Assert.True(reopened.TryResumeAtStartup());

                var restored = (WorkspaceConfiguration)init.ReceivedCalls()
                    .Single(c => c.GetMethodInfo().Name == nameof(IWorkspaceInitializer.RestoreWorkspace))
                    .GetArguments()[0]!;
                Assert.True(restored.Tabs[0].Symbol == "BTC/USD",
                    $"the reopened tab resumed {restored.Tabs[0].Symbol}, the tab that only received ticks, "
                  + "instead of BTC/USD, the tab the user closed");
            }
            finally { System.IO.Directory.Delete(dir, recursive: true); }
        }

        private static int Writes(Harness h) =>
            h.Library.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(IWorkspaceLibraryService.SaveProfile));

        private static async Task<bool> WaitForWritesAsync(Harness h, int n, int timeoutMs = 3000)
        {
            var deadline = Environment.TickCount64 + timeoutMs;
            while (Writes(h) < n && Environment.TickCount64 < deadline) await Task.Delay(10);
            return Writes(h) >= n;
        }

        [Fact]
        public async Task A_sample_that_would_write_the_same_workspace_writes_nothing_but_a_change_is_written()
        {
            var h = new Harness();
            using var svc = h.Build(interval: TimeSpan.FromMilliseconds(30));
            h.MakeMeaningful();

            h.StateSubject.OnNext(h.State);
            Assert.True(await WaitForWritesAsync(h, 1), "the first sample of a meaningful session never wrote its slot");
            // Ticks: the state object changes, what would be written does not.
            for (int i = 0; i < 5; i++) { h.StateSubject.OnNext(h.State with { }); await Task.Delay(40); }
            Assert.True(Writes(h) == 1,
                $"five samples that would write the same workspace rewrote the slot {Writes(h) - 1} time(s)");

            // The user changes the chart: written on the next sample.
            h.State = h.State with { Identity = new ChartIdentity("Spot", "Bitstamp", "ETH/USD", "1h") };
            h.StateSubject.OnNext(h.State);
            Assert.True(await WaitForWritesAsync(h, 2), "a changed workspace was never written");
        }

        [Fact]
        public async Task An_unchanged_session_rewrites_its_slot_if_the_slot_was_pruned()
        {
            // Another session's resume prunes all but the newest few slots. A session that no
            // longer writes on ticks can fall out of them while still open; its next sample
            // must put the slot back rather than leave the session with nothing on disk.
            var h = new Harness();
            using var svc = h.Build(interval: TimeSpan.FromMilliseconds(30));
            h.MakeMeaningful();
            h.Library.GetAllProfilesWithTimes().Returns(Array.Empty<(string, DateTime)>());

            h.StateSubject.OnNext(h.State);
            Assert.True(await WaitForWritesAsync(h, 1));
            h.StateSubject.OnNext(h.State with { });
            Assert.True(await WaitForWritesAsync(h, 2), "the slot is not on disk, and the unchanged sample did not write it back");
        }

        [Fact]
        public void SaveNow_writes_even_when_nothing_changed_since_the_last_save()
        {
            // The shutdown hooks' save is how resume learns which session the user left last:
            // its write time is the signal, so it is never skipped as "unchanged".
            var h = new Harness();
            using var svc = h.Build();
            h.MakeMeaningful();

            svc.SaveNow();
            svc.SaveNow();

            Assert.True(Writes(h) == 2,
                "the second SaveNow, with nothing changed and the slot on disk, was skipped — so a "
              + "shutdown save no longer marks this as the session the user left last");
        }

        [Fact]
        public void A_session_saved_before_per_slot_autosave_still_resumes()
        {
            // The legacy unsuffixed name has to keep working, or the upgrade silently throws
            // away the session the user had.
            var h = new Harness();
            using var svc = h.Build();
            var legacy = new WorkspaceConfiguration { Tabs = new List<TabConfiguration> { new() } };

            h.SeedSlot(SessionAutosaveService.LastSessionProfileName, DateTime.UtcNow, legacy);

            Assert.True(svc.TryResumeAtStartup());
            h.Initializer.Received(1).RestoreWorkspace(legacy);
        }

        [Fact]
        public void Old_slots_are_pruned_but_recent_ones_are_left_alone()
        {
            // Slots accumulate one per session otherwise. Pruning must not touch the recent
            // ones, because another tab may still be running and writing to its own.
            var h = new Harness();
            using var svc = h.Build();

            for (int i = 0; i < 7; i++)
            {
                h.SeedSlot($"__last-session__{i}", DateTime.UtcNow.AddMinutes(-i),
                    new WorkspaceConfiguration { Tabs = new List<TabConfiguration> { new() } });
            }

            Assert.True(svc.TryResumeAtStartup());

            var deleted = h.Library.ReceivedCalls()
                .Where(c => c.GetMethodInfo().Name == nameof(IWorkspaceLibraryService.DeleteProfile))
                .Select(c => (string)c.GetArguments()[0]!)
                .ToList();

            // Four newest kept, three oldest pruned.
            Assert.Equal(new[] { "__last-session__4", "__last-session__5", "__last-session__6" },
                         deleted.OrderBy(x => x, StringComparer.Ordinal).ToArray());
        }

        [Fact]
        public void Every_session_slot_is_hidden_from_the_workspace_list()
        {
            var dir = TestTemp.NewDir("att-autosave-slots-");
            try
            {
                var lib = new WorkspaceLibraryService(NullLogger<WorkspaceLibraryService>.Instance, new TempWorkspacePaths())
                    { LibraryDirectoryOverride = dir };
                System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "my-setup.json"), "{}");
                System.IO.File.WriteAllText(System.IO.Path.Combine(dir,
                    SessionAutosaveService.LastSessionProfileName + "deadbeef.json"), "{}");

                var profiles = lib.GetAvailableProfiles();

                Assert.Contains("my-setup", profiles);
                // The filter used to be an equality check on the one fixed name, which would
                // have leaked every per-session slot into the user's workspace list.
                Assert.DoesNotContain(profiles,
                    p => p.StartsWith(SessionAutosaveService.LastSessionProfileName, StringComparison.Ordinal));
            }
            finally { System.IO.Directory.Delete(dir, recursive: true); }
        }

        [Fact]
        public void Reserved_profile_is_hidden_from_the_workspace_list()
        {
            var dir = TestTemp.NewDir("att-autosave-");
            try
            {
                var lib = new WorkspaceLibraryService(NullLogger<WorkspaceLibraryService>.Instance, new TempWorkspacePaths())
                    { LibraryDirectoryOverride = dir };
                System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "my-setup.json"), "{}");
                System.IO.File.WriteAllText(System.IO.Path.Combine(dir,
                    SessionAutosaveService.LastSessionProfileName + ".json"), "{}");

                var profiles = lib.GetAvailableProfiles();

                Assert.Contains("my-setup", profiles);
                Assert.DoesNotContain(SessionAutosaveService.LastSessionProfileName, profiles);
            }
            finally { System.IO.Directory.Delete(dir, recursive: true); }
        }
    }
}
