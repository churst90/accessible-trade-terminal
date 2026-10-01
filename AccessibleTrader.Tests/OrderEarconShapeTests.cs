using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using NSubstitute;

namespace AccessibleTrader.Tests;

/// <summary>
/// The order earcons are the one place money is told apart by SOUND alone — a fill can arrive
/// while the screen reader is mid-sentence, and the earcon is what plays first. So the shapes are
/// the contract: a buy fill rises and a sell fill falls; a stop loss descends and a take profit
/// climbs. The exact notes are a sound designer's to change; the direction is not.
///
/// <para>
/// A2q (2026-10-01): no test told a buy fill from a sell fill or a stop from a take profit. The
/// campaign swapped both pairs and both survived — a stop loss that sounds like a win.
/// </para>
/// </summary>
public sealed class OrderEarconShapeTests
{
    private static (EarconService Earcons, List<double> Notes) Build()
    {
        var notes = new List<double>();
        var device = Substitute.For<ISonificationManager>();
        device.When(d => d.PlayNote(Arg.Any<double>(), Arg.Any<double>(), Arg.Any<string>(),
                Arg.Any<float>(), Arg.Any<float>(), Arg.Any<double>(), Arg.Any<bool>()))
            .Do(c => notes.Add(c.ArgAt<double>(0)));
        var lib = Substitute.For<ISoundPatchLibrary>();
        lib.EarconOverrides.Returns(new EarconSettings());
        return (new EarconService(device, lib), notes);
    }

    /// <summary>Last note against first: positive rises, negative falls.</summary>
    private static double Direction(List<double> notes)
    {
        Assert.True(notes.Count >= 2, $"expected a motif of several notes, got {notes.Count}");
        return notes[^1] - notes[0];
    }

    [Fact]
    public void A_buy_fill_rises()
    {
        var (e, notes) = Build();
        e.PlayOrderFill(OrderSide.Buy);
        Assert.True(Direction(notes) > 0, $"buy fill notes: {string.Join(", ", notes)}");
    }

    [Fact]
    public void A_sell_fill_falls()
    {
        var (e, notes) = Build();
        e.PlayOrderFill(OrderSide.Sell);
        Assert.True(Direction(notes) < 0, $"sell fill notes: {string.Join(", ", notes)}");
    }

    [Fact]
    public void A_buy_fill_and_a_sell_fill_sit_in_different_registers()
    {
        // Direction alone is half the cue; the other half is where it sits. A buy that only
        // differs from a sell by its last interval is lost under speech.
        var (b, buy) = Build();
        b.PlayOrderFill(OrderSide.Buy);
        var (s, sell) = Build();
        s.PlayOrderFill(OrderSide.Sell);
        Assert.True(buy.Min() > sell.Max(),
            $"buy [{string.Join(", ", buy)}] overlaps sell [{string.Join(", ", sell)}]");
    }

    [Fact]
    public void A_stop_loss_descends()
    {
        var (e, notes) = Build();
        e.PlayStopHit();
        Assert.True(Direction(notes) < 0, $"stop-loss notes: {string.Join(", ", notes)}");
    }

    [Fact]
    public void A_take_profit_climbs()
    {
        var (e, notes) = Build();
        e.PlayTakeProfitHit();
        Assert.True(Direction(notes) > 0, $"take-profit notes: {string.Join(", ", notes)}");
    }
}
