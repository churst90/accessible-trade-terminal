using System.Globalization;
using AccessibleTrader.Sdk.Models;
using Newtonsoft.Json.Linq;

namespace AccessibleTrader.Plugins.Hyperliquid
{
    /// <summary>One tradable market, as Hyperliquid numbers it.</summary>
    /// <param name="Display">The symbol the app shows and stores, "BTC-PERP" or "HYPE-USDC".</param>
    /// <param name="Norm">Display with separators removed and uppercased; how typed symbols are matched.</param>
    /// <param name="Coin">The name the API wants in info calls: "BTC" for a perp, "@107" for most spot pairs.</param>
    /// <param name="AssetId">The integer in a signed order: the perp's index, or 10000 + the spot pair's index.</param>
    internal sealed record AssetInfo(
        string Display, string Norm, string Coin, int AssetId, int SzDecimals,
        bool IsSpot, int MaxLeverage, bool OnlyIsolated);

    /// <summary>The outcome of one entry in an order action's <c>statuses</c> list.</summary>
    internal sealed record OrderOutcome(string? Oid, string? Error, bool Filled, double FilledSize, double AvgPrice)
    {
        public bool IsError => Error != null;
    }

    /// <summary>
    /// Everything about Hyperliquid's wire format that does not need a network: number
    /// formatting and rounding, asset-id mapping, the action shapes, and reading replies.
    /// Kept apart from the provider so each rule can be pinned by a test on its own.
    /// </summary>
    internal static class HyperliquidWire
    {
        // Perp prices may carry at most 6 - szDecimals decimals, spot at most 8 - szDecimals.
        public const int PerpMaxDecimals = 6;
        public const int SpotMaxDecimals = 8;
        public const int SpotAssetOffset = 10000;

        // ── Numbers ───────────────────────────────────────────────────────────

        /// <summary>
        /// The string a price or size travels as: plain decimal, no exponent, no trailing zeros,
        /// "0" for zero. The exchange hashes these strings, so "1.50" and "1.5" would be two
        /// different orders; the Python SDK's <c>float_to_wire</c> normalises the same way.
        /// </summary>
        public static string FormatWire(decimal d)
        {
            string s = d.ToString("0.########", CultureInfo.InvariantCulture);
            if (s.Contains('.')) s = s.TrimEnd('0').TrimEnd('.');
            return s is "" or "-0" ? "0" : s;
        }

        /// <summary>
        /// A price the exchange will accept: at most five significant figures (a whole-number
        /// price is always allowed, so large prices keep their integer part), and no more
        /// than 6 (perp) or 8 (spot) minus szDecimals decimal places. Matches the Python SDK's
        /// <c>round(float(f"{px:.5g}"), maxDecimals - szDecimals)</c>.
        /// </summary>
        public static decimal RoundPrice(double price, int szDecimals, bool isSpot)
        {
            if (!(price > 0) || double.IsInfinity(price)) return 0m;
            decimal px = (decimal)price;
            int maxDecimals = Math.Max(0, (isSpot ? SpotMaxDecimals : PerpMaxDecimals) - szDecimals);

            if (px >= 100000m)
            {
                px = Math.Round(px, 0, MidpointRounding.ToEven);   // an integer price is always valid
            }
            else
            {
                int digitsBeforePoint = (int)Math.Floor(Math.Log10((double)px)) + 1;   // negative below 0.1
                int scale = Math.Clamp(5 - digitsBeforePoint, 0, 28);
                px = Math.Round(px, scale, MidpointRounding.ToEven);
            }
            return Math.Round(px, Math.Min(maxDecimals, 28), MidpointRounding.ToEven);
        }

        /// <summary>
        /// A size the exchange will accept: at most szDecimals decimals. Truncated toward zero,
        /// never rounded up, so the order cannot be bigger than the one asked for (a rounded-up
        /// reduce-only close or a rounded-up buy past the balance is refused or over-sized).
        /// </summary>
        public static decimal RoundSize(double size, int szDecimals)
        {
            if (!(size > 0) || double.IsInfinity(size)) return 0m;
            decimal factor = 1m;
            for (int i = 0; i < Math.Clamp(szDecimals, 0, 28); i++) factor *= 10m;
            return Math.Truncate((decimal)size * factor) / factor;
        }

        // ── Metadata ──────────────────────────────────────────────────────────

        /// <summary>
        /// Perps from the <c>meta</c> reply. The asset id is the position in the universe list,
        /// which is why delisted entries are kept in the numbering and only skipped from the
        /// list of tradable symbols.
        /// </summary>
        public static List<AssetInfo> ParseMeta(JObject meta)
        {
            var result = new List<AssetInfo>();
            var universe = meta["universe"] as JArray ?? new JArray();
            for (int i = 0; i < universe.Count; i++)
            {
                var u = universe[i];
                string name = u["name"]?.ToString() ?? "";
                if (name.Length == 0) continue;
                if (u["isDelisted"]?.Value<bool>() == true) continue;
                result.Add(new AssetInfo(
                    Display: name + "-PERP",
                    Norm: Normalise(name + "-PERP"),
                    Coin: name,
                    AssetId: i,
                    SzDecimals: u["szDecimals"]?.Value<int>() ?? 0,
                    IsSpot: false,
                    MaxLeverage: u["maxLeverage"]?.Value<int>() ?? 1,
                    OnlyIsolated: u["onlyIsolated"]?.Value<bool>() == true));
            }
            return result;
        }

        /// <summary>
        /// Spot pairs from the <c>spotMeta</c> reply. The pair's own <c>index</c> (not its list
        /// position) plus 10000 is the asset id. Size precision is the BASE token's.
        /// </summary>
        public static List<AssetInfo> ParseSpotMeta(JObject spotMeta)
        {
            var result = new List<AssetInfo>();
            var tokens = new Dictionary<int, (string Name, int SzDecimals)>();
            foreach (var t in spotMeta["tokens"] as JArray ?? new JArray())
                tokens[t["index"]?.Value<int>() ?? -1] = (t["name"]?.ToString() ?? "", t["szDecimals"]?.Value<int>() ?? 0);

            foreach (var u in spotMeta["universe"] as JArray ?? new JArray())
            {
                var pair = u["tokens"] as JArray;
                if (pair is null || pair.Count < 2) continue;
                if (!tokens.TryGetValue(pair[0].Value<int>(), out var b)) continue;
                if (!tokens.TryGetValue(pair[1].Value<int>(), out var q)) continue;
                string display = b.Name + "-" + q.Name;
                result.Add(new AssetInfo(
                    Display: display,
                    Norm: Normalise(display),
                    Coin: u["name"]?.ToString() ?? "",
                    AssetId: SpotAssetOffset + (u["index"]?.Value<int>() ?? 0),
                    SzDecimals: b.SzDecimals,
                    IsSpot: true,
                    MaxLeverage: 1,
                    OnlyIsolated: false));
            }
            return result;
        }

        /// <summary>Separators out, uppercase: "btc-perp", "BTC/PERP" and "BTCPERP" are one market.</summary>
        public static string Normalise(string symbol) =>
            symbol.Replace("-", "").Replace("/", "").Replace("_", "").Trim().ToUpperInvariant();

        // ── Action shapes (field order is part of the signed bytes) ───────────

        public static OrderedMap LimitType(string tif) =>
            new OrderedMap().Add("limit", new OrderedMap().Add("tif", tif));

        public static OrderedMap TriggerType(bool isMarket, string triggerPx, string tpsl) =>
            new OrderedMap().Add("trigger", new OrderedMap()
                .Add("isMarket", isMarket)
                .Add("triggerPx", triggerPx)
                .Add("tpsl", tpsl));

        /// <summary>One order, keys in the SDK's order: a, b, p, s, r, t, then c when there is a client id.</summary>
        public static OrderedMap OrderWire(int asset, bool isBuy, string price, string size, bool reduceOnly,
            OrderedMap type, string? cloid)
        {
            var w = new OrderedMap()
                .Add("a", asset).Add("b", isBuy).Add("p", price).Add("s", size)
                .Add("r", reduceOnly).Add("t", type);
            if (!string.IsNullOrEmpty(cloid)) w.Add("c", cloid);
            return w;
        }

        public static OrderedMap OrderAction(IEnumerable<OrderedMap> orders, string grouping = "na") =>
            new OrderedMap().Add("type", "order").Add("orders", orders.ToList()).Add("grouping", grouping);

        public static OrderedMap CancelAction(IEnumerable<(int Asset, long Oid)> cancels) =>
            new OrderedMap().Add("type", "cancel").Add("cancels",
                cancels.Select(c => (object)new OrderedMap().Add("a", c.Asset).Add("o", c.Oid)).ToList());

        public static OrderedMap ModifyAction(long oid, OrderedMap order) =>
            new OrderedMap().Add("type", "modify").Add("oid", oid).Add("order", order);

        public static OrderedMap UpdateLeverageAction(int asset, bool isCross, int leverage) =>
            new OrderedMap().Add("type", "updateLeverage").Add("asset", asset)
                .Add("isCross", isCross).Add("leverage", leverage);

        /// <summary>The dead-man's switch: cancel everything at <paramref name="timeMs"/>, or clear it when null.</summary>
        public static OrderedMap ScheduleCancelAction(long? timeMs)
        {
            var a = new OrderedMap().Add("type", "scheduleCancel");
            if (timeMs.HasValue) a.Add("time", timeMs.Value);
            return a;
        }

        /// <summary>
        /// A client order id is 16 bytes as 0x + 32 hex. A caller's id that already is one is
        /// used as is; anything else is hashed down to one, so the same text always gives the
        /// same cloid and a retry cannot place a second order.
        /// </summary>
        public static string? ToCloid(string? clientOid)
        {
            if (string.IsNullOrWhiteSpace(clientOid)) return null;
            string c = clientOid.Trim();
            if (c.Length == 34 && c.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                && c[2..].All(Uri.IsHexDigit))
                return c.ToLowerInvariant();
            var hash = HyperliquidSigner.Keccak256(System.Text.Encoding.UTF8.GetBytes(c));
            return "0x" + Convert.ToHexString(hash, 0, 16).ToLowerInvariant();
        }

        /// <summary>The action as JSON for the request body, built from the SAME tree that was hashed.</summary>
        public static JToken ToJson(object? v) => v switch
        {
            null => JValue.CreateNull(),
            OrderedMap m => new JObject(m.Select(kv => new JProperty(kv.Key, ToJson(kv.Value)))),
            string s => new JValue(s),
            bool b => new JValue(b),
            int i => new JValue(i),
            long l => new JValue(l),
            ulong u => new JValue(u),
            System.Collections.IEnumerable list => new JArray(list.Cast<object?>().Select(ToJson)),
            _ => throw new NotSupportedException(v.GetType().Name),
        };

        // ── Replies ───────────────────────────────────────────────────────────

        /// <summary>
        /// The outcome of each entry of an order or modify reply. A reply whose top-level
        /// status is not "ok" becomes a single error carrying the exchange's text. A bare string
        /// status ("waitingForTrigger") is accepted with no id; an unrecognised shape is an
        /// error, never a success, so an order we cannot read is never reported placed.
        /// </summary>
        public static List<OrderOutcome> ParseOrderReply(JObject reply)
        {
            if (!string.Equals(reply["status"]?.ToString(), "ok", StringComparison.OrdinalIgnoreCase))
                return new() { new(null, reply["response"]?.ToString() ?? "Hyperliquid refused the request", false, 0, 0) };

            var statuses = reply["response"]?["data"]?["statuses"] as JArray;
            if (statuses is null)   // modify / updateLeverage / scheduleCancel reply with no list
                return new();

            var list = new List<OrderOutcome>();
            foreach (var st in statuses)
            {
                if (st.Type == JTokenType.String)
                {
                    string text = st.ToString();
                    list.Add(text is "success" or "waitingForFill" or "waitingForTrigger"
                        ? new OrderOutcome(null, null, false, 0, 0)
                        : new OrderOutcome(null, text, false, 0, 0));
                }
                else if (st["error"] != null)
                    list.Add(new OrderOutcome(null, st["error"]!.ToString(), false, 0, 0));
                else if (st["resting"] != null)
                    list.Add(new OrderOutcome(st["resting"]!["oid"]?.ToString(), null, false, 0, 0));
                else if (st["filled"] != null)
                    list.Add(new OrderOutcome(st["filled"]!["oid"]?.ToString(), null, true,
                        D(st["filled"]!["totalSz"]), D(st["filled"]!["avgPx"])));
                else
                    list.Add(new OrderOutcome(null, "Hyperliquid answered with a status this terminal does not recognise: " + st, false, 0, 0));
            }
            return list;
        }

        public static double D(JToken? t) =>
            double.TryParse(t?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out double v) ? v : 0;

        /// <summary>One <c>candleSnapshot</c> / websocket candle as a bar and its volume.</summary>
        public static bool TryParseCandle(JToken c, out Ohlcv bar, out long timeMs)
        {
            bar = default;
            timeMs = c["t"]?.Value<long>() ?? 0;
            if (timeMs <= 0) return false;
            bar = new Ohlcv(DateTimeOffset.FromUnixTimeMilliseconds(timeMs).UtcDateTime,
                D(c["o"]), D(c["h"]), D(c["l"]), D(c["c"]), D(c["v"]));
            return true;
        }
    }
}
