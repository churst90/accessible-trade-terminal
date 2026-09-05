using AccessibleTrader.Core.Services;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Tests;

/// <summary>
/// What an indicator instance is CALLED.
///
/// <para>
/// Two reports, one day apart, and the second corrected the fix for the first. Cody,
/// 2026-09-04 (morning): <i>"when I nav to a series it doesn't list the parameters in the name
/// like cipher b reads as 'cipher b 9 12 60 50 14 …' not necessary"</i> — the name joined EVERY
/// parameter value onto the indicator's name. That was fixed by naming only what differed from
/// the declared DEFAULTS. Then, the same day: <i>"I don't like how it says cipher b 11 … the
/// reason I wanted those parameters listed was so I could identify things like EMAs and SMAs on
/// the chart"</i>.
/// </para>
///
/// <para>
/// So the rule these tests pin is the SECOND one, and it is a different question: not "did the
/// user change anything" but "is there anything on this chart it could be confused with". Alone,
/// an indicator is called what it is called — including a Cipher B with a retuned RSI length,
/// because "11" tells a listener nothing when there is nothing to tell it apart from. With
/// siblings, the suffix is what the cohort DISAGREES on, which is the period whether or not
/// either EMA sits at the default.
/// </para>
/// </summary>
public class IndicatorInstanceNameTests
{
    private static IndicatorMetadata Meta(string name, params (string Name, object Default)[] ps)
    {
        var m = new IndicatorMetadata { Code = name.ToUpperInvariant(), Name = name };
        foreach (var (n, d) in ps)
            m.Parameters.Add(new IndicatorParameterMetadata { Name = n, DisplayName = n, DefaultValue = d });
        return m;
    }

    private static IReadOnlyList<IReadOnlyDictionary<string, object>> Siblings(
        params Dictionary<string, object>[] sets) => sets;

    [Fact]
    public void AloneOnTheChart_AnIndicatorIsJustItsName_EvenRetuned()
    {
        // Cipher B's shape, and both halves of the complaint at once: eight parameters recited
        // when none were touched, and a bare "11" recited when one was. Neither says anything —
        // there is one Cipher B on the chart and it is THE Cipher B.
        var meta = Meta("Cipher B",
            ("ChannelLength", 9), ("AverageLength", 12), ("OverBought", 60),
            ("OverSold", -60), ("RsiLength", 14), ("MfiLength", 60),
            ("WtSmoothing", 3), ("DivergenceBars", 5));

        var atDefaults = new Dictionary<string, object>
        {
            ["ChannelLength"] = 9, ["AverageLength"] = 12, ["OverBought"] = 60,
            ["OverSold"] = -60, ["RsiLength"] = 14, ["MfiLength"] = 60,
            ["WtSmoothing"] = 3, ["DivergenceBars"] = 5,
        };
        var retuned = new Dictionary<string, object>(atDefaults) { ["RsiLength"] = 11 };

        Assert.Equal("Cipher B", IndicatorInstanceName.For(meta, atDefaults));
        Assert.Equal("Cipher B", IndicatorInstanceName.For(meta, retuned));
        Assert.Equal("Cipher B", IndicatorInstanceName.For(meta, retuned, Siblings()));
    }

    [Fact]
    public void TwoEmas_AreToldApartByThePeriod_WhicheverSitsAtTheDefault()
    {
        // The case the suffix exists for, and the vacuity guard on the test above: a rule that
        // dropped the suffix outright would leave the user with two series called "EMA".
        //
        // Note the 20 is the DECLARED DEFAULT and is still spoken. That is the whole difference
        // between this rule and the one it replaced: what matters is that the cohort disagrees,
        // not that anybody edited anything.
        var meta = Meta("EMA", ("Period", 20));
        var twenty = new Dictionary<string, object> { ["Period"] = 20 };
        var fifty  = new Dictionary<string, object> { ["Period"] = 50 };

        Assert.Equal("EMA 20", IndicatorInstanceName.For(meta, twenty, Siblings(fifty)));
        Assert.Equal("EMA 50", IndicatorInstanceName.For(meta, fifty,  Siblings(twenty)));
    }

    [Fact]
    public void ASiblingThatNeverStoredTheParameter_IsRunningOnTheDefault()
    {
        // An instance added before a parameter existed, or added without touching the form, has
        // no entry for it — but it is running on the default, so that is the value it must be
        // compared at. Comparing against "absent" would find a difference on every key and blow
        // the name past the cap into an ordinal.
        var meta = Meta("EMA", ("Period", 20));
        var stored = new Dictionary<string, object>();                        // running on 20
        var fifty  = new Dictionary<string, object> { ["Period"] = 50 };

        Assert.Equal("EMA 20", IndicatorInstanceName.For(meta, stored, Siblings(fifty)));
        Assert.Equal("EMA 50", IndicatorInstanceName.For(meta, fifty, Siblings(stored)));
    }

    [Fact]
    public void OnlyTheParametersTheCohortDisagreesOn_AreNamed()
    {
        // Two Cipher Bs differing in ONE of eight. Seven agree and say nothing; the eighth is
        // the entire name, because it is the entire difference.
        var meta = Meta("Cipher B",
            ("ChannelLength", 9), ("AverageLength", 12), ("RsiLength", 14), ("MfiLength", 60));
        var a = new Dictionary<string, object>
            { ["ChannelLength"] = 9, ["AverageLength"] = 12, ["RsiLength"] = 14, ["MfiLength"] = 60 };
        var b = new Dictionary<string, object>(a) { ["RsiLength"] = 11 };

        Assert.Equal("Cipher B 14", IndicatorInstanceName.For(meta, a, Siblings(b)));
        Assert.Equal("Cipher B 11", IndicatorInstanceName.For(meta, b, Siblings(a)));
    }

    [Fact]
    public void DiscriminatingValues_ComeOutInDeclaredOrder_NotDictionaryOrder()
    {
        // "MACD 8 21 5", the way a trader writes it. Dictionary order is an implementation
        // detail of whoever built the parameter set, and the original defect was partly this:
        // eight numbers in an order that matched nothing the user had ever seen.
        var meta = Meta("MACD", ("Fast", 12), ("Slow", 26), ("Signal", 9));
        var mine = new Dictionary<string, object> { ["Signal"] = 5, ["Fast"] = 8, ["Slow"] = 21 };
        var other = new Dictionary<string, object> { ["Fast"] = 12, ["Slow"] = 26, ["Signal"] = 9 };

        Assert.Equal("MACD 8 21 5", IndicatorInstanceName.For(meta, mine, Siblings(other)));
    }

    [Fact]
    public void PastTheCap_TheSuffixIsAnOrdinal_NotAWallOfNumbers()
    {
        // "Only what the cohort disagrees on" is not itself a bound: two instances retuned
        // wholesale would put the recitation straight back. An ordinal is short, is a name
        // rather than a reading, and is still unique — which is the entire job.
        var meta = Meta("Cipher B", ("A", 1), ("B", 2), ("C", 3), ("D", 4), ("E", 5));
        var mine  = new Dictionary<string, object> { ["A"] = 9, ["B"] = 8, ["C"] = 7, ["D"] = 6, ["E"] = 5 };
        var other = new Dictionary<string, object> { ["A"] = 1, ["B"] = 2, ["C"] = 3, ["D"] = 4, ["E"] = 5 };

        Assert.Equal("Cipher B 2", IndicatorInstanceName.For(meta, mine, Siblings(other)));
    }

    [Fact]
    public void TwoInstancesConfiguredIdentically_StillGetDistinctNames()
    {
        // Nothing disagrees, so there is nothing to name — and "EMA" twice is not two names.
        // The ordinal is the honest answer: they really are the same indicator twice.
        var meta = Meta("EMA", ("Period", 20));
        var same = new Dictionary<string, object> { ["Period"] = 20 };

        Assert.Equal("EMA 2", IndicatorInstanceName.For(meta, same, Siblings(same)));
    }

    [Fact]
    public void TheOrdinalIsThePosition_NotTheCohortSize()
    {
        // The nineteenth pass returned siblings + 1 for the ordinal, which for a pair is "2"
        // for BOTH of them — two objects, one name, on exactly the path whose whole purpose is
        // telling them apart. The caller passes each instance's place in the cohort.
        var meta = Meta("EMA", ("Period", 20));
        var same = new Dictionary<string, object> { ["Period"] = 20 };

        Assert.Equal("EMA 1", IndicatorInstanceName.For(meta, same, Siblings(same), ordinal: 1));
        Assert.Equal("EMA 2", IndicatorInstanceName.For(meta, same, Siblings(same), ordinal: 2));
    }

    // ── Named-by parameters: "the 50 EMA" ───────────────────────────────────────

    private static IndicatorMetadata NamedBy(IndicatorMetadata meta, params string[] names)
    {
        meta.NamedByParameters.AddRange(names);
        return meta;
    }

    [Fact]
    public void AnIndicatorNamedByAParameter_SaysItEvenWhenAlone()
    {
        // Cody, 2026-09-05: "Which indicator realistically need the user to know the period?
        // ema 50, ema 21, sma 50, etc." A moving average IS its period to the person who
        // added it; the cohort rule's "alone → bare name" throws that away. The indicator
        // declares which parameter names it, and that value is always spoken.
        var meta = NamedBy(Meta("EMA", ("Period", 20)), "Period");
        var mine = new Dictionary<string, object> { ["Period"] = 50 };

        Assert.Equal("EMA 50", IndicatorInstanceName.For(meta, mine));
    }

    [Fact]
    public void ANamedByParameterAtItsDefault_IsStillSpoken()
    {
        // "Differs from the default" was the eighteenth pass's rule and it is wrong here for
        // the same reason it was wrong for siblings: a 20 EMA left at 20 is still the 20.
        var meta = NamedBy(Meta("EMA", ("Period", 20)), "Period");

        Assert.Equal("EMA 20", IndicatorInstanceName.For(meta, new Dictionary<string, object>()));
    }

    [Fact]
    public void TwoNamedByInstances_ThatDifferInTheNamingParameter_NeedNothingElse()
    {
        // "EMA 21" and "EMA 50" are already apart; the cohort rule has no work to do even if
        // they also differ on something else.
        var meta = NamedBy(Meta("EMA", ("Period", 20), ("Source", "close")), "Period");
        var fast = new Dictionary<string, object> { ["Period"] = 21, ["Source"] = "hl2" };
        var slow = new Dictionary<string, object> { ["Period"] = 50, ["Source"] = "close" };

        Assert.Equal("EMA 21", IndicatorInstanceName.For(meta, fast, Siblings(slow)));
        Assert.Equal("EMA 50", IndicatorInstanceName.For(meta, slow, Siblings(fast)));
    }

    [Fact]
    public void TwoNamedByInstances_SharingTheNamingParameter_FallToTheCohortRule()
    {
        // Two 50 EMAs on different sources: the period cannot tell them apart, so what the
        // cohort disagrees on follows it — and the period stays in front, because it is still
        // the name.
        var meta = NamedBy(Meta("EMA", ("Period", 20), ("Source", "close")), "Period");
        var a = new Dictionary<string, object> { ["Period"] = 50, ["Source"] = "hl2" };
        var b = new Dictionary<string, object> { ["Period"] = 50, ["Source"] = "close" };

        Assert.Equal("EMA 50 hl2", IndicatorInstanceName.For(meta, a, Siblings(b)));
        Assert.Equal("EMA 50 close", IndicatorInstanceName.For(meta, b, Siblings(a)));
    }

    [Fact]
    public void ACloudIsNamedByBothItsPeriods_InDeclaredOrder()
    {
        // "clouds maybe" — a cloud sits between two averages and is called by both, fast
        // first, the way its own two lines would be.
        var meta = NamedBy(Meta("MA Cloud", ("FastPeriod", 9), ("SlowPeriod", 21), ("FastType", "EMA")),
                           "FastPeriod", "SlowPeriod");
        var mine = new Dictionary<string, object> { ["FastPeriod"] = 21, ["SlowPeriod"] = 55 };

        Assert.Equal("MA Cloud 21 55", IndicatorInstanceName.For(meta, mine));
    }

    [Fact]
    public void TheShippedMovingAverages_DeclareTheirPeriodAsTheirName()
    {
        // The rule above is only as good as the declarations. Every single-line moving
        // average the terminal ships is named by its lookback; a new one added without the
        // declaration would be "EMA"-shaped noise again for exactly the family the report
        // named.
        var trend  = new AccessibleTrader.Core.Services.Indicators.SkenderTrendProvider().GetIndicators();
        var volume = new AccessibleTrader.Core.Services.Indicators.SkenderVolumeProvider().GetIndicators();
        var all = trend.Concat(volume).ToList();

        foreach (var code in new[] { "Ema", "Sma", "Wma", "Hma", "Alma", "Dema", "Tema", "Kama", "Zlema", "Smma", "Tma", "Vwma" })
        {
            var meta = Assert.Single(all, m => m.Code == code);
            Assert.Equal(new[] { "lookbackPeriods" }, meta.NamedByParameters);
        }

        var cloud = Assert.Single(new AccessibleTrader.Core.Services.Indicators.MACloudProvider().GetIndicators());
        Assert.Equal(new[] { "FastPeriod", "SlowPeriod" }, cloud.NamedByParameters);
    }

    [Fact]
    public void EveryDeclaredNamingParameter_IsAParameterTheIndicatorActuallyHas()
    {
        // A typo in a declaration would name nothing and fail nowhere — the lookup falls
        // through to "no value" and the suffix silently disappears.
        var providers = new AccessibleTrader.Sdk.Interfaces.IIndicatorProvider[]
        {
            new AccessibleTrader.Core.Services.Indicators.SkenderTrendProvider(),
            new AccessibleTrader.Core.Services.Indicators.SkenderVolumeProvider(),
            new AccessibleTrader.Core.Services.Indicators.MACloudProvider(),
        };
        int declared = 0;
        foreach (var meta in providers.SelectMany(p => p.GetIndicators()))
            foreach (var name in meta.NamedByParameters)
            {
                declared++;
                Assert.Contains(meta.Parameters, p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
            }
        Assert.True(declared >= 12, $"only {declared} naming parameters declared — the sweep found nothing to check");
    }

    [Theory]
    [InlineData(20)]
    [InlineData(20.0)]
    [InlineData("20")]
    public void ADefaultMatchesWhateverTypeItArrivesAs(object supplied)
    {
        // The two sides come from different places — a default declared as int 20 and a value
        // that came back from a form as "20" or 20.0 are the same setting. Compared as strings,
        // every one of them reads as a difference and the sibling comparison finds one where
        // there is none.
        var meta = Meta("EMA", ("Period", 20));
        var mine  = new Dictionary<string, object> { ["Period"] = supplied };
        var other = new Dictionary<string, object> { ["Period"] = 20 };

        // Identical settings, expressed three ways: no discriminator, so the ordinal.
        Assert.Equal("EMA 2", IndicatorInstanceName.For(meta, mine, Siblings(other)));
    }

    [Fact]
    public void AnUndeclaredParameterCanStillBeTheDiscriminator()
    {
        // The metadata not knowing about a parameter is no reason to let two instances share a
        // name because of it. Two funding-rate series on different symbols are told apart by the
        // one thing that differs, declared or not.
        var meta = Meta("Funding Rate");
        var btc = new Dictionary<string, object> { ["Symbol"] = "BTC-USDT-SWAP" };
        var eth = new Dictionary<string, object> { ["Symbol"] = "ETH-USDT-SWAP" };

        Assert.Equal("Funding Rate BTC-USDT-SWAP", IndicatorInstanceName.For(meta, btc, Siblings(eth)));
        Assert.Equal("Funding Rate", IndicatorInstanceName.For(meta, btc));
    }

    [Fact]
    public void WithNoParametersAtAll_TheNameIsUntouched()
    {
        Assert.Equal("VWAP", IndicatorInstanceName.For(Meta("VWAP"), null));
        Assert.Equal("VWAP", IndicatorInstanceName.For(Meta("VWAP"), new Dictionary<string, object>()));
    }

    [Fact]
    public void TheMetadataFreePath_CapsWithoutPretendingToKnowDefaults()
    {
        // Custom indicators and the legacy registration path arrive with values and no declared
        // defaults — so no order to name them in and no default for an absent one. The caller
        // decides whether there is a sibling to be told apart from; all this can add is the cap.
        Assert.Equal("Custom 5 10", IndicatorInstanceName.ForValues("Custom", new[] { "5", "10" }));
        Assert.Equal("Custom, 5 parameters",
            IndicatorInstanceName.ForValues("Custom", new[] { "1", "2", "3", "4", "5" }));
        Assert.Equal("Custom", IndicatorInstanceName.ForValues("Custom", null));
    }
}
