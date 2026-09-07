using System.Globalization;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Tests.Fakes;
using Newtonsoft.Json.Linq;

namespace AccessibleTrader.Tests.OrderConformance
{
    /// <summary>
    /// One order as the VENUE would read it — decoded out of the captured HTTP request by the
    /// rig that knows the venue's encoding. Every field is semantic: the rig has already
    /// translated "STOP_LOSS" / "stp" / "stop-loss" / <c>auxPrice</c> into <see cref="IsStop"/>
    /// and <see cref="Trigger"/>, so the theories in <see cref="ProviderOrderConformanceTests"/>
    /// can assert the same property on twelve venues without knowing any of their vocabularies.
    /// Two <see cref="WireOrder"/>s comparing equal means the venue would have received the same
    /// order (nonces, timestamps and signatures are deliberately not part of it).
    /// </summary>
    internal sealed record WireOrder(
        string  Host,
        string  Symbol,
        string  Side,        // "buy" | "sell", lower-cased by the rig
        double  Quantity,
        string  Type,        // the venue's own token, for the failure message
        bool    IsMarket,
        bool    IsStop,
        double? Trigger,
        double? LimitPrice,
        bool    ReduceOnly);

    internal enum StopSupport
    {
        /// <summary>The venue has a native stop-market (or an emulation that keeps the trigger).</summary>
        StopMarket,
        /// <summary>Only stop-LIMIT exists (Gemini); the stop theories use that type.</summary>
        StopLimitOnly,
        /// <summary>No stop orders at all; a stop type must be REFUSED before any request.</summary>
        None,
    }

    internal enum TakeProfitSupport
    {
        /// <summary>A take-profit type leaves as a resting order carrying the trigger — never as a market order.</summary>
        Native,
        /// <summary>The plugin refuses the type before any request.</summary>
        Refused,
    }

    /// <summary>
    /// What one venue needs in order to be driven through the shared theories: how to build and
    /// configure the plugin with a fake transport, what a success and a rejection look like on
    /// that venue, and how to decode the order(s) the plugin sent. The rig declares the venue's
    /// capabilities so a theory can ask for the right order type, and NEVER silently skips —
    /// a capability the venue lacks is asserted as a refusal instead.
    /// </summary>
    internal abstract class OrderRig
    {
        public abstract string Name { get; }
        /// <summary>The provider's CLR type name, so the anti-vacuity fact can prove every
        /// trading provider in the roster has a rig.</summary>
        public abstract string ProviderTypeName { get; }
        public virtual string? SubType => null;

        /// <summary>The symbol as the app passes it, and what the venue must receive.</summary>
        public abstract string OrderSymbol { get; }
        public abstract string ExpectedWireSymbol { get; }
        /// <summary>A DIFFERENT symbol the chart is showing while the order above is placed.</summary>
        public abstract string ChartedSymbol { get; }

        public abstract string LiveHost { get; }
        /// <summary>Null when the venue has no practice environment at all.</summary>
        public abstract string? PracticeHost { get; }
        /// <summary>A Paper credential must send NOTHING (the venue's demo is gone).</summary>
        public virtual bool PaperRefusesOutright => false;

        public virtual bool WholeSharesOnly => false;
        public virtual bool HasReduceOnlyFlag => false;
        public abstract StopSupport Stops { get; }
        public abstract TakeProfitSupport TakeProfits { get; }

        /// <summary>The venue offers a validate-without-placing form of its order endpoint and
        /// the plugin implements <see cref="IOrderDryRunProvider"/> over it.</summary>
        public virtual bool HasDryRun => false;
        public virtual void ArmDryRunSuccess(FakeHttpMessageHandler h) => throw new NotSupportedException(Name);
        public virtual bool IsDryRunRequest(HttpRequestMessage req, string body) => false;
        /// <summary>By default the placement rejection route serves the dry run too (same endpoint);
        /// a venue with a separate test endpoint overrides.</summary>
        public virtual void ArmDryRunRejection(FakeHttpMessageHandler h) => ArmRejection(h);

        public abstract ITradingProvider Build(FakeHttpMessageHandler h, string? environment);
        public virtual void SeedChartedSymbol(ITradingProvider p, string chartedSymbol) { }
        public abstract void ArmSuccess(FakeHttpMessageHandler h, string orderId);
        public abstract void ArmRejection(FakeHttpMessageHandler h);
        /// <summary>Every ORDER the plugin sent — pre-flight calls (clock, token, contract lookup)
        /// excluded, bracket legs and duplicate children INCLUDED, because "one signal, one order"
        /// is one of the properties.</summary>
        public abstract IReadOnlyList<WireOrder> Orders(FakeHttpMessageHandler h);

        // ── shared helpers ────────────────────────────────────────────────────

        protected static void SwapField(object target, string fieldName, object? value)
        {
            var f = target.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
            if (f == null) throw new InvalidOperationException($"{target.GetType().Name} has no field '{fieldName}'");
            f.SetValue(target, value);
        }

        protected static Dictionary<string, string> Pairs(string form) =>
            form.Split('&', StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.Split('=', 2))
                .ToDictionary(p => Uri.UnescapeDataString(p[0]),
                              p => p.Length > 1 ? Uri.UnescapeDataString(p[1]) : "");

        protected static Dictionary<string, string> Query(HttpRequestMessage r) =>
            Pairs(r.RequestUri!.Query.TrimStart('?'));

        protected static double? Num(string? s) =>
            s != null && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : null;

        protected static double? Num(JToken? t) => t == null || t.Type == JTokenType.Null ? null : t.Value<double>();

        protected static IEnumerable<(HttpRequestMessage Req, string Body)> Posts(FakeHttpMessageHandler h, string pathContains) =>
            Enumerable.Range(0, h.Captured.Count)
                .Where(i => h.Captured[i].Method == HttpMethod.Post
                         && h.Captured[i].RequestUri!.AbsolutePath.Contains(pathContains, StringComparison.Ordinal))
                .Select(i => (h.Captured[i], h.CapturedBodies[i]));

        protected static void Configure(ITradingProvider p, Dictionary<string, string> config, string? environment)
        {
            if (environment != null) config[ProviderConfigKeys.Environment] = environment;
            ((BaseMarketDataProvider)p).Configure(config);
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Alpaca — JSON body, paper vs live host, stop/limit types by name
    // ═══════════════════════════════════════════════════════════════════════════
    internal sealed class AlpacaRig : OrderRig
    {
        public override string Name => "Alpaca";
        public override string ProviderTypeName => "AlpacaProvider";
        public override string OrderSymbol => "MSFT";
        public override string ExpectedWireSymbol => "MSFT";
        public override string ChartedSymbol => "AAPL";
        public override string LiveHost => "api.alpaca.markets";
        public override string? PracticeHost => "paper-api.alpaca.markets";
        public override StopSupport Stops => StopSupport.StopMarket;
        public override TakeProfitSupport TakeProfits => TakeProfitSupport.Native;

        public override ITradingProvider Build(FakeHttpMessageHandler h, string? environment)
        {
            var p = new AccessibleTrader.Plugins.Alpaca.AlpacaProvider();
            Configure(p, new() { ["ApiKey"] = "k", ["ApiSecret"] = "s" }, environment);
            SwapField(p, "_httpClient", new HttpClient(h));
            return p;
        }

        public override void ArmSuccess(FakeHttpMessageHandler h, string orderId) =>
            h.Post(@"alpaca\.markets/v2/orders", $$"""{"id":"{{orderId}}","status":"accepted"}""");

        public override void ArmRejection(FakeHttpMessageHandler h) =>
            h.Post(@"alpaca\.markets/v2/orders", """{"code":40310000,"message":"insufficient buying power"}""", HttpStatusCode.Forbidden);

        public override IReadOnlyList<WireOrder> Orders(FakeHttpMessageHandler h) =>
            Posts(h, "/v2/orders").Select(x =>
            {
                var j = JObject.Parse(x.Body);
                string type = j["type"]?.ToString() ?? "";
                return new WireOrder(x.Req.RequestUri!.Host, j["symbol"]!.ToString(), j["side"]!.ToString(),
                    Num(j["qty"]?.ToString()) ?? double.NaN, type,
                    IsMarket: type == "market", IsStop: type is "stop" or "stop_limit",
                    Trigger: Num(j["stop_price"]), LimitPrice: Num(j["limit_price"]), ReduceOnly: false);
            }).ToList();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Binance — signed QUERY STRING, spot and futures on different hosts
    // ═══════════════════════════════════════════════════════════════════════════
    internal abstract class BinanceRigBase : OrderRig
    {
        public override string ProviderTypeName => "BinanceProvider";
        public override string OrderSymbol => "BTC/USDT";
        public override string ExpectedWireSymbol => "BTCUSDT";
        public override string ChartedSymbol => "ETH/USDT";
        protected abstract string OrderPath { get; }

        public override ITradingProvider Build(FakeHttpMessageHandler h, string? environment)
        {
            var p = new AccessibleTrader.Plugins.Binance.BinanceProvider();
            Configure(p, new() { ["ApiKey"] = "bk", ["ApiSecret"] = "bs" }, environment);
            SwapField(p, "_httpField", new HttpClient(h));
            // The clock probe precedes every signed call; it is a pre-flight, not an order.
            h.Get(@"/api/v3/time", """{"serverTime":1700000000000}""");
            return p;
        }

        public override void ArmSuccess(FakeHttpMessageHandler h, string orderId) =>
            h.Post(OrderPath + @"\?", $$"""{"orderId":{{orderId}},"status":"NEW"}""");

        public override void ArmRejection(FakeHttpMessageHandler h) =>
            h.Post(OrderPath + @"\?", """{"code":-2010,"msg":"Account has insufficient balance for requested action."}""", HttpStatusCode.BadRequest);

        public override IReadOnlyList<WireOrder> Orders(FakeHttpMessageHandler h) =>
            Posts(h, OrderPath).Select(x =>
            {
                var q = Query(x.Req);
                string type = q.GetValueOrDefault("type", "");
                return new WireOrder(x.Req.RequestUri!.Host, q["symbol"], q["side"].ToLowerInvariant(),
                    Num(q.GetValueOrDefault("quantity")) ?? double.NaN, type,
                    IsMarket: type == "MARKET",
                    IsStop: type is "STOP_LOSS" or "STOP_LOSS_LIMIT" or "STOP_MARKET" or "STOP",
                    Trigger: Num(q.GetValueOrDefault("stopPrice")), LimitPrice: Num(q.GetValueOrDefault("price")),
                    ReduceOnly: q.GetValueOrDefault("reduceOnly") == "true");
            }).ToList();
    }

    internal sealed class BinanceSpotRig : BinanceRigBase
    {
        public override string Name => "Binance spot";
        public override string LiveHost => "api.binance.com";
        public override string? PracticeHost => "testnet.binance.vision";
        protected override string OrderPath => "/api/v3/order";
        public override StopSupport Stops => StopSupport.StopMarket;
        public override TakeProfitSupport TakeProfits => TakeProfitSupport.Native;
        public override bool HasDryRun => true;
        public override void ArmDryRunSuccess(FakeHttpMessageHandler h) => h.Post(@"/api/v3/order/test\?", "{}");
        public override void ArmDryRunRejection(FakeHttpMessageHandler h) =>
            h.Post(@"/api/v3/order/test\?", """{"code":-1013,"msg":"Filter failure: MIN_NOTIONAL"}""", HttpStatusCode.BadRequest);
        public override bool IsDryRunRequest(HttpRequestMessage req, string body) =>
            req.RequestUri!.AbsolutePath.EndsWith("/api/v3/order/test", StringComparison.Ordinal);
    }

    internal sealed class BinanceFuturesRig : BinanceRigBase
    {
        public override string Name => "Binance futures";
        public override string? SubType => "Futures";
        public override string LiveHost => "fapi.binance.com";
        public override string? PracticeHost => "testnet.binancefuture.com";
        protected override string OrderPath => "/fapi/v1/order";
        public override bool HasReduceOnlyFlag => true;
        public override StopSupport Stops => StopSupport.StopMarket;
        public override TakeProfitSupport TakeProfits => TakeProfitSupport.Native;
        // The futures branch also POSTs /fapi/v1/listenKey before the order; it is left
        // un-armed on purpose — a canned listen key would open a REAL socket — and the plugin
        // swallows the strict-mode throw. Orders() filters on the order path, so it is invisible.
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Bitstamp — endpoint per side, market in the PATH, form body, no stops at all
    // ═══════════════════════════════════════════════════════════════════════════
    internal sealed class BitstampRig : OrderRig
    {
        public override string Name => "Bitstamp";
        public override string ProviderTypeName => "BitstampProvider";
        public override string OrderSymbol => "BTC/USD";
        public override string ExpectedWireSymbol => "btcusd";
        public override string ChartedSymbol => "ETH/USD";
        public override string LiveHost => "www.bitstamp.net";
        public override string? PracticeHost => null;
        public override StopSupport Stops => StopSupport.None;
        public override TakeProfitSupport TakeProfits => TakeProfitSupport.Refused;

        public override ITradingProvider Build(FakeHttpMessageHandler h, string? environment)
        {
            var p = new AccessibleTrader.Plugins.Bitstamp.BitstampProvider();
            Configure(p, new() { ["ApiKey"] = "stampkey", ["ApiSecret"] = "stampsecret", ["CustomerId"] = "cust77" }, environment);
            SwapField(p, "_httpClient", new HttpClient(h));
            return p;
        }

        public override void ArmSuccess(FakeHttpMessageHandler h, string orderId) =>
            h.Post(@"bitstamp\.net/api/v2/(buy|sell)/", $$"""{"id":"{{orderId}}"}""");

        public override void ArmRejection(FakeHttpMessageHandler h) =>
            h.Post(@"bitstamp\.net/api/v2/(buy|sell)/", """{"status":"error","reason":"Minimum order size is 10 USD"}""");

        public override IReadOnlyList<WireOrder> Orders(FakeHttpMessageHandler h) =>
            Posts(h, "/api/v2/").Where(x => x.Req.RequestUri!.AbsolutePath.Contains("/buy/") || x.Req.RequestUri!.AbsolutePath.Contains("/sell/"))
                .Select(x =>
                {
                    var segs = x.Req.RequestUri!.AbsolutePath.Trim('/').Split('/'); // api v2 buy [market] pair
                    string side = segs[2];
                    bool market = segs[3] == "market";
                    string pair = market ? segs[4] : segs[3];
                    var f = Pairs(x.Body);
                    return new WireOrder(x.Req.RequestUri.Host, pair, side, Num(f.GetValueOrDefault("amount")) ?? double.NaN,
                        market ? "market" : "limit", IsMarket: market, IsStop: false,
                        Trigger: null, LimitPrice: Num(f.GetValueOrDefault("price")), ReduceOnly: false);
                }).ToList();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Coinbase Advanced Trade — JSON with a one-key order_configuration, HTTP 200 rejections
    // ═══════════════════════════════════════════════════════════════════════════
    internal sealed class CoinbaseRig : OrderRig
    {
        private static readonly string Pem = ECDsa.Create(ECCurve.NamedCurves.nistP256).ExportECPrivateKeyPem();

        public override string Name => "Coinbase";
        public override string ProviderTypeName => "CoinbaseProvider";
        public override string OrderSymbol => "BTC/USD";
        public override string ExpectedWireSymbol => "BTC-USD";
        public override string ChartedSymbol => "ETH/USD";
        public override string LiveHost => "api.coinbase.com";
        public override string? PracticeHost => null;
        public override StopSupport Stops => StopSupport.StopMarket; // emulated as a stop-limit; the trigger survives
        public override TakeProfitSupport TakeProfits => TakeProfitSupport.Refused;

        public override ITradingProvider Build(FakeHttpMessageHandler h, string? environment)
        {
            var p = new AccessibleTrader.Plugins.Coinbase.CoinbaseProvider();
            Configure(p, new() { ["ApiKey"] = "organizations/x/apiKeys/y", ["ApiSecret"] = Pem }, environment);
            SwapField(p, "_httpClient", new HttpClient(h));
            return p;
        }

        public override void ArmSuccess(FakeHttpMessageHandler h, string orderId) =>
            h.Post(@"api\.coinbase\.com/api/v3/brokerage/orders$", $$$"""{"success":true,"success_response":{"order_id":"{{{orderId}}}","product_id":"BTC-USD","side":"BUY","client_order_id":"x"}}""");

        // Coinbase answers HTTP 200 for a rejected order; the verdict is in the body.
        public override void ArmRejection(FakeHttpMessageHandler h) =>
            h.Post(@"api\.coinbase\.com/api/v3/brokerage/orders$", """{"success":false,"error_response":{"error":"INSUFFICIENT_FUND","message":"Insufficient balance in source account","error_details":"","preview_failure_reason":"PREVIEW_INSUFFICIENT_FUND"}}""");

        public override IReadOnlyList<WireOrder> Orders(FakeHttpMessageHandler h) =>
            Posts(h, "/api/v3/brokerage/orders").Select(x =>
            {
                var j = JObject.Parse(x.Body);
                var cfg = (JObject)j["order_configuration"]!;
                var kind = cfg.Properties().Single();
                var o = (JObject)kind.Value;
                return new WireOrder(x.Req.RequestUri!.Host, j["product_id"]!.ToString(), j["side"]!.ToString().ToLowerInvariant(),
                    Num(o["base_size"]?.ToString()) ?? double.NaN, kind.Name,
                    IsMarket: kind.Name.StartsWith("market_", StringComparison.Ordinal),
                    IsStop: kind.Name.StartsWith("stop_", StringComparison.Ordinal),
                    Trigger: Num(o["stop_price"]?.ToString()), LimitPrice: Num(o["limit_price"]?.ToString()), ReduceOnly: false);
            }).ToList();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Gemini — the payload travels base64 in a HEADER; no stop-market, no market
    // ═══════════════════════════════════════════════════════════════════════════
    internal sealed class GeminiRig : OrderRig
    {
        public override string Name => "Gemini";
        public override string ProviderTypeName => "GeminiProvider";
        public override string OrderSymbol => "BTCUSD";
        public override string ExpectedWireSymbol => "btcusd";
        public override string ChartedSymbol => "ETHUSD";
        public override string LiveHost => "api.gemini.com";
        public override string? PracticeHost => "api.sandbox.gemini.com";
        public override StopSupport Stops => StopSupport.StopLimitOnly;
        public override TakeProfitSupport TakeProfits => TakeProfitSupport.Refused;

        public override ITradingProvider Build(FakeHttpMessageHandler h, string? environment)
        {
            var p = new AccessibleTrader.Plugins.Gemini.GeminiProvider();
            Configure(p, new() { ["ApiKey"] = "gk", ["ApiSecret"] = "gs" }, environment);
            SwapField(p, "_http", new HttpClient(h));
            return p;
        }

        public override void ArmSuccess(FakeHttpMessageHandler h, string orderId) =>
            h.Post(@"gemini\.com/v1/order/new", $$"""{"order_id":"{{orderId}}","is_cancelled":false,"executed_amount":"0"}""");

        public override void ArmRejection(FakeHttpMessageHandler h) =>
            h.Post(@"gemini\.com/v1/order/new", """{"result":"error","reason":"InsufficientFunds","message":"Insufficient funds"}""", HttpStatusCode.BadRequest);

        public override IReadOnlyList<WireOrder> Orders(FakeHttpMessageHandler h) =>
            Posts(h, "/v1/order/new").Select(x =>
            {
                string b64 = x.Req.Headers.GetValues("X-GEMINI-PAYLOAD").Single();
                var j = JObject.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(b64)));
                string type = j["type"]?.ToString() ?? "";
                bool ioc = j["options"] is JArray a && a.Any(t => t.ToString() == "immediate-or-cancel");
                return new WireOrder(x.Req.RequestUri!.Host, j["symbol"]!.ToString(), j["side"]!.ToString(),
                    Num(j["amount"]?.ToString()) ?? double.NaN, type,
                    IsMarket: ioc, IsStop: type == "exchange stop limit",
                    Trigger: Num(j["stop_price"]?.ToString()), LimitPrice: Num(j["price"]?.ToString()), ReduceOnly: false);
            }).ToList();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Interactive Brokers — the symbol is a conId resolved per order; legs ride as rows
    // ═══════════════════════════════════════════════════════════════════════════
    internal sealed class IbkrRig : OrderRig
    {
        public override string Name => "Interactive Brokers";
        public override string ProviderTypeName => "InteractiveBrokersProvider";
        public override string OrderSymbol => "MSFT";
        public override string ExpectedWireSymbol => "272093"; // MSFT's conId, as the gateway would answer
        public override string ChartedSymbol => "AAPL";
        public override string LiveHost => "localhost";
        public override string? PracticeHost => null; // paper vs live is the GATEWAY login, invisible here
        public override StopSupport Stops => StopSupport.StopMarket;
        public override TakeProfitSupport TakeProfits => TakeProfitSupport.Native;

        public override ITradingProvider Build(FakeHttpMessageHandler h, string? environment)
        {
            var p = new AccessibleTrader.Plugins.InteractiveBrokers.InteractiveBrokersProvider();
            Configure(p, new() { ["AccountId"] = "DU111" }, environment);
            SwapField(p, "_httpClient", new HttpClient(h));
            h.Post(@"/iserver/secdef/search", """[{"conid":"272093","symbol":"MSFT"}]""");
            return p;
        }

        public override void SeedChartedSymbol(ITradingProvider p, string chartedSymbol) =>
            ((AccessibleTrader.Plugins.InteractiveBrokers.InteractiveBrokersProvider)p).SeedConIdCacheForTest(chartedSymbol, "265598");

        public override void ArmSuccess(FakeHttpMessageHandler h, string orderId) =>
            h.Post(@"/iserver/account/DU111/orders", $$"""[{"order_id":"{{orderId}}","order_status":"Submitted"}]""");

        public override void ArmRejection(FakeHttpMessageHandler h) =>
            h.Post(@"/iserver/account/DU111/orders", """{"error":"Insufficient funds"}""", HttpStatusCode.BadRequest);

        public override IReadOnlyList<WireOrder> Orders(FakeHttpMessageHandler h) =>
            Posts(h, "/iserver/account/DU111/orders")
                .SelectMany(x => ((JArray)JObject.Parse(x.Body)["orders"]!).Cast<JObject>().Select(row =>
                {
                    string type = row["orderType"]?.ToString() ?? "";
                    return new WireOrder(x.Req.RequestUri!.Host, row["conid"]!.ToString(), row["side"]!.ToString().ToLowerInvariant(),
                        Num(row["quantity"]) ?? double.NaN, type,
                        IsMarket: type == "MKT", IsStop: type is "STP" or "STP LMT",
                        Trigger: Num(row["auxPrice"]), LimitPrice: Num(row["price"]), ReduceOnly: false);
                })).ToList();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Kraken spot — signed form body; a stop's trigger is `price`, its limit is `price2`
    // ═══════════════════════════════════════════════════════════════════════════
    internal sealed class KrakenRig : OrderRig
    {
        public override string Name => "Kraken";
        public override string ProviderTypeName => "KrakenProvider";
        public override string OrderSymbol => "BTC/USD";
        public override string ExpectedWireSymbol => "BTCUSD";
        public override string ChartedSymbol => "ETH/USD";
        public override string LiveHost => "api.kraken.com";
        public override string? PracticeHost => null; // UAT is by request only
        public override StopSupport Stops => StopSupport.StopMarket;
        public override TakeProfitSupport TakeProfits => TakeProfitSupport.Native;

        public override ITradingProvider Build(FakeHttpMessageHandler h, string? environment)
        {
            var p = new AccessibleTrader.Plugins.Kraken.KrakenProvider();
            Configure(p, new() { ["ApiKey"] = "k", ["ApiSecret"] = Convert.ToBase64String(new byte[32]) }, environment);
            SwapField(p, "_httpClient", new HttpClient(h));
            return p;
        }

        public override void ArmSuccess(FakeHttpMessageHandler h, string orderId) =>
            h.Post(@"api\.kraken\.com/0/private/AddOrder", $$$$"""{"error":[],"result":{"txid":["{{{{orderId}}}}"],"descr":{"order":"x"}}}""");

        public override bool HasDryRun => true;
        // validate=true answers with the venue's description of the order and NO txid.
        public override void ArmDryRunSuccess(FakeHttpMessageHandler h) =>
            h.Post(@"api\.kraken\.com/0/private/AddOrder", """{"error":[],"result":{"descr":{"order":"buy 0.60000000 BTCUSD @ limit 101.5"}}}""");
        public override bool IsDryRunRequest(HttpRequestMessage req, string body) =>
            req.RequestUri!.AbsolutePath.EndsWith("/AddOrder", StringComparison.Ordinal) && Pairs(body).GetValueOrDefault("validate") == "true";

        public override void ArmRejection(FakeHttpMessageHandler h) =>
            h.Post(@"api\.kraken\.com/0/private/AddOrder", """{"error":["EOrder:Insufficient funds"]}""");

        public override IReadOnlyList<WireOrder> Orders(FakeHttpMessageHandler h) =>
            Posts(h, "/0/private/AddOrder").Select(x =>
            {
                var f = Pairs(x.Body);
                string type = f.GetValueOrDefault("ordertype", "");
                bool stop = type.StartsWith("stop-loss", StringComparison.Ordinal);
                bool tp = type.StartsWith("take-profit", StringComparison.Ordinal);
                bool triggered = stop || tp;
                return new WireOrder(x.Req.RequestUri!.Host, f["pair"], f["type"],
                    Num(f.GetValueOrDefault("volume")) ?? double.NaN, type,
                    IsMarket: type == "market", IsStop: stop,
                    Trigger: triggered ? Num(f.GetValueOrDefault("price")) : null,
                    LimitPrice: triggered ? Num(f.GetValueOrDefault("price2")) : Num(f.GetValueOrDefault("price")),
                    ReduceOnly: false);
            }).ToList();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Kraken Futures — signed form body; demo venue decommissioned, so Paper must send nothing
    // ═══════════════════════════════════════════════════════════════════════════
    internal sealed class KrakenFuturesRig : OrderRig
    {
        public override string Name => "Kraken Futures";
        public override string ProviderTypeName => "KrakenFuturesProvider";
        public override string OrderSymbol => "PI_XBTUSD";
        public override string ExpectedWireSymbol => "pi_xbtusd";
        public override string ChartedSymbol => "PI_ETHUSD";
        public override string LiveHost => "futures.kraken.com";
        public override string? PracticeHost => null;
        public override bool PaperRefusesOutright => true;
        public override bool HasReduceOnlyFlag => true;
        public override StopSupport Stops => StopSupport.StopMarket;
        public override TakeProfitSupport TakeProfits => TakeProfitSupport.Native;

        public override ITradingProvider Build(FakeHttpMessageHandler h, string? environment)
        {
            var p = new AccessibleTrader.Plugins.KrakenFutures.KrakenFuturesProvider();
            Configure(p, new() { ["ApiKey"] = "kfk", ["ApiSecret"] = Convert.ToBase64String(Enumerable.Range(1, 32).Select(i => (byte)i).ToArray()) }, environment);
            SwapField(p, "_http", new HttpClient(h));
            return p;
        }

        public override void ArmSuccess(FakeHttpMessageHandler h, string orderId) =>
            h.Post(@"futures\.kraken\.com/derivatives/api/v3/sendorder", $$$"""{"result":"success","sendStatus":{"order_id":"{{{orderId}}}","status":"placed"}}""");

        public override void ArmRejection(FakeHttpMessageHandler h) =>
            h.Post(@"futures\.kraken\.com/derivatives/api/v3/sendorder", """{"result":"success","sendStatus":{"status":"insufficientAvailableFunds"}}""");

        public override IReadOnlyList<WireOrder> Orders(FakeHttpMessageHandler h) =>
            Posts(h, "/sendorder").Select(x =>
            {
                var f = Pairs(x.Body);
                string type = f.GetValueOrDefault("orderType", "");
                return new WireOrder(x.Req.RequestUri!.Host, f["symbol"], f["side"],
                    Num(f.GetValueOrDefault("size")) ?? double.NaN, type,
                    IsMarket: type == "mkt", IsStop: type == "stp",
                    Trigger: Num(f.GetValueOrDefault("stopPrice")), LimitPrice: Num(f.GetValueOrDefault("limitPrice")),
                    ReduceOnly: f.GetValueOrDefault("reduceOnly") == "true");
            }).ToList();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // MEXC — spot is a signed query string, futures a signed JSON body with integer sides
    // ═══════════════════════════════════════════════════════════════════════════
    internal abstract class MexcRigBase : OrderRig
    {
        public override string ProviderTypeName => "MexcProvider";
        public override string OrderSymbol => "BTC/USDT";
        public override string ChartedSymbol => "ETH/USDT";
        public override string LiveHost => "api.mexc.com";
        public override string? PracticeHost => null;
        public override StopSupport Stops => StopSupport.None;
        public override TakeProfitSupport TakeProfits => TakeProfitSupport.Refused;

        public override ITradingProvider Build(FakeHttpMessageHandler h, string? environment)
        {
            var p = new AccessibleTrader.Plugins.Mexc.MexcProvider();
            Configure(p, new() { ["ApiKey"] = "mk", ["ApiSecret"] = "ms" }, environment);
            SwapField(p, "_rest", new AccessibleTrader.Plugins.Mexc.MexcRestApi(new HttpClient(h)));
            SwapField(p, "_connected", true);
            h.Get(@"api\.mexc\.com/api/v3/time", """{"serverTime":1700000000000}""");
            return p;
        }
    }

    internal sealed class MexcSpotRig : MexcRigBase
    {
        public override string Name => "MEXC spot";
        public override string ExpectedWireSymbol => "BTCUSDT";

        public override void ArmSuccess(FakeHttpMessageHandler h, string orderId) =>
            h.Post(@"api\.mexc\.com/api/v3/order\?", $$"""{"symbol":"BTCUSDT","orderId":"{{orderId}}"}""");

        // NB: the body must not contain "-1021" or "700003" — the client re-sends on those substrings.
        public override void ArmRejection(FakeHttpMessageHandler h) =>
            h.Post(@"api\.mexc\.com/api/v3/order\?", """{"code":30004,"msg":"Insufficient balance"}""");

        public override IReadOnlyList<WireOrder> Orders(FakeHttpMessageHandler h) =>
            Posts(h, "/api/v3/order").Select(x =>
            {
                var q = Query(x.Req);
                string type = q.GetValueOrDefault("type", "");
                return new WireOrder(x.Req.RequestUri!.Host, q["symbol"], q["side"].ToLowerInvariant(),
                    Num(q.GetValueOrDefault("quantity")) ?? double.NaN, type,
                    IsMarket: type == "MARKET", IsStop: false, Trigger: null,
                    LimitPrice: Num(q.GetValueOrDefault("price")), ReduceOnly: false);
            }).ToList();
    }

    internal sealed class MexcFuturesRig : MexcRigBase
    {
        public override string Name => "MEXC futures";
        public override string? SubType => "Futures";
        public override string ExpectedWireSymbol => "BTC_USDT";
        public override string LiveHost => "contract.mexc.com";
        public override bool HasReduceOnlyFlag => true;

        public override void ArmSuccess(FakeHttpMessageHandler h, string orderId) =>
            h.Post(@"contract\.mexc\.com/api/v1/private/order/submit", $$"""{"success":true,"code":0,"data":"{{orderId}}"}""");

        public override void ArmRejection(FakeHttpMessageHandler h) =>
            h.Post(@"contract\.mexc\.com/api/v1/private/order/submit", """{"success":false,"code":2005,"message":"Insufficient margin"}""");

        public override IReadOnlyList<WireOrder> Orders(FakeHttpMessageHandler h) =>
            Posts(h, "/api/v1/private/order/submit").Select(x =>
            {
                var j = JObject.Parse(x.Body);
                int side = j["side"]!.Value<int>();      // 1 open long, 2 close short, 3 open short, 4 close long
                int type = j["type"]!.Value<int>();      // 1 limit, 5 market
                return new WireOrder(x.Req.RequestUri!.Host, j["symbol"]!.ToString(), side is 1 or 2 ? "buy" : "sell",
                    Num(j["vol"]) ?? double.NaN, type.ToString(CultureInfo.InvariantCulture),
                    IsMarket: type == 5, IsStop: false, Trigger: null,
                    LimitPrice: Num(j["price"]), ReduceOnly: side is 2 or 4);
            }).ToList();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // OANDA — JSON, side is the SIGN of units, practice vs live host
    // ═══════════════════════════════════════════════════════════════════════════
    internal sealed class OandaRig : OrderRig
    {
        public override string Name => "OANDA";
        public override string ProviderTypeName => "OandaProvider";
        public override string OrderSymbol => "EUR/USD";
        public override string ExpectedWireSymbol => "EUR_USD";
        public override string ChartedSymbol => "GBP/USD";
        public override string LiveHost => "api-fxtrade.oanda.com";
        public override string? PracticeHost => "api-fxpractice.oanda.com";
        public override StopSupport Stops => StopSupport.StopMarket;
        public override TakeProfitSupport TakeProfits => TakeProfitSupport.Native;

        public override ITradingProvider Build(FakeHttpMessageHandler h, string? environment)
        {
            var p = new AccessibleTrader.Plugins.Oanda.OandaProvider();
            Configure(p, new() { ["AccessToken"] = "tok", ["AccountId"] = "001-001-1234567-001" }, environment);
            SwapField(p, "_httpClient", new HttpClient(h));
            return p;
        }

        public override void ArmSuccess(FakeHttpMessageHandler h, string orderId) =>
            h.Post(@"oanda\.com/v3/accounts/[^/]+/orders", $$$"""{"orderCreateTransaction":{"id":"{{{orderId}}}","type":"LIMIT_ORDER"}}""");

        public override void ArmRejection(FakeHttpMessageHandler h) =>
            h.Post(@"oanda\.com/v3/accounts/[^/]+/orders", """{"orderRejectTransaction":{"rejectReason":"INSUFFICIENT_MARGIN"},"errorMessage":"Insufficient margin"}""", HttpStatusCode.BadRequest);

        public override IReadOnlyList<WireOrder> Orders(FakeHttpMessageHandler h) =>
            Posts(h, "/v3/accounts/").Where(x => x.Req.RequestUri!.AbsolutePath.EndsWith("/orders", StringComparison.Ordinal)).Select(x =>
            {
                var o = (JObject)JObject.Parse(x.Body)["order"]!;
                string type = o["type"]?.ToString() ?? "";
                double units = Num(o["units"]?.ToString()) ?? double.NaN;
                bool triggered = type is "STOP" or "MARKET_IF_TOUCHED";
                return new WireOrder(x.Req.RequestUri.Host, o["instrument"]!.ToString(), units < 0 ? "sell" : "buy",
                    Math.Abs(units), type,
                    IsMarket: type == "MARKET", IsStop: type == "STOP",
                    Trigger: triggered ? Num(o["price"]?.ToString()) : null,
                    LimitPrice: triggered ? Num(o["priceBound"]?.ToString()) : Num(o["price"]?.ToString()),
                    ReduceOnly: false);
            }).ToList();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Schwab — OAuth pre-flight, JSON legs, the order id arrives in a Location header
    // ═══════════════════════════════════════════════════════════════════════════
    internal sealed class SchwabRig : OrderRig
    {
        public override string Name => "Schwab";
        public override string ProviderTypeName => "SchwabProvider";
        public override string OrderSymbol => "MSFT";
        public override string ExpectedWireSymbol => "MSFT";
        public override string ChartedSymbol => "AAPL";
        public override string LiveHost => "api.schwabapi.com";
        public override string? PracticeHost => null;
        public override StopSupport Stops => StopSupport.StopMarket;
        public override TakeProfitSupport TakeProfits => TakeProfitSupport.Native;

        public override ITradingProvider Build(FakeHttpMessageHandler h, string? environment)
        {
            var p = new AccessibleTrader.Plugins.Schwab.SchwabProvider();
            SwapField(p, "_http", new HttpClient(h));
            SwapField(p, "_oauth", new AccessibleTrader.Plugins.Schwab.SchwabOAuthService(new HttpClient(h)));
            Configure(p, new() { ["ApiKey"] = "cid", ["ApiSecret"] = "csec", ["Passphrase"] = "refresh-token" }, environment);
            SwapField(p, "_primaryAccountHash", "HASH");
            h.Post(@"api\.schwabapi\.com/v1/oauth/token", """{"access_token":"at","refresh_token":"rt","token_type":"Bearer","expires_in":1800}""");
            return p;
        }

        public override void ArmSuccess(FakeHttpMessageHandler h, string orderId) =>
            h.Add(HttpMethod.Post, @"api\.schwabapi\.com/trader/v1/accounts/HASH/orders$", _ =>
            {
                var r = new HttpResponseMessage(HttpStatusCode.Created) { Content = new StringContent("") };
                r.Headers.Location = new Uri($"https://api.schwabapi.com/trader/v1/accounts/HASH/orders/{orderId}");
                return r;
            });

        public override void ArmRejection(FakeHttpMessageHandler h) =>
            h.Post(@"api\.schwabapi\.com/trader/v1/accounts/HASH/orders$", """{"message":"Insufficient buying power","errors":["Insufficient buying power"]}""", HttpStatusCode.BadRequest);

        public override IReadOnlyList<WireOrder> Orders(FakeHttpMessageHandler h) =>
            Posts(h, "/trader/v1/accounts/HASH/orders")
                .SelectMany(x => Flatten(x.Req.RequestUri!.Host, JObject.Parse(x.Body))).ToList();

        private static IEnumerable<WireOrder> Flatten(string host, JObject node)
        {
            if (node["orderLegCollection"] is JArray legs && legs.Count > 0)
            {
                var leg = (JObject)legs[0];
                string type = node["orderType"]?.ToString() ?? "";
                yield return new WireOrder(host, leg["instrument"]!["symbol"]!.ToString(),
                    leg["instruction"]!.ToString().StartsWith("BUY", StringComparison.Ordinal) ? "buy" : "sell",
                    Num(leg["quantity"]) ?? double.NaN, type,
                    IsMarket: type == "MARKET", IsStop: type is "STOP" or "STOP_LIMIT",
                    Trigger: Num(node["stopPrice"]?.ToString()), LimitPrice: Num(node["price"]?.ToString()), ReduceOnly: false);
            }
            if (node["childOrderStrategies"] is JArray children)
                foreach (var c in children.Cast<JObject>())
                    foreach (var w in Flatten(host, c)) yield return w;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Tradier — form body, whole shares only, sandbox vs live host
    // ═══════════════════════════════════════════════════════════════════════════
    internal sealed class TradierRig : OrderRig
    {
        public override string Name => "Tradier";
        public override string ProviderTypeName => "TradierProvider";
        public override string OrderSymbol => "MSFT";
        public override string ExpectedWireSymbol => "MSFT";
        public override string ChartedSymbol => "AAPL";
        public override string LiveHost => "api.tradier.com";
        public override string? PracticeHost => "sandbox.tradier.com";
        public override bool WholeSharesOnly => true;
        public override StopSupport Stops => StopSupport.StopMarket;
        public override TakeProfitSupport TakeProfits => TakeProfitSupport.Native;

        public override ITradingProvider Build(FakeHttpMessageHandler h, string? environment)
        {
            var p = new AccessibleTrader.Plugins.Tradier.TradierProvider();
            Configure(p, new() { ["ApiKey"] = "tok", ["AccountId"] = "ACC1" }, environment);
            SwapField(p, "_httpClient", new HttpClient(h));
            return p;
        }

        public override void ArmSuccess(FakeHttpMessageHandler h, string orderId) =>
            h.Post(@"tradier\.com/v1/accounts/ACC1/orders", $$$"""{"order":{"id":{{{orderId}}},"status":"ok"}}""");

        public override bool HasDryRun => true;
        public override void ArmDryRunSuccess(FakeHttpMessageHandler h) =>
            h.Post(@"tradier\.com/v1/accounts/ACC1/orders", """{"order":{"status":"ok","cost":609.0,"commission":0.0,"fees":0.0,"symbol":"MSFT","quantity":6.0,"side":"buy","type":"limit","duration":"gtc","result":true,"order_cost":609.0,"margin_change":0.0,"request_date":"2026-09-07T00:00:00Z","extended_hours":false,"class":"equity","strategy":"equity"}}""");
        public override bool IsDryRunRequest(HttpRequestMessage req, string body) =>
            req.RequestUri!.AbsolutePath.EndsWith("/orders", StringComparison.Ordinal) && Pairs(body).GetValueOrDefault("preview") == "true";

        public override void ArmRejection(FakeHttpMessageHandler h) =>
            h.Post(@"tradier\.com/v1/accounts/ACC1/orders", """{"errors":{"error":["Insufficient buying power"]}}""");

        public override IReadOnlyList<WireOrder> Orders(FakeHttpMessageHandler h) =>
            Posts(h, "/v1/accounts/ACC1/orders").SelectMany(x =>
            {
                var f = Pairs(x.Body);
                string host = x.Req.RequestUri!.Host;
                if (f.ContainsKey("side[0]"))
                {
                    // Bracket: indexed legs, one non-indexed symbol.
                    return Enumerable.Range(0, 3).Where(i => f.ContainsKey($"side[{i}]")).Select(i =>
                        Row(host, f["symbol"], f[$"side[{i}]"], f[$"quantity[{i}]"], f[$"type[{i}]"], f.GetValueOrDefault($"price[{i}]"), f.GetValueOrDefault($"stop[{i}]")));
                }
                return new[] { Row(host, f["symbol"], f["side"], f["quantity"], f["type"], f.GetValueOrDefault("price"), f.GetValueOrDefault("stop")) };
            }).ToList();

        private static WireOrder Row(string host, string symbol, string side, string qty, string type, string? price, string? stop) =>
            new(host, symbol, side.StartsWith("buy", StringComparison.Ordinal) ? "buy" : "sell", Num(qty) ?? double.NaN, type,
                IsMarket: type == "market", IsStop: type is "stop" or "stop_limit",
                Trigger: Num(stop), LimitPrice: Num(price), ReduceOnly: false);
    }

    internal static class OrderRigs
    {
        public static readonly IReadOnlyList<OrderRig> All = new OrderRig[]
        {
            new AlpacaRig(), new BinanceSpotRig(), new BinanceFuturesRig(), new BitstampRig(), new CoinbaseRig(),
            new GeminiRig(), new IbkrRig(), new KrakenRig(), new KrakenFuturesRig(), new MexcSpotRig(),
            new MexcFuturesRig(), new OandaRig(), new SchwabRig(), new TradierRig(),
        };

        public static OrderRig Named(string name) => All.Single(r => r.Name == name);
    }
}
