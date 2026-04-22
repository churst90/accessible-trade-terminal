using System.Text;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Strategies;

namespace AccessibleTrader.Core.Services.Strategies
{
    /// <summary>
    /// Builds a plain-English sentence from an <see cref="EditableStrategySpec"/> so the user
    /// can verify their build by ear before saving. The narration follows the order the user
    /// composed the spec in: side + name + conditions + stop + TP ladder + R:R gate + sizing +
    /// entry trigger.
    ///
    /// Uses an injected descriptor lookup so the narrator remains decoupled from
    /// <c>ISignalCatalog</c> at the service boundary — tests can supply any Func.
    /// </summary>
    public sealed class StrategySpecNarrator : IStrategySpecNarrator
    {
        /// <summary>Maps a signal descriptor id to the human-readable label shown in the UI.
        /// Falls back to "(unset)" when the id is unknown or empty.</summary>
        public delegate string DescriptorLabelLookup(string descriptorId);

        private readonly DescriptorLabelLookup _label;

        public StrategySpecNarrator(DescriptorLabelLookup label)
        {
            _label = label;
        }

        public string Narrate(EditableStrategySpec spec)
        {
            var sb = new StringBuilder();
            sb.Append(spec.Side == OrderSide.Buy ? "Long " : "Short ");
            sb.Append("setup. ");
            if (!string.IsNullOrEmpty(spec.Name)) sb.Append(spec.Name).Append(". ");

            sb.Append("Conditions: ");
            if (spec.Root == null) sb.Append("none defined. ");
            else NarrateNode(spec.Root, sb);

            sb.Append(" Stop: ").Append(spec.StopKind);
            if (spec.StopKind == StopSourceKind.PercentOfPrice)
                sb.Append(" at ").Append(spec.StopPercent.ToString("F2")).Append(" percent");
            else if (spec.StopKind == StopSourceKind.AtrMultiple)
                sb.Append(" at ").Append(spec.StopAtrMultiple.ToString("F1")).Append(" times ATR period ").Append(spec.StopAtrPeriod);
            else if (spec.StopKind == StopSourceKind.BelowSwingLow)
                sb.Append(" below ").Append(spec.StopLookback).Append(" bar swing");

            sb.Append(". Take profit ladder: ");
            if (spec.TpRungs.Count == 0) sb.Append("none. ");
            else
            {
                for (int i = 0; i < spec.TpRungs.Count; i++)
                {
                    var r = spec.TpRungs[i];
                    sb.Append("rung ").Append(i + 1).Append(" ").Append(r.Kind);
                    if (r.Kind == TargetSourceKind.RiskRewardMultiple)
                        sb.Append(" ").Append(r.Multiple.ToString("F1")).Append(" R");
                    sb.Append(", close ").Append((r.ClosePortion * 100).ToString("F0")).Append(" percent. ");
                }
            }

            sb.Append("Minimum reward to risk ").Append(spec.MinRewardRiskRatio.ToString("F1")).Append(". ");
            if (spec.SizingMode == SizingMode.FixedRiskPercent)
                sb.Append("Risking ").Append((spec.RiskPercent * 100).ToString("F2")).Append(" percent of equity per trade. ");

            sb.Append("Entry trigger: ").Append(spec.EntryKind);
            if (spec.EntryKind == EntryTriggerKind.OnPullbackToLevel || spec.EntryKind == EntryTriggerKind.OnBreakoutOf)
                sb.Append(" at ").Append(spec.EntryLevelPrice.ToString("F4"));
            sb.Append(".");

            return sb.ToString();
        }

        private void NarrateNode(EditableConditionNode node, StringBuilder sb)
        {
            if (node.IsGroup)
            {
                if (node.Children.Count == 0) { sb.Append("empty group. "); return; }
                sb.Append("(");
                for (int i = 0; i < node.Children.Count; i++)
                {
                    if (i > 0) sb.Append(" ").Append(node.Logic).Append(" ");
                    NarrateNode(node.Children[i], sb);
                }
                sb.Append(")");
            }
            else
            {
                sb.Append(_label(node.SignalDescriptorId)).Append(" ").Append(node.Operator);
                if (NeedsValueForOperator(node.Operator))
                    sb.Append(" ").Append(node.Value.ToString("F2"));
                if (!string.IsNullOrEmpty(node.Timeframe))
                    sb.Append(" on ").Append(node.Timeframe);
            }
        }

        private static bool NeedsValueForOperator(LeafOperator op) =>
            op == LeafOperator.GreaterThan || op == LeafOperator.LessThan || op == LeafOperator.Between
            || op == LeafOperator.CrossesAbove || op == LeafOperator.CrossesBelow;
    }

    /// <summary>Factory-friendly abstraction over <see cref="StrategySpecNarrator"/>.
    /// The UI creates a narrator on demand because the descriptor-label lookup depends on the
    /// runtime <c>ISignalCatalog</c>.</summary>
    public interface IStrategySpecNarrator
    {
        string Narrate(EditableStrategySpec spec);
    }
}
