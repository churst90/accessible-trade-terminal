using System.Text.RegularExpressions;
using AccessibleTrader.Sdk.Enums;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Every control the trading dashboard draws must correspond to a capability
    /// the connected provider actually has.
    ///
    /// <para>
    /// Before this, six of seven capability flags were referenced in zero
    /// <c>.razor</c> files, so the ticket showed the same surface on every
    /// provider — a leverage box on a spot-only exchange, a time-in-force selector
    /// for a broker that ignores it. Four unrelated controls (margin type,
    /// leverage, position side, reduce-only) shared a single
    /// <c>SupportsMarginTrading</c> gate and so appeared and vanished together
    /// regardless of which of them the provider honoured.
    /// </para>
    ///
    /// <para>
    /// The gate and the outgoing signal field must move together. A control drawn
    /// over nothing and a field sent to a provider that ignores it are the same
    /// defect from opposite ends, and the second is worse because the order goes
    /// out looking exactly like the one that was asked for.
    /// </para>
    /// </summary>
    public class DashboardCapabilityGatingTests
    {
        private static string Dashboard()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AccessibleTrader.slnx")))
                dir = dir.Parent;
            Assert.NotNull(dir);
            string path = Path.Combine(dir!.FullName, "AccessibleTrader.BlazorClient.Components",
                                       "TradingDashboardModal.razor");
            Assert.True(File.Exists(path), $"Trading dashboard not found at {path}");
            return File.ReadAllText(path);
        }

        /// <summary>
        /// Capabilities that gate a control on the order ticket, and what each one
        /// draws. If a control is added for one of these without a gate, the
        /// capability stops appearing here and this fails.
        /// </summary>
        public static readonly (ProviderCapabilities Cap, string Control)[] TicketGates =
        {
            (ProviderCapabilities.TrailingStop,   "trailing stop / trailing take-profit distance and mode"),
            (ProviderCapabilities.Leverage,       "leverage multiplier"),
            (ProviderCapabilities.IsolatedMargin, "cross / isolated margin selector"),
            (ProviderCapabilities.HedgeMode,      "position side (one-way / long / short)"),
            (ProviderCapabilities.ReduceOnly,     "reduce-only checkbox"),
            (ProviderCapabilities.PostOnly,       "post-only (maker) checkbox"),
            (ProviderCapabilities.TimeInForce,    "time-in-force selector"),
        };

        public static TheoryData<ProviderCapabilities, string> Gates()
        {
            var d = new TheoryData<ProviderCapabilities, string>();
            foreach (var (cap, control) in TicketGates) d.Add(cap, control);
            return d;
        }

        [Theory]
        [MemberData(nameof(Gates))]
        public void EveryTicketControlIsGatedOnItsOwnCapability(ProviderCapabilities cap, string control)
        {
            Assert.True(Dashboard().Contains($"ProviderCapabilities.{cap}", StringComparison.Ordinal),
                $"The dashboard never mentions ProviderCapabilities.{cap}, so the {control} is drawn "
              + "for every provider regardless of whether it honours it. A control that cannot work "
              + "must be absent, not present and inert.");
        }

        [Fact]
        public void TheBluntMarginGateIsGone()
        {
            // _supportsMargin gated four unrelated features at once. Its removal is
            // the point of this change, so its return is a regression: it would
            // silently re-couple controls that have nothing to do with each other.
            Assert.DoesNotContain("_supportsMargin", Dashboard(), StringComparison.Ordinal);
        }

        [Fact]
        public void EveryGatedFieldIsAlsoGatedWhenTheSignalIsBuilt()
        {
            string src = Dashboard();

            // The signal construction must route these through Can(...), not send
            // them unconditionally. Checked by name because the field and the gate
            // sit on the same line in the TradeSignal initialiser.
            foreach (string field in new[] { "MarginType:", "ReduceOnly:", "PostOnly:" })
            {
                var line = src.Split('\n').FirstOrDefault(l => l.Contains(field, StringComparison.Ordinal));
                Assert.True(line != null, $"Could not find the {field} argument in the TradeSignal build.");
                Assert.True(line!.Contains("Can(", StringComparison.Ordinal),
                    $"{field} is set without a capability gate, so it is sent to providers that "
                  + $"ignore it. The order then goes out looking like the one that was asked for: {line.Trim()}");
            }
        }

        [Fact]
        public void LeverageAlsoRequiresAMaxAboveOne()
        {
            // A provider can declare Leverage and cap MaxLeverage at 1, which is a
            // selector with exactly one position.
            Assert.Matches(new Regex(@"ProviderCapabilities\.Leverage\s*\)\s*&&\s*_maxLeverage\s*>\s*1"),
                Dashboard());
        }

        [Fact]
        public void ASpotOnlyProviderExplainsItsEmptyPositionsTab()
        {
            // On a spot venue what you hold IS your balance; an empty positions
            // table reads as a bug, or worse as "your position has gone".
            string src = Dashboard();

            Assert.Contains("IsSpotOnly", src, StringComparison.Ordinal);
            Assert.Contains("spot only", src, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void TheGateTableCoversEveryTicketCapability()
        {
            // Guard the guard: if a capability that gates a control is dropped from
            // TicketGates, the theory above silently stops checking it.
            Assert.True(TicketGates.Length >= 7,
                "The ticket-gate table has shrunk; it is probably no longer checking everything.");
            Assert.Equal(TicketGates.Length, TicketGates.Select(g => g.Cap).Distinct().Count());
        }
    }
}
