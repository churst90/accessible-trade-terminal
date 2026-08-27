namespace AccessibleTrader.Tests
{
    /// <summary>
    /// One rule, scanned across every shipping project: **a service does not build its own
    /// per-user path.** It asks <c>IPlatformPathService</c>, and the two or three files that
    /// implement that interface are the only places <see cref="Environment.GetFolderPath(Environment.SpecialFolder)"/> may
    /// appear.
    ///
    /// <para>
    /// This exists because the same defect has now shipped three times.
    /// <c>WorkspacePerUserIsolationTests</c> and <c>IndicatorPrefsPerUserIsolationTests</c> both
    /// exist, both are good, and both docstrings describe the identical bug — a service composing
    /// <c>Path.Combine(Environment.GetFolderPath(LocalApplicationData), "AccessibleTrader", …)</c>
    /// instead of taking the path service. Each was fixed at the site where it was reported. Only
    /// a scan catches the fourth one, and the scan is what turned up the third.
    /// </para>
    ///
    /// <para>
    /// Two things make the hand-rolled version wrong rather than merely inelegant. On Unix
    /// <c>GetFolderPath</c> returns an <b>empty string</b> when the directory it resolves to does
    /// not exist yet, so <c>Path.Combine</c> yields a RELATIVE path that resolves against the
    /// process's current directory — on the hosted server that is the deployment directory, which
    /// a redeploy replaces. And in the hosted, multi-user head there is no such thing as "the"
    /// local app data directory: <c>UserScopedPathService</c> exists precisely because two signed-in
    /// users must not share one.
    /// </para>
    /// </summary>
    public class PerUserPathPolicyTests
    {
        /// <summary>
        /// The files that ARE the path layer, plus the one caller that legitimately cannot use it.
        /// Every entry needs a reason; "it was already like that" is not one — that is what
        /// <see cref="KnownOffenders"/> is for.
        /// </summary>
        private static readonly Dictionary<string, string> Sanctioned = new()
        {
            ["PlatformPaths.cs"] =
                "Core's local-app-data resolver. This is the file that adds the absolute-path "
                + "guarantee GetFolderPath does not give you, so it is the one place that has to call it.",
            ["WebHostPathService.cs"] =
                "The WebHost's IPlatformPathService for the single-user desktop host.",
            ["UserScopedPathService.cs"] =
                "The WebHost's IPlatformPathService for the hosted multi-user head, which is the "
                + "whole reason this rule exists.",
            ["App.xaml.cs"] =
                "Windows last-chance crash handler. It runs when the app is already failing, "
                + "possibly before or because of DI, so it cannot resolve a service — and on "
                + "Windows GetFolderPath always returns a valid absolute path, so the Unix "
                + "empty-string failure mode does not apply.",
        };

        /// <summary>
        /// Real instances of the defect that are tracked but not yet fixed. **This list may only
        /// ever shrink.** Adding a file here is not a fix, and the test below fails if an entry
        /// stops being an offender, so a fix cannot leave a stale exemption behind.
        ///
        /// <para>
        /// Empty since 2026-08-22, when the last entry — <c>SchwabOAuthService.cs</c>, the third
        /// instance, found by this scan — was fixed: the refresh token now persists only through
        /// <c>PluginHostServices.SecureStorage</c>, and the legacy DPAPI file is located via the
        /// Windows-only <c>%APPDATA%</c> environment variable purely to migrate and delete it.
        /// </para>
        /// </summary>
        private static readonly Dictionary<string, string> KnownOffenders = new();

        private static string[] Sources() =>
            StrategyLibraryPolicyTests.ShippingProjectDirectories()
                .SelectMany(d => Directory.EnumerateFiles(d, "*.*", SearchOption.AllDirectories))
                .Where(f => f.EndsWith(".cs") || f.EndsWith(".razor"))
                .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                         && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
                .ToArray();

        /// <summary>
        /// Every file that calls <see cref="Environment.GetFolderPath(Environment.SpecialFolder)"/> outside a comment, by
        /// file name. Comments are stripped first — <c>IndicatorPreferencesService</c> and
        /// <c>WorkspaceLibraryService</c> both *describe* this bug in their docstrings, having
        /// each been the victim of it once, and a scan that flagged them would be flagging the
        /// documentation of the fix.
        /// </summary>
        private static (Dictionary<string, string> hits, int scanned) Scan()
        {
            var hits = new Dictionary<string, string>();
            int scanned = 0;
            foreach (var file in Sources())
            {
                scanned++;
                var code = PipelineIdentityAndResilienceTests.StripCommentsAndStrings(File.ReadAllText(file));
                if (!code.Contains("Environment.GetFolderPath")) continue;
                hits[Path.GetFileName(file)] = file;
            }
            return (hits, scanned);
        }

        [Fact]
        public void NoServiceBuildsItsOwnLocalAppDataPath()
        {
            var (hits, scanned) = Scan();

            Assert.True(scanned >= 300,
                $"the scan only saw {scanned} shipping sources; it is not covering the tree. "
                + "Fix the discovery, do not lower the floor.");

            var offenders = hits.Keys
                .Where(f => !Sanctioned.ContainsKey(f) && !KnownOffenders.ContainsKey(f))
                .OrderBy(f => f, StringComparer.Ordinal)
                .ToList();

            Assert.True(offenders.Count == 0,
                "These files build a local-app-data path themselves instead of taking "
                + "IPlatformPathService. On Unix GetFolderPath returns \"\" when the directory does "
                + "not exist yet, which turns Path.Combine into a RELATIVE path, and in the hosted "
                + "head there is no single per-user directory to resolve:\n  "
                + string.Join("\n  ", offenders.Select(f => $"{f}  ({hits[f]})")));
        }

        /// <summary>
        /// The exemption lists have to stay honest in both directions. An entry that is no longer
        /// an offender means the bug was fixed and the exemption outlived it — which is how a
        /// baseline quietly becomes permission. Removing the file from the list is part of the fix.
        /// </summary>
        [Fact]
        public void EveryExemptionIsStillLoadBearing()
        {
            var (hits, _) = Scan();

            var stale = Sanctioned.Keys.Concat(KnownOffenders.Keys)
                .Where(f => !hits.ContainsKey(f))
                .OrderBy(f => f, StringComparer.Ordinal)
                .ToList();

            Assert.True(stale.Count == 0,
                "These files are exempted from the per-user-path rule but no longer call "
                + "Environment.GetFolderPath. If that is because they were fixed, delete the "
                + "exemption — the list must only ever shrink:\n  " + string.Join("\n  ", stale));
        }

        /// <summary>
        /// The tracked-but-unfixed list is not a place to park new work. If it grows, the rule has
        /// stopped being a rule.
        /// </summary>
        [Fact]
        public void TheKnownOffenderListHasNotGrown()
        {
            Assert.True(KnownOffenders.Count == 0,
                "A file was added to KnownOffenders. That is not a fix — it is a record that the "
                + "defect shipped again. Fix it, or raise this number deliberately and say why in "
                + "docs/TODO.md. The list reached zero on 2026-08-22 and must stay there. "
                + "Current list: " + string.Join(", ", KnownOffenders.Keys));
        }

        // ── Rule 2: asking the right path service is not enough. You have to ask it LATE ──

        /// <summary>
        /// A path service that answers differently once the user is known — which is exactly what
        /// <c>UserScopedPathService</c> does when the circuit sets <c>ICurrentUser</c>.
        /// </summary>
        private sealed class ShiftingPaths : AccessibleTrader.Core.Services.IPlatformPathService
        {
            private readonly string _root = TestTemp.NewPath("atc-shifting-");
            public int Reads { get; private set; }
            public string User { get; set; } = "anon";
            public string AppDataDirectory
            {
                get
                {
                    Reads++;
                    var dir = Path.Combine(_root, "users", User);
                    Directory.CreateDirectory(dir);
                    return dir;
                }
            }
            public string CacheDirectory => AppDataDirectory;
        }

        /// <summary>
        /// <c>ShortcutManager</c> resolved its file path in its CONSTRUCTOR, and
        /// <c>WebHostBrowserCircuitHandler</c> takes it as a constructor parameter — so on the
        /// hosted head the object always existed before <c>ICurrentUser.Set</c> had run, and the
        /// path always came out as <c>users/anon</c>. Every signed-in user therefore read and
        /// wrote one shared <c>shortcuts.json</c>: rebinding a key silently changed a stranger's
        /// trading keyboard.
        ///
        /// <para>
        /// This is the same defect <c>WorkspacePerUserIsolationTests</c> and
        /// <c>IndicatorPrefsPerUserIsolationTests</c> each proved for their own service. Those
        /// tests hand each service a DIFFERENT path service and cannot fail for one that captures
        /// the path too early — the two managers below share ONE path service, which is what makes
        /// the timing visible.
        /// </para>
        /// </summary>
        [Fact]
        public void ShortcutManager_ResolvesItsPathAfterTheUserIsKnown()
        {
            var paths = new ShiftingPaths();

            // Construction happens while the circuit still says "anon" — the real ordering.
            var alice = new AccessibleTrader.Core.Services.ShortcutManager(paths);
            Assert.Equal(0, paths.Reads);   // nothing may be read yet

            paths.User = "alice";
            alice.UpdateBinding(AccessibleTrader.Core.Models.SystemCommand.NavLeft, "F7");
            Assert.True(paths.Reads > 0);

            // Bob's circuit opens next and sets a different identity before anything is read.
            var bob = new AccessibleTrader.Core.Services.ShortcutManager(paths);
            paths.User = "bob";

            var bobsLeft = bob.CurrentProfile.Shortcuts.First(
                s => s.Command == AccessibleTrader.Core.Models.SystemCommand.NavLeft);
            Assert.Equal("LEFT", bobsLeft.Key);
            Assert.Equal(AccessibleTrader.Core.Models.SystemCommand.NavLeft,
                bob.GetCommand("LEFT", false, false, false));

            // …and Alice still has hers.
            paths.User = "alice";
            var reloaded = new AccessibleTrader.Core.Services.ShortcutManager(paths);
            Assert.Equal(AccessibleTrader.Core.Models.SystemCommand.NavLeft,
                reloaded.GetCommand("F7", false, false, false));
        }

        /// <summary>
        /// The structural half, and the one that closes the class rather than the instance.
        ///
        /// <para>
        /// An object must exist before any of its methods can be called, so every service in
        /// <c>WebHostBrowserCircuitHandler</c>'s constructor is necessarily built BEFORE
        /// <c>OnCircuitOpenedAsync</c> runs <c>ICurrentUser.Set</c>. Any user-scoped service
        /// there is one lazy-loading mistake away from serving the wrong user's data — the
        /// shortcuts bug was precisely that. The fix for a new dependency is to resolve it from
        /// <c>_scope</c> INSIDE <c>OnCircuitOpenedAsync</c>, after <c>Set</c>, not to add it here.
        /// </para>
        /// </summary>
        [Fact]
        public void TheCircuitHandlerTakesNothingUserScopedInItsConstructor()
        {
            var type = typeof(AccessibleTrader.WebHost.Services.WebHostBrowserCircuitHandler);
            var ctors = type.GetConstructors();
            Assert.Single(ctors);

            // Allowed: things that cannot be user-scoped by construction. ILogger<T> is
            // stateless per category; IServiceProvider IS the escape hatch this rule points at.
            bool Allowed(Type t) =>
                t == typeof(IServiceProvider)
                || (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(Microsoft.Extensions.Logging.ILogger<>));

            var offenders = ctors[0].GetParameters()
                .Where(p => !Allowed(p.ParameterType))
                .Select(p => $"{p.ParameterType.Name} {p.Name}")
                .ToList();

            Assert.True(offenders.Count == 0,
                "WebHostBrowserCircuitHandler's constructor runs before OnCircuitOpenedAsync sets "
                + "the per-circuit identity, so anything resolved here is built while the user is "
                + "still \"anon\". IShortcutManager sat here and every hosted user shared one "
                + "shortcuts.json because of it. Resolve these from the injected IServiceProvider "
                + "inside OnCircuitOpenedAsync instead, after ICurrentUser.Set:\n  "
                + string.Join("\n  ", offenders));
        }
    }
}
