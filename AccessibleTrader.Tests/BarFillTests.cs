using AccessibleTrader.Core.Services.Trading;
using AccessibleTrader.Sdk.Plugins;
using Xunit;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// The one rule every fill site in the app now shares: an order fills at its
    /// stated level only if the market was ever at that level.
    ///
    /// <para>
    /// These are written as the four faces of the single mistake, because that is how
    /// the defect actually shipped — four sites, each reviewed on its own, each wrong
    /// in a way that looked local. Asserting all four here is what makes the fifth
    /// fill site inherit the answer instead of guessing.
    /// </para>
    /// </summary>
    public class BarFillTests
    {
        // ── Face 1: a stop on the wrong side of the market ───────────────────

        [Fact]
        public void A_buy_stop_below_the_open_fills_at_the_open_not_below_the_market()
        {
            // The free-money case. Trigger 50, market opened at 100.
            Assert.Equal(100.0, BarFill.Price(level: 50, barOpen: 100, OrderSide.Buy, stopLike: true));
        }

        [Fact]
        public void A_sell_stop_above_the_open_fills_at_the_open()
        {
            Assert.Equal(100.0, BarFill.Price(level: 150, barOpen: 100, OrderSide.Sell, stopLike: true));
        }

        // ── Face 2: a limit on the wrong side of the market ──────────────────

        [Fact]
        public void A_buy_limit_above_the_open_fills_at_the_open_not_at_the_limit()
        {
            // The mirror, opposite sign: the user would have been charged 150 for
            // something the market was selling at 100.
            Assert.Equal(100.0, BarFill.Price(level: 150, barOpen: 100, OrderSide.Buy, stopLike: false));
        }

        [Fact]
        public void A_sell_limit_below_the_open_fills_at_the_open()
        {
            Assert.Equal(100.0, BarFill.Price(level: 50, barOpen: 100, OrderSide.Sell, stopLike: false));
        }

        // ── Face 3 and 4: gaps ───────────────────────────────────────────────

        [Fact]
        public void A_long_stop_gapped_through_fills_at_the_gap_not_at_the_stop()
        {
            // The bar opened at 80 with the stop at 90. 90 was never available.
            Assert.Equal(80.0, BarFill.StopExit(stop: 90, barOpen: 80, OrderSide.Buy));
        }

        [Fact]
        public void A_short_stop_gapped_through_fills_at_the_gap()
        {
            Assert.Equal(120.0, BarFill.StopExit(stop: 110, barOpen: 120, OrderSide.Sell));
        }

        [Fact]
        public void A_long_target_gapped_past_fills_at_the_gap_and_keeps_the_extra()
        {
            // Opposite sign, same rule: the gap paid better than the target.
            Assert.Equal(130.0, BarFill.TargetExit(target: 120, barOpen: 130, OrderSide.Buy));
        }

        [Fact]
        public void A_short_target_gapped_past_fills_at_the_gap()
        {
            Assert.Equal(70.0, BarFill.TargetExit(target: 80, barOpen: 70, OrderSide.Sell));
        }

        // ── The ordinary cases the rule must not disturb ─────────────────────

        [Fact]
        public void An_order_the_market_reaches_during_the_bar_fills_at_its_level()
        {
            // Buy stop above the open, reached intrabar — fills at the trigger.
            Assert.Equal(120.0, BarFill.Price(level: 120, barOpen: 100, OrderSide.Buy, stopLike: true));
            // Buy limit below the open, reached intrabar — fills at the limit.
            Assert.Equal(90.0, BarFill.Price(level: 90, barOpen: 100, OrderSide.Buy, stopLike: false));
            // And both exit forms.
            Assert.Equal(90.0, BarFill.StopExit(stop: 90, barOpen: 100, OrderSide.Buy));
            Assert.Equal(120.0, BarFill.TargetExit(target: 120, barOpen: 100, OrderSide.Buy));
        }

        [Fact]
        public void A_level_exactly_at_the_open_fills_at_that_price_either_way()
        {
            // The boundary is not a special case — both readings give 100 — but it is
            // where an off-by-one in the comparison would show up.
            Assert.Equal(100.0, BarFill.Price(level: 100, barOpen: 100, OrderSide.Buy, stopLike: true));
            Assert.Equal(100.0, BarFill.Price(level: 100, barOpen: 100, OrderSide.Buy, stopLike: false));
            Assert.Equal(100.0, BarFill.Price(level: 100, barOpen: 100, OrderSide.Sell, stopLike: true));
            Assert.Equal(100.0, BarFill.Price(level: 100, barOpen: 100, OrderSide.Sell, stopLike: false));
        }

        [Fact]
        public void A_nonsense_level_or_open_is_returned_untouched_rather_than_invented()
        {
            // Callers pass bar.Close as a last-resort level; a zero or negative price
            // means "unknown", and guessing a fill from it would be worse than the
            // caller's own fallback.
            Assert.Equal(0.0, BarFill.Price(level: 0, barOpen: 100, OrderSide.Buy, stopLike: true));
            Assert.Equal(50.0, BarFill.Price(level: 50, barOpen: 0, OrderSide.Buy, stopLike: true));
            Assert.Equal(50.0, BarFill.Price(level: 50, barOpen: double.NaN, OrderSide.Buy, stopLike: false));
        }

        // ── The structural one ───────────────────────────────────────────────

        [Fact]
        public void No_fill_is_free_money_and_no_fill_is_an_overcharge()
        {
            // The two invariants behind every case above, asserted over a grid rather
            // than at chosen points.
            //
            // A STOP must never fill better than the bar opened — that was the free
            // money. A LIMIT must never fill worse than the bar opened — that was the
            // overcharge. Note the asymmetry is real and load-bearing: a buy limit
            // SHOULD fill below the open when the market trades down to it, so a
            // blanket "never better than the open" would be the wrong fix.
            double[] levels = { 60, 80, 95, 100, 105, 120, 140 };
            double[] opens = { 70, 90, 100, 110, 130 };

            foreach (double open in opens)
            foreach (double level in levels)
            {
                double buyStop = BarFill.Price(level, open, OrderSide.Buy, stopLike: true);
                double sellStop = BarFill.Price(level, open, OrderSide.Sell, stopLike: true);
                double buyLimit = BarFill.Price(level, open, OrderSide.Buy, stopLike: false);
                double sellLimit = BarFill.Price(level, open, OrderSide.Sell, stopLike: false);

                Assert.True(buyStop >= open - 1e-9,
                    $"buy stop filled at {buyStop}, below the open of {open} (level {level}) — free money");
                Assert.True(sellStop <= open + 1e-9,
                    $"sell stop filled at {sellStop}, above the open of {open} (level {level}) — free money");
                Assert.True(buyLimit <= open + 1e-9,
                    $"buy limit filled at {buyLimit}, above the open of {open} (level {level}) — overcharge");
                Assert.True(sellLimit >= open - 1e-9,
                    $"sell limit filled at {sellLimit}, below the open of {open} (level {level}) — overcharge");

                // And a fill is always one of the two real prices, never a blend.
                foreach (double px in new[] { buyStop, sellStop, buyLimit, sellLimit })
                    Assert.True(px == level || px == open,
                        $"filled at {px}, which is neither the level {level} nor the open {open}");
            }
        }
    }
}
