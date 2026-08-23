using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using AccessibleTrader.Sdk.Services;
using Xunit;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// The SDK-helper adoption sweep (2026-08-23) drove three baselines to zero
    /// and one invariant to whole-roster; these guards keep them there.
    ///
    /// Scan guards read SOURCE under Plugins/Providers so a new provider (or a
    /// regressing edit) cannot reintroduce a hand-rolled copy of something the
    /// SDK owns. Each scan carries a vacuity floor: if the directory layout
    /// moves and the scan sees nothing, the guard fails rather than passing
    /// against an empty file list.
    /// </summary>
    [Collection("ProviderCredentialBridge")]
    public class SdkHelperAdoptionScanTests
    {
        // Providers on disk today; the floor is deliberately below the real
        // count so adding providers never trips it, while a broken glob
        // (scanning nothing) always does.
        private const int MinProviderFiles = 16;

        private static List<(string File, string Text)> ProviderSources()
        {
            var root = Path.Combine(RepoPaths.RepoRoot(), "Plugins", "Providers");
            Assert.True(Directory.Exists(root), $"Provider plugin root moved: {root}");
            var files = Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories)
                .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") &&
                            !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
                .Select(f => (File: Path.GetRelativePath(root, f), Text: File.ReadAllText(f)))
                .ToList();
            Assert.True(files.Count >= MinProviderFiles,
                $"Vacuity floor: expected ≥{MinProviderFiles} provider source files, scanned {files.Count} — the glob is broken, not the code clean.");
            return files;
        }

        [Fact]
        public void No_provider_hand_rolls_a_ClientWebSocket()
        {
            // Baseline zero since the Binance market-data loop moved to
            // ReconnectingWebSocket — the last raw ClientWebSocket in the tier.
            // The SDK socket is what carries the connect timeout, backoff, the
            // half-open watchdog, and the 16 MB frame cap; a raw socket has none
            // of them until its author rediscovers each the hard way.
            var offenders = ProviderSources()
                .Where(s => s.Text.Contains("new ClientWebSocket("))
                .Select(s => s.File)
                .ToList();
            Assert.True(offenders.Count == 0,
                "Hand-rolled ClientWebSocket in provider code — use Sdk.Services.ReconnectingWebSocket:\n  "
                + string.Join("\n  ", offenders));
        }

        [Fact]
        public void No_provider_hand_rolls_an_HMAC()
        {
            // Baseline zero since the signing sweep: every HMAC in the provider
            // tier now goes through RestSigning (each venue keeps its RECIPE —
            // what gets hashed, which header — but the primitive lives once).
            // A hand-rolled copy is where the uppercase/culture/encoding bugs
            // this repo has already paid for come back.
            var offenders = ProviderSources()
                .Where(s => s.Text.Contains("new HMACSHA"))
                .Select(s => s.File)
                .ToList();
            Assert.True(offenders.Count == 0,
                "Hand-rolled HMAC in provider code — use Sdk.Services.RestSigning:\n  "
                + string.Join("\n  ", offenders));
        }

        [Fact]
        public void Every_provider_that_owns_an_HttpClient_owns_a_RateLimiter()
        {
            // Gemini and KrakenFutures shipped with NO rate limiter — and on
            // Gemini that was a correctness bug, not politeness: its
            // second-resolution monotonic nonce drifts ahead under burst until
            // the venue rejects it as InvalidNonce, which the user is told is a
            // credentials problem. Roster-enumerated so a new provider cannot
            // ship unmetered.
            static bool DeclaresField<T>(Type t)
            {
                for (var cur = t; cur != null; cur = cur.BaseType)
                    if (cur.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly)
                           .Any(f => typeof(T).IsAssignableFrom(f.FieldType)))
                        return true;
                return false;
            }

            var withHttp = ProviderRoster.Types.Where(DeclaresField<HttpClient>).ToList();
            Assert.True(withHttp.Count >= 14,
                $"Vacuity floor: expected ≥14 roster providers with an HttpClient field, found {withHttp.Count}.");

            var unmetered = withHttp.Where(t => !DeclaresField<RateLimiter>(t)).Select(t => t.Name).ToList();
            Assert.True(unmetered.Count == 0,
                "Providers making REST calls with no Sdk.Services.RateLimiter field:\n  "
                + string.Join("\n  ", unmetered));
        }
    }
}
