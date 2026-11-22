namespace AccessibleTrader.Core.Services.Headlines
{
    /// <summary>Why a news read came back empty-handed, or that it did not.
    /// <see cref="RateLimited"/> is Finnhub's 429: too many requests this minute.</summary>
    public enum NewsReadStatus { Ok, NoKey, Refused, Failed, RateLimited }

    /// <summary>One read of a symbol's recent wire stories. <see cref="Stories"/> is empty unless
    /// <see cref="Status"/> is <see cref="NewsReadStatus.Ok"/>. <see cref="Who"/> names the
    /// service ("Finnhub") so a refused key can be said as whose.</summary>
    public sealed record NewsReadResult(NewsReadStatus Status, IReadOnlyList<NewsStory> Stories, string Who = "")
    {
        public static NewsReadResult Of(NewsReadStatus status, string who = "") => new(status, Array.Empty<NewsStory>(), who);
    }

    /// <summary>A symbol's recent wire stories, newest first — the dossier's "Recent news".</summary>
    public interface INewsSource
    {
        Task<NewsReadResult> RecentAsync(NewsTarget target, int limit, CancellationToken ct = default);
    }
}
