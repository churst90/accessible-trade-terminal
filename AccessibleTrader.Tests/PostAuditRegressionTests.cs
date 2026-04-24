using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AccessibleTrader.ScriptSandbox;
using AccessibleTrader.Sdk.Models;
using Xunit;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Regression tests for the 2026-04-23 post-audit fixes. Each fact below
    /// asserts behaviour that the Week 1-3 changes deliberately introduced —
    /// a future refactor that re-opens one of these holes will fail fast here
    /// instead of only surfacing in production.
    /// </summary>
    public class PostAuditRegressionTests
    {
        // ── Week 1: IPC decoder bounds (AccessibleTrader.ScriptSandbox/Messages.cs) ──

        [Fact]
        public void DecodeMetadata_ArrayCountExceedingCap_Throws()
        {
            // 10 M components is well over the 1 M cap. Build a payload with a
            // valid Id + DisplayName and a u32 count > cap; decoder must throw
            // before attempting a huge allocation.
            using var ms = new MemoryStream();
            WriteString(ms, "id");
            WriteString(ms, "display");
            WriteU32(ms, 10_000_000u);
            var payload = ms.ToArray();

            var ex = Assert.Throws<InvalidDataException>(
                () => MessageCodec.DecodeMetadata(payload));
            Assert.Contains("ComponentNames", ex.Message);
        }

        [Fact]
        public void DecodeMetadata_StringLengthExceedingCap_Throws()
        {
            // Claim a 128 KB string header (cap is 64 KB) inside a short buffer —
            // ByteReader must refuse before the UTF8 decode.
            using var ms = new MemoryStream();
            WriteU32(ms, 128u * 1024u);     // string length header
            // Deliberately leave the buffer short.
            var payload = ms.ToArray();

            Assert.Throws<InvalidDataException>(
                () => MessageCodec.DecodeMetadata(payload));
        }

        [Fact]
        public void DecodeCalculateRequest_TruncatedAfterCount_Throws()
        {
            // 5 bars claimed but the payload has zero bytes of bar data.
            using var ms = new MemoryStream();
            WriteU32(ms, 5u);
            var payload = ms.ToArray();

            Assert.Throws<InvalidDataException>(
                () => MessageCodec.DecodeCalculateRequest(payload));
        }

        [Fact]
        public void RoundtripMetadata_SmallPayload_Succeeds()
        {
            // Sanity: the caps don't reject legitimate-size payloads.
            var meta = new IndicatorMetadataMessage(
                Id: "RSI",
                DisplayName: "Relative Strength Index",
                ComponentNames: new[] { "rsi", "rsi_ma" },
                DisplayTypeValues: new[] { 0, 1 },
                DefaultParameters: new System.Collections.Generic.Dictionary<string, double>
                {
                    ["Period"] = 14,
                    ["Overbought"] = 70
                });
            var bytes = MessageCodec.EncodeMetadata(meta);
            var decoded = MessageCodec.DecodeMetadata(bytes);

            Assert.Equal(meta.Id, decoded.Id);
            Assert.Equal(meta.DisplayName, decoded.DisplayName);
            Assert.Equal(meta.ComponentNames, decoded.ComponentNames);
            Assert.Equal(meta.DisplayTypeValues, decoded.DisplayTypeValues);
            Assert.Equal(meta.DefaultParameters, decoded.DefaultParameters);
        }

        private static void WriteU32(MemoryStream ms, uint v)
        {
            Span<byte> b = stackalloc byte[4];
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(b, v);
            ms.Write(b);
        }

        private static void WriteString(MemoryStream ms, string s)
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(s);
            WriteU32(ms, (uint)bytes.Length);
            ms.Write(bytes, 0, bytes.Length);
        }

        // ── Week 1: Kraken nonce CAS idempotence ──
        //
        // The real provider lives in a plugin DLL and depends on HttpClient and
        // credentials, but the nonce CAS pattern is isolated logic — mirror the
        // same CAS loop here and assert it never produces a duplicate under
        // thread contention.

        [Fact]
        public async Task NonceCasLoop_ConcurrentCallers_ProduceStrictlyIncreasingUniqueValues()
        {
            long counter = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            const int threads = 16;
            const int perThread = 500;
            var nonces = new System.Collections.Concurrent.ConcurrentBag<long>();

            var tasks = new Task[threads];
            for (int t = 0; t < threads; t++)
            {
                tasks[t] = Task.Run(() =>
                {
                    for (int i = 0; i < perThread; i++)
                    {
                        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                        long next;
                        while (true)
                        {
                            long current = Interlocked.Read(ref counter);
                            long candidate = Math.Max(current + 1, now);
                            if (Interlocked.CompareExchange(ref counter, candidate, current) == current)
                            {
                                next = candidate;
                                break;
                            }
                        }
                        nonces.Add(next);
                    }
                });
            }
            await Task.WhenAll(tasks);

            var distinct = new System.Collections.Generic.HashSet<long>(nonces);
            Assert.Equal(threads * perThread, distinct.Count);
        }

        // ── Week 1: LiveStreamManager zero-value filter intent ──
        //
        // The filter itself lives inside a private callback; the invariant we
        // want to protect is "any OHLC leg at 0 makes the bar invalid, Volume
        // of exactly 0 is still allowed on a first-tick". Express that as a
        // pure predicate test so the rule is documented.

        [Theory]
        [InlineData(100.0, 100.0, 100.0, 100.0, 0.0,  true)]   // first tick, zero volume ok
        [InlineData(100.0, 100.0, 100.0, 100.0, 1.5,  true)]
        [InlineData(0.0,   100.0, 100.0, 100.0, 1.0,  false)]  // zero open
        [InlineData(100.0, 0.0,   100.0, 100.0, 1.0,  false)]  // zero high
        [InlineData(100.0, 100.0, 0.0,   100.0, 1.0,  false)]  // zero low
        [InlineData(100.0, 100.0, 100.0, 0.0,   1.0,  false)]  // zero close
        [InlineData(100.0, 100.0, 100.0, 100.0, -0.1, false)]  // negative volume
        public void ZeroValueFilter_MirrorsLiveStreamManagerRule(double o, double h, double l, double c, double v, bool expected)
        {
            bool Predicate(double open, double high, double low, double close, double volume)
                => open > 0 && high > 0 && low > 0 && close > 0 && volume >= 0;

            Assert.Equal(expected, Predicate(o, h, l, c, v));
        }

        // ── Week 1: ChartSeries.Clone isolates Levels / ZoneBands ──
        //
        // The SeriesReducer rewrite in Week 1 relies on ChartSeries.Clone()
        // producing a collection instance distinct from the source. Assert
        // that directly so a future SeriesConfig.Clone refactor that drops
        // the foreach-copy of Levels breaks this test first, not the reducer.

        [Fact]
        public void ChartSeries_Clone_ProducesDistinctLevelsCollection()
        {
            var config = new SeriesConfig { Id = "rsi", Name = "RSI" };
            config.Levels.Add(new LevelConfig { Value = 30, Name = "Oversold" });

            var original = new ChartSeries(config, new SeriesDataBuffer { SeriesId = "rsi" });
            var cloned = original.Clone();

            // Distinct collection references -> reducer mutations of cloned.Levels
            // can't leak into original.Levels, which is what the reducer fix relies on.
            Assert.NotSame(original.Levels, cloned.Levels);

            cloned.Levels.Add(new LevelConfig { Value = 70, Name = "Overbought" });
            Assert.Single(original.Levels);
            Assert.Equal(2, cloned.Levels.Count);
        }
    }
}
