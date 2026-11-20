using System.Net;
using AccessibleTrader.Sdk.Services;
using Microsoft.Extensions.Logging;

namespace AccessibleTrader.Core.Services.Headlines
{
    /// <summary>Why a news read came back empty-handed, or that it did not.</summary>
    public enum NewsReadStatus { Ok, NoKey, Refused, Failed }

    /// <summary>One read of a symbol's recent wire stories. <see cref="Stories"/> is empty unless
    /// <see cref="Status"/> is <see cref="NewsReadStatus.Ok"/>.</summary>
    public sealed record NewsReadResult(NewsReadStatus Status, IReadOnlyList<NewsStory> Stories)
    {
        public static NewsReadResult Of(NewsReadStatus status) => new(status, Array.Empty<NewsStory>());
    }

    /// <summary>A symbol's recent wire stories, newest first — the dossier's "Recent news".</summary>
    public interface INewsSource
    {
        Task<NewsReadResult> RecentAsync(NewsTarget target, int limit, CancellationToken ct = default);
    }

    /// <summary>
    /// Alpaca's news REST endpoint (<c>GET data.alpaca.markets/v1beta1/news</c>), read with the
    /// Alpaca key from API Keys. The key is checked out for each read and dropped with it, never
    /// kept or logged.
    /// </summary>
    public sealed class AlpacaNewsRestSource : INewsSource
    {
        public const string Endpoint = "https://data.alpaca.markets/v1beta1/news";

        private readonly HttpClient _http;
        private readonly IApiKeyCheckout _keys;
        private readonly ILogger<AlpacaNewsRestSource>? _logger;

        public AlpacaNewsRestSource(HttpClient http, IApiKeyCheckout keys, ILogger<AlpacaNewsRestSource>? logger = null)
        {
            _http = http;
            _keys = keys;
            _logger = logger;
        }

        public async Task<NewsReadResult> RecentAsync(NewsTarget target, int limit, CancellationToken ct = default)
        {
            var key = await _keys.CheckoutAsync("Alpaca", ct: ct).ConfigureAwait(false);
            if (!key.HasCredentials) return NewsReadResult.Of(NewsReadStatus.NoKey);

            string url = $"{Endpoint}?symbols={Uri.EscapeDataString(target.RestSymbol)}"
                + $"&limit={Math.Clamp(limit, 1, 50)}&sort=desc&include_content=false";
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Add("APCA-API-KEY-ID", key.Key);
                request.Headers.Add("APCA-API-SECRET-KEY", key.Secret);
                using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
                if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                    return NewsReadResult.Of(NewsReadStatus.Refused);
                if (!response.IsSuccessStatusCode)
                {
                    _logger?.LogDebug("Alpaca news read returned {Status}.", (int)response.StatusCode);
                    return NewsReadResult.Of(NewsReadStatus.Failed);
                }
                string json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                return new NewsReadResult(NewsReadStatus.Ok, AlpacaNewsParser.ParseRest(json));
            }
            catch (Exception ex) when (ex is HttpRequestException
                                       || (ex is TaskCanceledException && !ct.IsCancellationRequested))
            {
                _logger?.LogDebug("Alpaca news read failed: {Type}.", ex.GetType().Name);
                return NewsReadResult.Of(NewsReadStatus.Failed);
            }
        }
    }
}
