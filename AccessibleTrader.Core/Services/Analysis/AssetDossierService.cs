using System.Globalization;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Analysis
{
    public interface IAssetDossierService
    {
        Task<AssetDossier> BuildAsync(string symbol, string market,
                                      IReadOnlyList<Ohlcv>? bars, CancellationToken ct = default);
    }

    /// <summary>
    /// Assembles everything the terminal knows about the loaded asset into one narrated document.
    ///
    /// <para>
    /// ── The organising rule ─────────────────────────────────────────────────────
    /// Sections are grouped by <b>question</b>, not by source. Tabs labelled "CoinGecko",
    /// "GitHub", "SEC" would push the synthesis back onto the user — they would have to visit four
    /// tabs and hold four half-answers in their head to decide one thing. Grouping by question
    /// ("Supply and dilution", "Development") puts everything bearing on that question in one place
    /// and cites the source on each individual field instead. Which is also the standing rule for
    /// this layer: display everything with its provenance.
    /// </para>
    ///
    /// <para>
    /// ── Nothing is scored ───────────────────────────────────────────────────────
    /// The checks are arithmetic over fields that are already displayed, each shown with its own
    /// reasoning, and they are never summed into a rating. A single number would be read as a
    /// recommendation, which is exactly what the strategy-library policy bans and what the evidence
    /// does not support. The standing prediction, recorded before any of this was built: this family
    /// works as a <b>veto</b>, not as a timing signal — it should help avoid losses, not pick winners.
    /// </para>
    ///
    /// <para>
    /// ── Failure is a first-class outcome ────────────────────────────────────────
    /// Every remote call is wrapped, and a failure produces a section marked
    /// <see cref="DossierStatus.Unavailable"/> with the reason rather than a missing section or an
    /// exception. "No insider filings in 90 days" and "EDGAR did not answer" are different
    /// statements and the dossier always says which one it means.
    /// </para>
    /// </summary>
    public sealed class AssetDossierService : IAssetDossierService
    {
        private readonly ICryptoProfileSource? _crypto;
        private readonly ICompanyProfileSource? _company;
        private readonly IChartPatternDetector? _patterns;
        private readonly ISwingStructureAnalyzer? _swings;

        public AssetDossierService(
            ICryptoProfileSource? crypto = null,
            ICompanyProfileSource? company = null,
            IChartPatternDetector? patterns = null,
            ISwingStructureAnalyzer? swings = null)
        {
            _crypto = crypto;
            _company = company;
            _patterns = patterns;
            _swings = swings;
        }

        /// <summary>
        /// Crypto or equity, decided from the market the chart was loaded from rather than guessed
        /// from the ticker. "ETH" is a coin on Bitstamp and could be a ticker somewhere else; the
        /// market the user chose is the reliable answer and it is already on the chart identity.
        /// </summary>
        internal static bool IsCrypto(string market) =>
            market.Contains("crypto", StringComparison.OrdinalIgnoreCase);

        public async Task<AssetDossier> BuildAsync(string symbol, string market,
                                                   IReadOnlyList<Ohlcv>? bars,
                                                   CancellationToken ct = default)
        {
            bool crypto = IsCrypto(market);
            var sections = new List<DossierSection>();
            var flags = new List<DossierFlag>();

            if (crypto) await BuildCryptoAsync(symbol, sections, flags, ct);
            else await BuildEquityAsync(symbol, sections, flags, ct);

            // The chart read is last in construction and FIRST in the tab order: it is the only
            // section that always has an answer, and it is the one a sighted person gets for free.
            sections.Insert(0, ChartSection(bars));

            string cls = crypto ? "crypto" : "equity";
            return new AssetDossier(symbol, cls,
                DossierHeadline.Build(symbol, cls, sections, flags),
                sections, flags, DateTime.UtcNow);
        }

        // ── Crypto ──────────────────────────────────────────────────────────────

        private async Task BuildCryptoAsync(string symbol, List<DossierSection> sections,
                                            List<DossierFlag> flags, CancellationToken ct)
        {
            if (_crypto == null)
            {
                sections.Add(Unavailable("Identity", "No crypto profile source is configured."));
                sections.Add(Unavailable("Supply and dilution", "No crypto profile source is configured."));
                sections.Add(Unavailable("Development", "No crypto profile source is configured."));
                sections.Add(Unavailable("Disclosure", "No crypto profile source is configured."));
                return;
            }

            CryptoProfile? p;
            try
            {
                p = await _crypto.GetAsync(symbol, ct);
            }
            catch (Exception ex)
            {
                foreach (var t in new[] { "Identity", "Supply and dilution", "Development", "Disclosure" })
                    sections.Add(Unavailable(t, $"Lookup failed: {ex.Message}"));
                return;
            }

            if (p == null)
            {
                // The genuinely-unknown case, and it deserves its own wording. A ticker with no
                // listing is not a broken dossier; for a brand-new token it is the single most
                // informative thing the screen can say.
                foreach (var t in new[] { "Identity", "Supply and dilution", "Development", "Disclosure" })
                    sections.Add(new DossierSection(t, DossierStatus.NoData, Array.Empty<DossierField>(), null,
                        $"No listing found for '{symbol}'. For a very new token this is expected, and it means "
                        + "nothing here can be verified — treat every claim about it as unchecked."));
                return;
            }

            var now = DateTime.UtcNow;

            sections.Add(new DossierSection("Identity", DossierStatus.Ok, new List<DossierField>
            {
                F("Name", p.Name, "CoinGecko"),
                F("Market cap rank", p.MarketCapRank?.ToString() ?? "unranked", "CoinGecko"),
                F("Genesis date", p.GenesisDate?.ToString("yyyy-MM-dd") ?? "not published", "CoinGecko",
                    p.GenesisDate == null ? DossierStatus.NoData : DossierStatus.Ok),
                F("Categories", p.Categories.Count > 0 ? string.Join(", ", p.Categories.Take(5)) : "none listed",
                    "CoinGecko", p.Categories.Count == 0 ? DossierStatus.NoData : DossierStatus.Ok),
            }, now));

            // ── Supply and dilution ──
            double? fdvRatio = p.MarketCapUsd is > 0 && p.FullyDilutedUsd is > 0
                ? p.FullyDilutedUsd / p.MarketCapUsd : null;
            double? circPct = p.MaxSupply is > 0 && p.CirculatingSupply is > 0
                ? p.CirculatingSupply / p.MaxSupply * 100 : null;
            double? turnover = p.MarketCapUsd is > 0 && p.Volume24hUsd is >= 0
                ? p.Volume24hUsd / p.MarketCapUsd : null;

            sections.Add(new DossierSection("Supply and dilution", DossierStatus.Ok, new List<DossierField>
            {
                F("Circulating supply", Fmt(p.CirculatingSupply), "CoinGecko"),
                F("Max supply", p.MaxSupply is > 0 ? Fmt(p.MaxSupply) : "none — issuance is uncapped",
                    "CoinGecko", p.MaxSupply is > 0 ? DossierStatus.Ok : DossierStatus.NoData),
                F("Circulating share of max", circPct.HasValue ? $"{circPct:0.0}%" : "not computable",
                    "derived", circPct.HasValue ? DossierStatus.Ok : DossierStatus.NotApplicable,
                    "Low means most of the supply has yet to reach the market."),
                F("Fully diluted / market cap", fdvRatio.HasValue ? $"{fdvRatio:0.00}x" : "not computable",
                    "derived", fdvRatio.HasValue ? DossierStatus.Ok : DossierStatus.NotApplicable,
                    "How much the market cap would grow at today's price if every token existed."),
                F("24h volume / market cap", turnover.HasValue ? $"{turnover:0.000}" : "not computable",
                    "derived", turnover.HasValue ? DossierStatus.Ok : DossierStatus.NotApplicable,
                    "Very low is illiquid and hard to exit; very high can be a wash-trading tell."),
            }, now));

            // ── Development, and this is the section that justifies the whole feature ──
            IReadOnlyList<GithubRepoStats> repos = Array.Empty<GithubRepoStats>();
            try { repos = await _crypto.GetRepoStatsAsync(p.Repos, ct); }
            catch { /* fall back to the aggregator's figures */ }

            var liveRepo = repos.Where(r => r.LastPush.HasValue).OrderByDescending(r => r.LastPush).FirstOrDefault();
            var devFields = new List<DossierField>
            {
                F("Repositories listed", p.Repos.Count > 0 ? string.Join(", ", p.Repos.Take(3)) : "none",
                    "CoinGecko", p.Repos.Count > 0 ? DossierStatus.Ok : DossierStatus.NoData),
                F("Commits, last 4 weeks", p.CommitsLast4Weeks?.ToString() ?? "unknown", "CoinGecko",
                    p.CommitsLast4Weeks.HasValue ? DossierStatus.Ok : DossierStatus.NoData,
                    "Aggregator figure — only as current as the repository registered with them."),
                F("Stars / forks", $"{p.Stars?.ToString() ?? "?"} / {p.Forks?.ToString() ?? "?"}", "CoinGecko"),
            };

            if (liveRepo != null)
            {
                int days = (int)(DateTime.UtcNow - liveRepo.LastPush!.Value).TotalDays;

                // Say plainly whether this is the repository the listing points at or a DIFFERENT
                // one found by sweeping the owning organisation. Presenting "kaspanet/silverscript
                // pushed today" as though it were the flagship would be a more subtle version of the
                // error this whole second lookup exists to fix.
                bool isListed = p.Repos.Any(r =>
                    string.Equals(CoinGeckoCryptoProfileSource.RepoSlug(r), liveRepo.FullName,
                        StringComparison.OrdinalIgnoreCase));

                devFields.Add(F(
                    isListed ? "Most recent push (live)" : "Most recent push elsewhere in the org",
                    $"{liveRepo.FullName}, {liveRepo.LastPush:yyyy-MM-dd} ({days} days ago)", "GitHub",
                    DossierStatus.Ok,
                    isListed
                        ? "Queried directly, because an aggregator's developer figures go stale silently."
                        : "The listed repository looked stale, so the owning organisation was checked. "
                        + "This is activity in the same org, NOT necessarily the project's main repository."));

                if (liveRepo.Archived)
                    devFields.Add(F("Repository archived", "YES — the project has stopped work here", "GitHub"));
            }
            else if (p.Repos.Count > 0)
            {
                devFields.Add(F("Most recent push (live)", "GitHub did not answer", "GitHub",
                    DossierStatus.Unavailable, "Unauthenticated GitHub allows 60 requests an hour."));
            }

            sections.Add(new DossierSection("Development", DossierStatus.Ok, devFields, now));

            // ── Disclosure: absence IS the measurement here ──
            sections.Add(new DossierSection("Disclosure", DossierStatus.Ok, new List<DossierField>
            {
                F("Website", p.HasHomepage ? "published" : "MISSING", "CoinGecko",
                    p.HasHomepage ? DossierStatus.Ok : DossierStatus.NoData),
                F("Whitepaper", p.HasWhitepaper ? "published" : "MISSING", "CoinGecko",
                    p.HasWhitepaper ? DossierStatus.Ok : DossierStatus.NoData),
                F("Public source code", p.Repos.Count > 0 ? "published" : "MISSING", "CoinGecko",
                    p.Repos.Count > 0 ? DossierStatus.Ok : DossierStatus.NoData),
                F("Block explorer", p.HasExplorer ? "published" : "MISSING", "CoinGecko",
                    p.HasExplorer ? DossierStatus.Ok : DossierStatus.NoData),
                F("Twitter followers", p.TwitterFollowers?.ToString("N0") ?? "unknown", "CoinGecko",
                    p.TwitterFollowers.HasValue ? DossierStatus.Ok : DossierStatus.NoData),
            }, now,
                "No whitepaper or no public source is the loudest cheap signal there is."));

            flags.AddRange(CryptoChecks(p, fdvRatio, circPct, turnover, liveRepo));
        }

        /// <summary>
        /// The deterministic checks. Each is one comparison over a field already on screen, and each
        /// carries its own reasoning so the user can disagree with it.
        ///
        /// <para>
        /// Every threshold here is a convention, not a measured one — none of these has been tested
        /// against forward returns, and testing them properly needs point-in-time universe snapshots
        /// taken FORWARD, because dead tokens vanish from today's listings and any backtest on the
        /// surviving universe is poisoned. Until that exists these are labelled as what they are:
        /// conventional red flags, not evidence.
        /// </para>
        /// </summary>
        internal static IEnumerable<DossierFlag> CryptoChecks(
            CryptoProfile p, double? fdvRatio, double? circPct, double? turnover, GithubRepoStats? liveRepo)
        {
            yield return new DossierFlag("Dilution overhang",
                fdvRatio is > 3.0,
                fdvRatio.HasValue
                    ? $"Fully diluted value is {fdvRatio:0.00}x the market cap." +
                      (fdvRatio > 3.0 ? " Most of the supply has yet to reach the market." : "")
                    : "Not computable — no fully diluted valuation published.",
                "derived from CoinGecko");

            yield return new DossierFlag("Supply not yet released",
                circPct is < 50,
                circPct.HasValue ? $"{circPct:0.0}% of max supply is circulating."
                                 : "No max supply published, so this cannot be computed.",
                "derived from CoinGecko");

            yield return new DossierFlag("Uncapped issuance",
                !(p.MaxSupply > 0),
                p.MaxSupply > 0 ? $"Capped at {Fmt(p.MaxSupply)}." : "No maximum supply — issuance is uncapped.",
                "CoinGecko");

            yield return new DossierFlag("Illiquid",
                turnover is < 0.005,
                turnover.HasValue ? $"24h volume is {turnover:0.000}x market cap."
                                  : "Not computable.",
                "derived from CoinGecko");

            yield return new DossierFlag("Possible wash trading",
                turnover is > 1.0,
                turnover.HasValue
                    ? $"24h volume is {turnover:0.000}x market cap." +
                      (turnover > 1.0 ? " The whole float turning over daily is unusual." : "")
                    : "Not computable.",
                "derived from CoinGecko");

            yield return new DossierFlag("No whitepaper", !p.HasWhitepaper,
                p.HasWhitepaper ? "Published." : "None published.", "CoinGecko");

            yield return new DossierFlag("No public source code", p.Repos.Count == 0,
                p.Repos.Count > 0 ? $"{p.Repos.Count} repository/ies listed." : "None listed.", "CoinGecko");

            yield return new DossierFlag("No block explorer", !p.HasExplorer,
                p.HasExplorer ? "Published." : "None published.", "CoinGecko");

            // Development staleness uses the LIVE push where available, precisely because the
            // aggregator's count is the number that goes wrong.
            bool stale = liveRepo?.LastPush is DateTime lp
                ? (DateTime.UtcNow - lp).TotalDays > 180
                : p.Repos.Count > 0 && p.CommitsLast4Weeks is 0 && liveRepo == null;
            yield return new DossierFlag("Development stalled", stale,
                liveRepo?.LastPush is DateTime d
                    ? $"Most recent push {d:yyyy-MM-dd} ({(int)(DateTime.UtcNow - d).TotalDays} days ago), from {liveRepo.FullName}."
                    : p.Repos.Count == 0 ? "No repository to check."
                    : "GitHub did not answer; the aggregator's commit count is the only figure available.",
                liveRepo != null ? "GitHub" : "CoinGecko");

            yield return new DossierFlag("Repository archived", liveRepo?.Archived == true,
                liveRepo?.Archived == true ? $"{liveRepo.FullName} is archived." : "Not archived.",
                "GitHub");

            yield return new DossierFlag("Very new listing",
                p.GenesisDate is DateTime g && (DateTime.UtcNow - g).TotalDays < 365,
                p.GenesisDate is DateTime gd ? $"Genesis {gd:yyyy-MM-dd}." : "No genesis date published.",
                "CoinGecko");
        }

        // ── Equities ────────────────────────────────────────────────────────────

        private async Task BuildEquityAsync(string symbol, List<DossierSection> sections,
                                            List<DossierFlag> flags, CancellationToken ct)
        {
            if (_company == null)
            {
                foreach (var t in new[] { "Company", "Financials", "Filing activity" })
                    sections.Add(Unavailable(t, "No company profile source is configured."));
                return;
            }

            CompanyProfile? c;
            try { c = await _company.GetAsync(symbol, ct); }
            catch (Exception ex)
            {
                foreach (var t in new[] { "Company", "Financials", "Filing activity" })
                    sections.Add(Unavailable(t, $"Lookup failed: {ex.Message}"));
                return;
            }

            if (c == null)
            {
                foreach (var t in new[] { "Company", "Financials", "Filing activity" })
                    sections.Add(new DossierSection(t, DossierStatus.NoData, Array.Empty<DossierField>(), null,
                        $"'{symbol}' is not in EDGAR's ticker index. Non-US listings, ETFs and index "
                        + "vehicles are not filers, so this is expected for most of them."));
                return;
            }

            var now = DateTime.UtcNow;

            sections.Add(new DossierSection("Company", DossierStatus.Ok, new List<DossierField>
            {
                F("Name", c.Name, "SEC EDGAR"),
                F("Industry", c.Industry ?? "not stated", "SEC EDGAR",
                    string.IsNullOrEmpty(c.Industry) ? DossierStatus.NoData : DossierStatus.Ok),
                F("CIK", c.Cik?.ToString() ?? "unknown", "SEC EDGAR"),
            }, now));

            // Financials are stated with their FILING date, never the period they cover — the point
            // being that the dossier reports what was knowable when.
            var fin = new List<DossierField>();
            AddSeries(fin, "Diluted EPS", c.Eps);
            AddSeries(fin, "Revenue", c.Revenue);
            sections.Add(fin.Count > 0
                ? new DossierSection("Financials", DossierStatus.Ok, fin, now,
                    "Dated by FILING, not by period end — this is what was public when.")
                : new DossierSection("Financials", DossierStatus.NoData, fin, now,
                    "EDGAR returned no values for the tracked concepts."));

            // Filing activity: recency windows, because "none in 90 days" is the informative case.
            var cutoff = DateTime.UtcNow.AddDays(-90);
            int insider90 = c.InsiderFilings.Where(x => x.Date >= cutoff).Sum(x => x.Count);
            int events90 = c.MaterialEvents.Where(x => x.Date >= cutoff).Sum(x => x.Count);
            var lastInsider = c.InsiderFilings.OrderByDescending(x => x.Date).FirstOrDefault();

            sections.Add(new DossierSection("Filing activity", DossierStatus.Ok, new List<DossierField>
            {
                F("Insider filings, last 90 days",
                    insider90 > 0 ? insider90.ToString() : "none", "SEC EDGAR",
                    insider90 > 0 ? DossierStatus.Ok : DossierStatus.NoData,
                    "Form 4. A count, not a judgement about direction."),
                F("Most recent insider filing",
                    lastInsider.Date == default ? "none on record" : lastInsider.Date.ToString("yyyy-MM-dd"),
                    "SEC EDGAR", lastInsider.Date == default ? DossierStatus.NoData : DossierStatus.Ok),
                F("Material events, last 90 days",
                    events90 > 0 ? events90.ToString() : "none", "SEC EDGAR",
                    events90 > 0 ? DossierStatus.Ok : DossierStatus.NoData, "8-K."),
            }, now));

            flags.Add(new DossierFlag("No recent financial filing",
                c.Eps.Count == 0 || c.Eps.Max(e => e.Filed) < DateTime.UtcNow.AddDays(-200),
                c.Eps.Count == 0 ? "No EPS values on record."
                    : $"Most recent EPS filing {c.Eps.Max(e => e.Filed):yyyy-MM-dd}.",
                "SEC EDGAR"));
        }

        private static void AddSeries(List<DossierField> into, string label,
                                      IReadOnlyList<(DateTime Filed, double Value)> series)
        {
            if (series.Count == 0)
            {
                into.Add(F(label, "no values on record", "SEC EDGAR", DossierStatus.NoData));
                return;
            }
            var latest = series.OrderByDescending(s => s.Filed).First();
            into.Add(F($"{label}, most recent", $"{latest.Value:N2} (filed {latest.Filed:yyyy-MM-dd})", "SEC EDGAR"));

            var prior = series.OrderByDescending(s => s.Filed).Skip(1).FirstOrDefault();
            if (prior.Filed != default && Math.Abs(prior.Value) > 1e-9)
            {
                double chg = (latest.Value - prior.Value) / Math.Abs(prior.Value) * 100;
                into.Add(F($"{label}, change on prior filing", $"{chg:+0.0;-0.0}%", "derived"));
            }
            into.Add(F($"{label}, filings on record", series.Count.ToString(), "SEC EDGAR"));
        }

        // ── The chart read: always available, never predictive ──────────────────

        /// <summary>
        /// What a sighted person reads off the chart in one glance, and the only section that never
        /// depends on a network. Description only — no pattern here is called bullish or bearish,
        /// because every price-derived pattern claim tested in this project has come back null.
        /// </summary>
        private DossierSection ChartSection(IReadOnlyList<Ohlcv>? bars)
        {
            if (bars == null || bars.Count < 30)
                return new DossierSection("Chart read", DossierStatus.NoData, Array.Empty<DossierField>(), null,
                    $"Only {bars?.Count ?? 0} bars loaded — not enough to describe the shape.");

            var fields = new List<DossierField>();
            var last = bars[^1];

            double hi = bars.Max(b => b.High), lo = bars.Min(b => b.Low);
            double pos = hi > lo ? (last.Close - lo) / (hi - lo) * 100 : 50;
            fields.Add(F("Position in loaded range", $"{pos:0}% of the way up", "chart"));
            fields.Add(F("Bars loaded", $"{bars.Count:N0}, {bars[0].Date:yyyy-MM-dd} to {last.Date:yyyy-MM-dd}", "chart"));

            if (_swings != null)
            {
                try
                {
                    var st = _swings.Analyze(bars);
                    var state = st.StatePerBar.Length > 0 ? st.StatePerBar[^1] : StructureState.Undefined;
                    fields.Add(F("Market structure", state.ToString(), "swing analyzer", DossierStatus.Ok,
                        "Confirmed swing labels, not a forecast."));
                    var recent = st.Swings.TakeLast(4).Select(s => $"{(s.IsHigh ? "high" : "low")} {s.Label}").ToList();
                    if (recent.Count > 0)
                        fields.Add(F("Recent swings", string.Join(", ", recent), "swing analyzer"));
                }
                catch { fields.Add(F("Market structure", "could not be computed", "swing analyzer", DossierStatus.Unavailable)); }
            }

            if (_patterns != null)
            {
                try
                {
                    var found = _patterns.Detect(bars);
                    var here = ChartPatternNarrator.AtBar(found, bars.Count - 1);
                    fields.Add(here.Count > 0
                        ? F("Formations in view",
                            string.Join(" ", here.Select(p =>
                                $"{(p.State == ChartPatternState.Forming ? "possible " : "")}{ChartPatternNarrator.Name(p.Kind)}"
                                + $" ({(p.State == ChartPatternState.Forming ? "forming" : "completed")}, trigger {p.TriggerLevel:0.####})")),
                            "pattern detector", DossierStatus.Ok,
                            "Described, not scored. No formation here is called bullish or bearish.")
                        : F("Formations in view", "none", "pattern detector", DossierStatus.NoData));
                }
                catch { fields.Add(F("Formations in view", "could not be computed", "pattern detector", DossierStatus.Unavailable)); }
            }

            return new DossierSection("Chart read", DossierStatus.Ok, fields, DateTime.UtcNow);
        }

        // ── Helpers ─────────────────────────────────────────────────────────────

        private static DossierField F(string label, string value, string source,
                                      DossierStatus status = DossierStatus.Ok, string? note = null)
            => new(label, value, source, status, note);

        private static DossierSection Unavailable(string title, string why)
            => new(title, DossierStatus.Unavailable, Array.Empty<DossierField>(), null, why);

        private static string Fmt(double? v) => v.HasValue
            ? v.Value >= 1e9 ? $"{v / 1e9:N2} billion"
            : v.Value >= 1e6 ? $"{v / 1e6:N2} million"
            : v.Value.ToString("N2", CultureInfo.InvariantCulture)
            : "unknown";
    }
}
