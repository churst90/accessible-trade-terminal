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
            h.Library.Received(1).SaveWorkspaceProfile(
                Arg.Is<string>(n => n.StartsWith(SessionAutosaveService.LastSessionProfileName,
                                                 StringComparison.Ordinal)),
                h.Store);
        }

        [Fact]
        public void Blank_sessions_never_clobber_the_saved_one()
        {
            var h = new Harness();
            using var svc = h.Build();

            svc.SaveNow(); // store is WorkspaceState.Initial — blank

            h.Library.DidNotReceiveWithAnyArgs().SaveWorkspaceProfile(default!, default!);
        }

        [Fact]
        public void Demo_sessions_never_autosave()
        {
            var h = new Harness();
            using var svc = h.Build(demo: true);
            h.MakeMeaningful();

            svc.SaveNow();

            h.Library.DidNotReceiveWithAnyArgs().SaveWorkspaceProfile(default!, default!);
        }

        [Fact]
        public void Opt_out_setting_disables_autosave()
        {
            var h = new Harness();
            using var svc = h.Build(settingValue: false);
            h.MakeMeaningful();

            svc.SaveNow();

            h.Library.DidNotReceiveWithAnyArgs().SaveWorkspaceProfile(default!, default!);
        }

        [Fact]
        public async Task State_changes_are_sampled_into_periodic_saves()
        {
            var h = new Harness();
            using var svc = h.Build(interval: TimeSpan.FromMilliseconds(50));
            h.MakeMeaningful();

            h.StateSubject.OnNext(h.State); // activity within the sample window

            var deadline = Environment.TickCount64 + 3000;
            while (System.Linq.Enumerable.Count(h.Library.ReceivedCalls()) == 0 && Environment.TickCount64 < deadline)
                await Task.Delay(20);
            h.Library.Received().SaveWorkspaceProfile(
                Arg.Is<string>(n => n.StartsWith(SessionAutosaveService.LastSessionProfileName,
                                                 StringComparison.Ordinal)),
                h.Store);
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
                .First(c => c.GetMethodInfo().Name == nameof(IWorkspaceLibraryService.SaveWorkspaceProfile))
                .GetArguments()[0]!;
            string nameB = (string)b.Library.ReceivedCalls()
                .First(c => c.GetMethodInfo().Name == nameof(IWorkspaceLibraryService.SaveWorkspaceProfile))
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
