using System.Text.RegularExpressions;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.Analysis;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Mocks;
using NSubstitute;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// <b>Cody, 2026-09-30: "F2 should silence just chart navigation and narration."</b> Narration
    /// is F2's alone (not also Shift+F2's), and new bars are narration. Before that day F2
    /// silenced everything the user asked for ANYWHERE: "Settings dialog opened", a dialog's
    /// confirmations, a refused button's reason. And Shift+F2, not F2, silenced narration.
    /// </summary>
    public class F2ChartOnlyMuteTests
    {
        // ── The rule, as a table ─────────────────────────────────────────────

        private static SpeechFeedbackRouter Router(ISpeechManager speech, bool f2On, bool shiftF2On)
        {
            var store = Substitute.For<IWorkspaceStore>();
            store.State.Returns(WorkspaceState.Initial with
            {
                IsSpeechEnabled = f2On,
                IsEventSpeechEnabled = shiftF2On,
            });
            return new SpeechFeedbackRouter(speech, Substitute.For<ISpeechFormatter>(), store,
                Substitute.For<IAppSettings>());
        }

        /// <summary>
        /// Every channel against every mute. Written out as the policy, not derived from the
        /// router: f2Off/shiftF2Off name the mute that is ENGAGED, and the last column is whether
        /// the user hears it.
        /// </summary>
        [Theory]
        //          channel                    F2 off  Shift+F2 off  heard
        [InlineData(SpeechChannel.Chart,       false,  false,        true)]
        [InlineData(SpeechChannel.Chart,       true,   false,        false)]   // F2 is the chart's mute
        [InlineData(SpeechChannel.Chart,       false,  true,         true)]
        [InlineData(SpeechChannel.Narration,   false,  false,        true)]
        [InlineData(SpeechChannel.Narration,   true,   false,        false)]   // …and narration's
        [InlineData(SpeechChannel.Narration,   false,  true,         true)]    // NOT Shift+F2's any more
        [InlineData(SpeechChannel.Interface,   true,   false,        true)]    // the interface: neither
        [InlineData(SpeechChannel.Interface,   false,  true,         true)]
        [InlineData(SpeechChannel.Interface,   true,   true,         true)]
        [InlineData(SpeechChannel.Event,       true,   false,        true)]    // alerts: not F2's
        [InlineData(SpeechChannel.Event,       false,  true,         false)]   // Shift+F2's
        [InlineData(SpeechChannel.OrderEvent,  true,   true,         true)]    // money breaks through
        [InlineData(SpeechChannel.Critical,    true,   true,         true)]
        public void Each_channel_answers_to_the_mute_the_rule_gives_it(
            SpeechChannel channel, bool f2Off, bool shiftF2Off, bool heard)
        {
            var speech = Substitute.For<ISpeechManager>();
            var router = Router(speech, f2On: !f2Off, shiftF2On: !shiftF2Off);

            router.Speak("something", interrupt: false, channel: channel);

            speech.Received(heard ? 1 : 0).Speak(Arg.Any<string>(), Arg.Any<bool>());
        }

        [Fact]
        public void Narration_keeps_its_priority_so_an_arrow_press_does_not_cut_it_off()
        {
            // Why Narration is a channel of its own rather than Chart: it rode Event before, and
            // Event outranks the chart, so a bar-close sentence was protected from the arrow key
            // that follows it. Moving it under F2 must not lose that.
            var speech = Substitute.For<ISpeechManager>();
            var router = Router(speech, f2On: true, shiftF2On: true);

            router.Speak("1 hour bar closed at 199.50, up 2 percent. Triple confluence buy.",
                interrupt: false, channel: SpeechChannel.Narration);
            router.Speak("Close 201.00", interrupt: true, channel: SpeechChannel.Chart);

            speech.DidNotReceive().Silence();
            speech.Received(1).Speak("Close 201.00", false);
        }

        [Fact]
        public void A_speak_that_names_no_channel_is_the_chart()
        {
            // The default is the conservative one: a publisher that forgot to choose behaves as
            // everything did before the change, muted by F2.
            var speech = Substitute.For<ISpeechManager>();
            Router(speech, f2On: false, shiftF2On: true).Speak("Zoomed to 120 bars");
            speech.DidNotReceive().Speak(Arg.Any<string>(), Arg.Any<bool>());
        }

        // ── Through the coordinator ──────────────────────────────────────────

        private sealed class Harness
        {
            public SpyEventBus Bus { get; } = new();
            public List<string> Spoken { get; } = new();

            public Harness(bool f2On)
            {
                var speech = new CounterSpeechManager { OnSpeak = t => Spoken.Add(t) };
                var store = new MockWorkspaceStore();
                store.EmitState(WorkspaceState.Initial with { IsSpeechEnabled = f2On });
                var formatter = new SpeechFormatter();
                var router = new SpeechFeedbackRouter(speech, formatter, store);
                _ = new AccessibilityFeedbackCoordinator(
                    store, new NavigationFeedbackManager(router, formatter), router,
                    new AudioFeedbackRouter(new MockNavigationSonifier(), new MockEarconService()),
                    formatter, Bus, new MockEarconService(), new SdkCandlePatternAnalyzer(),
                    new ChartPatternCache(new ChartPatternDetector(new SwingStructureAnalyzer())),
                    new ChartPatternFocus(), new MockAutoNarrationService());
            }
        }

        [Fact]
        public void With_F2_off_a_dialog_still_says_what_it_did()
        {
            var h = new Harness(f2On: false);

            h.Bus.Publish(new FeedbackRequestEvent(FeedbackType.StateChange, "Settings saved.",
                Channel: SpeechChannel.Interface));
            h.Bus.Publish(new AnnouncementEvent("Workspace loaded.", Channel: SpeechChannel.Interface));

            Assert.Contains("Settings saved.", h.Spoken);
            Assert.Contains("Workspace loaded.", h.Spoken);
        }

        [Fact]
        public void With_F2_off_the_chart_says_nothing()
        {
            // The partner: without it, a coordinator that spoke everything regardless of the mute
            // would pass the test above.
            var h = new Harness(f2On: false);

            h.Bus.Publish(new FeedbackRequestEvent(FeedbackType.StateChange, "Zoomed to 120 bars."));
            h.Bus.Publish(new AnnouncementEvent("Pane summary."));

            Assert.Empty(h.Spoken);
        }

        // ── Every dialog names its channel ───────────────────────────────────

        /// <summary>
        /// Everything published from the Blazor components is the interface, except the chart
        /// surfaces, which publish only Navigation (chart by definition). A generic message that
        /// names no channel falls to <see cref="SpeechChannel.Chart"/>, which F2 silences. That
        /// fall-back is how "Settings saved" went quiet under F2 for a user who had muted only
        /// the chart, so every such publish names its channel: Interface for the dialogs, or a
        /// deliberate other (the trading dashboard's money messages are OrderEvent).
        /// </summary>
        [Fact]
        public void Every_generic_message_a_component_publishes_names_its_channel()
        {
            var root = Path.Combine(RepoPaths.RepoRoot(), "AccessibleTrader.BlazorClient.Components");
            var call = new Regex(@"new\s+(FeedbackRequestEvent|AnnouncementEvent)\s*\(");
            // The TYPE argument, not just its start: `ok ? FeedbackType.StateChange : FeedbackType.Error`
            // is generic on its success branch, and the first version of this scan (and the edit
            // it guards) missed exactly that shape: three trading-dashboard successes, a settings
            // reset and two imports stayed on the chart's mute. Found by the accessibility review.
            var generic = new Regex(@"FeedbackType\.(StateChange|Info|Boundary|VolumeChange)\b");
            var offenders = new List<string>();
            int examined = 0;

            foreach (var file in Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
                         .Where(f => (f.EndsWith(".razor") || f.EndsWith(".cs"))
                                  && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                                  && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")))
            {
                string src = File.ReadAllText(file);
                foreach (Match m in call.Matches(src))
                {
                    string args = ArgumentsOf(src, m.Index + m.Length - 1);
                    bool isAnnouncement = m.Groups[1].Value == "AnnouncementEvent";
                    if (!isAnnouncement && !generic.IsMatch(TypeArgument(args))) continue;
                    examined++;
                    if (!Regex.IsMatch(args, @"\bChannel\s*:"))
                        offenders.Add($"{Path.GetFileName(file)}:{src[..m.Index].Count(c => c == '\n') + 1}");
                }
            }

            // A component that injects the speech router and calls it directly bypasses the
            // event, so its Speak must name a channel too (the chart and drawing context menus
            // said "Crosshair on", "{name} removed" on the chart's mute until the review).
            foreach (var file in Directory.EnumerateFiles(root, "*.razor", SearchOption.AllDirectories))
            {
                string src = File.ReadAllText(file);
                var inject = Regex.Match(src, @"@inject\s+(?:[\w.]+\.)?ISpeechFeedbackRouter\s+(\w+)");
                if (!inject.Success) continue;
                foreach (Match m in Regex.Matches(src, $@"\b{inject.Groups[1].Value}\.Speak\s*\("))
                {
                    examined++;
                    string args = ArgumentsOf(src, m.Index + m.Length - 1);
                    if (!Regex.IsMatch(args, @"\bchannel\s*:"))
                        offenders.Add($"{Path.GetFileName(file)}:{src[..m.Index].Count(c => c == '\n') + 1} (router Speak)");
                }
            }

            Assert.True(examined >= 60,
                $"Only {examined} component publishes examined; there are about 75. The scan has gone blind.");
            Assert.True(offenders.Count == 0,
                "These publish a message without naming its channel, so F2 (the CHART's mute) "
              + "silences them. Name SpeechChannel.Interface, or another channel on purpose:\n  "
              + string.Join("\n  ", offenders));
        }

        /// <summary>The first argument at nesting depth zero: a FeedbackRequestEvent's Type.</summary>
        private static string TypeArgument(string args)
        {
            int depth = 0;
            for (int i = 0; i < args.Length; i++)
            {
                char c = args[i];
                if (c == '"' || (c == '$' && i + 1 < args.Length && args[i + 1] == '"')) { i = SkipString(args, i); continue; }
                if (c is '(' or '[' or '{') depth++;
                else if (c is ')' or ']' or '}') depth--;
                else if (c == ',' && depth == 0) return args[..i];
            }
            return args;
        }

        /// <summary>The text between the parenthesis at <paramref name="open"/> and its match,
        /// skipping string and char literals, including interpolation holes.</summary>
        private static string ArgumentsOf(string s, int open)
        {
            int depth = 0;
            for (int i = open; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '"' || (c == '$' && i + 1 < s.Length && s[i + 1] == '"'))
                {
                    i = SkipString(s, i);
                    continue;
                }
                if (c == '\'') { i = s.IndexOf('\'', i + (s[i + 1] == '\\' ? 3 : 2)); continue; }
                if (c == '(') depth++;
                else if (c == ')' && --depth == 0) return s[(open + 1)..i];
            }
            throw new InvalidOperationException("unbalanced call at " + open);
        }

        private static int SkipString(string s, int i)
        {
            bool interp = s[i] == '$';
            if (interp) i++;
            i++; // opening quote
            while (true)
            {
                char c = s[i];
                if (c == '\\') { i += 2; continue; }
                if (c == '"') return i;
                if (interp && c == '{')
                {
                    if (s[i + 1] == '{') { i += 2; continue; }
                    int depth = 1; i++;
                    while (depth > 0)
                    {
                        if (s[i] == '"' || (s[i] == '$' && s[i + 1] == '"')) { i = SkipString(s, i) + 1; continue; }
                        if (s[i] == '{') depth++;
                        else if (s[i] == '}') depth--;
                        i++;
                    }
                    continue;
                }
                i++;
            }
        }
    }
}
