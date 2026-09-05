using System.Text.RegularExpressions;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// No test file may locate a provider's <see cref="HttpClient"/> BY POSITION.
    ///
    /// <para>
    /// The defect class (hosted notes §5a): a helper that took "the first HttpClient-typed
    /// field" rode CLR declaration order, and on a provider with two clients — Tradier, Oanda,
    /// MEXC — it was one field reordering away from faking the stream client and letting
    /// <c>PlaceOrderAsync</c> reach the real venue from a test. It is the only recent defect
    /// class whose worst case is an outbound side effect rather than a wrong number, which is
    /// why it gets a scan and not just a fix. The 25 sites that did it are on
    /// <see cref="HttpClientSwap"/> now, which replaces every client the object holds.
    /// </para>
    ///
    /// <para>
    /// What is allowed: replacing ALL matching fields (a <c>Where</c>/<c>foreach</c> over the
    /// type), or picking one BY NAME (<c>BrokerParityTests.Swap</c> matches <c>_httpClient</c> /
    /// <c>_http</c>). What is not: <c>First</c>, <c>FirstOrDefault</c>, <c>Single</c>,
    /// <c>Last</c>, <c>ElementAt</c>, <c>[0]</c>, or a loop that <c>break</c>s on the first hit,
    /// over a type-only filter.
    /// </para>
    /// </summary>
    public class HttpClientSwapScanTests
    {
        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AccessibleTrader.slnx")))
                dir = dir.Parent;
            Assert.NotNull(dir);
            return dir!.FullName;
        }

        private static readonly Regex TypeFilter = new(@"typeof\s*\(\s*HttpClient\s*\)", RegexOptions.Compiled);
        private static readonly Regex SinglePick = new(
            @"\.(First|FirstOrDefault|Single|SingleOrDefault|Last|LastOrDefault|ElementAt)\s*\(|\[\s*0\s*\]|\bbreak\s*;",
            RegexOptions.Compiled);
        private static readonly Regex ByName = new(@"\.Name\b", RegexOptions.Compiled);

        /// <summary>
        /// The paragraphs (blank-line-delimited runs) of <paramref name="source"/> that locate an
        /// HttpClient field by type and then take ONE of the matches without naming it.
        /// </summary>
        public static IEnumerable<string> PositionalLookups(string source)
        {
            foreach (var paragraph in Regex.Split(source, @"\r?\n\s*\r?\n"))
            {
                if (!TypeFilter.IsMatch(paragraph)) continue;
                if (ByName.IsMatch(paragraph)) continue;          // matched by name: fine
                if (!SinglePick.IsMatch(paragraph)) continue;    // replaces/inspects them all: fine
                yield return paragraph.Trim();
            }
        }

        [Fact]
        public void NoTestFileLocatesAnHttpClientFieldByPosition()
        {
            string testsDir = Path.Combine(RepoRoot(), "AccessibleTrader.Tests");
            var offenders = new List<string>();
            int scanned = 0, withTypeFilter = 0;

            foreach (var file in Directory.EnumerateFiles(testsDir, "*.cs", SearchOption.AllDirectories))
            {
                if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")) continue;
                if (file.EndsWith("HttpClientSwapScanTests.cs", StringComparison.Ordinal)) continue;
                if (file.EndsWith(Path.Combine("Fakes", "HttpClientSwap.cs"), StringComparison.Ordinal)) continue;
                scanned++;
                string text = File.ReadAllText(file);
                if (TypeFilter.IsMatch(text)) withTypeFilter++;
                foreach (var hit in PositionalLookups(text))
                    offenders.Add($"{Path.GetRelativePath(testsDir, file)}:\n{hit}");
            }

            Assert.True(scanned > 100, $"only {scanned} test files scanned — wrong directory?");
            // Vacuity floor: the by-type "replace all" and by-name helpers still exist and are
            // still found, so an empty offender list is the scan finding nothing WRONG rather
            // than finding nothing.
            Assert.True(withTypeFilter >= 3,
                $"only {withTypeFilter} files mention typeof(HttpClient); the scan is reading the wrong tree");
            Assert.True(offenders.Count == 0,
                "Test code locates an HttpClient field by POSITION. Use HttpClientSwap.ReplaceAll "
                + "(every client) or filter by field NAME — the first HttpClient field is whichever "
                + "the CLR lists first, and on a two-client provider that may be the one the test "
                + "does not fake:\n\n" + string.Join("\n\n", offenders));
        }

        // ── The classifier, proven on the shapes it must and must not catch ────────────────

        [Theory]
        [InlineData("""
            var field = typeof(SecEdgarProvider)
                .GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
                .First(f => f.FieldType == typeof(HttpClient));
            field.SetValue(p, new HttpClient(h));
            """)]
        [InlineData("""
            var target = provider.GetType()
                .GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
                .FirstOrDefault(f => f.FieldType == typeof(HttpClient));
            """)]
        [InlineData("""
            foreach (var f in fields)
            {
                if (f.FieldType == typeof(HttpClient)) { target = f; break; }
            }
            """)]
        [InlineData("""
            var clients = t.GetFields(Flags).Where(f => f.FieldType == typeof(HttpClient)).ToArray();
            clients[0].SetValue(p, new HttpClient(h));
            """)]
        public void TheShapesThatRodeDeclarationOrder_AreCaught(string snippet)
        {
            Assert.Single(PositionalLookups(snippet));
        }

        [Theory]
        [InlineData("""
            foreach (var field in provider.GetType()
                         .GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
                         .Where(f => f.FieldType == typeof(HttpClient)))
            {
                field.SetValue(provider, new HttpClient(handler));
            }
            """)]
        [InlineData("""
            var candidates = provider.GetType()
                .GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
                .Where(f => f.FieldType == typeof(HttpClient)
                         && f.Name is "_httpClient" or "_http")
                .ToList();
            var target = Assert.Single(candidates);
            """)]
        public void ReplacingThemAll_OrPickingByName_IsNotCaught(string snippet)
        {
            Assert.Empty(PositionalLookups(snippet));
        }
    }
}
