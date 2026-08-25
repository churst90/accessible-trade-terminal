using AccessibleTrader.Core.Services.Accessibility;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// The arrow-key rule for a <c>role="tablist"</c>, tested once so the eight tablists in
    /// the app cannot each get their own subtly different answer.
    ///
    /// <para>
    /// The defect this closes: Settings set a roving <c>tabindex</c> — <c>0</c> on the active
    /// tab, <c>-1</c> on the others — which tells the browser Tab reaches the group and arrows
    /// move within it. No arrow handler existed, so five of six tabs were unreachable by
    /// keyboard: Appearance, the entire Keyboard-rebinding UI, Alerts, License and About.
    /// </para>
    /// </summary>
    public class TablistNavigatorTests
    {
        // Six, like Settings.
        private const int Six = 6;

        [Theory]
        [InlineData("ArrowRight", 0, 1)]
        [InlineData("ArrowRight", 4, 5)]
        [InlineData("ArrowLeft", 3, 2)]
        [InlineData("ArrowLeft", 1, 0)]
        public void ArrowsStepThroughTheTabs(string key, int from, int expected)
        {
            Assert.Equal(expected, TablistNavigator.Target(key, from, Six));
        }

        [Fact]
        public void TheEndsWrapAround()
        {
            // WAI-ARIA specifies wrapping, and it matters more than it looks: a user who
            // cannot see the row has no cue that they have run out of tabs, so a key that
            // does nothing is indistinguishable from a key that is broken.
            Assert.Equal(0, TablistNavigator.Target("ArrowRight", Six - 1, Six));
            Assert.Equal(Six - 1, TablistNavigator.Target("ArrowLeft", 0, Six));
        }

        [Fact]
        public void HomeAndEndJumpToTheEnds()
        {
            Assert.Equal(0, TablistNavigator.Target("Home", 3, Six));
            Assert.Equal(Six - 1, TablistNavigator.Target("End", 3, Six));
        }

        [Fact]
        public void AVerticalTablistUsesUpAndDown()
        {
            Assert.Equal(1, TablistNavigator.Target("ArrowDown", 0, Six, vertical: true));
            Assert.Equal(0, TablistNavigator.Target("ArrowUp", 1, Six, vertical: true));

            // ...and ignores the horizontal pair, so the keys do not fight the page.
            Assert.Null(TablistNavigator.Target("ArrowRight", 0, Six, vertical: true));
            Assert.Null(TablistNavigator.Target("ArrowLeft", 1, Six, vertical: true));
        }

        [Theory]
        [InlineData("Tab")]
        [InlineData("Escape")]
        [InlineData("Enter")]
        [InlineData(" ")]
        [InlineData("a")]
        [InlineData(null)]
        [InlineData("")]
        public void AnyOtherKeyIsNotOurs(string? key)
        {
            // Returning null is what lets the caller leave the event completely alone.
            // Claiming Tab here would trap focus inside the tab row — a worse bug than the
            // one being fixed, and the reason this returns null rather than the current index.
            Assert.Null(TablistNavigator.Target(key, 2, Six));
        }

        [Fact]
        public void AKeyThatWouldNotMoveReportsNothingToDo()
        {
            // Home while already on the first tab, End while already on the last. No move
            // means no focus call and no preventDefault.
            Assert.Null(TablistNavigator.Target("Home", 0, Six));
            Assert.Null(TablistNavigator.Target("End", Six - 1, Six));
        }

        [Fact]
        public void ASingleTabListHasNowhereToGo()
        {
            Assert.Null(TablistNavigator.Target("ArrowRight", 0, 1));
            Assert.Null(TablistNavigator.Target("ArrowLeft", 0, 1));
            Assert.Null(TablistNavigator.Target("Home", 0, 1));
        }

        [Fact]
        public void AnEmptyOrImpossibleListIsHandledRatherThanThrowing()
        {
            Assert.Null(TablistNavigator.Target("ArrowRight", 0, 0));
            Assert.Null(TablistNavigator.Target("ArrowRight", -3, 0));
        }

        [Fact]
        public void AStaleIndexIsClampedInsteadOfWrappingNegatively()
        {
            // If a tab list shrinks while a stale index is held, a raw modulo would go
            // negative and index out of range at the call site.
            Assert.Equal(0, TablistNavigator.Target("ArrowRight", 99, Six));
            Assert.Equal(Six - 2, TablistNavigator.Target("ArrowLeft", 99, Six));
            Assert.Equal(1, TablistNavigator.Target("ArrowRight", -5, Six));
        }

        [Fact]
        public void EveryReachableTargetIsAValidIndex()
        {
            // Structural: whatever the key and wherever the index, the answer is either
            // "not mine" or a position that can actually be indexed. This is the property
            // the call sites rely on when they do tabs[target.Value].
            foreach (int count in new[] { 1, 2, 3, 6, 9 })
            foreach (int current in new[] { -7, -1, 0, 1, count - 1, count, 50 })
            foreach (string key in new[] { "ArrowRight", "ArrowLeft", "Home", "End", "ArrowUp", "ArrowDown" })
            foreach (bool vertical in new[] { false, true })
            {
                int? t = TablistNavigator.Target(key, current, count, vertical);
                if (t == null) continue;

                Assert.InRange(t.Value, 0, count - 1);
            }
        }
    }
}
