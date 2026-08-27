using System.Text.RegularExpressions;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// <b>A provider that authenticates by URL query string never speaks a raw exception message.</b>
    ///
    /// <para>
    /// ── What went wrong ────────────────────────────────────────────────────────
    /// Several providers authenticate by putting the user's API key straight into the request URI
    /// (<c>?apikey=KEY</c>, <c>?token=KEY</c>, <c>?api_key=KEY</c>). <see cref="System.Net.Http.HttpRequestException"/>
    /// routinely carries the request URI in its <c>Message</c>. Interpolating <c>ex.Message</c> into
    /// <c>_errorStream</c> — or into the string returned by <c>ValidateApiKeyAsync</c> — therefore
    /// **reads the user's live credential out loud** and writes it to the log, because
    /// <c>_errorStream</c> is the channel the accessibility layer speaks.
    /// </para>
    ///
    /// <para>
    /// The repo has now fixed this four separate times — TwelveData and FRED in the 2026-08-21
    /// sweep, then Finnhub and FMP (10 sites) in the 2026-08-26 HIGH pass, which also found the
    /// same class unfiled in <b>Etherscan and Glassnode</b>. That is the signature of a defect
    /// nothing was watching for: each fix was a call site, never the class.
    /// </para>
    ///
    /// <para>
    /// ── What is enforced ───────────────────────────────────────────────────────
    /// The scanner first works out which plugins authenticate by query string, by looking for a
    /// key-bearing query parameter in the source. For exactly those plugins it then bans
    /// <c>ex.Message</c> (and <c>e.Message</c>) anywhere in the file. The provider list is
    /// *derived*, not hardcoded, so a brand-new query-string-auth provider is covered the day it
    /// lands rather than the day someone remembers to add it here.
    /// </para>
    ///
    /// <para>
    /// The replacement is <c>ex.GetType().Name</c>: it still tells the user whether they are
    /// looking at a timeout, a DNS failure or a JSON fault, which is the whole diagnostic value
    /// the message had, without the key.
    /// </para>
    /// </summary>
    public class CredentialLeakScanTests
    {
        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AccessibleTrader.slnx")))
                dir = dir.Parent;
            Assert.NotNull(dir);
            return dir!.FullName;
        }

        /// <summary>
        /// A query parameter carrying a credential, interpolated from a field. Matches
        /// <c>?apikey={_apiKey}</c>, <c>&amp;token={_apiKey}</c>, <c>api_key={ApiKey}</c> and friends.
        /// </summary>
        private static readonly Regex QueryStringAuth = new(
            @"[?&](apikey|api_key|apiKey|token|auth_token|access_token|key)=\{",
            RegexOptions.Compiled);

        /// <summary>
        /// <c>ex.Message</c> in code — not in a comment. Comments are how the fixed providers
        /// record *why* they do not do this, so a naive scan would flag the documentation of the
        /// fix as the bug.
        /// </summary>
        private static readonly Regex RawExceptionMessage = new(
            @"\b[a-z]{1,3}x?\.Message\b",
            RegexOptions.Compiled);

        private static IEnumerable<string> PluginSourceFiles()
        {
            var plugins = Path.Combine(RepoRoot(), "Plugins");
            return Directory.EnumerateFiles(plugins, "*.cs", SearchOption.AllDirectories)
                            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                                     && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"));
        }

        private static bool IsCode(string line)
        {
            var t = line.TrimStart();
            return !t.StartsWith("//") && !t.StartsWith("*") && !t.StartsWith("/*");
        }

        [Fact]
        public void QueryStringAuthenticatedProviders_NeverInterpolateARawExceptionMessage()
        {
            var offenders = new List<string>();
            var scanned = new List<string>();

            foreach (var file in PluginSourceFiles())
            {
                var text = File.ReadAllText(file);
                if (!QueryStringAuth.IsMatch(text)) continue;

                scanned.Add(Path.GetFileName(file));

                var lines = File.ReadAllLines(file);
                for (int i = 0; i < lines.Length; i++)
                {
                    if (!IsCode(lines[i])) continue;
                    if (RawExceptionMessage.IsMatch(lines[i]))
                        offenders.Add($"{Path.GetFileName(file)}:{i + 1}: {lines[i].Trim()}");
                }
            }

            // Vacuity check: if the derivation stops finding query-string-auth providers, the
            // scanner is green because it examined nothing, which is the failure mode this repo
            // has hit before (a guard that measures the machine, not the walker).
            Assert.True(scanned.Count >= 5,
                $"Expected to scan at least 5 query-string-authenticated plugin files, scanned {scanned.Count}: "
                + string.Join(", ", scanned));

            Assert.True(offenders.Count == 0,
                "These providers put the user's API key in the request URI and then interpolate a raw "
                + "exception message onto a channel that is spoken and logged. Use ex.GetType().Name:\n"
                + string.Join("\n", offenders));
        }
    }
}
