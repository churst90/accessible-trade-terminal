using System.Globalization;
using AccessibleTrader.Sdk.Plugins;
using Newtonsoft.Json.Linq;

namespace AccessibleTrader.Plugins.Kraken
{
    /// <summary>
    /// Kraken's balance answer, turned into the asset names everything else in the terminal uses.
    ///
    /// <para>
    /// <b>Why this exists.</b> The balance read passed Kraken's own codes straight through, and
    /// Kraken still keeps its oldest listings under four-letter codes with an X (crypto) or Z
    /// (cash) in front: dollars are <c>ZUSD</c>, bitcoin is <c>XXBT</c>. Nothing downstream knows
    /// those. The risk sizer looks for cash called <c>USD</c>, so a funded account read as
    /// "no cash balance"; the portfolio priced <c>ZUSD</c> through a market lookup and found no
    /// such market. Deposited money did not show as money.
    /// </para>
    ///
    /// <para>
    /// The translation is a fixed table, not a rule. "Strip a leading X or Z" would turn Tezos
    /// (<c>XTZ</c>) into <c>TZ</c> and ZRX into <c>RX</c>; the table is exactly the codes Kraken's
    /// Assets endpoint lists with a different alternative name (checked live 2026-11-10), and
    /// Kraken has not added a prefixed code in years. Every other code is already the plain name.
    /// </para>
    ///
    /// <para>
    /// <b>Suffixes.</b> Kraken lists some holdings under the asset plus a suffix: <c>.F</c> is
    /// Kraken Rewards earning automatically, and its docs say to trade it through the base asset,
    /// so it is added to the base asset's free amount. Everything else — <c>.S</c> staked,
    /// <c>.M</c> opt-in rewards, <c>.P</c> parachain, <c>.B</c> yield products, <c>.HOLD</c> funds
    /// on hold (a deposit still clearing shows here) — is its own row with the whole amount under
    /// Locked: it is yours and counts toward value, but it cannot be traded from here.
    /// </para>
    /// </summary>
    internal static class KrakenBalances
    {
        private static readonly Dictionary<string, string> LegacyCodes = new(StringComparer.OrdinalIgnoreCase)
        {
            ["XXBT"] = "XBT", ["XETH"] = "ETH", ["XXDG"] = "XDG", ["XLTC"] = "LTC", ["XXRP"] = "XRP",
            ["XXLM"] = "XLM", ["XXMR"] = "XMR", ["XZEC"] = "ZEC", ["XETC"] = "ETC", ["XMLN"] = "MLN",
            ["XREP"] = "REP", ["KFEE"] = "FEE",
            ["ZUSD"] = "USD", ["ZEUR"] = "EUR", ["ZGBP"] = "GBP", ["ZCAD"] = "CAD", ["ZJPY"] = "JPY",
            ["ZAUD"] = "AUD", ["ZARS"] = "ARS", ["ZCLP"] = "CLP", ["ZCOP"] = "COP", ["ZDKK"] = "DKK",
            ["ZGEL"] = "GEL", ["ZGHS"] = "GHS", ["ZLKR"] = "LKR", ["ZMXN"] = "MXN", ["ZPLN"] = "PLN",
            ["ZSEK"] = "SEK", ["ZUGX"] = "UGX", ["ZVND"] = "VND", ["ZXOF"] = "XOF",
        };

        /// <summary>The name the rest of the terminal uses for a Kraken asset code:
        /// <c>XXBT</c> → BTC, <c>ZUSD</c> → USD, <c>XBT.M</c> → BTC.M, <c>TAO</c> → TAO.</summary>
        internal static string AssetName(string krakenCode)
        {
            string code = (krakenCode ?? "").Trim().ToUpperInvariant();
            int dot = code.IndexOf('.');
            string head = dot < 0 ? code : code[..dot];
            string suffix = dot < 0 ? "" : code[dot..];
            if (LegacyCodes.TryGetValue(head, out var alt)) head = alt;
            head = head switch { "XBT" => "BTC", "XDG" => "DOGE", _ => head };
            return head + suffix;
        }

        /// <summary>
        /// The rows of a <c>BalanceEx</c> answer. Free is what can be spent: the balance less
        /// <c>hold_trade</c>, the amount tied up in open orders, which is Locked. (The plain
        /// Balance endpoint has no hold figure at all, so Locked was always zero and dollars
        /// sitting in a resting buy read as free to spend twice.) A credit line is not counted:
        /// it is the venue's money, not the account's.
        /// </summary>
        internal static List<Balance> FromBalanceEx(JObject result)
        {
            var free = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            var locked = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            var order = new List<string>();

            void Add(string asset, double f, double l)
            {
                if (!free.ContainsKey(asset)) { free[asset] = 0; locked[asset] = 0; order.Add(asset); }
                free[asset] += f;
                locked[asset] += l;
            }

            foreach (var p in result.Properties())
            {
                double total = Number(p.Value is JObject o ? o["balance"] : p.Value);
                double hold  = p.Value is JObject h ? Number(h["hold_trade"]) : 0;
                hold = Math.Clamp(hold, 0, Math.Max(0, total));

                string name = AssetName(p.Name);
                int dot = name.IndexOf('.');
                if (dot < 0)
                    Add(name, total - hold, hold);
                else if (string.Equals(name[(dot + 1)..], "F", StringComparison.Ordinal))
                    Add(name[..dot], total - hold, hold);
                else
                    Add(name, 0, total);
            }

            return order
                .Select(a => new Balance(a, free[a], locked[a]))
                .Where(b => b.Free > 0 || b.Locked > 0)
                .ToList();
        }

        // Kraken sends amounts as strings, but its own BalanceEx example shows bare numbers;
        // both are read, and a number is never round-tripped through the current culture.
        internal static double Number(JToken? t) => t?.Type switch
        {
            JTokenType.Float or JTokenType.Integer => t.Value<double>(),
            JTokenType.String => double.TryParse((string?)t, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : 0,
            _ => 0,
        };
    }
}
