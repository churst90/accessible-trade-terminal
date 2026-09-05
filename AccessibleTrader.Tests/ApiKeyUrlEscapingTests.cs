using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;
using AccessibleTrader.Tests.Fakes;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// <b>A provider that authenticates by query string escapes the key it puts there.</b>
    ///
    /// <para>
    /// ── What went wrong ────────────────────────────────────────────────────────
    /// Six providers authenticate by interpolating the user's key straight into the request
    /// URL. Interpolated raw, any key that is not already URL-safe is mangled by the URL
    /// grammar itself: <c>&amp;</c> ends the parameter and begins another, so the key is
    /// TRUNCATED at the ampersand; <c>+</c> is decoded to a space at the server; <c>#</c>
    /// discards the remainder of the URL as a fragment. All three are legal in a generated
    /// credential.
    /// </para>
    ///
    /// <para>
    /// The user-visible failure is the bad one: the request authenticates as somebody else's
    /// truncated key — that is, as nobody — and the provider reports "validation failed". The
    /// key they pasted is correct, the message says it is not, and there is no way to tell from
    /// inside the app which is true. <c>FredProvider</c> was the only member of the fleet that
    /// escaped its key, and it is the one that documented why.
    /// </para>
    ///
    /// <para>
    /// ── What is enforced ───────────────────────────────────────────────────────
    /// Two routes, because either alone has a hole. The behavioural tests configure each
    /// provider with a key containing <c>&amp;</c> and <c>+</c>, drive one real request through
    /// a fake transport, and require the key to survive the round trip — that is the defect
    /// itself, demonstrated. The source scan then covers what no fake transport reaches (the
    /// two WebSocket URLs, and every endpoint a test does not happen to call) by requiring the
    /// key hole at every key-bearing query parameter to be the escaped <c>KeyParam</c> member
    /// or an inline <c>Uri.EscapeDataString</c>.
    /// </para>
    /// </summary>
    [Collection("ProviderCredentialBridge")]
    public class ApiKeyUrlEscapingTests
    {
        /// <summary>
        /// A key that exercises both mangling characters. With a raw interpolation the query
        /// reads <c>apikey=ab</c> and the rest becomes a second parameter — the server sees a
        /// two-character key.
        /// </summary>
        private const string AwkwardKey = "ab&cd+ef";

        private static void SwapHttpClient(object provider, FakeHttpMessageHandler handler)
        {
            HttpClientSwap.ReplaceAll(provider, handler);
        }

        /// <summary>
        /// Reads one query parameter back out of a captured URL the way a server would: split
        /// the query on <c>&amp;</c>, then on the first <c>=</c>, then percent-decode. An
        /// unescaped key fails here without any special-casing, because the truncation is
        /// performed by the grammar and not by the assertion.
        /// </summary>
        private static string? QueryValue(Uri uri, string name)
        {
            foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                int eq = pair.IndexOf('=');
                if (eq < 0) continue;
                if (!string.Equals(pair[..eq], name, StringComparison.Ordinal)) continue;
                return Uri.UnescapeDataString(pair[(eq + 1)..]);
            }
            return null;
        }

        private static async Task AssertKeySurvives(
            AccessibleTrader.Sdk.Plugins.BaseMarketDataProvider provider,
            FakeHttpMessageHandler handler,
            string parameterName)
        {
            provider.Configure(new Dictionary<string, string> { ["ApiKey"] = AwkwardKey });
            SwapHttpClient(provider, handler);

            await provider.ValidateApiKeyAsync();

            Assert.NotEmpty(handler.Captured);
            var uri = handler.Captured[0].RequestUri!;
            Assert.Equal(AwkwardKey, QueryValue(uri, parameterName));
        }

        private static FakeHttpMessageHandler AnyGet(string body = "{}") =>
            new FakeHttpMessageHandler { StrictMode = false }
                .Add(HttpMethod.Get, ".*", body, HttpStatusCode.OK);

        // ── Behavioural: the key survives the URL ────────────────────────────────

        [Fact]
        public Task TwelveData_sends_the_whole_key() =>
            AssertKeySurvives(new AccessibleTrader.Plugins.TwelveData.TwelveDataProvider(),
                AnyGet("""{"current_usage":1,"plan_limit":800}"""), "apikey");

        [Fact]
        public Task Finnhub_sends_the_whole_key() =>
            AssertKeySurvives(new AccessibleTrader.Plugins.Finnhub.FinnhubProvider(),
                AnyGet("""{"c":100}"""), "token");

        [Fact]
        public Task Fmp_sends_the_whole_key() =>
            AssertKeySurvives(new AccessibleTrader.Plugins.Fmp.FmpProvider(),
                AnyGet("""[{"symbol":"AAPL","price":100}]"""), "apikey");

        [Fact]
        public Task FmpAnalytics_sends_the_whole_key() =>
            AssertKeySurvives(new AccessibleTrader.Plugins.Fmp.FmpAnalyticsProvider(),
                AnyGet("""[{"symbol":"AAPL"}]"""), "apikey");

        [Fact]
        public Task Etherscan_sends_the_whole_key() =>
            AssertKeySurvives(new AccessibleTrader.Plugins.Etherscan.EtherscanProvider(),
                AnyGet("""{"status":"1","result":{"ethusd":"2000"}}"""), "apikey");

        [Fact]
        public Task Glassnode_sends_the_whole_key() =>
            AssertKeySurvives(new AccessibleTrader.Plugins.Glassnode.GlassnodeProvider(),
                AnyGet("""[{"t":1700000000,"v":1}]"""), "api_key");

        [Fact]
        public Task Fred_sends_the_whole_key() =>
            AssertKeySurvives(new AccessibleTrader.Plugins.Fred.FredProvider(),
                AnyGet("""{"seriess":[]}"""), "api_key");

        // ── Source scan: every key-bearing query site, including the ones no fake reaches ──

        /// <summary>A credential-bearing query parameter interpolated from an expression.</summary>
        private static readonly Regex KeyHole = new(
            @"[?&](?:apikey|api_key|apiKey|token|auth_token|access_token)=\{([^}]+)\}",
            RegexOptions.Compiled);

        private static bool IsCode(string line)
        {
            var t = line.TrimStart();
            return !t.StartsWith("//") && !t.StartsWith("*") && !t.StartsWith("/*") && !t.StartsWith("///");
        }

        [Fact]
        public void Every_key_bearing_query_parameter_is_escaped()
        {
            var offenders = new List<string>();
            var files = new HashSet<string>();
            int sites = 0;

            var pluginRoot = Path.Combine(RepoPaths.RepoRoot(), "Plugins");
            foreach (var file in Directory.EnumerateFiles(pluginRoot, "*.cs", SearchOption.AllDirectories))
            {
                var norm = file.Replace('\\', '/');
                if (norm.Contains("/obj/") || norm.Contains("/bin/")) continue;

                var lines = File.ReadAllLines(file);
                for (int i = 0; i < lines.Length; i++)
                {
                    if (!IsCode(lines[i])) continue;
                    foreach (Match m in KeyHole.Matches(lines[i]))
                    {
                        sites++;
                        files.Add(Path.GetFileName(file));
                        var hole = m.Groups[1].Value.Trim();

                        bool escaped = hole.Contains("EscapeDataString", StringComparison.Ordinal)
                                    || string.Equals(hole, "KeyParam", StringComparison.OrdinalIgnoreCase);
                        if (!escaped)
                            offenders.Add($"{Path.GetFileName(file)}:{i + 1}: {lines[i].Trim()}");
                    }
                }
            }

            // Vacuity floors: a scan that finds nothing is green for the wrong reason, and this
            // repo has shipped exactly that mistake before.
            Assert.True(files.Count >= 6,
                $"Expected ≥6 plugin files putting a key in a query string, found {files.Count}: {string.Join(", ", files)}");
            Assert.True(sites >= 30, $"Expected ≥30 key-bearing query sites, found {sites}.");

            Assert.True(offenders.Count == 0,
                "These sites interpolate a credential into a URL without escaping it. A key containing "
                + "'&', '+' or '#' is truncated or mangled and the user is told their correct key failed. "
                + "Use the KeyParam member (or Uri.EscapeDataString inline):\n"
                + string.Join("\n", offenders));
        }

        /// <summary>
        /// The path check behind the name: <c>KeyParam</c> is only a licence to skip the scan
        /// because every declaration of it escapes. Without this, renaming a raw field to
        /// <c>KeyParam</c> would turn the guard above off.
        /// </summary>
        [Fact]
        public void Every_KeyParam_declaration_actually_escapes()
        {
            var declaration = new Regex(@"\b(?:string\s+)?[kK]eyParam\s*(?:=>|=)\s*([^;]+);", RegexOptions.Compiled);
            var found = new List<string>();
            var offenders = new List<string>();

            var pluginRoot = Path.Combine(RepoPaths.RepoRoot(), "Plugins");
            foreach (var file in Directory.EnumerateFiles(pluginRoot, "*.cs", SearchOption.AllDirectories))
            {
                var norm = file.Replace('\\', '/');
                if (norm.Contains("/obj/") || norm.Contains("/bin/")) continue;

                var lines = File.ReadAllLines(file);
                for (int i = 0; i < lines.Length; i++)
                {
                    if (!IsCode(lines[i])) continue;
                    var m = declaration.Match(lines[i]);
                    if (!m.Success) continue;
                    found.Add($"{Path.GetFileName(file)}:{i + 1}");
                    if (!m.Groups[1].Value.Contains("Uri.EscapeDataString", StringComparison.Ordinal))
                        offenders.Add($"{Path.GetFileName(file)}:{i + 1}: {lines[i].Trim()}");
                }
            }

            Assert.True(found.Count >= 6, $"Expected ≥6 KeyParam declarations, found {found.Count}.");
            Assert.True(offenders.Count == 0,
                "A KeyParam that does not call Uri.EscapeDataString silently disables "
                + nameof(Every_key_bearing_query_parameter_is_escaped) + ":\n" + string.Join("\n", offenders));
        }
    }
}
