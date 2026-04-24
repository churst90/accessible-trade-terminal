using System;
using System.Collections.Generic;
using System.Reflection;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services.Strategies;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Strategies;
using AccessibleTrader.Tests.Mocks;
using Xunit;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Pins the Tier B enhancements that don't have existing coverage:
    /// B.1 Coinbase product-id consolidation (private static helper),
    /// B.2 the obsolete legacy TimeframeUtility wrapping correctly into the
    /// Models-namespace regex-based parser that Bitstamp now uses,
    /// B.5 the multi-rung TP ladder safety warning added to <c>SetupSonifier</c>.
    /// </summary>
    public class TierBRegressionTests
    {
        // ── B.1 — Coinbase ToProductId ────────────────────────────────────────

        [Theory]
        [InlineData("BTC/USD",  "BTC-USD")]
        [InlineData("btc/usd",  "BTC-USD")]   // case normalised
        [InlineData("ETH-USDT", "ETH-USDT")]  // already dashed → only uppercased
        [InlineData("SOLUSD",   "SOLUSD")]    // no separator → passed through
        [InlineData("",         "")]           // null-safe empty
        public void Coinbase_ToProductId_SlashToDashUpperCase(string input, string expected)
        {
            var asm = Assembly.Load("AccessibleTrader.Plugins.Coinbase");
            var type = asm.GetType("AccessibleTrader.Plugins.Coinbase.CoinbaseProvider")
                ?? throw new InvalidOperationException("CoinbaseProvider not found.");
            var method = type.GetMethod("ToProductId",
                BindingFlags.NonPublic | BindingFlags.Static,
                binder: null, types: new[] { typeof(string) }, modifiers: null)
                ?? throw new MissingMethodException("CoinbaseProvider.ToProductId not found.");
            string actual = (string)method.Invoke(null, new object[] { input })!;
            Assert.Equal(expected, actual);
        }

        // ── B.2 — legacy TimeframeUtility wrapper ─────────────────────────────

        [Theory]
        [InlineData("1m",  60)]
        [InlineData("5m",  300)]
        [InlineData("1h",  3600)]
        [InlineData("4h",  14400)]
        [InlineData("1d",  86400)]
        [InlineData("1w",  604800)]
        public void LegacyTimeframeUtility_StillResolvesCanonicalTokens(string timeframe, int expectedSeconds)
        {
            // The legacy Configuration.TimeframeUtility is now marked [Obsolete] but kept
            // for binary-compat with already-loaded plugin DLLs. The test pins that the
            // hardcoded switch still returns the same integer seconds for the canonical
            // tokens — breaking this contract would break any plugin still linking to it.
#pragma warning disable CS0618
            int actual = AccessibleTrader.Sdk.Configuration.TimeframeUtility.ToSeconds(timeframe);
#pragma warning restore CS0618
            Assert.Equal(expectedSeconds, actual);
        }

        [Theory]
        [InlineData("1m",  60)]
        [InlineData("8h",  28800)]   // 8h is in the Models regex parser but NOT the legacy switch
        [InlineData("3d",  259200)]
        [InlineData("2w",  1209600)] // multi-week — regex handles arbitrary N<unit>
        public void ModelsTimeframeUtility_HandlesExtendedTokens(string timeframe, int expectedSeconds)
        {
            // The new-style Models.TimeframeUtility is regex-based, so it answers tokens
            // like "8h" that the legacy switch would silently return -1 for. Bitstamp's
            // FetchOhlcvAsync migrated to this on 2026-04-23 to pick up 8h support.
            int actual = AccessibleTrader.Sdk.Models.TimeframeUtility.ToSeconds(timeframe);
            Assert.Equal(expectedSeconds, actual);
        }

        [Fact]
        public void ModelsTimeframeUtility_UnrecognisedToken_ReturnsZero()
        {
            // Zero is the new "unrecognised" signal; Bitstamp's guard upgraded the old
            // `== -1` check to `<= 0` so both legacy (-1 never happens now) and new (0)
            // return paths map to the empty-result shape.
            int actual = AccessibleTrader.Sdk.Models.TimeframeUtility.ToSeconds("xyz");
            Assert.Equal(0, actual);
        }

        // ── B.5 — Multi-rung TP ladder warning ────────────────────────────────

        [Fact]
        public void SetupSonifier_SingleRungLadder_EmitsNoMultiRungWarning()
        {
            // Single-rung plans trade exactly like bracket orders — no warning needed.
            string msg = CaptureArmedSpeech(new[] { 50_100.0 });
            Assert.Contains("Long setup armed", msg);
            Assert.Contains("first target 50100.00", msg);
            Assert.DoesNotContain("Ladder has", msg);
            Assert.DoesNotContain("multi-rung", msg);
        }

        [Fact]
        public void SetupSonifier_MultiRungLadder_EmitsRungCountAndManualWarning()
        {
            // 3-rung plan → warning that only the first rung fires live until broker-
            // side bracket plumbing ships. Without this warning the trader thinks all
            // three rungs are active — the silent-failure rule exactly.
            string msg = CaptureArmedSpeech(new[] { 50_100.0, 50_250.0, 50_500.0 });
            Assert.Contains("Ladder has 3 rungs", msg);
            Assert.Contains("first target fires live", msg);
        }

        // ── Fixtures ──────────────────────────────────────────────────────────

        /// <summary>
        /// Drives <see cref="SetupSonifier"/> through one <c>SetupArmedEvent</c> and
        /// returns the speech string. Uses the project's existing mocks for speech /
        /// earcon / patch registry so the test doesn't pull the audio stack.
        /// </summary>
        private static string CaptureArmedSpeech(double[] tpPrices)
        {
            var bus = new SpyEventBus();
            var speech = new CapturingSpeech();
            var earcon = new MockEarconService();
            var sonifier = new SetupSonifier(bus, earcon, speech);

            var plan = new ResolvedRiskPlan(
                EntryPrice: 50_000.0,
                StopPrice: 49_500.0,
                TpPrices: tpPrices,
                ClosePortions: new double[tpPrices.Length],
                Quantity: 1.0,
                RewardRiskRatio: 2.0,
                RiskCash: 500.0,
                Notes: "test");

            bus.Publish(new SetupArmedEvent(
                StrategyName: "Test Strategy",
                InstanceId: "test-1",
                Side: OrderSide.Buy,
                TriggerDescription: "Test trigger.",
                ResolvedPlan: plan));

            return speech.LastMessage;
        }

        private sealed class CapturingSpeech : AccessibleTrader.Core.Services.ISpeechManager
        {
            public string LastMessage { get; private set; } = string.Empty;
            public bool IsActive => true;
            public bool IsSpeechEnabled { get; set; } = true;
            public string SpeechMode => "Test";
            public Action<string>? OnSpeak { get; set; }
            public void Silence() { }
            public void Speak(string text, bool interrupt = false) => LastMessage = text;
        }
    }
}
