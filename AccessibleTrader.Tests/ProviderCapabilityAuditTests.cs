using AccessibleTrader.StrategyLab;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Guards for <see cref="ProviderCapabilityAudit"/> — the static probe that
    /// compares what each trading provider DECLARES against what it IMPLEMENTS.
    ///
    /// <para>
    /// Two kinds of test here, and the distinction matters. The first kind guards
    /// the <b>instrument</b>: a probe that quietly stops finding anything passes
    /// every "no findings" assertion, so the parsing is tested against known
    /// shapes and the sweep is required to actually reach every provider. The
    /// second kind is a <b>ratchet</b>: today's known mismatches are listed by
    /// name and anything new fails. That allows the audit to ship before every
    /// finding is triaged, without letting the count grow.
    /// </para>
    ///
    /// <para>
    /// The probe's first run reported 17 mismatches and 15 of them were the
    /// probe's own fault — it conflated two different <c>GetOrderBookAsync</c>
    /// methods and treated "reads signal.StopLoss" as proof of bracketing when
    /// two providers read it only to map a standalone stop order. Hence the
    /// emphasis on testing the measurement, not just the result.
    /// </para>
    /// </summary>
    public class ProviderCapabilityAuditTests
    {
        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AccessibleTrader.slnx")))
                dir = dir.Parent;
            Assert.NotNull(dir);
            return dir!.FullName;
        }

        /// <summary>
        /// Mismatches that exist today, each verified by hand against the source.
        /// Listed so that a NEW one fails the build. Remove an entry when it is
        /// fixed — the list only ever shrinks.
        /// </summary>
        private static readonly (string Provider, string Capability)[] KnownMismatches =
        {
            // Empty, and it should stay that way.
            //
            // It briefly held two entries. OandaProvider/TrailingStop was real — the
            // flag was declared and the string "Trail" appeared nowhere else in the
            // file — and the flag has been withdrawn. InteractiveBrokersProvider/L2
            // was the AUDIT's mistake, not the provider's: it required
            // IOrderBookProvider (live streaming) when the panel reads snapshots
            // through a different method of the same name, and L2 is now marked not
            // statically verifiable for that reason.
        };

        // ── The instrument ───────────────────────────────────────────────────

        [Fact]
        public void TheAuditReachesEveryTradingProvider()
        {
            var audits = ProviderCapabilityAudit.Run(RepoRoot());

            // Guard the guard: if the sweep stopped finding providers, every
            // "no findings" assertion below would pass by examining nothing.
            Assert.True(audits.Count >= 10,
                $"The audit found only {audits.Count} trading providers. It is probably not "
              + "reaching them: " + string.Join(", ", audits.Select(a => a.Name)));

            foreach (string expected in new[] { "Binance", "Kraken", "Coinbase", "Alpaca", "Schwab" })
                Assert.Contains(audits, a => a.Name.StartsWith(expected, StringComparison.Ordinal));
        }

        [Fact]
        public void EveryProvidersCapabilityDeclarationParses()
        {
            var unparsed = ProviderCapabilityAudit.Run(RepoRoot())
                .Where(a => !a.CapabilitiesParsed).Select(a => a.Name).ToList();

            Assert.True(unparsed.Count == 0,
                "These providers' capability declarations could not be parsed, so the audit has no "
              + "opinion on them at all — which is NOT the same as them being clean, and is exactly "
              + "the hole a probe must never write silently: " + string.Join(", ", unparsed));
        }

        [Theory]
        // Single-line, multiple flags.
        [InlineData("public override ProviderCapabilities Capabilities => ProviderCapabilities.L2 | ProviderCapabilities.OCO;", "L2,OCO")]
        // Multi-line — Kraken and MEXC both declare across three lines.
        [InlineData("public override ProviderCapabilities Capabilities =>\n  ProviderCapabilities.L2 |\n  ProviderCapabilities.Leverage;", "L2,Leverage")]
        // None must parse as "declares nothing", distinct from "could not parse".
        [InlineData("public override ProviderCapabilities Capabilities => ProviderCapabilities.None;", "")]
        public void CapabilityDeclarationsParseInEveryShapeTheRepoUses(string src, string expected)
        {
            var (flags, parsed) = ProviderCapabilityAudit.ParseCapabilities(src);

            Assert.True(parsed);
            Assert.Equal(
                expected.Split(',', StringSplitOptions.RemoveEmptyEntries).OrderBy(x => x),
                flags.OrderBy(x => x));
        }

        [Fact]
        public void AnAbsentDeclarationIsReportedAsUnparsedNotAsEmpty()
        {
            var (flags, parsed) = ProviderCapabilityAudit.ParseCapabilities("class Foo { }");

            Assert.False(parsed);
            Assert.Empty(flags);
        }

        [Theory]
        [InlineData("public Task<double> SetLeverageAsync(string s, double l) => Task.FromResult(1.0);", true)]
        [InlineData("public Task<List<Position>> GetPositionsAsync() => Task.FromResult(new List<Position>());", true)]
        [InlineData("public async Task<double> SetLeverageAsync(string s, double l) { return await Real(); }", false)]
        public void ConstantReturnStubsAreDistinguishedFromRealImplementations(string src, bool isStub)
        {
            string method = src.Contains("SetLeverage") ? "SetLeverageAsync" : "GetPositionsAsync";

            Assert.Equal(isStub, ProviderCapabilityAudit.IsConstantReturnStub(src, method));
        }

        [Fact]
        public void AnInterfaceIsOnlyCountedFromTheClassDeclaration()
        {
            // A mention in a comment or a parameter type is not implementing it.
            const string real = "public class FooProvider : BaseMarketDataProvider, ITradingProvider, IOcoTradingProvider\n{ }";
            const string mention = "// see IOcoTradingProvider for the pair API\npublic class FooProvider : ITradingProvider\n{ }";

            Assert.True(ProviderCapabilityAudit.ImplementsInterface(real, "IOcoTradingProvider"));
            Assert.False(ProviderCapabilityAudit.ImplementsInterface(mention, "IOcoTradingProvider"));
        }

        [Fact]
        public void TheAuditStillDetectsAKnownStubbedReadPath()
        {
            // Coinbase's GetPositionsAsync returns a constant empty list. If this
            // ever stops being detected, the stub detector has broken rather than
            // the provider having been fixed — check which before deleting this.
            var coinbase = Assert.Single(ProviderCapabilityAudit.Run(RepoRoot()),
                a => a.Name == "CoinbaseProvider");

            Assert.Contains("GetPositionsAsync", coinbase.ReadPathStubs);
        }

        // ── The ratchet ──────────────────────────────────────────────────────

        [Fact]
        public void NoProviderMakesACapabilityClaimTheCodeDoesNotBack()
        {
            var found = ProviderCapabilityAudit.Run(RepoRoot())
                .SelectMany(a => a.Findings)
                .Where(f => f.Verdict is Verdict.DeclaredNotBacked or Verdict.DeclaredPartial
                                      or Verdict.BackedNotDeclared)
                .Select(f => (f.Provider, f.Capability, f.Verdict, f.Detail))
                .ToList();

            var unexpected = found
                .Where(f => !KnownMismatches.Contains((f.Provider, f.Capability)))
                .ToList();

            Assert.True(unexpected.Count == 0,
                "New capability claims that the code does not back. Each one renders a control that "
              + "cannot work, or hides one that could:\n  "
              + string.Join("\n  ", unexpected.Select(f => $"{f.Provider}.{f.Capability}: {f.Verdict} — {f.Detail}")));

            // And the list only shrinks: a fixed entry must be deleted, or it rots
            // into permission for the defect to come back.
            var stale = KnownMismatches
                .Where(k => !found.Any(f => f.Provider == k.Provider && f.Capability == k.Capability))
                .ToList();

            Assert.True(stale.Count == 0,
                "These are listed as known mismatches but the audit no longer reports them. If they "
              + "were fixed, delete them from KnownMismatches; if the audit stopped detecting them, "
              + "that is the real bug:\n  " + string.Join("\n  ", stale.Select(k => $"{k.Provider}.{k.Capability}")));
        }

        [Fact]
        public void CapabilitiesThatCannotBeCheckedStaticallySaySoRatherThanPassing()
        {
            // Brackets, MarketDepth and Shorting each had (or would have had) a
            // check that always passed. A rule that cannot fail is not a rule, and
            // three of the audit's first-run "findings" came from exactly that.
            var unverifiable = ProviderCapabilityAudit.Rules
                .Where(r => r.Groups.Count == 0).Select(r => r.Name).ToList();

            Assert.Contains("Shorting", unverifiable);
            Assert.Contains("MarketDepth", unverifiable);
            Assert.Contains("Brackets", unverifiable);
            Assert.Contains("L2", unverifiable);
            Assert.Contains("FuturesTrading", unverifiable);
            Assert.Contains("MarginTrading", unverifiable);

            foreach (var r in ProviderCapabilityAudit.Rules.Where(r => r.Groups.Count == 0))
                Assert.Contains("NOT STATICALLY VERIFIABLE", r.Why);
        }
    }
}
