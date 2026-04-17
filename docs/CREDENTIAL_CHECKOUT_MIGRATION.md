# Provider migration — `IApiKeyCheckout`

Phase 4 Track B replaces the long-lived `_apiKey` / `_apiSecret` pattern in
provider plugins with sign-time credential checkout from the host. This doc
walks you through migrating one provider. Kraken is the reference
implementation — see `Plugins/Providers/AccessibleTrader.Plugins.Kraken/
KrakenProvider.cs` `PostPrivateAsync` for the canonical shape.

---

## The contract

```csharp
// AccessibleTrader.Sdk.Services

public interface IApiKeyCheckout
{
    Task<ApiKeyCheckoutResult> CheckoutAsync(
        string providerId,
        string marketType = "Spot",
        CancellationToken ct = default);
}

public readonly record struct ApiKeyCheckoutResult(
    string Key, string Secret, string Passphrase, bool HasCredentials);

public static class PluginHostServices
{
    public static IApiKeyCheckout? ApiKeys { get; set; }
}
```

The host adapter (`MauiApiKeyCheckoutAdapter` in the BlazorClient) forwards
each call to `IApiKeyService.GetKeyForProviderAsync`, which reads from the
platform SecureStorage (DPAPI / keychain / KeyStore). One read per checkout —
the returned strings are meant as use-and-discard.

---

## Migration recipe

### 1. Keep `Configure(config)` populating the fallback fields

Unit tests and bare-CLI runs construct providers without the host bridge. Do
not delete the `_apiKey` / `_apiSecret` fields yet — they stay as the
non-bridge fallback path.

### 2. At every sign site, check the bridge first

Replace this:

```csharp
private async Task<string> PostPrivateAsync(string path, Dictionary<string, string> data)
{
    // ... compute nonce / payload ...
    byte[] secretBytes = Convert.FromBase64String(_apiSecret!);
    using var hmac = new HMACSHA512(secretBytes);
    byte[] signature = hmac.ComputeHash(combined);

    var request = new HttpRequestMessage(HttpMethod.Post, url)
    {
        Content = new FormUrlEncodedContent(data)
    };
    request.Headers.Add("API-Key", _apiKey);
    request.Headers.Add("API-Sign", Convert.ToBase64String(signature));
    // ...
}
```

…with this:

```csharp
private async Task<string> PostPrivateAsync(string path, Dictionary<string, string> data)
{
    // Sign-time credential checkout. Replaces the long-lived _apiKey /
    // _apiSecret fields that used to live for the full provider lifetime.
    string apiKey, apiSecret;
    var host = PluginHostServices.ApiKeys;
    if (host != null)
    {
        var checkout = await host.CheckoutAsync("YourProviderId").ConfigureAwait(false);
        if (!checkout.HasCredentials)
            throw new InvalidOperationException("YourProvider: no active API key configured.");
        apiKey    = checkout.Key;
        apiSecret = checkout.Secret;
    }
    else
    {
        if (string.IsNullOrEmpty(_apiKey) || string.IsNullOrEmpty(_apiSecret))
            throw new InvalidOperationException("YourProvider: no API credentials configured.");
        apiKey    = _apiKey!;
        apiSecret = _apiSecret!;
    }

    // ... compute nonce / payload, using apiSecret not _apiSecret ...
    byte[] secretBytes = Convert.FromBase64String(apiSecret);
    using var hmac = new HMACSHA512(secretBytes);
    byte[] signature = hmac.ComputeHash(combined);

    // Best-effort: zero the byte[] we own once we're done with it. The
    // managed `apiSecret` string stays in the heap until GC — .NET strings
    // are interned and immutable, there's no portable way to zero the
    // backing buffer — but this is still better than nothing for anything
    // we can actually reach.
    Array.Clear(secretBytes, 0, secretBytes.Length);

    var request = new HttpRequestMessage(HttpMethod.Post, url)
    {
        Content = new FormUrlEncodedContent(data)
    };
    request.Headers.Add("API-Key", apiKey);
    request.Headers.Add("API-Sign", Convert.ToBase64String(signature));
    // ...
}
```

### 3. For providers using a 3rd-party client library

Many providers (Binance, Alpaca, IBKR) delegate to a library that holds
credentials internally for the lifetime of the client object. The right
pattern there is **per-connection-lifecycle** rather than per-request:

- Defer client construction to `EnsureConnectedAsync`.
- Check out creds, build client, use it.
- On `DisconnectAsync`, dispose the client and drop the credential
  references (already landed in phase 3 via `ScrubCredentials`).

The credential is still alive for the duration of one connection — it's
not a per-request checkout — but the lifetime is bounded by the connect /
disconnect cycle rather than the whole process.

### 4. Hot-path considerations

A tick-rate strategy that signs dozens of requests per second may find the
per-request `CheckoutAsync` latency unacceptable on Android (KeyStore reads
are the slowest SecureStorage backend, ~1-5 ms). For those providers:

- Add a local `DateTime _credentialUnlockExpiry` field.
- On first sign in a session, call `CheckoutAsync` and cache for e.g. 60 s.
- Subsequent signs within the window use the cached value.
- A `Timer` or idle-check scrubs the cached value after the window expires.

Do this only when measurement shows it's needed. The default per-request
pattern is safer.

### 5. Do not cache across operations

The returned `ApiKeyCheckoutResult` is use-and-discard. Don't stash it in
a field that survives beyond the current method scope, don't copy it into
a long-lived struct, don't close over it in a lambda that outlives the
call. The whole point is to minimize the credential's GC-rooted lifetime.

---

## Testing

The `IApiKeyCheckout` indirection means unit tests can pass a mock bridge:

```csharp
PluginHostServices.ApiKeys = new FakeCheckout("testKey", "testSecret");
```

Or, to test the fallback path, leave `PluginHostServices.ApiKeys = null`
and populate the provider via `Configure(new Dictionary<string, string> {
{ "ApiKey", "..." }, { "ApiSecret", "..." } })`.

---

## Status

| Provider | Migrated | Notes |
|----------|:--------:|-------|
| Kraken   | ✅ (canary 2026-04-17) | `PostPrivateAsync` uses per-request checkout; `_apiKey`/`_apiSecret` retained as fallback. |
| Bitstamp | ✅ (2026-04-17)        | `PostAuthenticatedAsync` + private-channel WS subscribe both use per-request checkout. `_customerId` arrives via `ApiKeyCheckoutResult.Passphrase` (with Configure-field fallback). |
| Coinbase | ✅ (2026-04-17)        | `AddAuthHeadersAsync` + WS OnConnected JWT mint both checkout per use. `GenerateJwt` now takes explicit `apiKey`/`apiSecret` args. |
| Alpaca   | ✅ (2026-04-17)        | Per-connection-lifecycle pattern: `ApplyAlpacaHeadersAsync` refreshes `DefaultRequestHeaders` before each REST call; WS `OnConnected` handlers checkout before sending auth payloads. Configure no longer bakes credentials into the HttpClient. |
| Binance  | ✅ (2026-04-17)        | Per-connection-lifecycle pattern: `EnsureTradingClientAsync` builds `BinanceRestClient` lazily on first connect/trade op; disposed + nulled on `DisconnectAsync`. |
| Schwab   | N/A                    | OAuth flow — refresh token already lives in `PluginHostServices.SecureStorage` via `IPluginSecureStorage`; access tokens are minted per-call from the refresh token. No API-key/secret surface for this pattern to protect. |
| IBKR     | N/A                    | Gateway session auth only; no `_apiKey` / `_apiSecret` fields exist. The TLS cert pin (`GatewayCertSha256`) remains the security boundary. |

All eligible providers are now on either per-request or per-connection-lifecycle
checkout. Remaining hardening in this track would be the optional 60-second
session-cache optimization — only worth adding if per-request checkout latency
becomes user-visible (measure first).
