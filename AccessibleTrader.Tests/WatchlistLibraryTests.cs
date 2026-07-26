using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Screening;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Screening;
using Xunit;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Coverage for <see cref="JsonWatchlistLibrary"/>. Watchlists are user data with no other
    /// copy, so the invariants that matter are: entries de-duplicate, ordering is preserved and
    /// user-controlled, and a corrupt file is quarantined rather than silently replaced by an
    /// empty library on the next save.
    /// </summary>
    public class WatchlistLibraryTests : IDisposable
    {
        private sealed class TempPaths : IPlatformPathService
        {
            public TempPaths(string dir) { AppDataDirectory = dir; CacheDirectory = dir; }
            public string AppDataDirectory { get; }
            public string CacheDirectory { get; }
        }

        private readonly string _dir;
        private readonly TempPaths _paths;

        public WatchlistLibraryTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "at-watchlist-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            _paths = new TempPaths(_dir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_dir, recursive: true); } catch { /* best-effort cleanup */ }
        }

        private static WatchlistEntry Entry(string symbol, string provider = "Binance") =>
            new(provider, symbol, MarketType.Crypto);

        [Fact]
        public void MissingFile_StartsEmptyAndDoesNotThrow()
        {
            var lib = new JsonWatchlistLibrary(_paths);
            Assert.Empty(lib.All);
        }

        [Fact]
        public void UpsertAndReload_RoundTripsThroughDisk()
        {
            var lib = new JsonWatchlistLibrary(_paths);
            var list = Watchlist.Create("Majors");
            lib.Upsert(list);
            lib.AddEntry(list.Id, Entry("BTC/USDT"));
            lib.AddEntry(list.Id, Entry("ETH/USDT"));

            var reloaded = new JsonWatchlistLibrary(_paths);
            var restored = Assert.Single(reloaded.All);
            Assert.Equal("Majors", restored.Name);
            Assert.Equal(new[] { "BTC/USDT", "ETH/USDT" }, restored.Entries.Select(e => e.Symbol));
        }

        [Fact]
        public void AddEntry_IsIdempotentByKey()
        {
            var lib = new JsonWatchlistLibrary(_paths);
            var list = Watchlist.Create("Dupes");
            lib.Upsert(list);

            Assert.True(lib.AddEntry(list.Id, Entry("BTC/USDT")));
            Assert.False(lib.AddEntry(list.Id, Entry("BTC/USDT")));
            Assert.Single(lib.GetById(list.Id)!.Entries);
        }

        [Fact]
        public void AddEntry_SameSymbolDifferentProvider_IsNotADuplicate()
        {
            // "BTC/USDT" is a different instrument on each venue — different tick size, different
            // history. Collapsing them would silently drop one from the user's list.
            var lib = new JsonWatchlistLibrary(_paths);
            var list = Watchlist.Create("Cross venue");
            lib.Upsert(list);

            Assert.True(lib.AddEntry(list.Id, Entry("BTC/USDT", "Binance")));
            Assert.True(lib.AddEntry(list.Id, Entry("BTC/USDT", "Kraken")));
            Assert.Equal(2, lib.GetById(list.Id)!.Entries.Count);
        }

        [Fact]
        public void MoveEntry_ReordersAndClampsAtBounds()
        {
            var lib = new JsonWatchlistLibrary(_paths);
            var list = Watchlist.Create("Ordered");
            lib.Upsert(list);
            lib.AddEntry(list.Id, Entry("AAA"));
            lib.AddEntry(list.Id, Entry("BBB"));
            lib.AddEntry(list.Id, Entry("CCC"));

            string bbbKey = Entry("BBB").Key;
            Assert.Equal(0, lib.MoveEntry(list.Id, bbbKey, -1));
            Assert.Equal(new[] { "BBB", "AAA", "CCC" }, lib.GetById(list.Id)!.Entries.Select(e => e.Symbol));

            // Already at the front: clamps rather than wrapping or throwing.
            Assert.Equal(0, lib.MoveEntry(list.Id, bbbKey, -5));
            Assert.Equal(2, lib.MoveEntry(list.Id, bbbKey, 99));
            Assert.Equal(new[] { "AAA", "CCC", "BBB" }, lib.GetById(list.Id)!.Entries.Select(e => e.Symbol));
        }

        [Fact]
        public void MoveEntry_UnknownKey_ReturnsMinusOne()
        {
            var lib = new JsonWatchlistLibrary(_paths);
            var list = Watchlist.Create("Empty");
            lib.Upsert(list);
            Assert.Equal(-1, lib.MoveEntry(list.Id, "nope", 1));
        }

        [Fact]
        public void RemoveEntry_RemovesOnlyTheMatchingKey()
        {
            var lib = new JsonWatchlistLibrary(_paths);
            var list = Watchlist.Create("Removal");
            lib.Upsert(list);
            lib.AddEntry(list.Id, Entry("AAA"));
            lib.AddEntry(list.Id, Entry("BBB"));

            Assert.True(lib.RemoveEntry(list.Id, Entry("AAA").Key));
            Assert.False(lib.RemoveEntry(list.Id, Entry("AAA").Key));
            Assert.Equal(new[] { "BBB" }, lib.GetById(list.Id)!.Entries.Select(e => e.Symbol));
        }

        [Fact]
        public void CorruptFile_IsQuarantinedInsteadOfSilentlyOverwritten()
        {
            string path = Path.Combine(_dir, "watchlists.json");
            File.WriteAllText(path, "{ this is not valid json");

            var lib = new JsonWatchlistLibrary(_paths);
            Assert.Empty(lib.All);

            // The user's bytes must still exist somewhere on disk under a sibling name.
            var quarantined = Directory.GetFiles(_dir)
                .Where(f => !string.Equals(f, path, StringComparison.Ordinal))
                .ToList();
            Assert.NotEmpty(quarantined);
        }

        [Fact]
        public void Remove_DropsTheListAndPersists()
        {
            var lib = new JsonWatchlistLibrary(_paths);
            var list = Watchlist.Create("Doomed");
            lib.Upsert(list);
            lib.Remove(list.Id);

            Assert.Empty(new JsonWatchlistLibrary(_paths).All);
        }
    }
}
