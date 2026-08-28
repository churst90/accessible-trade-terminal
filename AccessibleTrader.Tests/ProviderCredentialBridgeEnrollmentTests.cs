using System.Reflection;
using System.Text.RegularExpressions;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Enforces the rule the "ProviderCredentialBridge" collection depends on but nothing checked:
    /// every test class that touches the process-global <c>PluginHostServices</c> bridge —
    /// by constructing a real provider (whose constructor and request paths read
    /// <c>ApiKeys</c>/<c>HttpClientFactory</c>), by using <see cref="ProviderRoster"/>, or by
    /// installing anything into <c>PluginHostServices</c> — must carry
    /// <c>[Collection("ProviderCredentialBridge")]</c> DECLARED ON THAT EXACT CLASS.
    ///
    /// <para>
    /// Declared, not inherited and not on the containing class, because xUnit gives a NESTED test
    /// class its own collection regardless of the outer class's attribute — verified empirically
    /// in this repo during the de-DE rerun work, and the reason
    /// <c>ProviderFetchOhlcvTests.Alpaca</c> needed its own attribute while its siblings silently
    /// ran unserialized. Enrollment that only works for some class shapes is how this collection's
    /// two recorded flakes happened; the explicit form works for all of them.
    /// </para>
    ///
    /// <para>
    /// Two independent routes in the <c>AllTradingProvidersAreEnumeratedHere</c> shape: reflection
    /// over this assembly enumerates the test classes and their attributes; a source scan (comments
    /// and strings stripped) finds the bridge-touching sites. Subclass reruns (the
    /// <c>DeDeCultureTests</c> pattern) are caught through the reflection route by walking base
    /// types, so a clean file whose base class touches the bridge is still required to enroll.
    /// </para>
    /// </summary>
    [Collection("ProviderCredentialBridge")]
    public class ProviderCredentialBridgeEnrollmentTests
    {
        private const string CollectionName = "ProviderCredentialBridge";

        /// <summary>
        /// Non-test helper classes that are ALLOWED to touch the bridge from a file of their own.
        /// A test class that uses one of these touches the bridge indirectly, so each helper's
        /// name is also matched as an offender pattern in the callers' files. A new bridge-touching
        /// helper must be added here — <see cref="Bridge_touching_helpers_are_all_known"/> fails
        /// loudly until it is, which is what keeps the indirect route from going blind.
        /// </summary>
        private static readonly string[] KnownBridgeHelpers =
        {
            "ProviderRoster",
            "ProviderRosterDriftTests", // declared in ProviderRoster.cs; a test class, listed so the file's declarations are fully accounted for
            "FakeApiKeyCheckout",
            "ApiKeyCheckoutScope",      // returned by FakeApiKeyCheckout.Install; unreachable without naming the fake, listed for completeness

            // Booting the real Program.cs is bridge contact, and since 2026-08-27 it is contact
            // with ApiKeys and SecurityEvents too — those were null on the WebHost until then,
            // which was the defect. A host boot that does not restore them leaves the REAL
            // (empty) credential store installed for every later provider test; that is not a
            // race the collection can fix, so the harness snapshots as well as serialising.
            "WebHostIntegration",       // the factories that boot Program.cs
            "PluginBridgeScope",        // the snapshot/restore helper itself
            "HostedWebHostFixture",     // holds a factory and a PluginBridgeScope
        };

        private sealed record ScannedFile(string Path, string Stripped, IReadOnlyList<string> DeclaredNames, string? OffenceReason);

        private static readonly Lazy<IReadOnlyList<ScannedFile>> _files = new(ScanFiles);
        private static readonly Regex DeclarationRegex = new(@"\b(?:class|record|struct)\s+([A-Za-z_]\w*)", RegexOptions.Compiled);

        private static IReadOnlyList<ScannedFile> ScanFiles()
        {
            // Route 1 of the cross-check: the provider names come from reflection over the build
            // output, not from a hand-written list, so a new provider is an offender pattern the
            // day its plugin is referenced.
            var providerNames = ProviderRoster.Types.Select(t => t.Name).ToList();
            Assert.NotEmpty(providerNames);

            // Qualified receivers matter: `new AccessibleTrader.Plugins.Bitstamp.BitstampProvider()`
            // is the common spelling in this suite, and an unqualified-only pattern misses every one.
            var newProvider = new Regex(
                @"new\s+(?:[A-Za-z_][\w.]*\.)?(?:" + string.Join("|", providerNames.Select(Regex.Escape)) + @")\s*\(",
                RegexOptions.Compiled);
            var bridgeAccess = new Regex(@"\bPluginHostServices\s*\.", RegexOptions.Compiled);
            var rosterUse = new Regex(@"\bProviderRoster\s*\.", RegexOptions.Compiled);
            var helperUse = new Regex(@"\b(?:" + string.Join("|", KnownBridgeHelpers.Select(Regex.Escape)) + @")\b", RegexOptions.Compiled);

            var results = new List<ScannedFile>();
            var testsRoot = Path.Combine(RepoPaths.RepoRoot(), "AccessibleTrader.Tests");
            foreach (var file in Directory.EnumerateFiles(testsRoot, "*.cs", SearchOption.AllDirectories))
            {
                var norm = file.Replace('\\', '/');
                if (norm.Contains("/obj/") || norm.Contains("/bin/")) continue;

                var stripped = PipelineIdentityAndResilienceTests.StripCommentsAndStrings(File.ReadAllText(file));
                var declared = DeclarationRegex.Matches(stripped).Select(m => m.Groups[1].Value).Distinct().ToList();

                string? reason =
                    newProvider.IsMatch(stripped) ? "constructs a real provider" :
                    bridgeAccess.IsMatch(stripped) ? "reads or writes PluginHostServices" :
                    rosterUse.IsMatch(stripped) ? "uses ProviderRoster" :
                    helperUse.IsMatch(stripped) ? "uses a bridge-touching helper (" + helperUse.Match(stripped).Value + ")" :
                    null;

                results.Add(new ScannedFile(norm, stripped, declared, reason));
            }

            Assert.NotEmpty(results);
            return results;
        }

        private static IEnumerable<Type> TestClasses() =>
            typeof(ProviderCredentialBridgeEnrollmentTests).Assembly.GetTypes()
                .Where(t => t.IsClass && !t.IsAbstract
                         && t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
                             .Any(m => m.GetCustomAttributes(true).OfType<FactAttribute>().Any()));

        private static bool IsEnrolled(Type t) =>
            // CustomAttributeData is declared-only, which is the point: an attribute this check
            // can see is one xUnit honors on every class shape, nested and derived included.
            t.CustomAttributes.Any(a => a.AttributeType == typeof(CollectionAttribute)
                                     && a.ConstructorArguments.Count == 1
                                     && CollectionName.Equals(a.ConstructorArguments[0].Value as string, StringComparison.Ordinal));

        /// <summary>The scanned files that declare this type — the outermost name and the whole
        /// nesting chain must appear, so <c>Kraken</c> nested in the fetch suite does not match a
        /// <c>Kraken</c> declared elsewhere.</summary>
        private static IEnumerable<ScannedFile> FilesDeclaring(Type t)
        {
            var chain = new List<string>();
            for (var cur = t; cur != null; cur = cur.DeclaringType)
                chain.Add(cur.Name.Split('`')[0]);
            return _files.Value.Where(f => chain.All(n => f.DeclaredNames.Contains(n)));
        }

        private static string? OffenceReason(Type t)
        {
            foreach (var f in FilesDeclaring(t))
                if (f.OffenceReason != null)
                    return f.OffenceReason + " in " + Path.GetFileName(f.Path);

            // The subclass-rerun shape: the class body is empty and clean, the inherited facts
            // are not. Only bases from this assembly can oblige enrollment.
            for (var b = t.BaseType; b != null && b != typeof(object); b = b.BaseType)
            {
                if (b.Assembly != t.Assembly) continue;
                foreach (var f in FilesDeclaring(b))
                    if (f.OffenceReason != null)
                        return "inherits from " + b.Name + ", which " + f.OffenceReason + " in " + Path.GetFileName(f.Path);
            }
            return null;
        }

        [Fact]
        public void Every_test_class_that_touches_the_bridge_is_enrolled()
        {
            var missing = TestClasses()
                .Select(t => (Type: t, Reason: OffenceReason(t)))
                .Where(x => x.Reason != null && !IsEnrolled(x.Type))
                .OrderBy(x => x.Type.FullName, StringComparer.Ordinal)
                .ToList();

            Assert.True(missing.Count == 0,
                "These test classes touch the global PluginHostServices bridge but do not declare "
              + "[Collection(\"" + CollectionName + "\")] on the class itself, so xUnit runs them in "
              + "parallel with the classes that install fakes into that bridge — the recorded flake. "
              + "Note: the attribute on an OUTER class does not cover a nested class, and enrollment "
              + "must be declared on each concrete subclass too.\n"
              + string.Join("\n", missing.Select(x => "  " + x.Type.FullName + " — " + x.Reason)));
        }

        [Fact]
        public void Every_enrolled_test_class_still_touches_the_bridge()
        {
            // The reverse direction doubles as the scan's vacuity check: if the source scan went
            // blind (moved directory, broken regex), every currently-enrolled class would show up
            // here as unjustified, which is a much louder failure than the forward fact silently
            // passing over an empty offender set.
            var unjustified = TestClasses()
                .Where(t => IsEnrolled(t) && OffenceReason(t) == null)
                .OrderBy(t => t.FullName, StringComparer.Ordinal)
                .ToList();

            Assert.True(unjustified.Count == 0,
                "These test classes are enrolled in the " + CollectionName + " collection but the scan "
              + "finds no bridge contact. Either the class stopped touching the bridge (drop the "
              + "attribute — needless enrollment costs parallelism and disguises which classes are "
              + "load-bearing) or it touches the bridge through a helper this guard does not know "
              + "(add the helper to KnownBridgeHelpers):\n"
              + string.Join("\n", unjustified.Select(t => "  " + t.FullName)));
        }

        [Fact]
        public void Bridge_touching_helpers_are_all_known()
        {
            // A file that touches the bridge but declares no test class is a helper; tests reach
            // the bridge through it, and the caller-side scan only knows to look for helpers by
            // name. An unknown helper is therefore a hole in the forward fact, not a style issue.
            var testClassNames = TestClasses().Select(t => t.Name.Split('`')[0]).ToHashSet(StringComparer.Ordinal);
            var guardFile = nameof(ProviderCredentialBridgeEnrollmentTests) + ".cs";

            var unknown = _files.Value
                .Where(f => f.OffenceReason != null
                         && Path.GetFileName(f.Path) != guardFile
                         && !f.DeclaredNames.Any(testClassNames.Contains)
                         && f.DeclaredNames.Any(n => !KnownBridgeHelpers.Contains(n)))
                .Select(f => Path.GetFileName(f.Path) + " (declares " + string.Join(", ", f.DeclaredNames) + ")")
                .OrderBy(s => s, StringComparer.Ordinal)
                .ToList();

            Assert.True(unknown.Count == 0,
                "These files touch the PluginHostServices bridge but declare no test class and are "
              + "not in KnownBridgeHelpers, so test classes using them would pass the enrollment "
              + "scan while racing the bridge. Add each helper type to KnownBridgeHelpers:\n"
              + string.Join("\n", unknown.Select(s => "  " + s)));
        }

        [Fact]
        public void The_scan_is_not_vacuous()
        {
            // The qualified-receiver spelling is the one a lazy pattern misses — the first draft
            // of this very scan missed all of ProviderLiveStreamTests because of it. Prove the
            // pattern still matches it before trusting any green above.
            var probe = PipelineIdentityAndResilienceTests.StripCommentsAndStrings(
                "var p = new AccessibleTrader.Plugins.Kraken.KrakenProvider();");
            var providerNames = ProviderRoster.Types.Select(t => t.Name).ToList();
            var newProvider = new Regex(
                @"new\s+(?:[A-Za-z_][\w.]*\.)?(?:" + string.Join("|", providerNames.Select(Regex.Escape)) + @")\s*\(");
            Assert.Matches(newProvider, probe);

            var offenders = TestClasses().Where(t => OffenceReason(t) != null).Select(t => t.FullName!).ToHashSet();
            Assert.True(offenders.Count >= 15,
                "The enrollment scan found only " + offenders.Count + " bridge-touching test classes; "
              + "this suite has dozens. The scan has gone blind: " + string.Join(", ", offenders.OrderBy(s => s)));

            // Anchors, one per offence route: direct PluginHostServices install, provider
            // construction, roster use, and the inherited-facts shape.
            foreach (var anchor in new[]
            {
                "AccessibleTrader.Tests.LLMProviderTransportTests",
                "AccessibleTrader.Tests.BrokerParityTests",
                "AccessibleTrader.Tests.ProviderRosterDriftTests",
                "AccessibleTrader.Tests.ProviderFetchAlpacaUnderDeDe",
            })
            {
                Assert.True(offenders.Contains(anchor), "Anchor class not flagged by the scan: " + anchor);
            }
        }
    }
}
