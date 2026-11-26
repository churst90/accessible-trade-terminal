using System.Globalization;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Trading;
using Newtonsoft.Json.Linq;

namespace AccessibleTrader.Plugins.Hyperliquid
{
    /// <summary>One tradable market, as Hyperliquid numbers it.</summary>
    /// <param name="Display">The symbol the app shows and stores, "BTC-PERP" or "HYPE-USDC".</param>
    /// <param name="Norm">Display with separators removed and uppercased; how typed symbols are matched.</param>
    /// <param name="Coin">The name the API wants in info calls: "BTC" for a perp, "@107" for most spot pairs.</param>
    /// <param name="AssetId">The integer in a signed order: the perp's index, 10000 + the spot pair's index,
    /// or for a builder's market 100000 + 10000 × the builder's number + its index there.</param>
    /// <param name="Dex">The builder ("xyz") for a market a builder deployed (HIP-3), empty for Hyperliquid's own.</param>
    internal sealed record AssetInfo(
        string Display, string Norm, string Coin, int AssetId, int SzDecimals,
        bool IsSpot, int MaxLeverage, bool OnlyIsolated, string Dex = "", int Collateral = 0)
    {
        /// <summary>Where the market sits in its own universe list, and so in its asset contexts.</summary>
        public int MetaIndex => Dex.Length == 0 ? AssetId : AssetId % HyperliquidWire.BuilderDexStride;

        /// <summary>The builder's number inside the asset id (its place in <c>perpDexs</c>); 0 for Hyperliquid's own.</summary>
        public int BuilderNumber => Dex.Length == 0 ? 0 : (AssetId - HyperliquidWire.BuilderAssetOffset) / HyperliquidWire.BuilderDexStride;
    }

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
        public const int BuilderAssetOffset = 100000;
        public const int BuilderDexStride = 10000;

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

        // ── Venue rules ───────────────────────────────────────────────────────

        /// <summary>
        /// The smallest order Hyperliquid takes, by value: $10 on a perp, 10 of the quote token on
        /// spot. From the exchange's own list of order errors ("MinTradeNtl: Order must have
        /// minimum value of $10." / "MinTradeSpotNtl: Order must have minimum value of 10
        /// {quote_token}.", hyperliquid-docs → API → Error responses, read 2026-11-10).
        /// </summary>
        public const decimal MinOrderValue = 10m;

        /// <summary>
        /// The order rules for one market: the size step from szDecimals and the $10 minimum.
        ///
        /// <para>
        /// <b>Why this exists.</b> An order under the minimum was signed, sent and refused with
        /// "Order must have minimum value of $10." after the leverage had already been changed
        /// for it. Checking first keeps the account as it was and says why in the same words
        /// the other venues use.
        /// </para>
        /// </summary>
        public static SymbolOrderRules OrderRules(AssetInfo asset)
        {
            int dash = asset.Display.LastIndexOf('-');
            string baseName = dash > 0 ? asset.Display[..dash] : asset.Display;
            string quote = asset.IsSpot && dash > 0 ? asset.Display[(dash + 1)..] : "USDC";
            return new SymbolOrderRules
            {
                Venue = "Hyperliquid", Symbol = asset.Display, QuantityUnit = baseName, QuoteAsset = quote,
                QuantityStep = SymbolOrderRules.StepFromDecimals(asset.SzDecimals),
                MinNotional = MinOrderValue,
            };
        }

        /// <summary>
        /// Why Hyperliquid will not take this leverage on this market, in words, or null.
        /// It takes whole numbers from 1 to the market's own maxLeverage (meta), and this used to
        /// round and clamp instead: 100x on ETH (max 25) went out as 25x, 2.5x as 2x, and the
        /// order reported success at a leverage nobody asked for.
        /// </summary>
        public static string? LeverageRefusal(AssetInfo asset, double leverage)
        {
            if (double.IsNaN(leverage) || double.IsInfinity(leverage) || leverage != Math.Floor(leverage) || leverage < 1)
                return $"Hyperliquid takes whole-number leverage from 1 up; {SymbolOrderRules.Plain(leverage)} times is not one";
            if (leverage > Math.Max(1, asset.MaxLeverage))
                return $"Hyperliquid's highest leverage on {asset.Display} is {Math.Max(1, asset.MaxLeverage)} times; "
                     + $"this asks for {SymbolOrderRules.Plain(leverage)}";
            return null;
        }

        // ── Metadata ──────────────────────────────────────────────────────────

        /// <summary>
        /// Perps from the <c>meta</c> reply. The asset id is the position in the universe list,
        /// which is why delisted entries are kept in the numbering and only skipped from the
        /// list of tradable symbols. <paramref name="dexIndex"/> is the builder's number for a
        /// builder's universe (whose names already carry its prefix, "xyz:TSLA"); 0 is Hyperliquid's own.
        /// </summary>
        public static List<AssetInfo> ParseMeta(JObject meta, int dexIndex = 0)
        {
            var result = new List<AssetInfo>();
            var universe = meta["universe"] as JArray ?? new JArray();
            for (int i = 0; i < universe.Count; i++)
            {
                var u = universe[i];
                string name = u["name"]?.ToString() ?? "";
                if (name.Length == 0) continue;
                if (u["isDelisted"]?.Value<bool>() == true) continue;
                int colon = name.IndexOf(':');
                result.Add(new AssetInfo(
                    Display: name + "-PERP",
                    Norm: Normalise(name + "-PERP"),
                    Coin: name,
                    AssetId: dexIndex == 0 ? i : BuilderAssetOffset + dexIndex * BuilderDexStride + i,
                    SzDecimals: u["szDecimals"]?.Value<int>() ?? 0,
                    IsSpot: false,
                    MaxLeverage: u["maxLeverage"]?.Value<int>() ?? 1,
                    OnlyIsolated: u["onlyIsolated"]?.Value<bool>() == true,
                    Dex: dexIndex == 0 || colon < 1 ? "" : name[..colon],
                    // The spot token the builder keeps margin in: 0 is USDC.
                    Collateral: meta["collateralToken"]?.Value<int>() ?? 0));
            }
            return result;
        }

        /// <summary>
        /// Builders' perps (HIP-3) from the <c>allPerpMetas</c> reply: one universe per builder,
        /// Hyperliquid's own first (skipped; <c>meta</c> reads it). A builder's number is its place
        /// in this list, which matched <c>perpDexs</c> on 2026-11-26 and is checked against it
        /// before any order (<see cref="VerifiedBuilders"/>): the Python SDK numbers from
        /// <c>perpDexs</c>, and a wrong number would sign an order for another market.
        /// </summary>
        public static List<AssetInfo> ParseAllPerpMetas(JArray? reply)
        {
            var result = new List<AssetInfo>();
            if (reply is null) return result;
            for (int k = 1; k < reply.Count; k++)
                if (reply[k] is JObject meta) result.AddRange(ParseMeta(meta, k));
            return result;
        }

        /// <summary>
        /// The builders whose number in our asset ids is their place in <c>perpDexs</c> (the list
        /// the Python SDK numbers from: 110000 + 10000 x its place after Hyperliquid's own).
        /// A builder missing here is not traded.
        /// </summary>
        public static HashSet<string> VerifiedBuilders(JArray? perpDexs, IEnumerable<AssetInfo> perps)
        {
            var named = new Dictionary<int, string>();
            for (int k = 1; perpDexs != null && k < perpDexs.Count; k++)
                if (perpDexs[k] is JObject d && d["name"]?.ToString() is { Length: > 0 } n) named[k] = n;
            var ok = new HashSet<string>(StringComparer.Ordinal);
            var bad = new HashSet<string>(StringComparer.Ordinal);
            foreach (var a in perps.Where(a => a.Dex.Length > 0))
                (named.TryGetValue(a.BuilderNumber, out var n) && n == a.Dex ? ok : bad).Add(a.Dex);
            ok.ExceptWith(bad);
            return ok;
        }

        /// <summary>
        /// Why an order on a builder's market (HIP-3) is not sent, or null. Each builder keeps its
        /// own margin; one that keeps it in a coin other than USDC is not traded (no live market
        /// did on 2026-11-26), nor one whose number could not be checked.
        /// </summary>
        public static string? BuilderMarketRefusal(AssetInfo asset, IReadOnlySet<string> verified)
        {
            if (asset.Dex.Length == 0) return null;
            if (asset.Collateral != 0)
                return $"{asset.Display} is margined in another coin than USDC on its builder, {asset.Dex}; the terminal trades only builders' markets margined in USDC";
            if (!verified.Contains(asset.Dex))
                return $"the terminal could not confirm with Hyperliquid which number the builder {asset.Dex} has, and an order sent under the wrong number would trade another market; try again in a minute";
            return null;
        }

        /// <summary>A builder's margin as a balance row: "USDC-PERPS-XYZ", apart from the main perps' "USDC-PERPS".</summary>
        public static string BuilderMarginAsset(string dex) => "USDC-PERPS-" + dex.ToUpperInvariant();

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

        /// <summary>
        /// A TWAP: the exchange splits <paramref name="size"/> into market slices about every 30
        /// seconds over <paramref name="minutes"/>. Keys in the docs' order: a, b, s, r, m, t.
        /// </summary>
        public static OrderedMap TwapOrderAction(int asset, bool isBuy, string size, bool reduceOnly, int minutes, bool randomize) =>
            new OrderedMap().Add("type", "twapOrder").Add("twap", new OrderedMap()
                .Add("a", asset).Add("b", isBuy).Add("s", size).Add("r", reduceOnly)
                .Add("m", minutes).Add("t", randomize));

        /// <summary>Stops a running TWAP. Keys a, t sit on the action itself, not in a nested object.</summary>
        public static OrderedMap TwapCancelAction(int asset, long twapId) =>
            new OrderedMap().Add("type", "twapCancel").Add("a", asset).Add("t", twapId);

        /// <summary>A TWAP's id among the open orders: "twap:" and the exchange's number, so it can never be read as an order number.</summary>
        public const string TwapIdPrefix = "twap:";

        public static bool TryTwapId(string? id, out long twapId)
        {
            twapId = 0;
            return id != null && id.StartsWith(TwapIdPrefix, StringComparison.Ordinal)
                && long.TryParse(id.AsSpan(TwapIdPrefix.Length), System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out twapId);
        }

        /// <summary>
        /// The id of a placed TWAP, or the refusal. Unlike an order's, the reply carries ONE
        /// status object, not a list: <c>{"running":{"twapId":N}}</c> or <c>{"error":"..."}</c>.
        /// </summary>
        public static (long? TwapId, string? Error) ParseTwapReply(JObject reply)
        {
            if (!string.Equals(reply["status"]?.ToString(), "ok", StringComparison.OrdinalIgnoreCase))
                return (null, reply["response"]?.ToString() ?? "Hyperliquid refused the request");
            var st = reply["response"]?["data"]?["status"];
            if (st is JObject o)
            {
                if (o["error"] != null) return (null, o["error"]!.ToString());
                if (o["running"]?["twapId"] is JToken id && long.TryParse(id.ToString(),
                        System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out long n))
                    return (n, null);
            }
            if (st?.Type == JTokenType.String && st.ToString() == "success") return (null, null);
            return (null, "Hyperliquid answered with a status this terminal does not recognise: " + (st?.ToString(Newtonsoft.Json.Formatting.None) ?? "nothing"));
        }

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
