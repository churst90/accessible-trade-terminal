using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.GoBar;
using AccessibleTrader.Core.Services.Input;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Mocks;
using NSubstitute;

namespace AccessibleTrader.Tests;

/// <summary>The Go bar's Renko forms: bare, a fixed box, and ATR with a length.</summary>
public sealed class RenkoGoBarTests
{
    private static readonly DateTime Start = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static WorkspaceState State(int count = 40, double step = 1)
        => WorkspaceState.Initial with
        {
            Identity = new ChartIdentity("Spot", "Bitstamp", "BTC/USD", "1h"),
            Data = new TimeSeriesBuffer<Ohlcv>(Enumerable.Range(0, count)
                .Select(i => new Ohlcv(Start.AddHours(i), 100 + i * step, 101 + i * step, 99 + i * step, 100 + i * step, 10))),
            CurrentDataIndex = count - 1,
            ViewportLength = 30,
            InitStatus = InitializationStatus.Ready,
        };

    [Theory]
    [InlineData("RENKO 500", RenkoBoxMode.Fixed, 500, 14)]
    [InlineData("RENKO 1,000", RenkoBoxMode.Fixed, 1000, 14)]
    [InlineData("RENKO ATR 20", RenkoBoxMode.Atr, 0, 20)]
    [InlineData("RENKO ATR", RenkoBoxMode.Atr, 0, 14)]
    public void The_argument_forms_parse(string typed, RenkoBoxMode mode, double box, int atr)
    {
        Assert.True(GoBarParser.TryParseRenko(typed[6..], out var settings));
        Assert.Equal(mode, settings.Mode);
        Assert.Equal(box, settings.BoxSize);
        Assert.Equal(atr, settings.AtrLength);
    }

    [Theory]
    [InlineData("RENKO")]
    [InlineData("bricks")]
    public void The_bare_forms_are_the_command_itself(string typed)
    {
        var parsed = GoBarParser.Parse(typed);

        Assert.Equal(GoBarParseKind.BareVerb, parsed.Kind);
        Assert.Equal(GoBarVerb.Renko, parsed.Verb);
    }

    [Fact]
    public void A_bad_argument_says_the_one_renko_sentence()
    {
        var parsed = GoBarParser.Parse("RENKO banana");

        Assert.Equal(GoBarParseKind.Invalid, parsed.Kind);
        Assert.Equal("Renko takes a box size, like RENKO 500, or ATR and a length, like RENKO ATR 14.",
            parsed.Refusal);
    }

    [Theory]
    [InlineData("Renko chart", null)]
    [InlineData("RENKO", "RENKO")]
    [InlineData("bricks", "bricks")]
    [InlineData("renko chart", null)]
    public void The_command_is_reachable_by_its_own_words(string typed, string? unused)
    {
        _ = unused;
        var top = GoBarMatcher.Match(typed, GoBarRegistry.All).FirstOrDefault();

        Assert.Equal(SystemCommand.ShowRenkoChart, top?.Entry.Command);
    }

    [Fact]
    public void Brick_settings_is_reachable_by_its_aliases()
    {
        foreach (string typed in new[] { "Brick settings", "renko settings" })
        {
            var top = GoBarMatcher.Match(typed, GoBarRegistry.All).FirstOrDefault();
            Assert.Equal(SystemCommand.ShowBrickSettings, top?.Entry.Command);
        }
    }

    [Fact]
    public void Suggest_RENKO_500_makes_a_fixed_box_action()
    {
        var bus = new EventBus();
        var dispatcher = Substitute.For<ICommandDispatcher>();
        using var stack = new ModalStack(bus);
        var service = new GoBarService(dispatcher, Substitute.For<IShortcutManager>(), bus, stack);

        var suggestion = service.Suggest("RENKO 500");

        var option = Assert.Single(suggestion.Options);
        var action = Assert.IsType<GoBarRenkoAction>(option.Action);
        Assert.Equal(RenkoBoxMode.Fixed, action.Settings.Mode);
        Assert.Equal(500, action.Settings.BoxSize);
    }

    [Fact]
    public void ShowRenko_dispatches_the_settings_then_the_style()
    {
        var bus = new SpyEventBus();
        var store = new MockWorkspaceStore();
        store.EmitState(State());
        var dispatcher = new CommandDispatcher(
            bus, Substitute.For<INavigationEngine>(), store,
            Substitute.For<IBarDetailService>(), new IndicatorCrossingEngine(store, bus));

        dispatcher.ShowRenko(new RenkoSettings(RenkoBoxMode.Fixed, 20));

        Assert.Single(store.DispatchedActions.OfType<SetRenkoSettingsAction>());
        Assert.Single(store.DispatchedActions.OfType<SetChartStyleAction>());
    }
}
