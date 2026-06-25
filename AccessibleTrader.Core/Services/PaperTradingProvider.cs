using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading.Tasks;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Trading;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace AccessibleTrader.Core.Services
{
    /// <summary>
    /// Simulated broker for paper trading. Implements <see cref="ITradingProvider"/>
    /// so the order-execution service can route to it transparently when paper mode
    /// is on. Fills are driven by the <b>real-time live price feed</b> of the
    /// currently-loaded chart (via <see cref="IWorkspaceStore"/>): market orders
    /// fill at the live price; limit / stop / take-profit orders rest and fill when
    /// live price action crosses their trigger. It emits the same
    /// <see cref="OrderUpdate"/> records as a real provider, so every fill / stop /
    /// take-profit announcement works unchanged.
    ///
    /// v1 simplifications (intentional): cash-flow (spot-style) accounting in a
    /// single quote currency; leverage is recorded and reported but not used to
    /// reduce required cash; no partial fills; fills assume the trigger price with
    /// no slippage. Resting orders only fill while their symbol is the loaded chart.
    /// </summary>
    public interface IPaperTradingProvider : ITradingProvider
    {
        /// <summary>Wipe the paper account back to the starting balance.</summary>
        void ResetAccount();

        /// <summary>The quote-currency balance the account resets to.</summary>
        double StartingBalance { get; }
    }

    public sealed class PaperTradingProvider : IPaperTradingProvider, IDisposable
    {
        private const string Quote = "USDT";
        public double StartingBalance => 100_000.0;

        private readonly IWorkspaceStore _store;
        private readonly ILogger<PaperTradingProvider> _logger;
        private readonly string _statePath;
        private readonly object _lock = new();
        private readonly IDisposable _priceSub;

        private readonly Subject<OrderUpdate> _orderUpdates = new();
        public IObservable<OrderUpdate> OrderUpdateStream => _orderUpdates.AsObservable();

        private double _cash;
        private readonly Dictionary<string, (double Qty, double Avg)> _positions = new();
        private readonly Dictionary<string, double> _leverage = new();
        private readonly List<PaperOrder> _open = new();

        public PaperTradingProvider(IWorkspaceStore store, IPlatformPathService paths, ILogger<PaperTradingProvider> logger)
        {
            _store = store;
            _logger = logger;
            _statePath = Path.Combine(paths.AppDataDirectory, "paper_account.json");
            Load();

            // Drive resting-order fills off the live chart state. StateStream emits
            // on every data change, including each live tick into the forming bar.
            _priceSub = _store.StateStream.Subscribe(OnState);
        }

        // ── IProviderPlugin ───────────────────────────────────────────────────
        public string Name => "Paper";
        public string Description => "Paper trading (simulated)";
        public ProviderCapabilities Capabilities =>
            ProviderCapabilities.Leverage | ProviderCapabilities.Brackets | ProviderCapabilities.Shorting | ProviderCapabilities.OCO;
        public T? GetCapability<T>() where T : class => this as T;

        // ── ITradingProvider flags ────────────────────────────────────────────
        public bool IsConnected => true;
        public bool SupportsMarginTrading => true;
        public bool SupportsFuturesTrading => true;
        public double MaxLeverage => 125.0;

        // ── Account queries ───────────────────────────────────────────────────

        public Task<List<Balance>> GetBalancesAsync()
        {
            lock (_lock)
                return Task.FromResult(new List<Balance> { new Balance(Quote, _cash, 0) });
        }

        public Task<List<Position>> GetPositionsAsync()
        {
            lock (_lock)
            {
                var list = _positions
                    .Where(kv => Math.Abs(kv.Value.Qty) > 1e-12)
                    .Select(kv =>
                    {
                        double price = PriceFor(kv.Key, kv.Value.Avg);
                        double lev = _leverage.TryGetValue(kv.Key, out var l) ? l : 1.0;
                        return new Position(
                            kv.Key, kv.Value.Qty, kv.Value.Avg,
                            kv.Value.Qty * price,
                            (price - kv.Value.Avg) * kv.Value.Qty,
                            lev, 0);
                    })
                    .ToList();
                return Task.FromResult(list);
            }
        }

        public Task<List<OpenOrder>> GetOpenOrdersAsync(string? symbol = null)
        {
            lock (_lock)
            {
                var list = _open
                    .Where(o => symbol == null || string.Equals(o.Symbol, symbol, StringComparison.OrdinalIgnoreCase))
                    .Select(o => new OpenOrder(o.Id, o.Symbol, o.Side, o.Type, o.Quantity, o.Price ?? o.Trigger ?? 0, "NEW"))
                    .ToList();
                return Task.FromResult(list);
            }
        }

        // ── Order management ──────────────────────────────────────────────────

        public Task<string> PlaceOrderAsync(TradeSignal signal)
        {
            string symbol = (signal.Symbol ?? "").ToUpperInvariant();
            if (signal.Quantity <= 0) return Task.FromResult("ORDER_FAILED:quantity must be positive");

            lock (_lock)
            {
                if (signal.Leverage is > 1) _leverage[symbol] = signal.Leverage.Value;

                if (signal.Type == OrderType.Market)
                {
                    double px = PriceFor(symbol, 0);
                    if (px <= 0) return Task.FromResult("ORDER_FAILED:no live price for symbol — load its chart first");
                    if (signal.Side == OrderSide.Buy && _cash < signal.Quantity * px)
                    {
                        Emit(NewId(), symbol, signal.Side, 0, 0, signal.Quantity, OrderStatus.Rejected, false, false);
                        return Task.FromResult("ORDER_FAILED:insufficient paper balance");
                    }

                    string id = NewId();
                    var pnl = ApplyFill(symbol, signal.Side, signal.Quantity, px);
                    Emit(id, symbol, signal.Side, signal.Quantity, px, 0, OrderStatus.Filled, false, false, pnl);

                    // Attach protective resting orders (reduce-only by nature here).
                    var exit = signal.Side == OrderSide.Buy ? OrderSide.Sell : OrderSide.Buy;
                    if (signal.StopLoss is > 0)
                        _open.Add(new PaperOrder(NewId(), symbol, exit, OrderType.StopMarket, signal.Quantity, null, signal.StopLoss, true, false));
                    if (signal.TakeProfit is > 0)
                        _open.Add(new PaperOrder(NewId(), symbol, exit, OrderType.TakeProfitMarket, signal.Quantity, null, signal.TakeProfit, false, true));

                    Persist();
                    return Task.FromResult(id);
                }

                // Resting limit / stop / take-profit order.
                double? price = signal.Type is OrderType.Limit or OrderType.StopLimit or OrderType.TakeProfitLimit ? signal.Price : null;
                double? trigger = signal.Type switch
                {
                    OrderType.StopMarket or OrderType.StopLimit => signal.TriggerPrice ?? signal.StopLoss ?? signal.Price,
                    OrderType.TakeProfitMarket or OrderType.TakeProfitLimit => signal.TriggerPrice ?? signal.TakeProfit ?? signal.Price,
                    OrderType.Limit => signal.Price,
                    _ => null
                };
                if (price == null && trigger == null)
                    return Task.FromResult("ORDER_FAILED:order needs a price or trigger");

                bool isStop = signal.Type is OrderType.StopMarket or OrderType.StopLimit;
                bool isTp   = signal.Type is OrderType.TakeProfitMarket or OrderType.TakeProfitLimit;
                string oid = NewId();
                _open.Add(new PaperOrder(oid, symbol, signal.Side, signal.Type, signal.Quantity, price, trigger, isStop, isTp));
                Persist();
                return Task.FromResult(oid);
            }
        }

        public Task<bool> CancelOrderAsync(string orderId, string symbol)
        {
            lock (_lock)
            {
                var o = _open.FirstOrDefault(x => x.Id == orderId);
                if (o == null) return Task.FromResult(false);
                _open.Remove(o);
                Emit(o.Id, o.Symbol, o.Side, 0, 0, o.Quantity, OrderStatus.Cancelled, false, false);
                Persist();
                return Task.FromResult(true);
            }
        }

        public Task<double> SetLeverageAsync(string symbol, double leverage)
        {
            double lev = Math.Clamp(leverage, 1, MaxLeverage);
            lock (_lock) _leverage[symbol.ToUpperInvariant()] = lev;
            return Task.FromResult(lev);
        }

        public void ResetAccount()
        {
            lock (_lock)
            {
                _cash = StartingBalance;
                _positions.Clear();
                _open.Clear();
                _leverage.Clear();
                Persist();
            }
        }

        // ── Live-price fill engine ────────────────────────────────────────────

        private void OnState(WorkspaceState st)
        {
            var data = st.Data;
            string sym = (st.Identity.Symbol ?? "").ToUpperInvariant();
            if (data == null || data.Count == 0 || sym.Length == 0) return;
            var bar = data[data.Count - 1];

            lock (_lock)
            {
                var fills = _open.Where(o => string.Equals(o.Symbol, sym, StringComparison.OrdinalIgnoreCase) && Crossed(o, bar)).ToList();
                if (fills.Count == 0) return;
                foreach (var o in fills)
                {
                    double px = o.Trigger ?? o.Price ?? bar.Close;
                    _open.Remove(o);
                    var pnl = ApplyFill(o.Symbol, o.Side, o.Quantity, px);
                    Emit(o.Id, o.Symbol, o.Side, o.Quantity, px, 0, OrderStatus.Filled, o.IsStop, o.IsTp, pnl);
                }
                Persist();
            }
        }

        private static bool Crossed(PaperOrder o, Ohlcv bar) => o.Type switch
        {
            OrderType.Limit
                => o.Side == OrderSide.Buy ? bar.Low <= o.Price : bar.High >= o.Price,
            OrderType.StopMarket or OrderType.StopLimit
                => o.Side == OrderSide.Buy ? bar.High >= o.Trigger : bar.Low <= o.Trigger,
            OrderType.TakeProfitMarket or OrderType.TakeProfitLimit
                => o.Side == OrderSide.Buy ? bar.Low <= o.Trigger : bar.High >= o.Trigger,
            _ => false
        };

        // ── Account mutation (caller holds _lock) ─────────────────────────────

        // Returns realized P&L (quote currency) for the portion of this fill that
        // reduces an existing position; null when it only opens/adds.
        private double? ApplyFill(string symbol, OrderSide side, double qty, double price)
        {
            var pos = _positions.TryGetValue(symbol, out var p) ? p : (Qty: 0.0, Avg: 0.0);
            double signed = side == OrderSide.Buy ? qty : -qty;
            double newQty = pos.Qty + signed;

            // Spot-style cash flow: buying spends quote, selling returns it.
            _cash += (side == OrderSide.Buy ? -1 : 1) * qty * price;

            double? realized = null;
            if (pos.Qty != 0 && Math.Sign(signed) != Math.Sign(pos.Qty))
            {
                double closedQty = Math.Min(Math.Abs(signed), Math.Abs(pos.Qty));
                realized = pos.Qty > 0 ? (price - pos.Avg) * closedQty : (pos.Avg - price) * closedQty;
            }

            if (Math.Abs(newQty) < 1e-12)
            {
                _positions.Remove(symbol);
                return realized;
            }
            double avg;
            if (pos.Qty == 0 || Math.Sign(newQty) != Math.Sign(pos.Qty))
                avg = price;                                   // new or flipped position
            else if (Math.Sign(signed) == Math.Sign(pos.Qty))
                avg = (pos.Avg * Math.Abs(pos.Qty) + price * qty) / Math.Abs(newQty); // adding
            else
                avg = pos.Avg;                                 // reducing — keep basis
            _positions[symbol] = (newQty, avg);
            return realized;
        }

        private double PriceFor(string symbol, double fallback)
        {
            var st = _store.State;
            if (st.Data != null && st.Data.Count > 0 &&
                string.Equals(st.Identity.Symbol, symbol, StringComparison.OrdinalIgnoreCase))
                return st.Data[st.Data.Count - 1].Close;
            return fallback;
        }

        private void Emit(string id, string symbol, OrderSide side, double filledQty, double filledPx, double remaining, OrderStatus status, bool stop, bool tp, double? pnl = null)
            => _orderUpdates.OnNext(new OrderUpdate(id, symbol, side, filledQty, filledPx, remaining, status, stop, tp, DateTime.UtcNow, pnl));

        private static string NewId() => "paper-" + Guid.NewGuid().ToString("N").Substring(0, 12);

        // ── Persistence (caller holds _lock for Persist) ──────────────────────

        private void Persist()
        {
            try
            {
                var dto = new PaperDto
                {
                    Cash = _cash,
                    Positions = _positions.Select(kv => new PosDto { Symbol = kv.Key, Qty = kv.Value.Qty, Avg = kv.Value.Avg }).ToList(),
                    Leverage = _leverage.Select(kv => new LevDto { Symbol = kv.Key, Value = kv.Value }).ToList(),
                    Open = _open.Select(o => new OrderDto
                    {
                        Id = o.Id, Symbol = o.Symbol, Side = o.Side.ToString(), Type = o.Type.ToString(),
                        Quantity = o.Quantity, Price = o.Price, Trigger = o.Trigger, Stop = o.IsStop, Tp = o.IsTp
                    }).ToList()
                };
                AtomicFile.WriteAllText(_statePath, JsonConvert.SerializeObject(dto, Formatting.Indented));
            }
            catch (Exception ex) { _logger.LogWarning(ex, "Paper account persist failed."); }
        }

        private void Load()
        {
            _cash = StartingBalance;
            try
            {
                if (!File.Exists(_statePath)) return;
                var dto = JsonConvert.DeserializeObject<PaperDto>(File.ReadAllText(_statePath));
                if (dto == null) return;
                _cash = dto.Cash;
                foreach (var p in dto.Positions) _positions[p.Symbol] = (p.Qty, p.Avg);
                foreach (var l in dto.Leverage) _leverage[l.Symbol] = l.Value;
                foreach (var o in dto.Open)
                    _open.Add(new PaperOrder(o.Id, o.Symbol,
                        Enum.TryParse<OrderSide>(o.Side, out var s) ? s : OrderSide.Buy,
                        Enum.TryParse<OrderType>(o.Type, out var t) ? t : OrderType.Market,
                        o.Quantity, o.Price, o.Trigger, o.Stop, o.Tp));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Paper account load failed; starting fresh.");
                CorruptFileQuarantine.MoveAside(_statePath, ex);
                _cash = StartingBalance;
            }
        }

        public void Dispose()
        {
            _priceSub.Dispose();
            _orderUpdates.OnCompleted();
            _orderUpdates.Dispose();
        }

        // ── Internal records ──────────────────────────────────────────────────

        private sealed record PaperOrder(string Id, string Symbol, OrderSide Side, OrderType Type,
            double Quantity, double? Price, double? Trigger, bool IsStop, bool IsTp);

        private sealed class PaperDto
        {
            public double Cash { get; set; }
            public List<PosDto> Positions { get; set; } = new();
            public List<LevDto> Leverage { get; set; } = new();
            public List<OrderDto> Open { get; set; } = new();
        }
        private sealed class PosDto { public string Symbol { get; set; } = ""; public double Qty { get; set; } public double Avg { get; set; } }
        private sealed class LevDto { public string Symbol { get; set; } = ""; public double Value { get; set; } }
        private sealed class OrderDto
        {
            public string Id { get; set; } = "";
            public string Symbol { get; set; } = "";
            public string Side { get; set; } = "Buy";
            public string Type { get; set; } = "Market";
            public double Quantity { get; set; }
            public double? Price { get; set; }
            public double? Trigger { get; set; }
            public bool Stop { get; set; }
            public bool Tp { get; set; }
        }
    }
}
