using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.ChangeLog;
using Microsoft.Extensions.Logging.Abstractions;

namespace AccessibleTrader.Tests;

public class ChangeLogStoreTests
{
    private static readonly DateTime Now = new(2026, 11, 18, 10, 0, 0, DateTimeKind.Utc);

    private sealed class TempPaths : IPlatformPathService
    {
        public TempPaths(string root) { AppDataDirectory = root; CacheDirectory = root; }
        public string AppDataDirectory { get; }
        public string CacheDirectory { get; }
    }

    private static ChangeLogStore NewStore(string dir)
        => new(new TempPaths(dir), NullLogger<ChangeLogStore>.Instance);

    private static ChangeLogEntry Entry(string key, ChangeLogCategory category = ChangeLogCategory.Money)
        => new(key, Now, category, "BTC/USDT filled.", "BTC/USDT", false);

    [Fact]
    public async Task EntriesAndMarksRoundTripThroughTheFile()
    {
        var dir = TestTemp.NewDir("att-changelog-store-");
        var store = NewStore(dir);
        await store.AppendAsync(Entry("one"));
        await store.AppendAsync(Entry("two", ChangeLogCategory.Alert));
        await store.MarkHeardAsync(new[] { "one" });
        await store.SetLastPresentAsync(Now.AddMinutes(-5));

        var reloaded = NewStore(dir);
        var doc = await reloaded.SnapshotAsync();

        Assert.Equal(new[] { "one", "two" }, doc.Entries.Select(e => e.Key));
        Assert.True(doc.Entries[0].Heard);
        Assert.False(doc.Entries[1].Heard);
        Assert.NotNull(doc.LastCatchUpUtc);
        Assert.Equal(Now.AddMinutes(-5), doc.LastPresentUtc);
    }

    [Fact]
    public async Task TwoInstancesOnOnePathAppendConcurrentlyAndLoseNothing()
    {
        var dir = TestTemp.NewDir("att-changelog-concurrent-");
        var first = NewStore(dir);
        var second = NewStore(dir);

        async Task Fill(ChangeLogStore store, char prefix)
        {
            await Task.Yield();
            for (int i = 0; i < 200; i++)
                await store.AppendAsync(Entry($"{prefix}|{i:000}"));
        }

        await Task.WhenAll(
            Task.Run(() => Fill(first, 'a')),
            Task.Run(() => Fill(second, 'b')));

        var doc = await first.SnapshotAsync();
        Assert.Equal(400, doc.Entries.Count);
        Assert.Equal(400, doc.Entries.Select(e => e.Key).Distinct().Count());
    }

    [Fact]
    public async Task ACorruptFileIsQuarantinedAndAFreshLogStarts()
    {
        var dir = TestTemp.NewDir("att-changelog-corrupt-");
        string path = Path.Combine(dir, "change-log.json");
        File.WriteAllText(path, "{ this is not json");

        var store = NewStore(dir);
        var doc = await store.SnapshotAsync();

        Assert.Empty(doc.Entries);
        Assert.True(Directory.EnumerateFiles(dir, "change-log.json.corrupt-*").Any(),
            "The unreadable change log was not moved aside.");

        await store.AppendAsync(Entry("fresh"));
        Assert.Single((await store.SnapshotAsync()).Entries);
    }
}
