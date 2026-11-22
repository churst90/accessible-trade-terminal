using System.Globalization;
using System.Net;
using AccessibleTrader.Core.Services.Analysis;
using AccessibleTrader.Sdk.Services;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AccessibleTrader.Core.Services.Headlines
{
    /// <summary>
    /// Finnhub's news lists, as its free key sends them (checked 2026-11-20 with Cody's key): a
    /// JSON array of stories, each with <c>category</c>, <c>datetime</c> (Unix seconds),
    /// <c>headline</c>, <c>id</c>, <c>image</c>, <c>related</c> (the symbol asked about, or empty
    /// in the crypto and general lists), <c>source</c> (the publisher: Yahoo, Benzinga, CNBC,
    /// Seeking Alpha, Cointelegraph, CoinDesk...), <c>summary</c> and <c>url</c>. The summary is
    /// not read: only the headline is said.
    /// </summary>
    public static class FinnhubNewsParser
    {
        public static IReadOnlyList<NewsStory> Parse(string json)
        {
            var list = new List<NewsStory>();
            JToken? root;
            try { root = JToken.Parse(json); }
            catch (JsonException) { return list; }
            if (root is not JArray items) return list;
            foreach (var m in items.OfType<JObject>())
            {
                if (m["id"]?.Type != JTokenType.Integer) continue;
                if (m["datetime"]?.Type != JTokenType.Integer) continue;
                string headline = NewsText.Clean((string?)m["headline"]);
                if (headline.Length == 0) continue;
                long seconds = (long)m["datetime"]!;
                if (seconds <= 0) continue;
                var created = DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime;
                var symbols = ((string?)m["related"] ?? "")
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(s => s.ToUpperInvariant()).Distinct().ToList();
                list.Add(new NewsStory((long)m["id"]!, headline, "", NewsText.Clean((string?)m["source"]),
                    created, created, symbols, (string?)m["url"] ?? "", NewsStory.FinnhubWire));
            }
            return list;
        }
    }

    /// <summary>Finnhub's two news lists the headlines read, so the tests can drive them without
    /// a network. Production is <see cref="FinnhubNewsHttpClient"/>.</summary>
    public interface IFinnhubNewsClient
    {
        /// <summary>A stock's stories published between two dates (New York dates are fine:
        /// Finnhub reads them as whole days).</summary>
        Task<NewsReadResult> CompanyNewsAsync(string symbol, DateOnly from, DateOnly to, CancellationToken ct = default);

        /// <summary>The newest crypto stories (about a hundred), for every coin at once.</summary>
        Task<NewsReadResult> CryptoNewsAsync(CancellationToken ct = default);
    }

    /// <summary>
    /// Finnhub's REST news with the Finnhub key from API Keys, sent in the
    /// <c>X-Finnhub-Token</c> header so it is never part of a URL that could be logged. The key is
    /// checked out for each read and dropped with it. No key, no request.
    /// </summary>
    public sealed class FinnhubNewsHttpClient : IFinnhubNewsClient
    {
        public const string RestUrl = "https://finnhub.io/api/v1";
        private const string Who = "Finnhub";

        private readonly HttpClient _http;
        private readonly IApiKeyCheckout _keys;
        private readonly ILogger<FinnhubNewsHttpClient>? _logger;

        public FinnhubNewsHttpClient(HttpClient http, IApiKeyCheckout keys, ILogger<FinnhubNewsHttpClient>? logger = null)
        {
            _http = http;
            _keys = keys;
            _logger = logger;
        }

        public Task<NewsReadResult> CompanyNewsAsync(string symbol, DateOnly from, DateOnly to, CancellationToken ct = default)
            => ReadAsync($"{RestUrl}/company-news?symbol={Uri.EscapeDataString(symbol)}"
                         + $"&from={from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}"
                         + $"&to={to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}", ct);

        public Task<NewsReadResult> CryptoNewsAsync(CancellationToken ct = default)
            => ReadAsync($"{RestUrl}/news?category=crypto", ct);

        private async Task<NewsReadResult> ReadAsync(string url, CancellationToken ct)
        {
            var key = await _keys.CheckoutAsync("Finnhub", ct: ct).ConfigureAwait(false);
            if (!key.HasCredentials) return NewsReadResult.Of(NewsReadStatus.NoKey, Who);
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Add("X-Finnhub-Token", key.Key);
                using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
                if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                    return NewsReadResult.Of(NewsReadStatus.Refused, Who);
                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                    return NewsReadResult.Of(NewsReadStatus.RateLimited, Who);
                if (!response.IsSuccessStatusCode)
                {
                    _logger?.LogDebug("Finnhub news read returned {Status}.", (int)response.StatusCode);
                    return NewsReadResult.Of(NewsReadStatus.Failed, Who);
                }
                string json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                return new NewsReadResult(NewsReadStatus.Ok, FinnhubNewsParser.Parse(json), Who);
            }
            catch (Exception ex) when (ex is HttpRequestException
                                       || (ex is TaskCanceledException && !ct.IsCancellationRequested))
            {
                _logger?.LogDebug("Finnhub news read failed: {Type}.", ex.GetType().Name);
                return NewsReadResult.Of(NewsReadStatus.Failed, Who);
            }
        }
    }

    /// <summary>
    /// Finnhub's stories for the dossier's "Recent news": a stock's last
    /// <see cref="StockDays"/> days, or the crypto list, kept to the stories whose headline
    /// names the stock or coin (<see cref="NewsStoryWords.NamesCompany"/>,
    /// <see cref="NewsStoryWords.NamesCoin"/>), newest first.
    /// </summary>
    public sealed class FinnhubNewsRestSource : INewsSource
    {
        public const int StockDays = 7;

        private readonly IFinnhubNewsClient _client;
        private readonly ICompanyNameSource? _names;
        private readonly TimeProvider _time;

        public FinnhubNewsRestSource(IFinnhubNewsClient client, ICompanyNameSource? names = null, TimeProvider? time = null)
        {
            _client = client;
            _names = names;
            _time = time ?? TimeProvider.System;
        }

        public async Task<NewsReadResult> RecentAsync(NewsTarget target, int limit, CancellationToken ct = default)
        {
            NewsReadResult read;
            Func<NewsStory, bool> about;
            if (target.Coin)
            {
                read = await _client.CryptoNewsAsync(ct).ConfigureAwait(false);
                about = s => NewsStoryWords.NamesCoin(s.Headline, target.Label);
            }
            else
            {
                var today = FinnhubNewsLiveNotices.NewYorkDate(_time.GetUtcNow().UtcDateTime);
                read = await _client.CompanyNewsAsync(target.Label, today.AddDays(-StockDays), today, ct).ConfigureAwait(false);
                string? name = null;
                if (read.Status == NewsReadStatus.Ok && _names != null)
                {
                    try { name = await _names.CompanyNameAsync(target.Label, ct).ConfigureAwait(false); }
                    catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested) { }
                }
                about = s => NewsStoryWords.NamesCompany(s.Headline, target.Label, name);
            }
            if (read.Status != NewsReadStatus.Ok) return read;
            var kept = read.Stories.Where(about).OrderByDescending(s => s.CreatedUtc).Take(Math.Max(1, limit)).ToList();
            return new NewsReadResult(NewsReadStatus.Ok, kept, read.Who);
        }
    }

    /// <summary>
    /// The dossier's "Recent news": its sources read together (only Finnhub since Alpaca's wire
    /// was taken out, 2026-11-22), newest first, a story told twice — by two publishers, or by
    /// two sources — (<see cref="NewsStoryWords.SameStory"/>) listed once, at most
    /// <c>limit</c>. One source answering is enough: the list says what it has, and a key missing
    /// from another is not an error. With none answering, a refused key is said before a
    /// failed read, and a failed read before no key.
    /// </summary>
    public sealed class CombinedNewsSource : INewsSource
    {
        private readonly IReadOnlyList<INewsSource> _sources;

        public CombinedNewsSource(params INewsSource[] sources) => _sources = sources;

        public async Task<NewsReadResult> RecentAsync(NewsTarget target, int limit, CancellationToken ct = default)
        {
            var reads = await Task.WhenAll(_sources.Select(async s =>
            {
                try { return await s.RecentAsync(target, limit, ct).ConfigureAwait(false); }
                catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
                {
                    return NewsReadResult.Of(NewsReadStatus.Failed);
                }
            })).ConfigureAwait(false);

            var ok = reads.Where(r => r.Status == NewsReadStatus.Ok).ToList();
            if (ok.Count == 0)
            {
                return reads.FirstOrDefault(r => r.Status == NewsReadStatus.Refused)
                    ?? reads.FirstOrDefault(r => r.Status is NewsReadStatus.Failed or NewsReadStatus.RateLimited)
                    ?? reads.FirstOrDefault()
                    ?? NewsReadResult.Of(NewsReadStatus.NoKey);
            }

            var kept = new List<(NewsStory Story, HashSet<string> Words)>();
            foreach (var s in ok.SelectMany(r => r.Stories).OrderByDescending(s => s.CreatedUtc))
            {
                var words = NewsStoryWords.StoryWords(s.Headline);
                if (kept.Any(k => NewsStoryWords.SameStory(k.Words, words))) continue;
                kept.Add((s, words));
                if (kept.Count >= Math.Max(1, limit)) break;
            }
            return new NewsReadResult(NewsReadStatus.Ok, kept.Select(k => k.Story).ToList(),
                string.Join(" and ", ok.Select(r => r.Who).Where(w => w.Length > 0).Distinct()));
        }
    }
}
