using System;
using System.Collections.Generic;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Input;
using NSubstitute;
using Xunit;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// The keyboard entry point of an audio-first app: KeyNormalizationService (the single
    /// spelling both bindings and incoming keys are folded through — if the two sides ever
    /// diverge, bindings quietly become unreachable) and InputRouter (raw platform key event
    /// → normalize → shortcut lookup → dispatch).
    /// </summary>
    public class InputRoutingTests
    {
        // ── KeyNormalizationService ─────────────────────────────────────────────

        [Theory]
        // Arrows across platforms (browser, Android DPAD, legacy spellings)
        [InlineData("ArrowLeft", "LEFT")]
        [InlineData("DPADRIGHT", "RIGHT")]
        [InlineData("UpArrow", "UP")]
        [InlineData("arrowdown", "DOWN")]
        // Misc aliases
        [InlineData("Esc", "ESCAPE")]
        [InlineData("Del", "DELETE")]
        [InlineData("Ins", "INSERT")]
        [InlineData("Back", "BACKSPACE")]
        [InlineData("PAGE_UP", "PAGEUP")]
        [InlineData("Prior", "PAGEUP")]
        [InlineData("MOVE_HOME", "HOME")]
        // OEM symbol names
        [InlineData("OEM_MINUS", "-")]
        [InlineData("OEM_4", "[")]
        [InlineData("OEM_3", "`")]
        public void Normalize_FoldsPlatformSpellings_ToTheSemanticStandard(string raw, string expected)
        {
            Assert.Equal(expected, new KeyNormalizationService().Normalize(raw));
        }

        [Theory]
        [InlineData("Enter")]
        [InlineData("ENTER")]
        [InlineData("enter")]
        public void Normalize_EnterBecomesReturn_InAnyCase(string raw)
        {
            // The regression this map guards: bindings declared "ENTER" were compared
            // against incoming "RETURN", so Shift+Enter / Ctrl+Enter (the quick-trade
            // keys) matched nothing.
            Assert.Equal("RETURN", new KeyNormalizationService().Normalize(raw));
        }

        [Theory]
        // A browser reports event.key as the produced character: Shift+1 arrives as "!".
        // Without this fold, Ctrl+Alt+Shift+1/2/3 (arm a quick trade) silently did nothing.
        [InlineData("!", "1")]
        [InlineData("@", "2")]
        [InlineData("#", "3")]
        [InlineData(")", "0")]
        // The layout-independent event.code forms resolve identically.
        [InlineData("Digit1", "1")]
        [InlineData("Numpad7", "7")]
        public void Normalize_ShiftedDigitsAndDigitCodes_ReachTheSameBinding(string raw, string expected)
        {
            Assert.Equal(expected, new KeyNormalizationService().Normalize(raw));
        }

        [Theory]
        [InlineData("KEY_A", "A")]   // Android KEY_ prefix
        [InlineData("KeyA", "A")]    // browser event.code
        [InlineData("KEY_LEFT", "LEFT")]
        [InlineData("q", "Q")]       // unknown keys pass through uppercased
        [InlineData(" F5 ", "F5")]   // trimmed
        public void Normalize_StripsPlatformPrefixes_AndUppercases(string raw, string expected)
        {
            Assert.Equal(expected, new KeyNormalizationService().Normalize(raw));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void Normalize_BlankInput_YieldsEmpty(string? raw)
        {
            Assert.Equal(string.Empty, new KeyNormalizationService().Normalize(raw!));
        }

        [Fact]
        public void StaticNormalizeKey_IsTheSameFunctionAsTheInstanceSide()
        {
            // ShortcutManager normalizes BINDING keys through the static entry point and
            // InputRouter normalizes INCOMING keys through the instance one. They must stay
            // the same function or a binding spelling can quietly become unreachable.
            var svc = new KeyNormalizationService();
            foreach (var key in new[] { "Enter", "ArrowLeft", "!", "OEM_4", "KeyA", "Esc", "unknown-key" })
                Assert.Equal(KeyNormalizationService.NormalizeKey(key), svc.Normalize(key));
        }

        // ── InputRouter ─────────────────────────────────────────────────────────

        private sealed class FakeInputService : IInputService
        {
            public event Action<string, bool, bool, bool>? KeyPressed;
            public event Action<double, double, string, double, double>? MouseEvent
            { add { } remove { } }

            public void ProcessKey(string key, bool shift, bool ctrl, bool alt)
                => KeyPressed?.Invoke(key, shift, ctrl, alt);
            public void ProcessMouse(double x, double y, string type, double width, double height) { }
            public bool HasSubscribers => KeyPressed != null;
        }

        private static (InputRouter router, FakeInputService input, IShortcutManager shortcuts,
                        ICommandDispatcher dispatcher) Build()
        {
            var input = new FakeInputService();
            var shortcuts = Substitute.For<IShortcutManager>();
            var dispatcher = Substitute.For<ICommandDispatcher>();
            var router = new InputRouter(input, shortcuts, dispatcher, new KeyNormalizationService());
            return (router, input, shortcuts, dispatcher);
        }

        [Fact]
        public void KeyEvent_IsNormalized_BeforeTheShortcutLookup()
        {
            var (_, input, shortcuts, dispatcher) = Build();
            shortcuts.GetCommand("LEFT", false, false, false).Returns(SystemCommand.None);

            input.ProcessKey("ArrowLeft", false, false, false);

            // The lookup must see the semantic spelling, not the platform one.
            shortcuts.Received(1).GetCommand("LEFT", false, false, false);
            shortcuts.DidNotReceive().GetCommand("ARROWLEFT", Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<bool>());
        }

        [Fact]
        public void ResolvedCommand_IsDispatched_WithModifiersPassedThrough()
        {
            var (_, input, shortcuts, dispatcher) = Build();
            shortcuts.GetCommand("RETURN", true, true, false).Returns(SystemCommand.OpenSettings);

            input.ProcessKey("Enter", shift: true, ctrl: true, alt: false);

            dispatcher.Received(1).Dispatch(SystemCommand.OpenSettings);
        }

        [Fact]
        public void UnboundKey_DispatchesNothing()
        {
            var (_, input, shortcuts, dispatcher) = Build();
            shortcuts.GetCommand(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<bool>())
                     .Returns(SystemCommand.None);

            input.ProcessKey("Q", false, false, false);

            dispatcher.DidNotReceive().Dispatch(Arg.Any<SystemCommand>());
        }

        [Fact]
        public void Dispose_Unsubscribes_SoALeakedRouterCannotDoubleDispatch()
        {
            var (router, input, shortcuts, dispatcher) = Build();
            shortcuts.GetCommand(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<bool>())
                     .Returns(SystemCommand.OpenSettings);

            router.Dispose();
            Assert.False(input.HasSubscribers);

            input.ProcessKey("Enter", false, false, false);
            dispatcher.DidNotReceive().Dispatch(Arg.Any<SystemCommand>());
        }
    }
}
