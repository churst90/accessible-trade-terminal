# Shared charts: a design for later

Written 2026-11-22 from a discussion with Cody; the second round's additions (what a share holds,
chart notes, the app and an account, the order of work) were accepted by him the same day.
**Status: design only, nothing built.** Cody's
answers to the three open questions are the basis of everything below:

1. **Live, not frozen.** "I'll update it in realtime and want people to see so they can keep that
   open and when I update it, it updates it in realtime in their view", with a "read only" tag on
   the tab when a viewer switches to it.
2. **Anyone can share**, and every shared chart is read-only to everyone but its owner, "so people
   can't mess up your own charts".
3. **Link-only.** No public gallery.

The point, in Cody's words: "this way I can do analysis and share my analysis with people", the way
TradingView lets you share a chart only you can change.

Cody's second round of questions (2026-11-22, the same day) is answered under **What a share holds**,
**Chart notes** and **The app and an account** below, with recommendations for him to confirm.

## What exists already

- A workspace tab records the whole chart: market, provider, symbol, timeframe, chart type and scale,
  Heikin Ashi and Renko settings, every indicator with its settings (`SeriesConfig`), pane heights
  (`WorkspaceConfiguration.cs`). Drawings and their anchor points are saved with the tab too
  (`WorkspaceLibraryService` syncs `ChartSeries.Drawing` into `SeriesConfig.Drawing`, since 2026-07-22).
  A shared chart is a copy of one or more tabs, plus the owner's notes.
- Every change to a chart goes through `WorkspaceStore.Dispatch(WorkspaceAction)` and its reducers.
  That one door is where read-only is enforced (below), and where an owner's edits are picked up to
  send to viewers.
- Hosted accounts live in `auth.db` (SQLite, `AuthDbContext`). `AppUser.DisplayName` already exists
  for the leaderboard; a shared chart shows it as the author.
- `/terminal/account/status` (`AccountStatusEndpoint`) is the precedent for an anonymous route in the
  terminal service.
- The demo at `/app/` (shown on `/chart`) proves a chart can be opened read-only with no account.
- `DemoPolicy.HostedProviders` lists the data sources the server can fetch for anyone with no
  user key. A shared chart can only use these (see Data).

## What a share holds

Cody asked: a single workspace, a whole session, or a flag saying public or not?

**Recommendation: a share holds the tabs the owner picks, one by default, and never the whole
session.** Sharing is switched on and off per share, like a flag, but the flag lives on the share, not
on the session.

- **One tab by default.** "Share this chart" shares the tab you are on. That is the common case: one
  idea, one chart, one link.
- **Several tabs when asked.** The dialog has "Add other tabs", a checklist of your open tabs. Cody's
  own way of working (one symbol on 1d, 4h, 1h, 15m, 1w and 1M) is a multi-timeframe analysis, and the
  viewer should get it as one link: each picked tab opens as its own read-only tab, in the owner's
  order. This costs little if it is designed in from the start (a share has a list of tabs), and a lot
  to add later.
- **Never the whole session.** A session holds things that are not analysis and some that are private:
  paper positions and orders, alerts, watchlist, the scratch tabs you opened for a moment, charts on
  **My Data** that nobody else can load. It also changes all day. Sharing it would publish more than
  the owner means to, and viewers would get every tab Cody opens.
- **The flag.** Each share is **on** (the link works) or **stopped** (the link says "stopped"). Turning
  it back on keeps the **same link**, so a link already in a YouTube description or a post still
  works. "New link" gives the share a fresh id and kills the old one, for a link that went where it
  should not.
- **Visibility is one field with room to grow:** stage 1 has only **link** (Cody's answer). A later
  **listed** value can put a share on the owner's public profile page if he ever wants that, without
  changing the table.

## Chart notes

Cody asked: a notes section he can update in real time, and where it would appear.

**Recommendation: yes. Notes belong to the tab, not only to the share, so they are useful before
anything is shared.** A note is a short timestamped entry ("4h closed above 61,400; watching the
retest"), newest first, like a running commentary. They are saved with the tab, so they also work in
the downloaded app with no account. When the tab is shared, its notes are shared with it, live.

These are different from **notes on drawings** (above), which describe one line. Chart notes are the
owner's thinking about the whole chart over time.

Where they appear:

- **For the owner:** a Notes key on the chart (key to choose) opens a small panel with a text box at
  the top and the entries below. Type, press Enter, and the note is posted with its time. Up and Down
  read earlier notes; an entry can be edited or deleted (viewers hear "Cody edited a note").
- **For a viewer, by ear:** a new note is spoken once, politely, like the other live updates:
  "Cody's note: 4h closed above 61,400; watching the retest." The same Notes key reads them, newest
  first, with their times. A setting can turn the spoken notes off; the list still fills.
- **For a viewer, by eye:** a column to the right of the chart on a wide screen, below it on a narrow
  one, which can be collapsed. The website page for the link (`/s/<id>`) shows them as plain text, so
  someone who never opens the chart can still read the thinking.
- **In the update log:** each note also appears there, in time order with the chart changes, so one
  key gives the whole story ("10:02 Cody added a line at 61,450. 10:03 Cody's note: ...").
- Notes are plain text with a length cap (500 characters each, the last 200 kept per tab; older ones
  stay in the update log).

## How it works

### The owner

1. On any tab in their hosted account, the owner presses **Share this chart** (key to choose; also in
   the tab's menu and the Go bar as `SHARE`). A short dialog asks for a title and an optional summary,
   then says the link and copies it: `accessibletrader.com/s/<id>`.
2. From then on the tab is **live-shared**. Everything the owner commits on it reaches viewers within
   about a second: a drawing placed, moved or deleted; an indicator added, removed or retuned; a note
   written; the symbol, timeframe, chart type or scale changed.
3. The tab name gets a mark so the owner always knows: "BTCUSDT 1h, shared". Switching to it says so.
4. **Notes on drawings.** Any drawing can carry a sentence ("the support that held three times").
   The owner hears it when arrowing onto the drawing, and so does every viewer.
5. **Stop sharing** from the same menu kills the link at once; viewers hear "Cody stopped sharing this
   chart" and keep a frozen copy of what they last saw until they close the tab.
6. Owners see a list of their shared charts with how many people are viewing each now (a count only,
   never who).

### The viewer

1. Opening the link in a browser:
   - **signed in:** the chart opens as a new tab in their own terminal, named
     **"BTCUSDT 1h, Cody's chart, read only"**. The name is what the screen reader says on switching
     to the tab, which is the "read only tag" Cody asked for;
   - **signed out:** it opens in a read-only viewer, like the demo, with the same name. No account
     needed.
2. The viewer moves freely: their own cursor, zoom, focused pane, mutes and speech settings. None of
   that is shared or overwritten by the owner's updates.
3. Any key that would change the chart says, instead: "Read only. This is Cody's chart. Press
   Ctrl+Shift+C to copy it to your own charts." (Signed-out viewers hear "Sign in to copy it.")
4. **Copy to my charts** makes an ordinary, editable tab in the viewer's account. It no longer
   follows the owner.
5. A sighted visitor arriving from YouTube or a post sees the chart drawn as usual, with the owner's
   title, summary and notes beside it.

### Hearing live updates

Live is the core of Cody's answer, so the updates must be heard, not only drawn (Cody's rule since
2026-11-19: a change to the chart is heard wherever he arrows, not only seen):

- Each committed change is spoken once, politely, after the owner pauses for about two seconds, so a
  run of edits becomes one sentence: "Cody added a horizontal line at 95,200: support."
  "Cody moved the trend line." "Cody switched to the 4-hour."
- If the viewer's cursor is on a drawing that changes, they hear the new value at once.
- **An update log** on one key reads the changes newest first, each with its time ("3 minutes ago:
  Cody added a horizontal line at 95,200"). This keeps the honesty a frozen chart would have given:
  a viewer can always hear what was said before and when it changed.
- A setting turns the spoken updates off (the log still records them).
- Prices keep streaming as on any chart; that is the viewer's own data feed, not the owner's.

### Why live, and what it costs

Live is the better fit for what Cody described: a chart he works on during the day that people keep
open, like a trading room. What it gives up is the "was the call right?" check of a frozen chart. The
update log restores most of that. A **Snapshot** button (later) can freeze a copy under its own link
for anyone who wants a fixed record of a call.

Optional later: **Follow Cody**, a viewer mode where the viewer's cursor follows the owner's, for live
streams and teaching. Off by default, because it takes the viewer's arrows away.

## Read-only, enforced on the server

Hiding keys is not enough. A viewer's circuit gets a `SharedViewer` flag on the shared tab, and
`WorkspaceStore.Dispatch` refuses every chart-changing action on that tab (with the spoken message
above) before any reducer runs. View-only actions (cursor, zoom, pane focus, mute, speech) pass,
because they are the viewer's own and are never sent anywhere. Tests:

- every `WorkspaceAction` type is classified as chart-changing or view-only, and a new action type
  with no classification fails the test (so a future action cannot slip through as editable);
- a viewer dispatching each chart-changing action leaves the shared state byte-identical;
- the owner's change reaches a second circuit and is spoken there.

## The live channel

Both the owner and signed-in viewers run in the **terminal** service, so a small in-process hub
(`SharedChartHub`, keyed by share id) carries the owner's committed changes to every circuit
following that id. The owner's store publishes after each committed action; viewers apply the change
through their own store as a "remote" action that bypasses the read-only check.

The open question is **signed-out viewers**: the demo runs in a separate process (`/app/`, port 5145).
Two choices:

- **(Recommended)** Serve signed-out viewers from the terminal service on an anonymous route
  (`/terminal/s/<id>`, like `account/status`), so there is one process and one hub. That needs a
  per-circuit read-only mode in a service that today requires sign-in for everything; the policy has
  to be per-circuit, not `DemoPolicy`'s per-process `HostMode`.
- Run the viewer in the demo process and pass changes between processes (through the database plus a
  short poll, or a socket). More moving parts; only worth it if the first proves hard.

The saved state is written to the database on every commit too, so a restart loses nothing and a new
viewer opens the current version.

## Data

- A shared chart must use a source the server can fetch for anyone: `DemoPolicy.HostedProviders`.
  The share dialog says so and refuses **My Data** (the owner's own imported files), which no one
  else can see.
- Crypto venues (Binance, Bitstamp, Kraken, MEXC and the rest) are public, no key: no concern.
- **Twelve Data** (stocks, forex, indices) uses one server key with rate limits, and its terms limit
  passing data on to others. Before stock charts can be shared: check the terms, and measure how many
  viewers the key's limit can feed. Until then stage 1 can allow crypto and the keyless sources only,
  and say why in the dialog.
- Each viewer's feed comes from the shared feed pool where one exists (`KEYED_FEEDS_DESIGN.md`: the
  hosted shared-feed pool is still on that design's list), so a thousand viewers of one BTC chart do
  not open a thousand sockets to the venue.

## Storage

A new table in `auth.db` (or its own SQLite file next to it):

| column | meaning |
|---|---|
| `Id` | random, at least 10 characters (base-58), the link; not guessable or listable |
| `OwnerId` | `AppUser.Id` |
| `Title`, `Summary` | plain text, length-capped (120 / 2,000) |
| `TabsJson` | the shared tabs (one or more `WorkspaceConfiguration` tabs, in order), drawings and chart notes included |
| `Visibility` | `link` in stage 1 (room for `listed` later) |
| `CreatedUtc`, `UpdatedUtc`, `StoppedUtc` | |
| `Hidden` | set by a takedown |

and a `SharedChartUpdates` table for the update log (share id, time, the spoken sentence, the
change). Keep the last few hundred per chart.

## Link-only, and abuse

Link-only is Cody's answer, and it also keeps abuse small. Still needed from the first public version,
because anyone can share:

- **Unguessable ids** and `noindex` on every shared page, so search engines do not turn link-only
  into public. (An owner opt-in to indexing can come later.)
- **A report button** on every shared chart (signed in or out). Reports go to Cody by email (the
  account email sender already exists) with the link.
- **Takedown**: an admin action that hides a chart at once and tells its owner.
- **Limits**: verified email to share; up to 20 shared charts per account; titles, summaries and notes
  are plain text with caps; a rate limit on updates per minute (a runaway script cannot flood
  viewers).
- **"Not investment advice"** under every shared chart, and the author's display name on it.

## The page around the link

`accessibletrader.com/s/<id>` is a website page (PHP), not the terminal:

- the title, the author, the summary and the notes as ordinary text, readable by screen readers and
  search previews;
- a picture of the chart for social previews (`og:image`), rendered by the server's own chart renderer
  (SkiaSharp already draws the hosted chart server-side);
- **Open the live chart**, which opens the terminal viewer;
- the report link and the advice line.

## The app and an account

Cody asked: should someone using the downloaded app (Windows, Mac, Linux, or a local WebHost) be able
to sign in to their website account? Someone may open Settings (F12), look for their account and find
nothing. Since the server never stores API keys, is signing in worth it for chart settings, or only
for shared charts? And how would the app show a shared chart?

**Recommendation: yes, allow it, as an optional connection and never a gate.** The app must keep
working fully with no account, exactly as now.

What signing in is for, in order of value:

1. **Sharing your own charts.** The owner's live copy has to live on the server so viewers can reach
   it, and the server has to know whose it is. This is the one thing that cannot work without an
   account.
2. **Your charts in both places (later).** Your tabs, drawings, notes, indicator templates and alert
   definitions, set up at home in the app and there in the browser on your phone. Start with two
   plain actions, **Copy my website charts here** and **Copy these charts to my website account**,
   instead of live sync: they cannot conflict, and real sync can come after if people want it.
3. **The leaderboard and paper account**, which already live in the website account.

**Settings alone are not a reason.** Speech, sounds and keys depend on the machine and the screen
reader, and differ between a desktop and a browser on purpose. They can be in the copy later; they
are not why anyone would sign in.

**API keys never leave the machine, signed in or not.** Signing in sends nothing about your venues.
The sign-in itself is a token kept in the same protected store as the keys.

**Viewing a shared chart needs no account**, in the app as in the browser: the link is the
permission (Cody's link-only answer).

### An Account tab in Settings

Even before any of this is built, Settings could have an **Account** tab, so the person looking for it
finds an answer instead of nothing: "You do not need an account to use this app. An account on
accessibletrader.com lets you share your charts. Sharing from the app is coming." With a link to
create one. When it is built, the tab gets **Sign in**, the account's name once signed in, and
**Sign out**.

**How the sign-in works:** the app opens the website's sign-in page in the browser, where the password
manager and the screen reader already work. After signing in, the site hands a token back to the app
(the way `gh auth login` works: a one-time code, or a return to a local port). The app never sees the
password. The account page lists **Devices signed in**, each of which can be signed out from there.

### Viewing a shared chart in the app

- **Open shared chart** in the Go bar (`OPEN`) and the tab menu: paste the link, press Enter. Later,
  an "Open in the app" button on the website's `/s/<id>` page, through an `accessibletrader://` link
  the installers register.
- The app fetches the share from a small public API on the terminal service: `GET /terminal/api/s/<id>`
  returns the tabs, notes and update log as JSON, and `/terminal/api/s/<id>/live` streams each change
  as it happens (server-sent events; one connection per viewer, a few bytes per change).
- The share opens as read-only tabs, named and refusing edits exactly as in the browser (the same
  `SharedViewer` flag in the app's own `WorkspaceStore`).
- **Prices come from the app's own feeds**, not from the server: the share says "BTCUSDT on Bitstamp,
  4 hours" and the app loads it the way it loads any chart. The server only carries the chart's
  description and its changes, which is very little load, and a stock share can open in the app on
  the viewer's own Twelve Data key.
- **One message format for both.** The in-process hub for browser viewers should send the same JSON
  change messages this API streams, from the start. Then the browser and the app follow a share the
  same way, and a signed-out browser viewer could even be a client of this API rather than of the hub.

## Order of work

0. **Chart notes on a tab** and an **Account tab in Settings** that explains itself. Both are useful
   on their own, before any sharing, and work in the app with no account.
1. **Share and view, signed in.** The table (with a list of tabs per share), the Share dialog, the
   read-only tab with its name and message, server-side refusal, the hub with the shared JSON change
   format, spoken updates and notes, the update log, Copy to my charts, Stop sharing and New link,
   notes on drawings, and the abuse basics (ids, noindex, report, takedown, limits). Crypto and keyless
   sources only.
2. **Signed-out viewers** on the anonymous route, and the website page with the picture and notes.
3. **The app:** the public share API, Open shared chart in the app, then sign-in from the app and
   sharing from it.
4. **Stocks and forex**, after the Twelve Data check.
5. **Later:** copying charts between the app and the website account, Follow Cody, Snapshot, the
   `accessibletrader://` link, an owner's opt-in to listing.

A rough guess: stage 1 is about a week of work, stage 2 a few days, plus testing and Cody listening to
it. Real-time sharing is the larger part of stage 1; the rest reuses what exists.

## Questions still open for Cody

1. If he changes the symbol or timeframe on a shared tab, should viewers follow? (Assumed **yes**
   above: the share is "my live chart", whatever is on it.)
2. Spoken updates on by default for viewers? (Assumed **yes**, short, with the setting to turn off.)
3. The key for Share this chart, and for the update log.
4. Should an owner be able to see who is viewing, or only how many? (Assumed **only how many**.)
5. ~~The recommendations of 2026-11-22 above~~ **Accepted** (Cody, 2026-11-22: "I like your
   additions to the shared charts implementation, file that in the doc"): a share is one tab by
   default or several picked tabs, never the session; on/stopped with the same link, plus New link;
   chart notes saved with the tab and shared with it; sign-in from the app allowed but optional, for
   sharing first and copying charts later, never for keys; viewing a share needs no account anywhere.
   They are part of the design now, not proposals.
