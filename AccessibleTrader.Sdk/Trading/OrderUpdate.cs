using System;
using AccessibleTrader.Sdk.Plugins;

namespace AccessibleTrader.Sdk.Trading;

public enum OrderStatus { PartialFill, Filled, Cancelled, Rejected, Triggered }

public record OrderUpdate(
    string OrderId,
    string Symbol,
    OrderSide Side,
    double FilledQuantity,
    double FilledPrice,
    double RemainingQuantity,
    OrderStatus Status,
    bool StopTriggered,
    bool TakeProfitTriggered,
    DateTime Timestamp
);
