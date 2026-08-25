using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AccessibleTrader.Sdk.Services;
using Xunit;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// An order that the venue accepted but whose response was lost must never be sent a second
    /// time.
    ///
    /// <para>
    /// ── The bug these guard ───────────────────────────────────────────────────
    /// Every trading provider ran its order POST inside <see cref="RateLimiter.ExecuteAsync{T}"/>,
    /// whose default is three retries and whose <c>ShouldRetry</c> deliberately retries an
    /// <see cref="OperationCanceledException"/> raised with the caller's token un-cancelled —
    /// which is exactly what an <c>HttpClient</c> timeout looks like. Four of those venues put no
    /// client order id on the wire at all, so the exchange had nothing to dedupe against. One
    /// market buy, one 60-second timeout, and the user held two positions and heard "Order placed"
    /// once. <c>GeneralOrderService</c>'s 30-second dedup gate sits <i>above</i>
    /// <c>PlaceOrderAsync</c> and cannot see a retry that happens inside one call.
    /// </para>
    ///
    /// <para>
    /// ── Why the scan is a PATH check ──────────────────────────────────────────
    /// A presence check ("the file mentions ExecuteOnceAsync somewhere") stays green while the
    /// order POST itself sits under the retrying wrapper — this repo has already been bitten by
    /// exactly that shape. So these tests brace-match the body of each order-creating method and
    /// assert on what is inside <i>that</i> body, ignoring the dozen legitimate
    /// <c>ExecuteAsync</c> calls elsewhere in the same file.
    /// </para>
    /// </summary>
    public class OrderPostRetrySafetyTests
    {
        // The methods that CREATE something at the venue. Repeating any of these is
        // not a retry, it is a second order (or a second withdrawal). Cancel and the
        // read paths are deliberately absent: cancelling twice is a no-op, and a GET
        // that times out should absolutely be retried.
        private static readonly string[] CreatingMethods =
        {
            "PlaceOrderAsync",
            "PlaceOcoPairAsync",
            "PlaceSpotOrderAsync",
            "PlaceFuturesOrderAsync",
            "PlaceBracketAsync",
            "WithdrawAsync",
        };

        // Providers on disk today declare far more than this between them; the floor
        // is set well below the real count so adding a provider never trips it, while
        // a broken glob or a renamed method (scanning nothing) always does.
        private const int MinCreatingBodies = 14;

        private static List<string> ProviderFiles()
        {
            var root = Path.Combine(RepoPaths.RepoRoot(), "Plugins", "Providers");
            Assert.True(Directory.Exists(root), $"Provider plugin root moved: {root}");
            return Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories)
                .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") &&
                            !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
                .ToList();
        }

        /// <summary>
        /// Every order- and withdrawal-creating method body found under Plugins/Providers,
        /// as (file, method, body) — comments and string literals already stripped, so a
        /// commented-out call and a call inside an error message can never be a match.
        /// </summary>
        private static List<(string File, string Method, string Body)> CreatingBodies()
        {
            var found = new List<(string, string, string)>();
            foreach (var path in ProviderFiles())
            {
                string source = PipelineIdentityAndResilienceTests.StripCommentsAndStrings(File.ReadAllText(path));
                string file = Path.GetFileName(path);
                foreach (var method in CreatingMethods)
                {
                    // Declaration, not invocation: a signature ends in ")" then "{" (with
                    // an optional constraint-free newline). Invocations are followed by
                    // ";" or ".", never by the opening brace of a body.
                    foreach (Match m in Regex.Matches(source, $@"\b{method}\s*\("))
                    {
                        int open = FindBodyBrace(source, m.Index + m.Length - 1);
                        if (open < 0) continue;
                        string body = ExtractBlock(source, open);
                        if (body.Length == 0) continue;
                        found.Add((file, method, body));
                    }
                }
            }
            Assert.True(found.Count >= MinCreatingBodies,
                $"Vacuity floor: expected ≥{MinCreatingBodies} order/withdrawal-creating method bodies, "
                + $"found {found.Count}. The scan is broken (glob, rename, or brace matching), not the code clean.");
            return found;
        }

        /// <summary>
        /// From the "(" of a candidate, walk past the balanced parameter list; return the
        /// index of the body's "{" if this really was a declaration, else -1.
        /// </summary>
        private static int FindBodyBrace(string source, int openParen)
        {
            int depth = 0;
            int i = openParen;
            for (; i < source.Length; i++)
            {
                if (source[i] == '(') depth++;
                else if (source[i] == ')')
                {
                    depth--;
                    if (depth == 0) { i++; break; }
                }
            }
            // Whitespace (and nothing else) may separate ")" from the body brace. An
            // expression body ("=>") and an invocation (";", ".") both bail out here.
            for (; i < source.Length; i++)
            {
                if (char.IsWhiteSpace(source[i])) continue;
                return source[i] == '{' ? i : -1;
            }
            return -1;
        }

        /// <summary>Brace-matched block starting at <paramref name="open"/>.</summary>
        private static string ExtractBlock(string source, int open)
        {
            int depth = 0;
            for (int i = open; i < source.Length; i++)
            {
                if (source[i] == '{') depth++;
                else if (source[i] == '}')
                {
                    depth--;
                    if (depth == 0) return source.Substring(open, i - open + 1);
                }
            }
            return "";
        }

        [Fact]
        public void No_order_creating_method_runs_its_POST_under_the_retrying_wrapper()
        {
            var offenders = CreatingBodies()
                .Where(b => Regex.IsMatch(b.Body, @"\.ExecuteAsync\s*\("))
                .Select(b => $"{b.File}.{b.Method}")
                .Distinct()
                .ToList();

            Assert.True(offenders.Count == 0,
                "An order- or withdrawal-creating method runs inside RateLimiter.ExecuteAsync, which retries "
                + "up to three times — including on the HttpClient timeout that is indistinguishable from "
                + "'the venue booked it and the reply was lost'. Use ExecuteOnceAsync:\n  "
                + string.Join("\n  ", offenders));
        }

        [Fact]
        public void The_creating_methods_that_meter_at_all_meter_through_ExecuteOnceAsync()
        {
            // The complement of the test above, and the reason it is not vacuous: a
            // method could satisfy "no ExecuteAsync" by taking no rate slot at all.
            // Bodies that delegate (a dispatcher calling PlaceSpotOrderAsync, a bracket
            // helper reached from inside its caller's slot) legitimately hold none, so
            // this asserts a POPULATION floor rather than requiring every body to meter:
            // if adoption collapses, the count drops and this goes red.
            var metered = CreatingBodies()
                .Where(b => b.Body.Contains("ExecuteOnceAsync"))
                .Select(b => $"{b.File}.{b.Method}")
                .Distinct()
                .ToList();

            Assert.True(metered.Count >= 11,
                $"Only {metered.Count} order/withdrawal-creating methods take their rate slot through "
                + "ExecuteOnceAsync; 11 did when this guard was written. If a provider was removed, lower the "
                + "floor deliberately — do not let adoption rot silently:\n  " + string.Join("\n  ", metered));
        }

        [Fact]
        public void Coinbase_mints_its_idempotency_key_outside_the_limiter_call()
        {
            // Coinbase rejects a repeat of client_order_id — the one venue mechanism
            // that can turn a re-sent order into a no-op. Minting the key INSIDE the
            // lambda handed every retry a fresh id, so the exchange saw a brand-new
            // order and the safety net defeated itself.
            string path = Path.Combine(RepoPaths.RepoRoot(), "Plugins", "Providers",
                "AccessibleTrader.Plugins.Coinbase", "CoinbaseProvider.cs");
            Assert.True(File.Exists(path), $"Coinbase provider moved: {path}");
            string source = PipelineIdentityAndResilienceTests.StripCommentsAndStrings(File.ReadAllText(path));

            int keyAt = source.IndexOf("string clientOid =", StringComparison.Ordinal);
            int limiterAt = source.IndexOf("_rateLimiter.ExecuteOnceAsync", StringComparison.Ordinal);
            Assert.True(keyAt >= 0, "Coinbase no longer declares clientOid — re-point this guard.");
            Assert.True(limiterAt >= 0, "Coinbase no longer places its order through ExecuteOnceAsync.");
            Assert.True(keyAt < limiterAt,
                "Coinbase mints its client_order_id inside the limiter call. Hoist it above, so the id on the "
                + "wire is the id we chose and stays stable for the whole attempt.");
        }

        [Fact]
        public async Task ExecuteOnceAsync_runs_the_action_exactly_once_when_it_throws()
        {
            // The unit half: prove the primitive itself does not retry, on the exact
            // exception shape ShouldRetry says yes to (a cancellation whose token was
            // never cancelled — an HttpClient timeout).
            var limiter = new RateLimiter(100, TimeSpan.FromSeconds(1));
            int calls = 0;

            await Assert.ThrowsAsync<OperationCanceledException>(async () =>
                await limiter.ExecuteOnceAsync<string>(() =>
                {
                    calls++;
                    throw new OperationCanceledException();
                }));

            Assert.Equal(1, calls);
        }

        [Fact]
        public async Task ExecuteAsync_by_contrast_does_retry_that_same_exception()
        {
            // The vacuity check for the test above: if ExecuteAsync stopped retrying,
            // ExecuteOnceAsync would be proving nothing and this pair says so.
            var limiter = new RateLimiter(100, TimeSpan.FromMilliseconds(1));
            int calls = 0;

            await Assert.ThrowsAsync<OperationCanceledException>(async () =>
                await limiter.ExecuteAsync<string>(() =>
                {
                    calls++;
                    throw new OperationCanceledException();
                }, maxRetries: 2));

            Assert.Equal(3, calls);
        }

        [Fact]
        public async Task ExecuteOnceAsync_returns_the_result_and_still_takes_a_rate_slot()
        {
            var limiter = new RateLimiter(1, TimeSpan.FromMilliseconds(50));
            Assert.Equal("ok", await limiter.ExecuteOnceAsync(() => Task.FromResult("ok")));
            // Second call must wait out the one-per-50ms window rather than sail through.
            var started = Environment.TickCount64;
            Assert.Equal("ok2", await limiter.ExecuteOnceAsync(() => Task.FromResult("ok2")));
            Assert.True(Environment.TickCount64 - started >= 20,
                "ExecuteOnceAsync returned without taking a rate slot.");
        }
    }
}
