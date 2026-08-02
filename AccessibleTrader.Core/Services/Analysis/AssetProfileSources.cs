using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace AccessibleTrader.Core.Services.Analysis
{
    // ── Profiles ────────────────────────────────────────────────────────────────

    /// <param name="CommitsLast4Weeks">
    /// CoinGecko's own count, which is only as good as the repository it is pointed at — see
    /// <see cref="GithubRepoStats"/> for why that matters enough to warrant a second source.
    /// </param>
    public record CryptoProfile(
        string Id, string Name, int? MarketCapRank, DateTime? GenesisDate,
        double? CirculatingSupply, double? MaxSupply, double? TotalSupply,
        double? MarketCapUsd, double? FullyDilutedUsd, double? Volume24hUsd,
        int? CommitsLast4Weeks, int? Stars, int? Forks, int? Contributors,
        bool HasHomepage, bool HasWhitepaper, bool HasExplorer,
        IReadOnlyList<string> Repos, IReadOnlyList<string> Categories,
        int? TwitterFollowers, int? RedditSubscribers);

    /// <summary>Live repository state, straight from GitHub rather than an aggregator's snapshot.</summary>
    public record GithubRepoStats(string FullName, DateTime? LastPush, int Stars, int Forks,
                                  int OpenIssues, bool Archived);

    public record CompanyProfile(
        string Ticker, string Name, string? Industry, int? Cik,
        IReadOnlyList<(DateTime Filed, double Value)> Eps,
        IReadOnlyList<(DateTime Filed, double Value)> Revenue,
        IReadOnlyList<(DateTime Date, int Count)> InsiderFilings,
        IReadOnlyList<(DateTime Date, int Count)> MaterialEvents);

    public interface ICryptoProfileSource
    {
        Task<CryptoProfile?> GetAsync(string symbol, CancellationToken ct = default);
        Task<IReadOnlyList<GithubRepoStats>> GetRepoStatsAsync(IEnumerable<string> repoUrls, CancellationToken ct = default);
    }

    public interface ICompanyProfileSource
    {
        Task<CompanyProfile?> GetAsync(string ticker, CancellationToken ct = default);
    }

    // ── Crypto ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// CoinGecko for the asset profile, GitHub for what the developers are actually doing.
    ///
    /// <para>
    /// ── Why two sources rather than one ─────────────────────────────────────────
    /// The brief was "good quality data, not a reprint of the CMC front page", and the honest
    /// answer is that price, market cap and rank ARE the front page — repeating them adds nothing.
    /// What is not on any price page, and is the thing the 24-point guide spends three of its steps
    /// on, is <b>whether anyone is still building the project</b> and <b>whether it discloses
    /// anything</b>. CoinGecko's coin document carries both, free and keyless: developer counts,
    /// supply and dilution, categories, and the presence or absence of a homepage, whitepaper,
    /// source repository and block explorer.
    /// </para>
    ///
    /// <para>
    /// ── And why GitHub is queried directly on top of it ─────────────────────────
    /// CoinGecko's developer figures are only as current as the repository someone registered with
    /// them, and that goes stale silently. Measured 2026-08-02: CoinGecko reports Kaspa at
    /// <b>zero commits in four weeks</b>, because it tracks <c>kaspanet/kaspad</c> — which was
    /// superseded. <c>kaspanet/rusty-kaspa</c> was pushed <b>that same day</b>, with 843 stars
    /// against kaspad's 528. Reading the aggregator alone would have shown one of the more active
    /// projects in the market as abandoned. So the repositories CoinGecko lists are then asked
    /// directly, and the LIVE push date is what gets displayed.
    /// </para>
    ///
    /// <para>
    /// GitHub allows 60 unauthenticated requests per hour, which is why only the first few
    /// repositories are queried and a failure degrades to the aggregator's numbers rather than
    /// failing the section.
    /// </para>
    /// </summary>
    public sealed class CoinGeckoCryptoProfileSource : ICryptoProfileSource
    {
        private readonly HttpClient _http;
        private static readonly Dictionary<string, string> SymbolToId = new(StringComparer.OrdinalIgnoreCase)
        {
            // The common cases, resolved without a round trip. Anything else falls through to
            // CoinGecko's search endpoint.
            ["BTC"] = "bitcoin", ["ETH"] = "ethereum", ["SOL"] = "solana", ["XRP"] = "ripple",
            ["DOGE"] = "dogecoin", ["ADA"] = "cardano", ["LTC"] = "litecoin", ["BCH"] = "bitcoin-cash",
            ["DOT"] = "polkadot", ["AVAX"] = "avalanche-2", ["LINK"] = "chainlink", ["XMR"] = "monero",
            ["KAS"] = "kaspa", ["TAO"] = "bittensor", ["MATIC"] = "matic-network", ["BNB"] = "binancecoin",
        };

        public CoinGeckoCryptoProfileSource(HttpClient http) => _http = http;

        /// <summary>
        /// Strips the quote currency. Chart symbols arrive as BTC/USDT, BTC-USD or BTCUSDT depending
        /// on the provider, and all three have to resolve to the same coin.
        /// </summary>
        internal static string BaseSymbol(string symbol)
        {
            if (string.IsNullOrWhiteSpace(symbol)) return "";
            var s = symbol.Trim().ToUpperInvariant().Replace("-", "/");
            if (s.Contains('/')) return s.Split('/')[0];
            foreach (var quote in new[] { "USDT", "USDC", "USD", "EUR", "BTC", "ETH" })
                if (s.Length > quote.Length && s.EndsWith(quote, StringComparison.Ordinal))
                    return s[..^quote.Length];
            return s;
        }

        public async Task<CryptoProfile?> GetAsync(string symbol, CancellationToken ct = default)
        {
            string base_ = BaseSymbol(symbol);
            if (base_.Length == 0) return null;

            if (!SymbolToId.TryGetValue(base_, out var id))
            {
                id = await SearchIdAsync(base_, ct);
                if (id == null) return null;
            }

            var url = $"https://api.coingecko.com/api/v3/coins/{Uri.EscapeDataString(id)}"
                    + "?localization=false&tickers=false&market_data=true&community_data=true"
                    + "&developer_data=true&sparkline=false";

            using var resp = await _http.GetAsync(url, ct);
            if (resp.StatusCode == HttpStatusCode.NotFound) return null;
            resp.EnsureSuccessStatusCode();

            return ParseCoin(await resp.Content.ReadAsStringAsync(ct));
        }

        private async Task<string?> SearchIdAsync(string symbol, CancellationToken ct)
        {
            try
            {
                var json = await _http.GetStringAsync(
                    $"https://api.coingecko.com/api/v3/search?query={Uri.EscapeDataString(symbol)}", ct);
                var coins = JObject.Parse(json)["coins"] as JArray;
                // Match on the SYMBOL rather than taking the first hit: a search for "TAO" returns
                // several tokens whose name merely contains it, and picking the wrong one produces a
                // confident dossier about a different asset.
                var exact = coins?.FirstOrDefault(c =>
                    string.Equals(c["symbol"]?.ToString(), symbol, StringComparison.OrdinalIgnoreCase));
                return (exact ?? coins?.FirstOrDefault())?["id"]?.ToString();
            }
            catch { return null; }
        }

        internal static CryptoProfile? ParseCoin(string json)
        {
            var d = JObject.Parse(json);
            string? id = d["id"]?.ToString();
            if (string.IsNullOrEmpty(id)) return null;

            var md = d["market_data"];
            var dev = d["developer_data"];
            var links = d["links"];
            var comm = d["community_data"];

            static double? Num(JToken? t) =>
                t == null || t.Type == JTokenType.Null ? null
                : double.TryParse(t.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : null;
            static int? Int(JToken? t) =>
                t == null || t.Type == JTokenType.Null ? null
                : int.TryParse(t.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : null;

            var repos = (links?["repos_url"]?["github"] as JArray)?
                .Select(x => x.ToString())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToList() ?? new List<string>();

            var homepage = (links?["homepage"] as JArray)?.FirstOrDefault()?.ToString();
            var whitepaper = links?["whitepaper"]?.ToString();
            var explorer = (links?["blockchain_site"] as JArray)?
                .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x.ToString()))?.ToString();

            DateTime? genesis = DateTime.TryParse(d["genesis_date"]?.ToString(),
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var g) ? g : null;

            return new CryptoProfile(
                id, d["name"]?.ToString() ?? id, Int(d["market_cap_rank"]), genesis,
                Num(md?["circulating_supply"]), Num(md?["max_supply"]), Num(md?["total_supply"]),
                Num(md?["market_cap"]?["usd"]), Num(md?["fully_diluted_valuation"]?["usd"]),
                Num(md?["total_volume"]?["usd"]),
                Int(dev?["commit_count_4_weeks"]), Int(dev?["stars"]), Int(dev?["forks"]),
                Int(dev?["pull_request_contributors"]),
                !string.IsNullOrWhiteSpace(homepage),
                !string.IsNullOrWhiteSpace(whitepaper),
                !string.IsNullOrWhiteSpace(explorer),
                repos,
                (d["categories"] as JArray)?.Select(x => x.ToString()).Where(x => !string.IsNullOrWhiteSpace(x)).ToList()
                    ?? new List<string>(),
                Int(comm?["twitter_followers"]), Int(comm?["reddit_subscribers"]));
        }

        /// <summary>How stale a listed repository has to look before the owning organisation is checked.</summary>
        private const int StaleDays = 30;

        public async Task<IReadOnlyList<GithubRepoStats>> GetRepoStatsAsync(
            IEnumerable<string> repoUrls, CancellationToken ct = default)
        {
            var outp = new List<GithubRepoStats>();
            var owners = new List<string>();

            // Unauthenticated GitHub allows 60 requests an hour, so this stays deliberately frugal.
            foreach (var url in repoUrls.Take(3))
            {
                var slug = RepoSlug(url);
                if (slug == null) continue;
                owners.Add(slug.Split('/')[0]);
                try
                {
                    using var resp = await _http.GetAsync($"https://api.github.com/repos/{slug}", ct);
                    if (!resp.IsSuccessStatusCode) continue;
                    var s = ParseRepo(await resp.Content.ReadAsStringAsync(ct));
                    if (s != null) outp.Add(s);
                }
                catch { /* one dead repo must not fail the section */ }
            }

            // ── The successor-repository problem ────────────────────────────────
            // Querying the LISTED repositories fixes staleness in the aggregator's COUNT, but not
            // the deeper failure: the listing itself can point at a repository the project has moved
            // on from, and nobody updates it. Kaspa is the worked example -- CoinGecko lists only
            // `kaspanet/kaspad`, last pushed 58 days ago, while `kaspanet/rusty-kaspa` in the same
            // organisation was pushed today. Reading either the aggregator or the listed repo alone
            // shows one of the most active projects in the market as winding down.
            //
            // So when everything listed looks stale, the owning ORGANISATION is asked for its most
            // recently pushed repositories. One extra call, only on the assets where it changes the
            // answer, and the result is reported as what it is: activity elsewhere in the same org.
            bool allStale = outp.Count == 0 ||
                outp.All(r => !r.LastPush.HasValue || (DateTime.UtcNow - r.LastPush.Value).TotalDays > StaleDays);

            if (allStale && owners.Count > 0)
            {
                foreach (var owner in owners.Distinct().Take(1))
                {
                    try
                    {
                        using var resp = await _http.GetAsync(
                            $"https://api.github.com/orgs/{owner}/repos?sort=pushed&direction=desc&per_page=5", ct);
                        if (!resp.IsSuccessStatusCode) continue;
                        foreach (var extra in ParseRepoList(await resp.Content.ReadAsStringAsync(ct)))
                            if (!outp.Any(r => r.FullName == extra.FullName)) outp.Add(extra);
                    }
                    catch { /* the listed repositories remain the answer */ }
                }
            }

            return outp;
        }

        internal static IReadOnlyList<GithubRepoStats> ParseRepoList(string json)
        {
            var outp = new List<GithubRepoStats>();
            if (JToken.Parse(json) is not JArray arr) return outp;
            foreach (var item in arr)
            {
                var s = ParseRepo(item.ToString());
                if (s != null) outp.Add(s);
            }
            return outp;
        }

        internal static string? RepoSlug(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return null;
            var parts = url.Replace("https://github.com/", "", StringComparison.OrdinalIgnoreCase)
                           .Trim('/').Split('/');
            return parts.Length >= 2 ? $"{parts[0]}/{parts[1]}" : null;
        }

        internal static GithubRepoStats? ParseRepo(string json)
        {
            var d = JObject.Parse(json);
            var name = d["full_name"]?.ToString();
            if (string.IsNullOrEmpty(name)) return null;
            DateTime? pushed = DateTime.TryParse(d["pushed_at"]?.ToString(),
                CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var p) ? p : null;
            return new GithubRepoStats(name!, pushed,
                d["stargazers_count"]?.Value<int>() ?? 0,
                d["forks_count"]?.Value<int>() ?? 0,
                d["open_issues_count"]?.Value<int>() ?? 0,
                d["archived"]?.Value<bool>() ?? false);
        }
    }
}
