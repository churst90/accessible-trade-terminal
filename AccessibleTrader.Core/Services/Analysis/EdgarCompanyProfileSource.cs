using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace AccessibleTrader.Core.Services.Analysis
{
    /// <summary>
    /// The dossier's company source: SEC EDGAR, free and keyless.
    ///
    /// <para>
    /// This deliberately duplicates a little of <c>SecEdgarProvider</c> rather than depending on it.
    /// That provider is a chart-series plugin — it answers "give me EPS as bars" — whereas the
    /// dossier needs several concepts plus the filings index in one pass, and Core cannot reference a
    /// plugin without inverting the dependency direction the plugin system is built on.
    /// </para>
    ///
    /// <para>
    /// ── The point-in-time rule, restated because it is the one that matters ─────
    /// Every XBRL fact is stamped by its <c>filed</c> date, never the period it describes. Placing a
    /// figure on its quarter-end date would show the dossier reporting numbers weeks before they
    /// were public, and for a reader deciding what to do next that is not a subtle error.
    /// </para>
    ///
    /// <para>
    /// ── The User-Agent is load-bearing ──────────────────────────────────────────
    /// <c>www.sec.gov</c> returns 403 to any agent without a contact email while
    /// <c>data.sec.gov</c> does not care. Since only the ticker→CIK map lives on the former, an
    /// agent without an email resolves NO tickers while every other call looks healthy. And a bare
    /// email is not a legal header value — RFC 9110 wants it inside a parenthesised comment, or
    /// <c>HttpHeaders.Add</c> throws.
    /// </para>
    /// </summary>
    public sealed class EdgarCompanyProfileSource : ICompanyProfileSource
    {
        private readonly HttpClient _http;
        private readonly ConcurrentDictionary<string, int> _cik = new(StringComparer.OrdinalIgnoreCase);
        private bool _mapLoaded;

        public EdgarCompanyProfileSource(HttpClient http) => _http = http;

        public async Task<CompanyProfile?> GetAsync(string ticker, CancellationToken ct = default)
        {
            ticker = Normalise(ticker);
            if (ticker.Length == 0) return null;

            int? cik = await ResolveCikAsync(ticker, ct);
            if (cik == null) return null;

            var (name, industry, insider, events) = await SubmissionsAsync(cik.Value, ct);
            var eps = await ConceptAsync(cik.Value, "EarningsPerShareDiluted", ct);
            var rev = await ConceptAsync(cik.Value, "RevenueFromContractWithCustomerExcludingAssessedTax", ct);

            return new CompanyProfile(ticker, name ?? ticker, industry, cik, eps, rev, insider, events);
        }

        /// <summary>
        /// Chart symbols carry decoration EDGAR does not use — "AAPL/USD", "AAPL.US". The base
        /// ticker is what the index is keyed on.
        /// </summary>
        internal static string Normalise(string symbol)
        {
            if (string.IsNullOrWhiteSpace(symbol)) return "";
            var s = symbol.Trim().ToUpperInvariant();
            foreach (var sep in new[] { '/', ':', ' ' })
                if (s.Contains(sep)) s = s.Split(sep)[0];
            if (s.EndsWith(".US", StringComparison.Ordinal)) s = s[..^3];
            return s;
        }

        private async Task<int?> ResolveCikAsync(string ticker, CancellationToken ct)
        {
            if (!_mapLoaded)
            {
                try
                {
                    var json = await _http.GetStringAsync("https://www.sec.gov/files/company_tickers.json", ct);
                    foreach (var prop in JObject.Parse(json).Properties())
                    {
                        var t = prop.Value["ticker"]?.ToString();
                        var c = prop.Value["cik_str"];
                        if (!string.IsNullOrEmpty(t) && c != null
                            && int.TryParse(c.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var v))
                            _cik[t!] = v;
                    }
                    _mapLoaded = true;
                }
                catch { return null; }
            }
            return _cik.TryGetValue(ticker, out var cik) ? cik : null;
        }

        private async Task<(string? Name, string? Industry,
                            List<(DateTime, int)> Insider, List<(DateTime, int)> Events)>
            SubmissionsAsync(int cik, CancellationToken ct)
        {
            try
            {
                using var resp = await _http.GetAsync($"https://data.sec.gov/submissions/CIK{cik:D10}.json", ct);
                if (!resp.IsSuccessStatusCode) return (null, null, new(), new());
                var d = JObject.Parse(await resp.Content.ReadAsStringAsync(ct));

                var recent = d["filings"]?["recent"] as JObject;
                var forms = recent?["form"] as JArray;
                var dates = recent?["filingDate"] as JArray;

                var insider = new SortedDictionary<DateTime, int>();
                var events = new SortedDictionary<DateTime, int>();
                if (forms != null && dates != null)
                {
                    for (int i = 0; i < Math.Min(forms.Count, dates.Count); i++)
                    {
                        if (!DateTime.TryParse(dates[i].ToString(), CultureInfo.InvariantCulture,
                                DateTimeStyles.None, out var dt)) continue;
                        string form = forms[i].ToString();
                        var target = form == "4" ? insider : form == "8-K" ? events : null;
                        if (target == null) continue;
                        target.TryGetValue(dt.Date, out var c);
                        target[dt.Date] = c + 1;
                    }
                }

                return (d["name"]?.ToString(), d["sicDescription"]?.ToString(),
                        insider.Select(kv => (kv.Key, kv.Value)).ToList(),
                        events.Select(kv => (kv.Key, kv.Value)).ToList());
            }
            catch { return (null, null, new(), new()); }
        }

        private async Task<List<(DateTime, double)>> ConceptAsync(int cik, string concept, CancellationToken ct)
        {
            try
            {
                using var resp = await _http.GetAsync(
                    $"https://data.sec.gov/api/xbrl/companyconcept/CIK{cik:D10}/us-gaap/{concept}.json", ct);
                // A filer that does not report this concept is ordinary, not an error.
                if (resp.StatusCode == HttpStatusCode.NotFound || !resp.IsSuccessStatusCode) return new();

                var units = JObject.Parse(await resp.Content.ReadAsStringAsync(ct))["units"] as JObject;
                var unit = units?.Properties().FirstOrDefault(p => p.Name == "USD")
                        ?? units?.Properties().FirstOrDefault();
                if (unit?.Value is not JArray facts) return new();

                // One value per filing date, taking the LATEST period covered — a 10-K restates
                // several quarters at once and the current figure is the one that matters.
                var best = new Dictionary<DateTime, (DateTime End, double Val)>();
                foreach (var f in facts)
                {
                    if (!DateTime.TryParse(f["filed"]?.ToString(), CultureInfo.InvariantCulture,
                            DateTimeStyles.None, out var filed)) continue;
                    if (!double.TryParse(f["val"]?.ToString(), NumberStyles.Any,
                            CultureInfo.InvariantCulture, out var val)) continue;
                    DateTime.TryParse(f["end"]?.ToString(), CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out var end);
                    if (!best.TryGetValue(filed.Date, out var cur) || end > cur.End)
                        best[filed.Date] = (end, val);
                }
                return best.OrderBy(kv => kv.Key).Select(kv => (kv.Key, kv.Value.Val)).ToList();
            }
            catch { return new(); }
        }
    }
}
