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

            h.Library.Received(1).SaveWorkspaceProfile(
                SessionAutosaveService.LastSessionProfileName, h.Store);
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
                SessionAutosaveService.LastSessionProfileName, h.Store);
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
            h.Library.LoadProfile(SessionAutosaveService.LastSessionProfileName).Returns(config);

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

            h.Library.LoadProfile(SessionAutosaveService.LastSessionProfileName)
                .Returns(new WorkspaceConfiguration { Tabs = new List<TabConfiguration> { new() } });
            h.MakeMeaningful(); // user already loaded something
            Assert.False(svc.TryResumeAtStartup());
            h.Initializer.DidNotReceiveWithAnyArgs().RestoreWorkspace(default!);
        }

        [Fact]
        public void Failed_resume_lands_on_the_blank_chart_not_a_crash()
        {
            var h = new Harness();
            using var svc = h.Build();
            h.Library.LoadProfile(SessionAutosaveService.LastSessionProfileName)
                .Returns(new WorkspaceConfiguration { Tabs = new List<TabConfiguration> { new() } });
            h.Initializer.When(i => i.RestoreWorkspace(Arg.Any<WorkspaceConfiguration>()))
                .Do(_ => throw new InvalidOperationException("corrupt series"));

            Assert.False(svc.TryResumeAtStartup());
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
