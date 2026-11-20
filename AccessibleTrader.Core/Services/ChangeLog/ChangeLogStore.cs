using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace AccessibleTrader.Core.Services.ChangeLog
{
    /// <summary>
    /// <c>change-log.json</c> for the current user, beside their other JSON files.
    ///
    /// <para>
    /// One lock per FULL PATH is shared by every instance in the process, because a browser
    /// circuit, the headless session and the hosted monitor can each hold an instance for the
    /// same user. Every write is a read-modify-write under that lock through
    /// <see cref="ChangeLogRules"/>, then <see cref="AtomicFile"/>.
    /// </para>
    ///
    /// <para>
    /// A read or write failure is logged by exception type and never thrown to a bus subscriber:
    /// a fill must never fail because the change log could not be written.
    /// </para>
    /// </summary>
    public class ChangeLogStore
    {
        private static readonly ConcurrentDictionary<string, SemaphoreSlim> PathLocks =
            new(OperatingSystem.IsWindows()
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal);

        private static readonly JsonSerializerOptions Options = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() },
        };

        private readonly string _filepath;
        private readonly string _lockKey;
        private readonly SemaphoreSlim _gate;
        private readonly ILogger<ChangeLogStore>? _logger;
        private readonly TimeProvider _time;

        public ChangeLogStore(
            IPlatformPathService paths,
            ILogger<ChangeLogStore>? logger = null,
            TimeProvider? time = null)
        {
            _filepath = Path.GetFullPath(Path.Combine(paths.AppDataDirectory, "change-log.json"));
            _lockKey = _filepath;
            _gate = PathLocks.GetOrAdd(_lockKey, _ => new SemaphoreSlim(1, 1));
            _logger = logger;
            _time = time ?? TimeProvider.System;
        }

        private DateTime NowUtc => _time.GetUtcNow().UtcDateTime;

        public virtual async Task AppendAsync(ChangeLogEntry entry)
        {
            await MutateAsync(doc => ChangeLogRules.Append(doc, entry, NowUtc))
                .ConfigureAwait(false);
        }

        public async Task<ChangeLogDocument> SnapshotAsync()
        {
            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                return ReadUnlocked();
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(
                    "Change log snapshot failed ({Type}).", ex.GetType().Name);
                return ChangeLogDocument.Empty;
            }
            finally
            {
                _gate.Release();
            }
        }

        public async Task MarkHeardAsync(IEnumerable<string> keys)
        {
            var heard = keys?.Distinct(StringComparer.Ordinal).ToList()
                       ?? new List<string>();
            await MutateAsync(doc => ChangeLogRules.MarkHeard(doc, heard, NowUtc))
                .ConfigureAwait(false);
        }

        public async Task SetLastPresentAsync(DateTime timeUtc)
        {
            // A new away mark drops the last one's positions: they belong to an earlier leave.
            await MutateAsync(doc => doc with { LastPresentUtc = timeUtc, AwayPositions = null })
                .ConfigureAwait(false);
        }

        /// <summary>
        /// The open positions when the user left. Kept only while <paramref name="leftUtc"/> is
        /// still the newest away mark: a slow read finishing after a later leave never replaces
        /// that leave's own.
        /// </summary>
        public async Task SetAwayPositionsAsync(DateTime leftUtc, IReadOnlyList<HeldPositionMark> positions)
        {
            await MutateAsync(doc => doc.LastPresentUtc == leftUtc
                    ? doc with { AwayPositions = new ChangeLogAwayPositions(leftUtc, positions.ToList()) }
                    : doc)
                .ConfigureAwait(false);
        }

        /// <summary>Forgets the kept positions once they have been said.</summary>
        public async Task ClearAwayPositionsAsync()
        {
            await MutateAsync(doc => doc.AwayPositions == null ? doc : doc with { AwayPositions = null })
                .ConfigureAwait(false);
        }

        private async Task MutateAsync(Func<ChangeLogDocument, ChangeLogDocument> change)
        {
            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                var doc = ReadUnlocked();
                var next = change(doc);
                string json = JsonSerializer.Serialize(next, Options);
                await AtomicFile.WriteAllTextAsync(_filepath, json).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(
                    "Change log write failed ({Type}).", ex.GetType().Name);
            }
            finally
            {
                _gate.Release();
            }
        }

        /// <remarks>Caller holds the per-path gate.</remarks>
        private ChangeLogDocument ReadUnlocked()
        {
            try
            {
                if (!File.Exists(_filepath)) return ChangeLogDocument.Empty;

                string json = File.ReadAllText(_filepath);
                if (string.IsNullOrWhiteSpace(json)) return ChangeLogDocument.Empty;

                var doc = JsonSerializer.Deserialize<ChangeLogDocument>(json, Options)
                          ?? ChangeLogDocument.Empty;
                return doc with { Entries = doc.Entries ?? new List<ChangeLogEntry>() };
            }
            catch (JsonException ex)
            {
                _logger?.LogWarning(
                    "Change log file did not parse ({Type}); quarantined.", ex.GetType().Name);
                CorruptFileQuarantine.MoveAside(_filepath, ex);
                return ChangeLogDocument.Empty;
            }
        }
    }
}
