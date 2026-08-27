namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Temp directories for tests, under one run-scoped root that is deleted when the
    /// test process exits.
    ///
    /// <para>
    /// Written 2026-08-26 after clearing this machine's <c>/tmp</c>: the suite had left
    /// roughly eight thousand directories behind — 3,789 <c>att-shortcut-*</c>, 1,084
    /// <c>at-webhost-shortcut-*</c>, 1,080 <c>att-paths-*</c>, 1,080
    /// <c>at-shortcut-tests-*</c> and more. Each test built an
    /// <c>IPlatformPathService</c> fake rooted in a fresh <c>Path.GetTempPath()</c>
    /// subdirectory and nothing ever removed it, so every run of the suite added
    /// another few hundred. On a box whose <c>/tmp</c> is a tmpfs that is a slow leak
    /// of real memory, and on CI it is a slow leak of runner disk.
    /// </para>
    ///
    /// <para>
    /// A run-scoped root rather than per-class cleanup because the leak keeps coming
    /// back the same way: someone adds a fake path service, forgets the
    /// <c>IDisposable</c>, and nothing fails. One root removed at process exit is
    /// robust to that — and if the process is killed outright, what remains is a
    /// single obviously-named directory instead of hundreds of anonymous ones.
    /// </para>
    /// </summary>
    public static class TestTemp
    {
        private static readonly string Root = Path.Combine(
            Path.GetTempPath(), "att-tests-" + Environment.ProcessId + "-" + Guid.NewGuid().ToString("N")[..8]);

        static TestTemp()
        {
            Directory.CreateDirectory(Root);
            AppDomain.CurrentDomain.ProcessExit += (_, _) => TryDelete(Root);
        }

        /// <summary>A fresh empty directory under the run root. <paramref name="prefix"/>
        /// is kept only so a directory left behind by a killed run still says where it
        /// came from.</summary>
        public static string NewDir(string prefix)
        {
            string dir = Path.Combine(Root, prefix + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        /// <summary>A path under the run root that is NOT created — for callers that
        /// need the directory to be absent, or that create it themselves.</summary>
        public static string NewPath(string prefix) =>
            Path.Combine(Root, prefix + Guid.NewGuid().ToString("N"));

        private static void TryDelete(string dir)
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* best effort at exit */ }
        }
    }

    /// <summary>
    /// The guard that keeps the leak from coming back. Converting the 64 existing
    /// sites was the easy half; the reason there were 64 is that nothing ever said
    /// no. This one does.
    /// </summary>
    public class TestTempScanTests
    {
        [Fact]
        public void No_test_reaches_for_the_system_temp_path_directly()
        {
            var root = SourceRoot();
            var offenders = new List<string>();

            foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
            {
                if (Path.GetFileName(file) == "TestTemp.cs") continue;
                if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                 || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")) continue;

                var lines = File.ReadAllLines(file);
                for (int i = 0; i < lines.Length; i++)
                {
                    if (lines[i].Contains("GetTempPath") || lines[i].Contains("CreateTempSubdirectory"))
                        offenders.Add($"{Path.GetFileName(file)}:{i + 1}");
                }
            }

            Assert.True(offenders.Count == 0,
                "Temp directories must come from TestTemp, which deletes them when the test process " +
                "exits. Directly-created ones are never cleaned up: on 2026-08-26 this suite had left " +
                "roughly 8,000 directories in /tmp on the dev box. Use TestTemp.NewDir/NewPath in:\n  " +
                string.Join("\n  ", offenders));
        }

        /// <summary>
        /// Anti-vacuity: a scan that found no files at all would report zero offenders
        /// and pass for the wrong reason. This is the same failure the repo has hit
        /// before with source-scanning guards.
        /// </summary>
        [Fact]
        public void The_scan_above_is_actually_reading_this_test_project()
        {
            var files = Directory.EnumerateFiles(SourceRoot(), "*.cs", SearchOption.AllDirectories).ToList();
            Assert.True(files.Count > 100, $"Expected to scan the whole test project; found {files.Count} files.");
            Assert.Contains(files, f => Path.GetFileName(f) == "TestTemp.cs");
        }

        /// <summary>Walks up from the test binary to the directory holding the .csproj.</summary>
        private static string SourceRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AccessibleTrader.Tests.csproj")))
                dir = dir.Parent;
            Assert.NotNull(dir);
            return dir!.FullName;
        }
    }
}
