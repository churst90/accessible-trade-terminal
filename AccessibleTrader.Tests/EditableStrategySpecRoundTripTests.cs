using AccessibleTrader.Core.Services.Strategies;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Strategies;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Opening a saved strategy in the builder and pressing Save without changing anything must
    /// save the SAME strategy.
    ///
    /// <para>
    /// Written for mutation campaign A2l (2026-09-24). <see cref="EditableStrategySpec"/> is the
    /// builder's mutable mirror of <see cref="StrategySpec"/>; every field has to be copied in by
    /// <c>LoadFromSpec</c> and back out by <c>ToSpec</c>, and a field missed on either side is
    /// silently reset to its default on the next save. Four such mutants survived the full suite —
    /// a new id on every save (a duplicate strategy per edit), FixedQuantity and RiskCash swapped
    /// (a 1-contract strategy trading 100), the stop buffer dropped on load, the level-strength
    /// gate dropped on save — and looking for them found the real one: <c>ToSpec</c> hard-coded
    /// <c>StopAdjust = MoveToBreakeven</c>, so the research catalogue's TrailByAtr specs (whose
    /// "real exit is the ATR trail after TP1") lost their trail the first time a user opened one
    /// in the builder and saved it, and a BelowComponent stop or AtComponent target lost the
    /// indicator it names.
    /// </para>
    /// </summary>
    public class EditableStrategySpecRoundTripTests
    {
        /// <summary>A spec with every risk field away from its default, so a dropped field shows.</summary>
        private static StrategySpec Rich(StopAdjustOnTp1 adjust = StopAdjustOnTp1.TrailByAtr) => new(
            Id: "rich-spec",
            Name: "Cycle low, trailed",
            Description: "every field set",
            Side: OrderSide.Sell,
            Conditions: new ConditionGroup("root", LogicOperator.And, new List<ConditionNode>
            {
                new ConditionLeaf("rej", "CIPHER_SR.Support", LeafOperator.PriceRejectsLevel,
                    Value: 0, WithinNBars: 3, MinLevelStrength: 0.7),
                new ConditionLeaf("rsi", "RSI.RSI", LeafOperator.LessThan, Value: 30, Timeframe: "4h"),
            }),
            Risk: new RiskPlan(
                Stop: new StopSource(StopSourceKind.BelowComponent, PercentValue: 1.25, AtrPeriod: 21,
                    AtrMultiple: 2.5, LookbackBars: 34, FixedPrice: 0, BufferTicks: 3,
                    IndicatorCode: "ICHIMOKU", ComponentName: "Kijun-sen"),
                TpLadder: new List<TpLadderRung>
                {
                    new(TargetSourceKind.RiskRewardMultiple, Multiple: 2.0, ClosePortion: 0.4),
                    new(TargetSourceKind.AtComponent, ClosePortion: 0.6,
                        IndicatorCode: "EMA", ComponentName: "EMA 200"),
                },
                Sizing: new PositionSizing(SizingMode.FixedQuantity, RiskPercent: 0.01, FixedQuantity: 2, RiskCash: 250),
                Entry: new EntryTrigger(EntryTriggerKind.OnPullbackToLevel, LevelPrice: 101.5, NCandles: 2),
                MinRewardRiskRatio: 2.0,
                StopAdjust: adjust,
                NotionalEquity: 25_000));

        private static StrategySpec RoundTrip(StrategySpec spec)
        {
            var e = new EditableStrategySpec();
            e.LoadFromSpec(spec);
            return e.ToSpec();
        }

        [Fact]
        public void Saving_an_opened_strategy_keeps_its_id_so_it_is_updated_not_duplicated()
        {
            var saved = RoundTrip(Rich());
            Assert.Equal("rich-spec", saved.Id);
        }

        [Fact]
        public void Fixed_quantity_and_cash_risk_keep_their_meaning()
        {
            // Straight from the builder, no load: the sizing the user typed is the sizing saved.
            var e = new EditableStrategySpec
            {
                Name = "sized",
                Root = new EditableConditionNode { SignalDescriptorId = "RSI.RSI", Operator = LeafOperator.LessThan, Value = 30 },
                SizingMode = SizingMode.FixedQuantity,
                FixedQuantity = 2,
                RiskCash = 250,
            };
            var s = e.ToSpec().Risk.Sizing;
            Assert.Equal(SizingMode.FixedQuantity, s.Mode);
            Assert.Equal(2, s.FixedQuantity);
            Assert.Equal(250, s.RiskCash);

            // And through a load/save.
            var r = RoundTrip(Rich()).Risk.Sizing;
            Assert.Equal(2, r.FixedQuantity);
            Assert.Equal(250, r.RiskCash);
            Assert.Equal(0.01, r.RiskPercent);
        }

        [Fact]
        public void The_stop_survives_a_load_and_save_including_its_buffer_and_the_component_it_names()
        {
            var stop = RoundTrip(Rich()).Risk.Stop;
            Assert.Equal(StopSourceKind.BelowComponent, stop.Kind);
            Assert.Equal(3, stop.BufferTicks);
            Assert.Equal(1.25, stop.PercentValue);
            Assert.Equal(21, stop.AtrPeriod);
            Assert.Equal(2.5, stop.AtrMultiple);
            Assert.Equal(34, stop.LookbackBars);
            // A BelowComponent stop with no component resolves against nothing.
            Assert.Equal("ICHIMOKU", stop.IndicatorCode);
            Assert.Equal("Kijun-sen", stop.ComponentName);
        }

        [Theory]
        [InlineData(StopAdjustOnTp1.TrailByAtr)]
        [InlineData(StopAdjustOnTp1.None)]
        [InlineData(StopAdjustOnTp1.MoveToBreakeven)]
        public void What_happens_to_the_stop_at_the_first_target_survives_a_load_and_save(StopAdjustOnTp1 adjust)
        {
            // The catalogue's v24 cycle-low spec rides a distant 8R rung on an ATR trail; saved
            // from the builder it used to come back as MoveToBreakeven, whatever it had been.
            Assert.Equal(adjust, RoundTrip(Rich(adjust)).Risk.StopAdjust);
        }

        [Fact]
        public void The_target_ladder_survives_a_load_and_save_including_the_component_a_rung_names()
        {
            var ladder = RoundTrip(Rich()).Risk.TpLadder;
            Assert.Equal(2, ladder.Count);
            Assert.Equal(TargetSourceKind.AtComponent, ladder[1].Kind);
            Assert.Equal(0.6, ladder[1].ClosePortion);
            Assert.Equal("EMA", ladder[1].IndicatorCode);
            Assert.Equal("EMA 200", ladder[1].ComponentName);
        }

        [Fact]
        public void The_conditions_survive_a_load_and_save_including_the_level_strength_gate()
        {
            var root = Assert.IsType<ConditionGroup>(RoundTrip(Rich()).Conditions);
            var rej = Assert.IsType<ConditionLeaf>(root.Children[0]);
            Assert.Equal(LeafOperator.PriceRejectsLevel, rej.Operator);
            Assert.Equal(0.7, rej.MinLevelStrength);
            Assert.Equal(3, rej.WithinNBars);
            var rsi = Assert.IsType<ConditionLeaf>(root.Children[1]);
            Assert.Equal("4h", rsi.Timeframe);
        }

        [Fact]
        public void Everything_else_in_the_risk_plan_survives_a_load_and_save()
        {
            var saved = RoundTrip(Rich());
            Assert.Equal(OrderSide.Sell, saved.Side);
            Assert.Equal(2.0, saved.Risk.MinRewardRiskRatio);
            Assert.Equal(25_000, saved.Risk.NotionalEquity);
            Assert.Equal(EntryTriggerKind.OnPullbackToLevel, saved.Risk.Entry.Kind);
            Assert.Equal(101.5, saved.Risk.Entry.LevelPrice);
            Assert.Equal(2, saved.Risk.Entry.NCandles);
        }
    }
}
