using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Newtonsoft.Json.Linq;

namespace AccessibleTrader.Core.Services.Headlines
{
    /// <summary>
    /// One SEC filing as the headline notices and the dossier use it. <see cref="AcceptedUtc"/> is
    /// EDGAR's acceptance time — the moment the filing became public, to the second — not the
    /// filing date. <see cref="Items"/> are the 8-K item numbers ("2.02", "9.01"); empty for every
    /// other form. <see cref="Accession"/> is EDGAR's own id and is what "already said" is keyed on.
    /// </summary>
    public sealed record EdgarFiling(
        string Ticker,
        string Company,
        string Form,
        IReadOnlyList<string> Items,
        DateTime AcceptedUtc,
        string Accession,
        string Url);

    /// <summary>
    /// The two EDGAR shapes a filing list arrives in, parsed without any network so both are
    /// pinned by fixtures captured from the live service.
    ///
    /// <para>
    /// <b>The Atom feed</b> (<c>browse-edgar?action=getcompany&amp;output=atom</c>) is what the live
    /// notices poll: twenty newest filings in about 10 KB, against 160 KB for the submissions
    /// JSON, and the host's HTTP client asks for no compression. Its <c>&lt;updated&gt;</c> is the
    /// acceptance time with its New York offset; the 8-K items are only in words
    /// (<c>items 8.01 and 9.01</c>), so they are read back out as numbers.
    /// </para>
    ///
    /// <para>
    /// <b>The submissions JSON</b> is what the dossier already fetches for its filing counts, so
    /// its recent-filings list costs no extra request. Its <c>acceptanceDateTime</c> is UTC
    /// (checked 2026-11-20: Apple's 2.02 results are stamped 20:30Z in summer and 21:30Z in
    /// winter, i.e. 4:30 PM New York both times).
    /// </para>
    /// </summary>
    public static class EdgarFilingParser
    {
        private static readonly Regex ItemNumber = new(@"\b\d{1,2}\.\d{2}\b", RegexOptions.CultureInvariant);

        /// <summary>The filings in an Atom feed, newest first as EDGAR lists them. A malformed
        /// feed or entry is skipped, never thrown.</summary>
        public static IReadOnlyList<EdgarFiling> ParseAtom(string xml, string ticker)
        {
            XDocument doc;
            try { doc = XDocument.Parse(xml); }
            catch { return Array.Empty<EdgarFiling>(); }

            XNamespace atom = "http://www.w3.org/2005/Atom";
            var root = doc.Root;
            if (root == null) return Array.Empty<EdgarFiling>();

            string company = root.Element(atom + "company-info")?.Element(atom + "conformed-name")?.Value?.Trim()
                ?? "";

            var list = new List<EdgarFiling>();
            foreach (var entry in root.Elements(atom + "entry"))
            {
                var content = entry.Element(atom + "content");
                string form = (content?.Element(atom + "filing-type")?.Value
                    ?? entry.Element(atom + "category")?.Attribute("term")?.Value
                    ?? "").Trim();
                string accession = (content?.Element(atom + "accession-number")?.Value ?? "").Trim();
                string updated = (entry.Element(atom + "updated")?.Value ?? "").Trim();
                if (form.Length == 0 || accession.Length == 0) continue;
                if (!DateTimeOffset.TryParse(updated, CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out var accepted)) continue;

                string itemsDesc = content?.Element(atom + "items-desc")?.Value ?? "";
                var items = ItemNumber.Matches(itemsDesc).Select(m => m.Value).Distinct().ToList();
                string url = (content?.Element(atom + "filing-href")?.Value
                    ?? entry.Element(atom + "link")?.Attribute("href")?.Value
                    ?? "").Trim();

                list.Add(new EdgarFiling(ticker, company, form, items, accepted.UtcDateTime, accession, url));
            }
            return list;
        }

        /// <summary>The newest <paramref name="max"/> filings in a submissions document's
        /// <c>filings.recent</c> block. Rows that will not parse are skipped.</summary>
        public static IReadOnlyList<EdgarFiling> ParseSubmissions(JObject doc, string ticker, int max)
        {
            var list = new List<EdgarFiling>();
            if (doc["filings"]?["recent"] is not JObject recent) return list;
            var forms = recent["form"] as JArray;
            var accepted = recent["acceptanceDateTime"] as JArray;
            var accessions = recent["accessionNumber"] as JArray;
            var items = recent["items"] as JArray;
            var primary = recent["primaryDocument"] as JArray;
            if (forms == null || accepted == null || accessions == null) return list;

            string company = doc["name"]?.ToString() ?? "";
            string cik = doc["cik"]?.ToString()?.TrimStart('0') ?? "";
            int n = Math.Min(forms.Count, Math.Min(accepted.Count, accessions.Count));
            for (int i = 0; i < n && list.Count < max; i++)
            {
                if (!DateTimeOffset.TryParse(accepted[i]?.ToString(), CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal, out var at)) continue;
                string form = forms[i]?.ToString().Trim() ?? "";
                string accession = accessions[i]?.ToString().Trim() ?? "";
                if (form.Length == 0 || accession.Length == 0) continue;

                string itemText = items != null && i < items.Count ? items[i]?.ToString() ?? "" : "";
                var itemList = ItemNumber.Matches(itemText).Select(m => m.Value).Distinct().ToList();
                string doc0 = primary != null && i < primary.Count ? primary[i]?.ToString() ?? "" : "";
                string url = cik.Length == 0 ? ""
                    : $"https://www.sec.gov/Archives/edgar/data/{cik}/{accession.Replace("-", "")}/"
                      + (doc0.Length > 0 ? doc0 : accession + "-index.htm");

                list.Add(new EdgarFiling(ticker, company, form, itemList, at.UtcDateTime, accession, url));
            }
            return list;
        }
    }
}
