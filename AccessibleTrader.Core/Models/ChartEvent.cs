namespace AccessibleTrader.Sdk.Models
{
    public class ChartEvent
    {
        public string Type { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public object? Data { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        // Static factory methods for common events
        public static ChartEvent Info(string message) => new() { Type = "INFO", Message = message };
        public static ChartEvent Error(string message) => new() { Type = "ERROR", Message = message };
        public static ChartEvent Alert(string message, object? data = null) => new() { Type = "ALERT", Message = message, Data = data };
        public static ChartEvent BackfillComplete(string symbol) => new() { Type = "BACKFILL_COMPLETE", Message = $"Backfill for {symbol} finished.", Data = symbol };
    }
}
