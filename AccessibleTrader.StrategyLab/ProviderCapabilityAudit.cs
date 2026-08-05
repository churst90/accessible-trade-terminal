using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace AccessibleTrader.StrategyLab
{
    /// <summary>
    /// **Shut up and measure.** A static audit of what every trading provider
    /// *declares* against what it *implements*, run offline with no credentials.
    ///
    /// <para>
    /// The paper-broker audit of 2026-08-04 showed capability declarations can be
    /// wrong in both directions — declared-and-absent (a leverage selector that
    /// changed nothing) and present-but-undeclared (working trailing stops hidden
    /// because the flag was never set). Only the first kind is findable by reading
    /// what the code claims, which is why this compares claims against evidence.
    /// </para>
    ///
    /// <para>
    /// **Source-based on purpose.** Reflection would need every provider assembly
    /// loaded, and plugin dependency flattening in this repo makes that its own
    /// problem. Reading source works identically from the CLI and from CI, has no
    /// load order, and is debuggable by eye. The cost is parsing, so anything that
    /// cannot be parsed is reported **loudly as unparsed** rather than silently
    /// treated as "declares nothing" — a probe that cannot tell "no" from "I could
    /// not tell" writes holes and calls them data.
    /// </para>
    ///
    /// <para>
    /// **The core check.** A capability is expressed through specific
    /// <c>TradeSignal</c> fields, so a provider that declares it and never reads
    /// those fields cannot be honouring it. That is hard to fake and does not care
    /// how the method is written.
    /// </para>
    /// </summary>
    public static class ProviderCapabilityAudit
    {
        /// <summary>
        /// What each capability is expressed through, and therefore what a provider
        /// must touch for the claim to be credible. <c>SignalFields</c> are matched
        /// as <c>signal.Field</c> reads; <c>Methods</c> must not be constant-return
        /// stubs. A capability with neither is honestly marked unverifiable rather
        /// than quietly passed.
        /// </summary>
        /// <summary>
        /// Evidence is an OR of ANDs: the capability is backed when **any one group**
        /// is fully satisfied, and reported as partial when a group is only partly
        /// satisfied. That shape is needed because a capability can legitimately be
        /// implemented more than one way — Binance's OCO rides a dedicated
        /// <c>IOcoTradingProvider</c> interface rather than the signal's group id,
        /// and an earlier version of this audit called that a defect. It was the
        /// audit that was wrong.
        /// </summary>
        public static readonly IReadOnlyList<CapabilityRule> Rules = new[]
        {
            new CapabilityRule("Leverage", new[]
                {
                    // Order-time leverage and session-level leverage are different
                    // features and either one honours the flag; the report says which.
                    new EvidenceGroup("order-time",    new[] { Ev.Field("Leverage") }),
                    new EvidenceGroup("session-level", new[] { Ev.LiveMethod("SetLeverageAsync") }),
                },
                "a leverage selector that changes nothing is the defect this whole audit started from"),

            new CapabilityRule("TrailingStop", new[]
                {
                    new EvidenceGroup("trailing stop",   new[] { Ev.Field("TrailStopValue") }),
                    new EvidenceGroup("trailing target", new[] { Ev.Field("TrailTpValue") }),
                },
                "the dashboard gates its trailing fields on this flag, so declaring it without "
              + "reading the trail fields renders controls that do nothing"),

            // Demoted to unverifiable after it produced three false findings.
            // Reading signal.StopLoss is NOT evidence of bracketing: Interactive
            // Brokers and Coinbase both read it purely to map a standalone
            // StopMarket order type, which is a different feature. A bracket means
            // protective legs ATTACHED to an entry — either several orders from one
            // signal or a broker-native order class — and no field read distinguishes
            // the two. Brackets are verified behaviourally instead, the way Alpaca's
            // were against a real paper account in 2.2.
            new CapabilityRule("Brackets", Array.Empty<EvidenceGroup>(),
                "NOT STATICALLY VERIFIABLE — reading the stop field is equally consistent with "
              + "mapping a standalone stop order, which is a different capability"),

            new CapabilityRule("OCO", new[]
                {
                    new EvidenceGroup("signal group id",  new[] { Ev.Field("OcoGroupId") }),
                    new EvidenceGroup("native OCO pair",  new[] { Ev.Interface("IOcoTradingProvider") }),
                },
                "without one of these nothing can cancel the sibling leg"),

            new CapabilityRule("L2", new[]
                {
                    // Deliberately the INTERFACE, not the method name. Several providers
                    // override IMarketDataProvider.GetOrderBookAsync (a bid/ask tuple)
                    // without implementing IOrderBookProvider (a real depth snapshot).
                    // They are two different methods sharing a name, and conflating them
                    // produced eight false findings.
                    new EvidenceGroup("depth snapshot", new[] { Ev.Interface("IOrderBookProvider") }),
                },
                "the order book panel and Alt+B read this"),

            // Not statically decidable, said plainly rather than given a check that
            // would always pass and look like verification.
            new CapabilityRule("MarketDepth", Array.Empty<EvidenceGroup>(),
                "NOT STATICALLY VERIFIABLE — 'full depth beyond standard L2' is a difference of "
              + "degree in the same endpoint, with nothing in the source that separates them"),

            new CapabilityRule("Shorting", Array.Empty<EvidenceGroup>(),
                "NOT STATICALLY VERIFIABLE — shorting rides OrderSide, which every provider "
              + "already reads for ordinary sells"),
        };

        /// <summary>Read-path methods whose constant-return stub is indistinguishable from real emptiness.</summary>
        public static readonly IReadOnlyList<string> ReadPaths = new[]
        {
            "GetPositionsAsync", "GetBalancesAsync", "GetOpenOrdersAsync", "GetFillsAsync", "GetOrderBookAsync",
        };

        public static IReadOnlyList<ProviderAudit> Run(string repoRoot)
        {
            string providersDir = Path.Combine(repoRoot, "Plugins", "Providers");
            if (!Directory.Exists(providersDir))
                throw new DirectoryNotFoundException($"Provider plugins not found at {providersDir}");

            var results = new List<ProviderAudit>();

            foreach (string file in Directory
                .EnumerateFiles(providersDir, "*Provider.cs", SearchOption.AllDirectories)
                .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                         && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
                .OrderBy(f => f))
            {
                string src = File.ReadAllText(file);

                // Data-only providers have no trading surface and are not in scope.
                if (!Regex.IsMatch(src, @"\bITradingProvider\b")) continue;

                results.Add(AuditOne(Path.GetFileNameWithoutExtension(file),
                                     Path.GetRelativePath(repoRoot, file), src));
            }

            return results.OrderBy(r => r.Name).ToList();
        }

        private static ProviderAudit AuditOne(string name, string relPath, string src)
        {
            var (declared, parsed) = ParseCapabilities(src);
            bool? margin  = ParseBool(src, "SupportsMarginTrading");
            bool? futures = ParseBool(src, "SupportsFuturesTrading");
            double? maxLev = ParseDouble(src, "MaxLeverage");

            var stubs = ReadPaths.Where(m => IsConstantReturnStub(src, m)).ToList();
            var findings = new List<CapabilityFinding>();

            foreach (var rule in Rules)
            {
                bool isDeclared = declared.Contains(rule.Name);

                if (rule.Groups.Count == 0)   // honestly undecidable from source
                {
                    findings.Add(new CapabilityFinding(name, rule.Name,
                        isDeclared ? Verdict.DeclaredUnverifiable : Verdict.NotDeclared, rule.Why));
                    continue;
                }

                // Evaluate every group; a satisfied group backs the capability, a
                // partly-satisfied one is reported so "half a bracket" is visible.
                var satisfied = new List<string>();
                var partial   = new List<string>();

                foreach (var g in rule.Groups)
                {
                    var held = g.Items.Where(i => Holds(src, i)).ToList();
                    if (held.Count == g.Items.Count) satisfied.Add(g.Name);
                    else if (held.Count > 0)
                        partial.Add($"{g.Name} (has {string.Join(", ", held.Select(i => i.Name))}; "
                                  + $"missing {string.Join(", ", g.Items.Except(held).Select(i => i.Name))})");
                }

                string verdict = (isDeclared, satisfied.Count > 0, partial.Count > 0) switch
                {
                    (true,  true,  _)     => Verdict.Ok,
                    (true,  false, true)  => Verdict.DeclaredPartial,
                    (true,  false, false) => Verdict.DeclaredNotBacked,
                    (false, true,  _)     => Verdict.BackedNotDeclared,
                    _                     => Verdict.NotDeclared,
                };

                string detail = satisfied.Count > 0 ? "via " + string.Join(" + ", satisfied)
                              : partial.Count   > 0 ? string.Join("; ", partial)
                              : "no evidence: " + string.Join(" or ",
                                    rule.Groups.Select(g => string.Join(" and ", g.Items.Select(i => i.Describe()))));

                findings.Add(new CapabilityFinding(name, rule.Name, verdict, detail));
            }

            return new ProviderAudit(name, relPath, declared, parsed, margin, futures, maxLev,
                stubs, Contradictions(name, declared, margin, futures, maxLev), findings);
        }

        /// <summary>
        /// Disagreements a provider has with ITSELF — no external truth needed. MEXC
        /// declares the Leverage flag while saying SupportsMarginTrading is false,
        /// eleven lines apart in one file, and nothing has ever flagged it.
        /// </summary>
        private static List<string> Contradictions(
            string name, ISet<string> declared, bool? margin, bool? futures, double? maxLev)
        {
            var found = new List<string>();
            bool lev = declared.Contains("Leverage");

            if (lev && margin == false && futures == false)
                found.Add("declares Leverage but both SupportsMarginTrading and SupportsFuturesTrading are false — "
                        + "there is no product for the leverage to apply to");
            if (lev && maxLev is <= 1)
                found.Add($"declares Leverage but MaxLeverage is {maxLev} — the selector has one position");
            if (!lev && maxLev is > 1)
                found.Add($"MaxLeverage is {maxLev} but Leverage is not declared — the control stays hidden");
            if (margin == true && !lev)
                found.Add("SupportsMarginTrading is true but Leverage is not declared — margin with no way to set it");
            if (declared.Contains("MarketDepth") && !declared.Contains("L2"))
                found.Add("declares MarketDepth without L2 — depth is the deeper form of the same book");

            return found;
        }

        // ── Source parsing ───────────────────────────────────────────────────

        /// <summary>
        /// The declared flag set, and whether the declaration was found at all. The
        /// bool matters: an unparsed provider must not be reported as declaring
        /// nothing, which would read as a clean result.
        /// </summary>
        internal static (HashSet<string> Flags, bool Parsed) ParseCapabilities(string src)
        {
            var m = Regex.Match(src,
                @"ProviderCapabilities\s+Capabilities\s*=>(?<body>[^;]*);",
                RegexOptions.Singleline);
            if (!m.Success) return (new HashSet<string>(StringComparer.Ordinal), false);

            var flags = Regex.Matches(m.Groups["body"].Value, @"ProviderCapabilities\.(?<f>\w+)")
                .Select(x => x.Groups["f"].Value)
                .Where(f => f != "None")
                .ToHashSet(StringComparer.Ordinal);
            return (flags, true);
        }

        internal static bool? ParseBool(string src, string member)
        {
            var m = Regex.Match(src, $@"\bbool\s+{Regex.Escape(member)}\s*=>\s*(?<v>true|false)\s*;");
            return m.Success ? m.Groups["v"].Value == "true" : null;
        }

        internal static double? ParseDouble(string src, string member)
        {
            var m = Regex.Match(src, $@"\bdouble\s+{Regex.Escape(member)}\s*=>\s*(?<v>[0-9.]+)\s*;");
            return m.Success && double.TryParse(m.Groups["v"].Value,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double d) ? d : null;
        }

        /// <summary>
        /// True when the method is an expression body returning a constant — the
        /// <c>=&gt; Task.FromResult(1.0)</c> / <c>=&gt; Task.FromResult(new List&lt;T&gt;())</c>
        /// shape. That is not a failure the caller can see: it looks exactly like a
        /// real answer, which is what makes it worth naming.
        /// </summary>
        internal static bool IsConstantReturnStub(string src, string method)
        {
            var m = Regex.Match(src,
                $@"\b{Regex.Escape(method)}\s*\([^)]*\)\s*=>\s*(?<body>[^;]*);",
                RegexOptions.Singleline);
            if (!m.Success) return false;

            string body = m.Groups["body"].Value.Trim();
            return Regex.IsMatch(body, @"^Task\.FromResult\s*\(\s*(new\s+List<[^>]+>\s*\(\s*\)|[0-9.]+|null|true|false|string\.Empty|"""")\s*\)$")
                || Regex.IsMatch(body, @"^Task\.CompletedTask$");
        }

        /// <summary>
        /// Whether the provider ever reads this <c>TradeSignal</c> field. Every
        /// provider names the parameter <c>signal</c> (checked, not assumed), so
        /// matching that one name exactly beats a looser alias pattern that could
        /// match an unrelated local.
        /// </summary>
        internal static bool ReadsSignalField(string src, string field) =>
            Regex.IsMatch(src, $@"\bsignal\s*\.\s*{Regex.Escape(field)}\b");

        /// <summary>
        /// Whether the provider's type declaration names this interface. Read from
        /// the class declaration specifically, so a mention in a comment or a
        /// parameter type does not count as implementing it.
        /// </summary>
        internal static bool ImplementsInterface(string src, string iface)
        {
            var m = Regex.Match(src, @"class\s+\w*Provider\b[^{]*", RegexOptions.Singleline);
            return m.Success && Regex.IsMatch(m.Value, $@"\b{Regex.Escape(iface)}\b");
        }

        private static bool Holds(string src, Evidence e) => e.Kind switch
        {
            "field"  => ReadsSignalField(src, e.Name),
            "iface"  => ImplementsInterface(src, e.Name),
            // A method that is absent OR a constant-return stub is not evidence of
            // anything. Absence matters as much as stubbing here.
            "method" => Regex.IsMatch(src, $@"\b{Regex.Escape(e.Name)}\s*\(") && !IsConstantReturnStub(src, e.Name),
            _        => false,
        };

        // ── Reporting ────────────────────────────────────────────────────────

        public static string ToMarkdown(IReadOnlyList<ProviderAudit> audits)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Provider capability audit — declared versus implemented");
            sb.AppendLine();
            sb.AppendLine("Generated by `StrategyLab capabilities`. **Static and offline**: this says what the *code*");
            sb.AppendLine("does, not what a venue's API offers or what a particular account is eligible for —");
            sb.AppendLine("those need the live probe and real keys.");
            sb.AppendLine();

            var unparsed = audits.Where(a => !a.CapabilitiesParsed).ToList();
            if (unparsed.Count > 0)
            {
                sb.AppendLine("## ⚠ Could not be parsed");
                sb.AppendLine();
                sb.AppendLine("Reported rather than treated as \"declares nothing\", which would read as a clean result.");
                sb.AppendLine();
                foreach (var a in unparsed) sb.AppendLine($"- **{a.Name}** (`{a.SourcePath}`)");
                sb.AppendLine();
            }

            sb.AppendLine("## Declared capabilities");
            sb.AppendLine();
            sb.AppendLine("| Provider | Declared | Margin | Futures | MaxLeverage |");
            sb.AppendLine("|---|---|---|---|---|");
            foreach (var a in audits)
                sb.AppendLine($"| {a.Name} | {(a.Declared.Count == 0 ? "—" : string.Join(", ", a.Declared.OrderBy(x => x)))} "
                            + $"| {Show(a.SupportsMarginTrading)} | {Show(a.SupportsFuturesTrading)} | {(a.MaxLeverage?.ToString() ?? "—")} |");
            sb.AppendLine();

            var mismatches = audits.SelectMany(a => a.Findings)
                .Where(f => f.Verdict is Verdict.DeclaredNotBacked or Verdict.BackedNotDeclared
                                      or Verdict.DeclaredPartial)
                .ToList();

            sb.AppendLine("## Claims that do not match the code");
            sb.AppendLine();
            if (mismatches.Count == 0) sb.AppendLine("None.");
            else
            {
                sb.AppendLine("| Provider | Capability | Verdict | Evidence |");
                sb.AppendLine("|---|---|---|---|");
                foreach (var f in mismatches.OrderBy(f => f.Verdict).ThenBy(f => f.Provider))
                    sb.AppendLine($"| {f.Provider} | {f.Capability} | **{f.Verdict}** | {f.Detail} |");
            }
            sb.AppendLine();

            var contra = audits.Where(a => a.Contradictions.Count > 0).ToList();
            sb.AppendLine("## Providers that contradict themselves");
            sb.AppendLine();
            sb.AppendLine("No external truth needed — these are disagreements inside one file.");
            sb.AppendLine();
            if (contra.Count == 0) sb.AppendLine("None.");
            foreach (var a in contra)
            {
                sb.AppendLine($"**{a.Name}**");
                foreach (string c in a.Contradictions) sb.AppendLine($"- {c}");
                sb.AppendLine();
            }

            var stubbed = audits.Where(a => a.ReadPathStubs.Count > 0).ToList();
            sb.AppendLine("## Read paths that return a constant");
            sb.AppendLine();
            sb.AppendLine("An empty list is indistinguishable from \"you have none\". These need a");
            sb.AppendLine("not-supported signal the UI can say out loud.");
            sb.AppendLine();
            if (stubbed.Count == 0) sb.AppendLine("None.");
            else
            {
                sb.AppendLine("| Provider | Stubbed |");
                sb.AppendLine("|---|---|");
                foreach (var a in stubbed)
                    sb.AppendLine($"| {a.Name} | {string.Join(", ", a.ReadPathStubs)} |");
            }
            sb.AppendLine();

            sb.AppendLine("## Not statically verifiable");
            sb.AppendLine();
            sb.AppendLine("Named rather than given a check that would always pass and look like verification.");
            sb.AppendLine();
            foreach (var r in Rules.Where(r => r.Groups.Count == 0))
                sb.AppendLine($"- **{r.Name}** — {r.Why}");

            return sb.ToString();
        }

        private static string Show(bool? b) => b is null ? "—" : b.Value ? "yes" : "no";
    }

    /// <summary>One piece of evidence that a capability is really implemented.</summary>
    public sealed record Evidence(string Kind, string Name)
    {
        public string Describe() => Kind switch
        {
            "field"  => $"reads signal.{Name}",
            "iface"  => $"implements {Name}",
            "method" => $"{Name} is not a stub",
            _        => Name,
        };
    }

    public static class Ev
    {
        /// <summary>The provider reads this <c>TradeSignal</c> field.</summary>
        public static Evidence Field(string f) => new("field", f);
        /// <summary>The provider implements this capability interface.</summary>
        public static Evidence Interface(string i) => new("iface", i);
        /// <summary>The method exists and is not a constant-return stub.</summary>
        public static Evidence LiveMethod(string m) => new("method", m);
    }

    /// <summary>Evidence that must ALL hold for this group to back the capability.</summary>
    public sealed record EvidenceGroup(string Name, IReadOnlyList<Evidence> Items);

    public sealed record CapabilityRule(string Name, IReadOnlyList<EvidenceGroup> Groups, string Why);

    public static class Verdict
    {
        public const string Ok                    = "backed";
        public const string DeclaredNotBacked     = "DECLARED, NOT BACKED";
        public const string DeclaredPartial       = "DECLARED, PARTIAL";
        public const string BackedNotDeclared     = "BACKED, NOT DECLARED";
        public const string NotDeclared           = "not declared";
        public const string DeclaredUnverifiable  = "declared, unverifiable";
    }

    public sealed record CapabilityFinding(string Provider, string Capability, string Verdict, string Detail);

    public sealed record ProviderAudit(
        string Name,
        string SourcePath,
        HashSet<string> Declared,
        bool CapabilitiesParsed,
        bool? SupportsMarginTrading,
        bool? SupportsFuturesTrading,
        double? MaxLeverage,
        IReadOnlyList<string> ReadPathStubs,
        IReadOnlyList<string> Contradictions,
        IReadOnlyList<CapabilityFinding> Findings);
}
