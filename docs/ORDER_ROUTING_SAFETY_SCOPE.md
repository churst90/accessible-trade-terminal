# Order routing safety — which key signs, which host receives, and what the dashboard says

**Written 2026-09-07, SCOPE ONLY — nothing here is implemented.** Cody asked for three things after
the conformance pass: (1) a Paper key on a venue with no practice environment must never route an
order to the live venue, and a live order must be unmistakable in the trading dashboard; (2) a
dropdown in the dashboard to choose WHICH key an order uses, so there is no mistake; (3) wire
Schwab's preview endpoint. Reading the code for (1) and (2) found that the dropdown already
exists and does not do what it says, and that three different parts of the app currently hold
three different opinions about which credential an order uses. Every claim below cites a line;
nothing was measured against a venue.

---

## 1. Findings

### F1. The key that SIGNS an order is chosen environment-blind, and the dashboard's switcher does not affect it

At sign time every plugin that uses the host bridge asks `PluginHostServices.ApiKeys.CheckoutAsync(providerId, marketType)`.
Both hosts answer with `GetKeyForProviderAsync(providerId, marketType)`
(`AccessibleTrader.BlazorClient/Services/MauiApiKeyCheckoutAdapter.cs:69`,
`AccessibleTrader.WebHost/Services/WebHostApiKeyCheckoutAdapter.cs:41`). That method
(`AccessibleTrader.Core/Services/ApiKeyService.cs:151-175`) returns **the first stored profile
whose `MarketType` matches**, falling back to the first active, then the first of any kind. It never
reads `Environment` and it ignores `IsActive` when a market-type match exists. The MAUI adapter's
own comment says why: *"the active-flag semantics are tied to Paper vs Live environment, and
providers usually pick the right Environment themselves"* (`:66-68`). They do not; see F2.

**Consequence:** with two Alpaca profiles stored — say "alpaca-paper" then "alpaca-live", both
`MarketType=Spot` — every order is signed with **alpaca-paper's** key, whatever is marked active
and whatever the dashboard's switcher says.

### F2. The HOST an order goes to is decided once, by the first ACTIVE key, and never changes

`DataService.ConfigureStoredKeyProvidersAsync` (`AccessibleTrader.Core/Services/DataService.cs:191-233`)
walks every stored key, skips inactive ones, and calls `provider.Configure(CredentialFor(k))` —
**unless `provider.IsConfigured` is already true, in which case it skips** (`:222`). Seven plugins
declare `IsConfigured => true` unconditionally (Binance, Bitstamp, Gemini, IBKR, Kraken, Kraken
Futures, MEXC), so for them the skip fires on every call after the first; the others become
configured on the first key and are skipped from then on. `Configure` is what chooses the host
(`Environment=Paper` → sandbox), so **the host is fixed by whichever active key the loop reached
first at startup**, and a later activation changes nothing.

Because activation is scoped to *provider + environment* (`ApiKeyService.SetActiveKeyAsync`,
`:234-247`), a Paper key and a Live key for one provider can both be active at once. Which of the
two configured the host is storage order.

**Consequence of F1 + F2 together:** the host can come from one profile and the signature from
another. A Live host signed with a Paper key fails authentication (the safe direction); a Paper
host signed with a Live key also fails; but a Paper-marked key on a venue with no practice
environment (F5) signs successfully against the live venue.

### F3. The dashboard's "Switch API Key" dropdown changes a flag and an announcement, not the credential

`TradingDashboardModal.razor:34-45` renders `<select id="key-select">` over the chart provider's
profiles; `OnKeyChanged` (`:1658-1668`) calls `SetActiveKeyAsync`, speaks *"Switched to API key
{nickname} ({environment})"*, and reloads account data. It does not call
`ConfigureStoredKeyProvidersAsync` — and per F2 that call would skip the provider anyway — and per
F1 the signing key is not read from the active flag. **So Cody's ask (2) is already on screen and
is not true.** `ApiKeysModal`'s Activate button (`ApiKeysModal.razor:82-88`, `SetActive` `:541-563`)
has the same shape: it does call `ConfigureStoredKeyProvidersAsync`, which skips.

### F4. GeneralOrderService cannot see the credential at all

Its constructor takes `IDataService, IGlobalErrorCoordinator, ILogger, IEventBus,
IPaperTradingProvider, ISettingsManager, DemoPolicy, QuickTradeEquity`
(`GeneralOrderService.cs:115-146`). No `IApiKeyService`. Its only notion of paper is the setting
plus `DemoPolicy.AllowLiveTrading` (`:329-331`); when that says "not paper", it hands the order to
the real provider object as-is (`:333-341`). Rule (1) cannot be enforced there today because the
information is not there.

### F5. The live-review gate keys off the KEY's label, so a mislabelled key gets no review

`SubmitOrder` (`TradingDashboardModal.razor:1731-1735`) arms the spoken review only when
`_isLiveEnvironment && !_paperMode`, and `_isLiveEnvironment` is `activeKey?.Environment == "Live"`
(`:1613`). With the built-in paper mode OFF and a key marked Paper (or a legacy profile whose
environment is empty) on **Bitstamp, Coinbase, IBKR, Kraken spot, MEXC or Schwab** — the six venues
with no practice environment — the order goes to `PlaceOrderAsync` with no review, the service
routes it to the real provider, and the plugin signs it against the live venue. **This is exactly
the scenario Cody described, and it is the dangerous polarity.** The conformance suite pins those
six by name (`Venues_that_route_a_Paper_credential_to_the_live_host_are_exactly_these`) precisely
so this list could not grow unnoticed; it does not yet prevent anything.

### F6. Live and paper are shown, not spoken

The dashboard banners (`:57-64` — "📄 PAPER TRADING" / "⚠ LIVE TRADING — Real funds at risk") are
visual only: not live regions, not announced on open. The Mode cell (`:96`) says "Live" or
"Paper" with no key name. The status bar has a PAPER badge (`StatusBar.razor:57-67`) and **no LIVE
badge** — the absence of PAPER is the only signal. The switcher's announcement says the environment
word but never "real money". For a screen-reader user, nothing on this screen says "you are about
to spend real money at Kraken with key X" until the review text, and F5 shows the review can be
skipped.

### F7. Small things found on the way

- `"trading.paperTradingMode"` is a string literal in three places (`TradingDashboardModal.razor:1140`,
  `:1611`, `StatusBar.razor:105`) where `SettingsKeys.PaperTradingMode` exists.
- `IMarketDataProvider.Environment` / `ProviderEnvironment { Live, Paper, Sandbox, HistoricalOnly }`
  (`AccessibleTrader.Sdk/Plugins/IMarketDataProvider.cs:8, :31`) are undocumented, and this enum is a
  third vocabulary beside the key's `"Paper"/"Live"` string and the `Configure` dictionary's
  `Environment`/`Testnet`. Useful fact: a plugin with no practice environment reports `Live` even
  when its key says Paper — that mismatch is the signal rule (1) can use.
- `GetActiveKeyForProviderAsync` has no production caller (its default argument asks for the
  *Paper* active key).

## 2. The rules, as Cody stated them

- **R1.** A key marked Paper on a venue with no practice environment must never produce a live
  order. Refuse, in words, at the one chokepoint.
- **R2.** A live order must be unmistakable in the dashboard: spoken on open and on key change, and
  reviewed before it goes.
- **R3.** A paper trade happens only by intentional selection — the built-in simulator, or a
  provider's practice key. Uncertainty resolves to REFUSE, never to live.
- **R4.** One dropdown chooses the key for the order, and that choice is the credential actually
  used for BOTH the host and the signature.

## 3. Design

### D1. One credential in use per provider, and everything reads it

- `DataService` records the `ApiKeyConfig` it last pushed into `Configure` per provider:
  `CredentialInUse(providerName) → ApiKeyConfig?` on `IDataService`.
- A new `ReconfigureProviderAsync(providerName, nickname)` that ALWAYS calls `Configure` with that
  key (no `IsConfigured` skip — that guard protects startup, not a user's choice), records it, and
  publishes `ApiKeysChangedEvent`.
- **The checkout adapters answer from the same record**: `CheckoutAsync(providerId, …)` returns the
  credential in use for that provider, not `GetKeyForProviderAsync`. Host and signature then cannot
  disagree. (Keep `GetKeyForProviderAsync` as the fallback only when nothing has been configured.)
- **One active key per provider** — change `SetActiveKeyAsync`'s scope from provider+environment to
  provider. Two active keys is what made "active" ambiguous. The API-keys dialog already shows the
  environment in every row, so nothing is lost.

### D2. The chokepoint rule (R1, R3)

In `GeneralOrderService.PlaceOrderAsync`, after the provider is resolved and before
`tp.PlaceOrderAsync`:

- `var cred = _dataService.CredentialInUse(providerName)`; if null → `PROVIDER_NOT_CONFIGURED`.
- If `!ProviderConfigKeys.IsLive(cred)` (the key is Paper, or empty) and the provider reports
  `Environment == ProviderEnvironment.Live` → refuse:
  *"{Provider} has no practice venue and the key '{nickname}' is marked Paper, so this order would
  be real. Mark the key Live to trade real money, or turn on Paper trading (F12)."*
- Clearer than inferring from `Environment`: add `bool HasPracticeEnvironment => true` to
  `ITradingProvider` in the SDK, overridden `false` on the six, and turn the conformance suite's
  by-name pin into an assertion on that property. Either works; the property is self-documenting.
- A conformance-style test on the service with a fake `ITradingProvider` (`Environment = Live`)
  and a recorded Paper credential must be RED before the rule and green after; sabotage by
  removing the `IsLive` check.

### D3. The dashboard (R2, R4) — accessibility notes inline

- **The switcher becomes the choice of record.** Label *"API key for this order"* (keep
  `<label for="key-select">`). Option text *"{nickname} — Live, real money"* /
  *"{nickname} — Paper, {venue's sandbox name}"*. `OnKeyChanged` calls `ReconfigureProviderAsync`,
  then `RefreshTradingEnvironment()`, then speaks on the StateChange channel with interrupt:
  *"{Provider}: LIVE account '{nickname}'. Orders here are real money."* or
  *"{Provider}: paper account '{nickname}' (sandbox)."*
- **Speak the environment once on open**, same sentence, on `SpeechChannel.OrderEvent`, after the
  modal's focus lands. The banners stay visual; do not make them live regions (the status-bar
  rationale at `StatusBar.razor:8-51` applies: a second live region repeating the sentence is how
  Orca dropped both).
- **Fail-safe review gate:** `!_paperMode && activeKey?.Environment != "Paper"` — anything not
  explicitly Paper is reviewed, so a legacy profile gets the review instead of skipping it.
- **Mode cell and banner name the key**: *"Live — kraken-main"*.
- **Status bar LIVE badge**, symmetrical to PAPER: `role="img" aria-label="Live trading — real
  money"`, outside `.status-content` like the PAPER badge, driven by the same event plus
  `ApiKeysChangedEvent`.
- Replace the three setting-key literals with `SettingsKeys.PaperTradingMode`.
- bUnit tests: the switcher speaks the environment and the consequence; opening speaks it once; a
  Paper-labelled key on a no-practice venue still arms the review; each proven red by sabotage
  (and, per 2026-09-07, `await ChangeAsync` — never the sync `Change()`).

### D4. Schwab preview — `IOrderDryRunProvider` on `SchwabProvider`

- Endpoint: `POST {TraderV1}/accounts/{_primaryAccountHash}/previewOrder`, the same JSON body
  `BuildSchwabOrder` produces for `/orders` (`SchwabProvider.cs:913-970`, `BuildBracket` `:983+`).
  `SendWithAuthCoreAsync` handles the token; a non-2xx arrives as `HttpRequestException` carrying
  the body — that is the venue's verdict, return it as `Accepted=false`.
- Response (Schwab's published schema; UNVERIFIED here): `orderValidationResult` with arrays
  `rejects`, `warns`, `reviews`, `accepts`, `alerts`, each item `{ activityMessage, … }`;
  `commissionAndFee` with the cost. **Accepted iff `rejects` is empty**; the message lists
  `warns`/`reviews` and the commission when accepted, the reject messages otherwise.
- Rig (`SchwabRig`): `HasDryRun => true`; `ArmDryRunSuccess` routes
  `trader/v1/accounts/HASH/previewOrder$` to a body with empty `rejects`; `ArmDryRunRejection`
  answers 200 with one reject; `IsDryRunRequest` = path ends with `/previewOrder`; add
  `"SchwabProvider"` to `Rigs_with_a_dry_run_are_exactly_the_providers_implementing_the_capability`.
  The existing `Dry_run_sends_the_placement_payload_plus_only_the_validate_flag` then covers Schwab
  with no new theory.
- **What running it against Cody's real Schwab account settles, with no order placed:** the
  `duration: "GTC"` spelling on every bracket (published enum: `GOOD_TILL_CANCEL`;
  `BrokerParityTests.cs:165` pins `"GTC"`), and whether option legs need `BUY_TO_OPEN`-style
  instructions. Both are marked UNVERIFIED in `PROVIDER_PLACEMENT_AUDIT_2026-09-07.md`.

## 4. Suggested order

1. **D4** (Schwab preview) — an hour, no dependencies, and it is the instrument for two open
   questions.
2. **D1** — the credential-in-use record and the checkout adapters. This is the root of F1–F3 and
   everything in D2/D3 reads from it.
3. **D2** — the chokepoint refusal, with its red-then-green test.
4. **D3** — the dashboard and status bar.
5. Then run the dry runs for real: Tradier sandbox first; Kraken `validate=true` and Schwab
   `previewOrder` with Cody present, because those keys are live.

## 5. Decisions this needs from Cody

- One active key per provider (recommended) — or keep one per environment and let the dashboard's
  choice override? The first is simpler and matches "so there is no mistake".
- Legacy profiles with an empty environment: after D2 they are treated as Paper and therefore
  REFUSED on the six no-practice venues until re-saved as Live. That is the intended fail-safe; it
  will surprise anyone with an old Kraken or Coinbase profile, and the refusal text should say how
  to fix it.
