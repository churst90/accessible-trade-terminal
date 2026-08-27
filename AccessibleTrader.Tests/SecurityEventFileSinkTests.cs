using System.Text.Json;
using AccessibleTrader.Core.Services.Security;
using AccessibleTrader.Sdk.Services;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Tests for the persistent JSONL file sink (post-audit 2026-04-23).
    /// Covers: single event persistence, round-trip of Data dictionary,
    /// daily rotation, append semantics (no truncation on reopen),
    /// forward-to-inner invariant, and degrade-gracefully-on-IO-failure.
    /// </summary>
    public class SecurityEventFileSinkTests : IDisposable
    {
        private readonly string _tempDir;

        public SecurityEventFileSinkTests()
        {
            _tempDir = TestTemp.NewPath("AccessibleTrader.Tests.SecurityEventFileSinkTests.");
        }

        public void Dispose()
        {
            try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); }
            catch { /* best-effort cleanup */ }
        }

        [Fact]
        public void Record_WritesJsonlLineWithAllFields()
        {
            using var sink = new SecurityEventFileSink(new SecurityEventLog(), _tempDir);
            var ts = new DateTime(2026, 4, 23, 18, 4, 2, 111, DateTimeKind.Utc);
            var ev = new SecurityEvent(
                UtcTimestamp: ts,
                Kind: SecurityEventKind.AppContainerFallback,
                Source: "WindowsAppContainerLauncher",
                Message: "Fallback: ACL gap",
                Data: new Dictionary<string, string> { ["Win32Error"] = "5" });

            sink.Record(ev);

            var path = Path.Combine(_tempDir, "security-events-2026-04-23.jsonl");
            Assert.True(File.Exists(path));
            var lines = File.ReadAllLines(path);
            Assert.Single(lines);

            using var doc = JsonDocument.Parse(lines[0]);
            var root = doc.RootElement;
            Assert.Equal("2026-04-23T18:04:02.1110000Z", root.GetProperty("ts").GetString());
            Assert.Equal("AppContainerFallback", root.GetProperty("kind").GetString());
            Assert.Equal("WindowsAppContainerLauncher", root.GetProperty("source").GetString());
            Assert.Equal("Fallback: ACL gap", root.GetProperty("message").GetString());
            Assert.Equal("5", root.GetProperty("data").GetProperty("Win32Error").GetString());
        }

        [Fact]
        public void Record_MultipleEventsSameDay_AppendsToSameFile()
        {
            using var sink = new SecurityEventFileSink(new SecurityEventLog(), _tempDir);
            var day = new DateTime(2026, 4, 23, 0, 0, 0, DateTimeKind.Utc);
            for (int i = 0; i < 5; i++)
            {
                sink.Record(new SecurityEvent(
                    UtcTimestamp: day.AddMinutes(i),
                    Kind: SecurityEventKind.MemoryQuotaKill,
                    Source: "OutOfProcessScriptHost",
                    Message: $"kill #{i}"));
            }

            var path = Path.Combine(_tempDir, "security-events-2026-04-23.jsonl");
            var lines = File.ReadAllLines(path);
            Assert.Equal(5, lines.Length);
        }

        [Fact]
        public void Record_EventsOnDifferentDays_GoToDifferentFiles()
        {
            using var sink = new SecurityEventFileSink(new SecurityEventLog(), _tempDir);
            sink.Record(new SecurityEvent(new DateTime(2026, 4, 23, 12, 0, 0, DateTimeKind.Utc),
                SecurityEventKind.Other, "src", "one"));
            sink.Record(new SecurityEvent(new DateTime(2026, 4, 24, 0, 0, 0, DateTimeKind.Utc),
                SecurityEventKind.Other, "src", "two"));

            Assert.True(File.Exists(Path.Combine(_tempDir, "security-events-2026-04-23.jsonl")));
            Assert.True(File.Exists(Path.Combine(_tempDir, "security-events-2026-04-24.jsonl")));
        }

        [Fact]
        public void Record_ForwardsToInnerRingBuffer()
        {
            var inner = new SecurityEventLog();
            using var sink = new SecurityEventFileSink(inner, _tempDir);
            var ev = new SecurityEvent(DateTime.UtcNow,
                SecurityEventKind.PluginTrustRejected, "PluginLoader", "hash mismatch");

            sink.Record(ev);

            var recent = sink.Recent(limit: 10);
            Assert.Single(recent);
            Assert.Equal("PluginLoader", recent[0].Source);

            // And Recent delegates through the inner ring buffer too.
            var innerRecent = inner.Recent(limit: 10);
            Assert.Single(innerRecent);
            Assert.Equal("PluginLoader", innerRecent[0].Source);
        }

        [Fact]
        public void Record_ReopenSink_AppendsRatherThanTruncates()
        {
            var firstSink = new SecurityEventFileSink(new SecurityEventLog(), _tempDir);
            firstSink.Record(new SecurityEvent(
                new DateTime(2026, 4, 23, 9, 0, 0, DateTimeKind.Utc),
                SecurityEventKind.Other, "src", "first"));
            firstSink.Dispose();

            var secondSink = new SecurityEventFileSink(new SecurityEventLog(), _tempDir);
            secondSink.Record(new SecurityEvent(
                new DateTime(2026, 4, 23, 9, 1, 0, DateTimeKind.Utc),
                SecurityEventKind.Other, "src", "second"));
            secondSink.Dispose();

            var lines = File.ReadAllLines(Path.Combine(_tempDir, "security-events-2026-04-23.jsonl"));
            Assert.Equal(2, lines.Length);
            Assert.Contains("first", lines[0]);
            Assert.Contains("second", lines[1]);
        }

        [Fact]
        public void Record_NullEvent_DoesNotThrow()
        {
            using var sink = new SecurityEventFileSink(new SecurityEventLog(), _tempDir);
            sink.Record(null!);  // inner guards; sink must match.
            // No file should exist because the null event never got persisted.
            if (Directory.Exists(_tempDir))
                Assert.Empty(Directory.GetFiles(_tempDir));
        }

        [Fact]
        public void Constructor_BadDirectory_DoesNotThrow_AndRecordDegradesGracefully()
        {
            // Path with a NUL byte is rejected by Directory.CreateDirectory — the sink
            // must swallow the failure on construction, forward to inner on Record, and
            // log (but not throw) the IO failure.
            string badPath = "\0/definitely-not-a-path";
            using var sink = new SecurityEventFileSink(new SecurityEventLog(), badPath);

            var ev = new SecurityEvent(DateTime.UtcNow, SecurityEventKind.Other, "src", "x");
            sink.Record(ev);   // must not throw

            // Ring-buffer path still works.
            Assert.Single(sink.Recent(limit: 5));
        }
    }
}
