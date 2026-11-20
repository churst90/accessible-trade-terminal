# Hosted accounts & the education strategy

A product-strategy record (drafted 2026-06-27), captured for future reference. This is
direction, not a build spec — the concrete engineering steps come later. It builds on the
multi-user WebHost work (`WEBHOST_MULTI_USER_SCOPING.md`) and the two-head platform
strategy (`PLATFORM_STRATEGY_AND_ROADMAP.md`).

---

## The positioning, in one line

**The accessible way to learn to trade *by ear*, free in your browser, with a path to
doing it for real on the desktop.** Not "a hosted trading app" (crowded, risky) — an
*education* platform, which is the category this can actually own.

## The core model: paper online, real money on the desktop

| | Hosted (browser, multi-user) | Desktop client (MAUI) |
|---|---|---|
| Trading | **Paper only** (simulated) | **Real money**, user's own API keys |
| API keys | None held server-side (public market data only) | Stay on the user's own machine / own IP |
| Custom user scripts | No (security — see below) | Yes |
| Tactile / Dot Pad | No (local hardware) | Yes |
| Indicators | Standard set + tasted/tiered premium | Full suite |
| Account / persistence | Yes (the draw) | Local (could sync later) |

This split is not a compromise — it's the design that **dissolves the BYOA risks** (see
`## Why this is the correct architecture`). The hosted side is the *safe* half: paper +
pooled, cached, keyless public market data. Real money never touches the server.

The hosted paper account is simultaneously the **product**, the **marketing demo**, and
the **on-ramp**: learn free in the browser → "graduate" to the desktop for real trading.
For an accessibility tool that funnel is ideal, because the no-install browser version is
exactly what reaches people who can't install software.

## Why this is the correct architecture (the BYOA problem it avoids)

Letting a server make requests with many users' API keys is risky — not because of the
*number of keys* (each key has its own quota) but because of **one server IP**:

- **Per-IP rate limits** (e.g. Binance weight limits) are shared across all keys from that
  IP, so aggregate traffic throttles long before any one key does.
- **Per-IP connection caps** — many providers limit concurrent WebSocket connections per
  IP; one socket per visitor blows the cap fast.
- **Abuse detection / IP bans** — one IP, many different keys looks like a scraper/bot;
  a ban takes *all* users down at once (single point of failure).
- **ToS** — many exchange/broker APIs restrict third-party/proxied use or require
  IP-whitelisting the key (tying your one IP to many accounts).
- **Custody liability** — holding many users' (possibly trading-capable) keys makes the
  server a high-value target and you a de-facto custodian of funds-moving credentials.

**The architectural insight that makes the hosted side safe and scalable:** public market
data doesn't need anyone's key. Split the world in two —

- **Public market data → shared, pooled, cached, keyless.** If 50 visitors watch BTC/USD,
  make *one* subscription and fan it out; cache historical bars once. Collapses N users'
  load to ~1× per symbol and eliminates the per-IP connection explosion. (This is the
  "shared connection pool keyed by symbol" parked as a future optimisation in
  `WEBHOST_MULTI_USER_SCOPING.md`.)
- **Private account/trading → per-user, their key, low volume.** Paper accounts need *no*
  real keys at all; real trading lives on the desktop.

Because the hosted tier is paper-only, the high-volume part (market data) is entirely
keyless and shared, and the risky part (real keys, trading) never exists on the server.

## What to reserve for the desktop — by *necessity*, not arbitrarily

Reserve where there's a real reason, so "download for more" is honest and self-justifying:

- **Real trading** — desktop only. The whole thesis.
- **Custom user-authored scripts** — desktop only, for **security**, not monetisation:
  running arbitrary user-compiled C# on a *shared multi-user server* is dangerous even
  with the sandbox (multi-tenant; an escape or resource-exhaustion hits everyone). On the
  desktop it's one person's own machine.
- **Tactile / Dot Pad** — desktop only, by physics (local hardware).

**Premium built-in indicators (Cipher A/B/C, S/R): do NOT hide them entirely online.**
They're the *wow* — hearing a Cipher signal fire is the hook — and for an education
product the advanced indicators *are* the advanced curriculum, so learners should reach
them. Gate them by **progress or tier inside the account** instead (free account = standard
set + a taste, e.g. Cipher A; full suite unlocks via the intermediate lessons or a
supporter tier). Keep real-money + scripts + tactile as the clean desktop differentiators.

## GPLv3 reality (shapes the money side)

The app is **GPLv3**, so reserving features for the desktop is *positioning*, not a hard
paywall — anyone could compile the desktop with everything (99% take the official build,
so it still works as differentiation). This pushes monetisation toward: a **hosted-service
supporter/subscription tier** (paying for hosting convenience + persistence + education +
alerts, not for code), **donations**, and — given the mission — **accessibility
grants/nonprofit funding**, which "the first accessible trading-education platform for
blind people" is genuinely strong for. Decide early which model, because it changes what's
gated.

## What makes an account enticing (ranked, for *this* audience)

1. **Your *sound*, saved.** The killer feature almost no other product can offer: a blind
   trader invests real effort tuning how their chart *sounds* (waveforms, bell patches,
   per-indicator sonification, the sound designer). Persisting that across devices is a
   genuine reason to have an account. Lead with it.
2. **A paper track record that compounds.** Persistent P&L, win rate, and a durable
   **journal** (the session journal exists — make it permanent and per-account). Progress
   you can hear and review is both engagement and education.
3. **Accessible trading education that doesn't exist anywhere else.** The real
   differentiator: a "learn to trade *by ear*" curriculum (what a divergence / overbought
   RSI / confluence zone *sounds* like) inside a safe paper sandbox, with progress
   tracking. There is essentially no accessible trading education for blind people — an
   unmet need, not a feature.
4. **Alerts that reach you off-site.** Email/Telegram delivery already exists; an account
   makes alerts durable and tied to identity, firing even when you're away.
5. **Pick up anywhere, on any machine.** Cross-device continuity is disproportionately
   valuable for people on managed/public/library machines who can't install — log in
   anywhere, your whole setup is there.
6. **A clear "graduation" path.** "You've run 50 profitable paper trades — here's how to
   take it live on the desktop with your own keys." Make the upgrade feel earned.

**Standout, uniquely enabled by the multi-user work — mentorship / paired sessions:** a
teacher (sighted or blind) and a learner navigating the same chart together, both hearing
it. A differentiator nobody else has, and a natural fit for an education platform.

## What to do, in order

1. **Auth + per-user persistence** (the foundation): accounts, and save workspaces +
   indicator settings + **sound design** + paper record + journal, keyed to identity.
2. **A "hosted" policy tier** — generalise `DemoPolicy` (`AccessibleTrader.Core/Services/
   DemoPolicy.cs`) from "demo" into a hosted-account policy: paper-only, no real keys,
   standard indicators, full save — vs. the desktop's everything.
3. **Shared public-data pool + cache** (from the BYOA analysis) so the hosted side scales:
   one market-data subscription per symbol, fanned out; shared historical cache.
4. **The education layer**: structured learn-by-ear lessons + guided paper challenges +
   progress tracking. Where the "enticing" really lives.
5. **Later**: mentorship/shared sessions; premium-indicator unlocks by progress/tier; the
   Blazor Server **circuit rate-limiter** before any public, un-gated exposure.

## Open decisions (to settle before building the account layer)

- **Money model:** free + donations, freemium/supporter tier, or grant-funded? Determines
  what's gated and whether there's a paid tier at all.
- **Identity provider:** roll-your-own auth vs. an external IdP (OAuth/OIDC). Accessibility
  of the login flow itself matters — it must be fully screen-reader-friendly.
- **Per-user secret storage:** today secure storage is a process-wide Singleton (shared);
  even paper accounts that later want per-user *data-provider* keys (read-only) would need
  it keyed by identity. Real-money keys stay desktop-only regardless.
- **Data licensing for pooled market data:** check each provider's ToS for redistributing
  pooled/cached market data to many end users (separate from the BYOA question).
- **Mentorship session model:** how two circuits are bridged into a shared one (the
  opposite of the isolation we just built — an explicit, opt-in feature on top).

---

*This is the durable direction. Cross-references: `WEBHOST_MULTI_USER_SCOPING.md`
(the per-circuit foundation), `PLATFORM_STRATEGY_AND_ROADMAP.md` (two-head strategy),
`docs/CHANGES.md` (v1.2.0).*
