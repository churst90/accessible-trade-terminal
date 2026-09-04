using AccessibleTrader.Core.Services;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Tests;

/// <summary>
/// What an indicator instance is CALLED.
///
/// <para>
/// Cody, 2026-09-04: <i>"when I nav to a series it doesn't list the parameters in the name like
/// cipher b reads as 'cipher b 9 12 60 50 14 …' not necessary"</i>. The name was built by joining
/// EVERY parameter value onto the indicator's name, unlabelled. On a one-parameter indicator that
/// is "EMA 20", which is exactly right and is why it was written; on Cipher B it is eight bare
/// numbers a listener cannot map to anything, on the name they hear most often in the app.
/// </para>
///
/// <para>
/// The name's job is to tell two INSTANCES apart, so the suffix now carries only what differs
/// from the indicator's own declared defaults — plus a cap, because "only what differs" is not
/// itself a bound.
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

    [Fact]
    public void AnIndicatorLeftAtItsDefaults_IsJustItsName()
    {
        // Cipher B's shape, and the whole complaint: eight parameters, none of them touched, all
        // eight recited every time the user arrows onto it.
        var meta = Meta("Cipher B",
            ("ChannelLength", 9), ("AverageLength", 12), ("OverBought", 60),
            ("OverSold", -60), ("RsiLength", 14), ("MfiLength", 60),
            ("WtSmoothing", 3), ("DivergenceBars", 5));

        var given = new Dictionary<string, object>
        {
            ["ChannelLength"] = 9, ["AverageLength"] = 12, ["OverBought"] = 60,
            ["OverSold"] = -60, ["RsiLength"] = 14, ["MfiLength"] = 60,
            ["WtSmoothing"] = 3, ["DivergenceBars"] = 5,
        };

        Assert.Equal("Cipher B", IndicatorInstanceName.For(meta, given));
    }

    [Fact]
    public void AChangedParameterIsSTILLSpoken_BecauseThatIsWhatTellsTwoApart()
    {
        // The vacuity guard, and the reason the fix is not "drop the suffix". Two EMAs on one
        // chart are told apart by exactly this, and a name that dropped it would leave the user
        // with two series called "EMA" and no way to know which is which.
        var meta = Meta("EMA", ("Period", 20));

        Assert.Equal("EMA", IndicatorInstanceName.For(meta, new Dictionary<string, object> { ["Period"] = 20 }));
        Assert.Equal("EMA 50", IndicatorInstanceName.For(meta, new Dictionary<string, object> { ["Period"] = 50 }));
    }

    [Theory]
    [InlineData(20)]
    [InlineData(20.0)]
    [InlineData("20")]
    public void ADefaultMatchesWhateverTypeItArrivesAs(object supplied)
    {
        // The two sides come from different places — a default declared as int 20 and a value
        // that came back from a form as "20" or 20.0 are the same setting. A string comparison
        // would call every one of them a change and put the whole list back into the name, which
        // is the defect wearing a different hat.
        var meta = Meta("EMA", ("Period", 20));

        Assert.Equal("EMA", IndicatorInstanceName.For(meta, new Dictionary<string, object> { ["Period"] = supplied }));
    }

    [Fact]
    public void RetuningEverything_CollapsesToACount_RatherThanRecitingItBack()
    {
        // "Only what differs" is not a bound. Someone who retunes an eight-parameter indicator
        // wholesale would otherwise be exactly where they started.
        var meta = Meta("Cipher B",
            ("A", 1), ("B", 2), ("C", 3), ("D", 4), ("E", 5));
        var given = new Dictionary<string, object>
        {
            ["A"] = 9, ["B"] = 8, ["C"] = 7, ["D"] = 6, ["E"] = 5,   // E is left alone
        };

        Assert.Equal("Cipher B, 4 custom parameters", IndicatorInstanceName.For(meta, given));
    }

    [Fact]
    public void ThreeChangesStillReadAsValues()
    {
        // MACD's shape. Three is the boundary and it reads naturally, so it stays on the value
        // side of it.
        var meta = Meta("MACD", ("Fast", 12), ("Slow", 26), ("Signal", 9));
        var given = new Dictionary<string, object> { ["Fast"] = 8, ["Slow"] = 21, ["Signal"] = 5 };

        Assert.Equal("MACD 8 21 5", IndicatorInstanceName.For(meta, given));
    }

    [Fact]
    public void AnUndeclaredParameterIsAlwaysSpoken()
    {
        // The metadata cannot say a parameter is at its default when it does not know the
        // parameter exists. Silently dropping a value that might be the only difference between
        // two instances is the one outcome worse than reciting one too many.
        var meta = Meta("Funding Rate");
        var given = new Dictionary<string, object> { ["Symbol"] = "BTC-USDT-SWAP" };

        Assert.Equal("Funding Rate BTC-USDT-SWAP", IndicatorInstanceName.For(meta, given));
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
        // defaults. Without knowing which are the indicator's own, the only safe reduction is a
        // length one — blunter by necessity, and it says so.
        Assert.Equal("Custom 5 10", IndicatorInstanceName.ForValues("Custom", new[] { "5", "10" }));
        Assert.Equal("Custom, 5 parameters",
            IndicatorInstanceName.ForValues("Custom", new[] { "1", "2", "3", "4", "5" }));
        Assert.Equal("Custom", IndicatorInstanceName.ForValues("Custom", null));
    }
}
