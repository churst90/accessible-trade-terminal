using System.Globalization;
using System.Threading.Tasks;
using Xunit;

namespace AccessibleTrader.Tests
{
    // ── Vacuity canary ───────────────────────────────────────────────────────
    //
    // Every rerun below relies on one mechanism: a [UseCulture] attribute on a
    // DERIVED class applying to facts INHERITED from the base. If an xUnit
    // update quietly stopped honouring that, all 150 reruns would go green
    // vacuously on any machine whose ambient culture is already invariant
    // (LANG=C — i.e. exactly this box and CI). These facts fail loudly instead.

    // Abstract so the base never runs on its own (the machine's ambient culture is
    // whatever LANG says — en-US here — and pinning an expectation to it would be
    // asserting the weather). Only the [UseCulture] derivation executes.
    public abstract class UseCultureCanaryBase
    {
        protected abstract string ExpectedCulture { get; }

        [Fact]
        public void TheAttributeReachesInheritedFacts()
            => Assert.Equal(ExpectedCulture, CultureInfo.CurrentCulture.Name);

        [Fact]
        public async Task TheCultureFlowsAcrossAwaits()
        {
            await Task.Delay(1).ConfigureAwait(false);
            Assert.Equal(ExpectedCulture, CultureInfo.CurrentCulture.Name);
        }
    }

    [UseCulture("de-DE")]
    public class UseCultureCanaryUnderDeDe : UseCultureCanaryBase
    {
        protected override string ExpectedCulture => "de-DE";
    }

    // ── The de-DE reruns ─────────────────────────────────────────────────────
    //
    // Each class below reruns an existing suite verbatim under de-DE — comma
    // decimal separator, dot group separator, German month names — the locale
    // where double.Parse("50000.5") reads 500005 and $"{50.25}" speaks "50,25".
    //
    // The shipped hosts all pin invariant culture at startup, so in production
    // the ambient culture is never de-DE. These reruns hold the SECOND layer of
    // the culture contract: every spoken string and every provider parse must
    // come out identical even when the ambient culture is hostile, because the
    // sites pin InvariantCulture themselves. A failure here is a site that only
    // works because the host saved it — fix the site, not the test.
    //
    // Subclassing reruns every [Fact]/[Theory] of the base with zero copies to
    // drift. UseCulture is a BeforeAfterTestAttribute, so the culture is set per
    // test on the executing thread (and flows across awaits via
    // ExecutionContext) — never process-globally, which would poison parallel
    // collections.

    // The speech-formatting suite: single-utterance dispatch, template
    // overrides, bar detail, price/quantity precision, chart layout description.
    [UseCulture("de-DE")]
    public class SpeechFormatterDispatchTestsUnderDeDe : SpeechFormatterDispatchTests { }

    [UseCulture("de-DE")]
    public class SpeechTemplateOverrideTestsUnderDeDe : SpeechTemplateOverrideTests { }

    [UseCulture("de-DE")]
    public class BarDetailContextTestsUnderDeDe : BarDetailContextTests { }

    [UseCulture("de-DE")]
    public class PricePrecisionTestsUnderDeDe : PricePrecisionTests { }

    [UseCulture("de-DE")]
    public class ChartLayoutDescriberTestsUnderDeDe : ChartLayoutDescriberTests { }

    [UseCulture("de-DE")]
    public class QuantityFormatterTestsUnderDeDe : QuantityFormatterTests { }

    [UseCulture("de-DE")]
    public class ComponentSpeechKeyTestsUnderDeDe : ComponentSpeechKeyTests { }

    [UseCulture("de-DE")]
    public class ProviderSpeechStrategyTestsUnderDeDe : ProviderSpeechStrategyTests { }

    // Provider parse round-trips: the three providers whose unpinned parse
    // sites were burned down in the 2026-08-23 culture pass (Coinbase 21,
    // Bitstamp 13, Alpaca 7). The whole canned-JSON fetch suite runs again
    // under de-DE, so a reintroduced ambient-culture Parse fails on values,
    // not on a scan. [Collection] is repeated explicitly: nested classes do
    // not inherit the outer class's collection (see the Alpaca note in
    // ProviderFetchOhlcvTests), and inheritance is not guaranteed to carry
    // it either.
    [Collection("ProviderCredentialBridge")]
    [UseCulture("de-DE")]
    public class ProviderFetchBitstampUnderDeDe : ProviderFetchOhlcvTests.Bitstamp { }

    [Collection("ProviderCredentialBridge")]
    [UseCulture("de-DE")]
    public class ProviderFetchCoinbaseUnderDeDe : ProviderFetchOhlcvTests.Coinbase { }

    [Collection("ProviderCredentialBridge")]
    [UseCulture("de-DE")]
    public class ProviderFetchAlpacaUnderDeDe : ProviderFetchOhlcvTests.Alpaca { }
}
