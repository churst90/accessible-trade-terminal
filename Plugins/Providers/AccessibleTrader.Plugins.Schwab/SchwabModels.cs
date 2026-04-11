using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace AccessibleTrader.Plugins.Schwab
{
    // ──────────────────────────────────────────────────────────────────────────
    // OAuth / token exchange
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Response body from POST /v1/oauth/token. Fields are snake_case per spec.
    /// </summary>
    public sealed class SchwabTokenResponse
    {
        [JsonProperty("access_token")]  public string? AccessToken  { get; set; }
        [JsonProperty("refresh_token")] public string? RefreshToken { get; set; }
        [JsonProperty("id_token")]      public string? IdToken      { get; set; }
        [JsonProperty("token_type")]    public string? TokenType    { get; set; }
        [JsonProperty("scope")]         public string? Scope        { get; set; }

        /// <summary>Access-token lifetime in seconds (Schwab default: 1800 = 30 minutes).</summary>
        [JsonProperty("expires_in")]    public int    ExpiresIn    { get; set; }
    }

    /// <summary>
    /// Thrown when refresh-token based recovery fails (typically because the
    /// 7-day refresh-token TTL has elapsed). The UI should catch this and
    /// prompt the user to run the full authorization-code flow again.
    /// </summary>
    public sealed class SchwabReauthRequiredException : Exception
    {
        public SchwabReauthRequiredException(string message) : base(message) { }
        public SchwabReauthRequiredException(string message, Exception inner) : base(message, inner) { }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Account identity
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Entry from GET /trader/v1/accounts/accountNumbers. The plain
    /// <see cref="AccountNumber"/> is used for display only; the
    /// <see cref="HashValue"/> is what every other trader endpoint expects.
    /// </summary>
    public sealed class SchwabAccountNumber
    {
        [JsonProperty("accountNumber")] public string AccountNumber { get; set; } = "";
        [JsonProperty("hashValue")]     public string HashValue     { get; set; } = "";
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Price history (OHLCV bars)
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Payload from GET /marketdata/v1/pricehistory. Datetimes are expressed
    /// as Unix epoch milliseconds.
    /// </summary>
    public sealed class SchwabPriceHistoryResponse
    {
        [JsonProperty("candles")] public List<SchwabCandle>? Candles { get; set; }
        [JsonProperty("symbol")]  public string?             Symbol  { get; set; }
        [JsonProperty("empty")]   public bool                Empty   { get; set; }
    }

    public sealed class SchwabCandle
    {
        [JsonProperty("open")]     public double Open     { get; set; }
        [JsonProperty("high")]     public double High     { get; set; }
        [JsonProperty("low")]      public double Low      { get; set; }
        [JsonProperty("close")]    public double Close    { get; set; }
        [JsonProperty("volume")]   public double Volume   { get; set; }

        /// <summary>Unix epoch milliseconds.</summary>
        [JsonProperty("datetime")] public long   Datetime { get; set; }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Order DTOs — sent to POST/PUT /trader/v1/accounts/{hash}/orders
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Minimal subset of the Schwab order schema — sufficient for equity
    /// MARKET / LIMIT / STOP / STOP_LIMIT single-leg orders. Options and
    /// multi-leg strategies require additional fields that we deliberately
    /// omit for v1.
    /// </summary>
    public sealed class SchwabOrderRequest
    {
        [JsonProperty("orderType")]         public string OrderType         { get; set; } = "MARKET";
        [JsonProperty("session")]           public string Session           { get; set; } = "NORMAL";
        [JsonProperty("duration")]          public string Duration          { get; set; } = "DAY";
        [JsonProperty("orderStrategyType")] public string OrderStrategyType { get; set; } = "SINGLE";

        [JsonProperty("price", NullValueHandling = NullValueHandling.Ignore)]
        public string? Price { get; set; }

        [JsonProperty("stopPrice", NullValueHandling = NullValueHandling.Ignore)]
        public string? StopPrice { get; set; }

        [JsonProperty("orderLegCollection")]
        public List<SchwabOrderLeg> OrderLegCollection { get; set; } = new();
    }

    public sealed class SchwabOrderLeg
    {
        [JsonProperty("instruction")]    public string                 Instruction    { get; set; } = "BUY";
        [JsonProperty("quantity")]       public double                 Quantity       { get; set; }
        [JsonProperty("instrument")]     public SchwabOrderInstrument  Instrument     { get; set; } = new();
    }

    public sealed class SchwabOrderInstrument
    {
        [JsonProperty("symbol")]    public string Symbol    { get; set; } = "";
        [JsonProperty("assetType")] public string AssetType { get; set; } = "EQUITY";
    }
}
