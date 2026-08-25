using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Workspaces must follow the same per-user routing as every other piece of user state.
    ///
    /// <see cref="WorkspaceLibraryService"/> used to build its own path from
    /// <c>Environment.GetFolderPath(LocalApplicationData)</c> and ignore
    /// <see cref="IPlatformPathService"/> entirely. On the hosted server that meant every
    /// logged-in user shared ONE workspace library: saved profiles were visible to everyone, and
    /// the <c>__last-session__</c> autosave was overwritten by whoever used the terminal last.
    /// Settings, strategies and paper accounts were always routed correctly — workspaces were the
    /// one thing that wasn't.
    /// </summary>
    public sealed class WorkspacePerUserIsolationTests
    {
        [Fact]
        public void TwoUsers_DoNotSeeEachOthersProfiles()
        {
            var alice = new TempWorkspacePaths();
            var bob = new TempWorkspacePaths();

            var aliceLib = new WorkspaceLibraryService(NullLogger<WorkspaceLibraryService>.Instance, alice);
            var bobLib = new WorkspaceLibraryService(NullLogger<WorkspaceLibraryService>.Instance, bob);

            aliceLib.SaveProfile("my-setup", new WorkspaceConfiguration { Symbol = "BTC/USD" });

            Assert.Contains("my-setup", aliceLib.GetAvailableProfiles());
            Assert.DoesNotContain("my-setup", bobLib.GetAvailableProfiles());
            Assert.Null(bobLib.LoadProfile("my-setup"));
        }

        [Fact]
        public void OneUsersAutosave_DoesNotClobberAnothers()
        {
            // The specific failure: a single shared __last-session__.json, so the last person to
            // use the terminal decided what everyone else resumed into.
            var alice = new TempWorkspacePaths();
            var bob = new TempWorkspacePaths();

            var aliceLib = new WorkspaceLibraryService(NullLogger<WorkspaceLibraryService>.Instance, alice);
            var bobLib = new WorkspaceLibraryService(NullLogger<WorkspaceLibraryService>.Instance, bob);

            aliceLib.SaveProfile("__last-session__", new WorkspaceConfiguration { Symbol = "BTC/USD" });
            bobLib.SaveProfile("__last-session__", new WorkspaceConfiguration { Symbol = "ETH/USD" });

            Assert.Equal("BTC/USD", aliceLib.LoadProfile("__last-session__")!.Symbol);
            Assert.Equal("ETH/USD", bobLib.LoadProfile("__last-session__")!.Symbol);
        }

        [Fact]
        public void TheLibraryLivesUnderTheProvidedAppDataDirectory()
        {
            var paths = new TempWorkspacePaths();
            var lib = new WorkspaceLibraryService(NullLogger<WorkspaceLibraryService>.Instance, paths);

            lib.SaveProfile("somewhere", new WorkspaceConfiguration());

            // Desktop keeps its existing location because AppDataDirectory is
            // ~/.local/share/AccessibleTrader there; hosted gets users/<id>/Workspaces.
            var expected = Path.Combine(paths.AppDataDirectory, "Workspaces", "somewhere.json");
            Assert.True(File.Exists(expected), $"expected the profile at {expected}");
        }
    }
}
