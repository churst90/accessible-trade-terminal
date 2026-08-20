using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AccessibleTrader.Core.Persistence;
using AccessibleTrader.Sdk.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AccessibleTrader.Core.Services
{
    public interface IOhlcvStore
    {
        /// <summary>
        /// Bars for a window that has already closed, or an empty list when the store cannot
        /// satisfy the request. Never returns a partial answer: the caller either gets everything
        /// it asked for or goes to the provider.
        /// </summary>
        Task<List<Ohlcv>> TryReadClosedWindowAsync(string market, string provider, string symbol,
            string timeframe, long until, int limit, CancellationToken ct = default);

        /// <summary>
        /// Records bars for later reuse. Bars belonging to the still-forming period are dropped —
        /// they are not final and must never be served back as history.
        /// </summary>
        Task SaveAsync(string market, string provider, string symbol, string timeframe,
            IReadOnlyList<Ohlcv> bars, CancellationToken ct = default);
    }

    /// <summary>
    /// On-disk store of historical OHLCV bars (SQLite, via <see cref="AppDbContext"/>), so the same
    /// history is not re-fetched from a provider every time a chart is opened or scrolled back.
    ///
    /// <para>
    /// The table and its composite key (market, provider, symbol, timeframe, timestamp) already
    /// existed; nothing ever wrote to it. The only writer, <c>BackfillManager.QueueBackfill</c>,
    /// had no callers anywhere in the codebase, and the read path was restricted to <c>1m</c> — so
    /// the file on disk was 0 bytes with no tables at all. This class is the writer, and it works
    /// at whatever timeframe was actually requested rather than only at 1m.
    /// </para>
    ///
    /// <para><b>What is deliberately NOT served from here: the live edge.</b> A request for "the
    /// newest N bars" must hit the provider, because the newest bar is still forming and because a
    /// stale-but-plausible last price is a worse failure than a slow chart — nothing about it looks
    /// wrong. Only windows that have entirely closed are served from disk, which is exactly the
    /// scrollback/prepend path: old bars are immutable, they are the bulk of the fetching, and
    /// re-reading them costs a provider request every time. Writes happen on EVERY successful
    /// fetch, so the store warms up from normal use.</para>
    /// </summary>
    public sealed class OhlcvStore : IOhlcvStore
    {
        /// <summary>Newest bars kept per (market, provider, symbol, timeframe). At 1h bars that is
        /// about six years of history per series, and a bounded few MB on disk.</summary>
        public const int MaxBarsPerSeries = 50_000;

        private readonly IDbContextFactory<AppDbContext> _factory;
        private readonly ILogger<OhlcvStore> _logger;

        // SQLite takes one writer at a time; concurrent Blazor circuits would otherwise collide
        // with "database is locked". Reads are unaffected. Instance-scoped, NOT static: the store
        // is registered as a singleton so this is process-wide in practice, while a static would
        // wrongly serialise — and, worse, wrongly share the schema flag with — a second instance
        // pointed at a different database file.
        private readonly SemaphoreSlim _writeLock = new(1, 1);
        private int _schemaReady;

        public OhlcvStore(IDbContextFactory<AppDbContext> factory, ILogger<OhlcvStore> logger)
        {
            _factory = factory;
            _logger = logger;
        }

        public async Task<List<Ohlcv>> TryReadClosedWindowAsync(string market, string provider, string symbol,
            string timeframe, long until, int limit, CancellationToken ct = default)
        {
            if (limit <= 0) return new List<Ohlcv>();

            long barMs = TimeframeUtility.ToMilliseconds(timeframe ?? "");
            if (barMs <= 0) return new List<Ohlcv>();

            try
            {
                await EnsureSchemaAsync(ct).ConfigureAwait(false);
                using var db = await _factory.CreateDbContextAsync(ct).ConfigureAwait(false);

                var rows = await db.OhlcvData
                    .Where(e => e.Market == market && e.Provider == provider
                             && e.Symbol == symbol && e.Timeframe == timeframe
                             && e.Timestamp <= until)
                    .OrderByDescending(e => e.Timestamp)
                    .Take(limit)
                    .ToListAsync(ct)
                    .ConfigureAwait(false);

                // Not enough history stored — let the provider answer rather than returning a
                // short block the caller would silently treat as "that's all there is".
                if (rows.Count < limit) return new List<Ohlcv>();

                // The newest stored bar must sit adjacent to the requested edge. Without this a
                // block stored during some earlier, unrelated session could be prepended with a
                // hole between it and the chart — a chart with an invisible gap in it.
                if (until - rows[0].Timestamp > barMs) return new List<Ohlcv>();

                rows.Reverse();
                return rows.Select(ToBar).ToList();
            }
            catch (Exception ex)
            {
                // A cache miss is always survivable; a cache that throws must not break charting.
                _logger.LogWarning(ex, "OHLCV store read failed for {Provider} {Symbol} {Timeframe}.",
                    provider, symbol, timeframe);
                return new List<Ohlcv>();
            }
        }

        public async Task SaveAsync(string market, string provider, string symbol, string timeframe,
            IReadOnlyList<Ohlcv> bars, CancellationToken ct = default)
        {
            if (bars == null || bars.Count == 0) return;

            long barMs = TimeframeUtility.ToMilliseconds(timeframe ?? "");
            if (barMs <= 0) return;

            // Drop the forming bar: its close, high, low and volume are all still moving.
            long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var closed = bars.Where(b => ToMs(b.Date) + barMs <= nowMs).ToList();
            if (closed.Count == 0) return;

            await _writeLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                await EnsureSchemaAsync(ct).ConfigureAwait(false);
                using var db = await _factory.CreateDbContextAsync(ct).ConfigureAwait(false);

                var stamps = closed.Select(b => ToMs(b.Date)).ToList();
                var existing = await db.OhlcvData
                    .Where(e => e.Market == market && e.Provider == provider
                             && e.Symbol == symbol && e.Timeframe == timeframe
                             && stamps.Contains(e.Timestamp))
                    .Select(e => e.Timestamp)
                    .ToListAsync(ct)
                    .ConfigureAwait(false);

                var known = new HashSet<long>(existing);
                var fresh = closed
                    .Where(b => known.Add(ToMs(b.Date)))   // also de-dupes within `closed` itself
                    .Select(b => new OhlcvEntity
                    {
                        Market = market, Provider = provider, Symbol = symbol, Timeframe = timeframe,
                        Timestamp = ToMs(b.Date),
                        Open = b.Open, High = b.High, Low = b.Low, Close = b.Close, Volume = b.Volume
                    })
                    .ToList();

                if (fresh.Count == 0) return;

                db.OhlcvData.AddRange(fresh);
                await db.SaveChangesAsync(ct).ConfigureAwait(false);

                await TrimAsync(db, market, provider, symbol, timeframe, ct).ConfigureAwait(false);

                _logger.LogDebug("OHLCV store: saved {Count} bars for {Provider} {Symbol} {Timeframe}.",
                    fresh.Count, provider, symbol, timeframe);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "OHLCV store write failed for {Provider} {Symbol} {Timeframe}.",
                    provider, symbol, timeframe);
            }
            finally
            {
                _writeLock.Release();
            }
        }

        /// <summary>Keeps the newest <see cref="MaxBarsPerSeries"/> bars per series so a long-lived
        /// server cannot grow the file without bound.</summary>
        private static async Task TrimAsync(AppDbContext db, string market, string provider,
            string symbol, string timeframe, CancellationToken ct)
        {
            int count = await db.OhlcvData
                .CountAsync(e => e.Market == market && e.Provider == provider
                              && e.Symbol == symbol && e.Timeframe == timeframe, ct)
                .ConfigureAwait(false);

            int excess = count - MaxBarsPerSeries;
            if (excess <= 0) return;

            var oldest = await db.OhlcvData
                .Where(e => e.Market == market && e.Provider == provider
                         && e.Symbol == symbol && e.Timeframe == timeframe)
                .OrderBy(e => e.Timestamp)
                .Take(excess)
                .ToListAsync(ct)
                .ConfigureAwait(false);

            db.OhlcvData.RemoveRange(oldest);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Creates the table on first use. Nothing else ever did — which is why the database file
        /// existed at 0 bytes with no schema in it.
        ///
        /// <para>
        /// <b>EnsureCreated is create-or-nothing.</b> On a database that already has tables it
        /// returns false and alters nothing, so if <see cref="OhlcvEntity"/> ever gains a column,
        /// every read and write here starts throwing "no such column" — and both call sites catch
        /// and log at Warning, which is indistinguishable from an ordinary cache miss. The store
        /// would look like it was simply never hitting. So probe the table once and log at ERROR
        /// when it is not usable: a silently dead cache is the failure worth being loud about.
        /// There is still no migration path — see the note in the release docs.
        /// </para>
        /// </summary>
        private async Task EnsureSchemaAsync(CancellationToken ct)
        {
            if (Volatile.Read(ref _schemaReady) == 1) return;
            using var db = await _factory.CreateDbContextAsync(ct).ConfigureAwait(false);
            await db.Database.EnsureCreatedAsync(ct).ConfigureAwait(false);

            try
            {
                await db.OhlcvData.Take(1).ToListAsync(ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "OHLCV store schema is not usable — the table is missing or its shape has " +
                    "drifted from OhlcvEntity, and EnsureCreated will not alter an existing " +
                    "database. Every read and write will fail and the store will silently serve " +
                    "nothing. Delete the OHLCV database file to have it recreated.");
                throw;
            }

            Volatile.Write(ref _schemaReady, 1);
        }

        private static long ToMs(DateTime d) =>
            new DateTimeOffset(DateTime.SpecifyKind(d, DateTimeKind.Utc)).ToUnixTimeMilliseconds();

        private static Ohlcv ToBar(OhlcvEntity e) => new Ohlcv(
            DateTimeOffset.FromUnixTimeMilliseconds(e.Timestamp).UtcDateTime,
            e.Open, e.High, e.Low, e.Close, e.Volume);
    }
}
