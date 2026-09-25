using System.Reflection;
using System.Text.RegularExpressions;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Every test class that touches the WebHost's global circuit registries declares
    /// <c>[Collection("CircuitCoverage")]</c>, and every class that declares it still touches
    /// them. Same two-direction shape as <see cref="ProviderCredentialBridgeEnrollmentTests"/>;
    /// see <see cref="CircuitCoverageCollection"/> for the flake that made it necessary.
    /// </summary>
    [Collection("CircuitCoverage")]
    public class CircuitCoverageEnrollmentTests
    {
        private const string CollectionName = "CircuitCoverage";

        // Contact, on source with comments and strings stripped. Polling through the harness is
        // contact: LocalBackgroundMonitor.PollOnceAsync reads CircuitAlertCoverage every pass.
        private static readonly (Regex Pattern, string Reason)[] Contact =
        {
            (new Regex(@"\bnew\s+HeadlessMonitorHarness\s*\(", RegexOptions.Compiled), "polls the monitor through HeadlessMonitorHarness"),
            (new Regex(@"\bCircuitAlertCoverage\s*\.", RegexOptions.Compiled), "reads or writes CircuitAlertCoverage"),
            (new Regex(@"\bBrowserPresence\s*\.", RegexOptions.Compiled), "reads or writes BrowserPresence"),
        };

        private static readonly Regex DeclarationRegex = new(@"\b(?:class|record|struct)\s+([A-Za-z_]\w*)", RegexOptions.Compiled);

        private sealed record ScannedFile(string Name, IReadOnlyList<string> Declared, string? Reason);

        private static readonly Lazy<IReadOnlyList<ScannedFile>> _files = new(() =>
        {
            var root = Path.Combine(RepoPaths.RepoRoot(), "AccessibleTrader.Tests");
            var guardFile = nameof(CircuitCoverageEnrollmentTests) + ".cs";
            var list = new List<ScannedFile>();
            foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
            {
                var norm = file.Replace('\\', '/');
                if (norm.Contains("/obj/") || norm.Contains("/bin/") || Path.GetFileName(file) == guardFile) continue;
                var stripped = PipelineIdentityAndResilienceTests.StripCommentsAndStrings(File.ReadAllText(file));
                var reason = Contact.FirstOrDefault(c => c.Pattern.IsMatch(stripped)).Reason;
                list.Add(new ScannedFile(Path.GetFileName(file),
                    DeclarationRegex.Matches(stripped).Select(m => m.Groups[1].Value).Distinct().ToList(), reason));
            }
            return list;
        });

        private static IEnumerable<Type> TestClasses() =>
            typeof(CircuitCoverageEnrollmentTests).Assembly.GetTypes()
                .Where(t => t.IsClass && !t.IsAbstract
                         && t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
                             .Any(m => m.GetCustomAttributes(true).OfType<FactAttribute>().Any()));

        private static bool IsEnrolled(Type t) =>
            t.CustomAttributes.Any(a => a.AttributeType == typeof(CollectionAttribute)
                                     && a.ConstructorArguments.Count == 1
                                     && CollectionName.Equals(a.ConstructorArguments[0].Value as string, StringComparison.Ordinal));

        private static string? Reason(Type t)
        {
            var chain = new List<string>();
            for (var cur = t; cur != null; cur = cur.DeclaringType) chain.Add(cur.Name.Split('`')[0]);
            return _files.Value
                .Where(f => f.Reason != null && chain.All(f.Declared.Contains))
                .Select(f => f.Reason + " in " + f.Name)
                .FirstOrDefault();
        }

        [Fact]
        public void Every_test_class_that_touches_the_circuit_registries_is_enrolled()
        {
            var missing = TestClasses()
                .Select(t => (Type: t, Reason: Reason(t)))
                .Where(x => x.Reason != null && !IsEnrolled(x.Type))
                .OrderBy(x => x.Type.FullName, StringComparer.Ordinal)
                .ToList();

            Assert.True(missing.Count == 0,
                "These test classes touch CircuitAlertCoverage or BrowserPresence (process-global) but do "
              + "not declare [Collection(\"" + CollectionName + "\")], so they run in parallel with the "
              + "classes that register pretend browser circuits, and go silent when one covers their "
              + "symbol:\n" + string.Join("\n", missing.Select(x => "  " + x.Type.FullName + " — " + x.Reason)));
        }

        [Fact]
        public void Every_enrolled_test_class_still_touches_them()
        {
            // The reverse direction, and the scan's vacuity check: a blind scan would list every
            // enrolled class here.
            var unjustified = TestClasses()
                .Where(t => IsEnrolled(t) && t != typeof(CircuitCoverageEnrollmentTests) && Reason(t) == null)
                .Select(t => t.FullName!)
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToList();

            Assert.True(unjustified.Count == 0,
                "Enrolled in " + CollectionName + " but the scan finds no contact with the circuit "
              + "registries. Drop the attribute, or teach the scan the route:\n"
              + string.Join("\n", unjustified.Select(n => "  " + n)));
        }

        [Fact]
        public void The_scan_is_not_vacuous()
        {
            var flagged = TestClasses().Where(t => Reason(t) != null).Select(t => t.FullName!).ToHashSet();
            Assert.True(flagged.Count >= 8,
                "The scan found only " + flagged.Count + " classes touching the circuit registries: "
              + string.Join(", ", flagged.OrderBy(s => s)));

            // One anchor per contact route.
            foreach (var anchor in new[]
            {
                "AccessibleTrader.Tests.ProfileNarrationTests",            // polls through the harness only
                "AccessibleTrader.Tests.WebHost.HeadlessSessionTests",      // registers circuits
                "AccessibleTrader.Tests.WebHost.BrowserPresenceTests",      // BrowserPresence
            })
            {
                Assert.True(flagged.Contains(anchor), "Anchor class not flagged by the scan: " + anchor);
            }
        }
    }
}
