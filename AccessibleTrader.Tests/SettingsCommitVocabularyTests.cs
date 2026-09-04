using System.Text.RegularExpressions;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// One commit idiom in the Settings dialog: a control is either STAGED — held in a field and
    /// written by Save — or a LIVE PREVIEW, which Cancel puts back. There is no third kind.
    ///
    /// <para>
    /// Before 2026-09-04 there was. Fifteen controls called <c>App.Save()</c> from their own
    /// <c>@onchange</c> handler, so the dialog's own Cancel could not take them back: background
    /// monitoring, the poll interval, live background tabs, resume-session, sound theme, magnet
    /// snap, market-structure default, touch-nav mode, hover sonification, speech output mode,
    /// and the appearance group. Three more — "Speech enabled", "Sonification enabled" and the
    /// panning step — dispatched straight into the workspace store from the markup. The dialog
    /// said Save and Cancel and meant it for about half its controls (Cody, 2026-09-04: "for
    /// things like text field, check boxes, radio buttons, those things should save on pressing
    /// the save button").
    /// </para>
    ///
    /// <para>
    /// This is a PATH check, not a presence check: it reads each change handler's body and asks
    /// whether that body commits, which is the question. A scan that merely counted
    /// <c>App.Save()</c> occurrences in the file would pass on the broken version too, because
    /// <see cref="Save"/> itself contains one.
    /// </para>
    /// </summary>
    public class SettingsCommitVocabularyTests
    {
        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AccessibleTrader.slnx")))
                dir = dir.Parent;
            Assert.NotNull(dir);
            return dir!.FullName;
        }

        private static string Source() =>
            File.ReadAllText(Path.Combine(RepoRoot(),
                "AccessibleTrader.BlazorClient.Components", "SettingsModal.razor"));

        /// <summary>
        /// The live-preview controls, and the whole list of them. Each applies its effect the
        /// moment it changes because a visual setting you cannot see while deciding is not a
        /// setting you can judge — and each is undone by <c>RevertPreviews</c>.
        /// </summary>
        private static readonly string[] PreviewHandlers =
        {
            "OnThemeChanged",
            "OnPatternVisualsChanged",
            "OnVisualEarconsChanged",
            "OnColorVisionChanged",
            "OnUiScaleChanged",
            "OnHollowCandlesChanged",
        };

        /// <summary>
        /// Extracts a method body by brace-matching from its signature. Regex cannot do this —
        /// a body containing a nested block or a string with a brace in it ends the match early —
        /// and a truncated body is a body with the offending call scanned out of it.
        /// </summary>
        private static string MethodBody(string src, string name)
        {
            var m = Regex.Match(src, @"(?:private|protected)[^\n]*\b" + Regex.Escape(name) + @"\s*\(");
            Assert.True(m.Success, $"No method named {name} in SettingsModal.razor — the scan is aimed at a method that no longer exists.");

            int i = src.IndexOf('{', m.Index);
            int arrow = src.IndexOf("=>", m.Index, StringComparison.Ordinal);
            int semi = src.IndexOf(';', m.Index);

            // Expression-bodied member: everything up to the terminating semicolon.
            if (arrow >= 0 && (i < 0 || arrow < i) && semi > arrow)
                return src[m.Index..semi];

            Assert.True(i >= 0, $"{name} has no body.");
            int depth = 0;
            for (int j = i; j < src.Length; j++)
            {
                if (src[j] == '{') depth++;
                else if (src[j] == '}' && --depth == 0) return src[i..(j + 1)];
            }
            Assert.Fail($"{name}'s body is unterminated.");
            return "";
        }

        private static IEnumerable<string> ChangeHandlerNames(string src) =>
            Regex.Matches(src, @"(?:private|protected)[^\n]*\b(On\w+Changed)\s*\(")
                 .Select(m => m.Groups[1].Value)
                 .Distinct();

        // ── The rule ─────────────────────────────────────────────────────────────

        /// <summary>
        /// No change handler commits, except the declared previews. The exemption is by NAME, so
        /// a new handler is covered by saying nothing — the safe default is the enforced one.
        /// </summary>
        [Fact]
        public void No_change_handler_persists_except_the_declared_previews()
        {
            string src = Source();
            var handlers = ChangeHandlerNames(src).ToList();
            Assert.True(handlers.Count >= 10,
                $"Only {handlers.Count} change handlers found; the scan is not reading the file it thinks it is.");

            var offenders = new List<string>();
            foreach (var name in handlers)
            {
                if (PreviewHandlers.Contains(name)) continue;
                string body = MethodBody(src, name);
                if (body.Contains("App.Save()", StringComparison.Ordinal)
                    || body.Contains("Settings.SetSetting", StringComparison.Ordinal)
                    || body.Contains("Store.Dispatch", StringComparison.Ordinal))
                    offenders.Add(name);
            }

            Assert.True(offenders.Count == 0,
                "These Settings controls commit the moment they change, so Cancel and Escape "
                + "cannot take them back: " + string.Join(", ", offenders)
                + ". Stage the value in a field and write it in Save() — or, if it is a live "
                + "preview, add it to PreviewHandlers here AND to RevertPreviews.");
        }

        /// <summary>
        /// The vacuity partner. If the preview handlers stopped applying anything, the exemption
        /// above would be a list of names protecting nothing and this file would still be green.
        /// </summary>
        [Fact]
        public void The_declared_previews_really_do_apply_immediately()
        {
            string src = Source();
            foreach (var name in PreviewHandlers)
            {
                string body = MethodBody(src, name);
                bool applies = body.Contains("App.Save()", StringComparison.Ordinal)
                            || body.Contains("ApplySelectedTheme", StringComparison.Ordinal);
                Assert.True(applies,
                    $"{name} is listed as a live preview but applies nothing. Either it is staged "
                    + "now — remove it from PreviewHandlers — or the preview stopped working.");
            }
        }

        /// <summary>
        /// Every preview is reachable by Cancel. A control that previews and is NOT reverted is
        /// the original bug wearing the new vocabulary.
        /// </summary>
        [Fact]
        public void Every_preview_is_reverted_by_cancel()
        {
            string src = Source();
            string revert = MethodBody(src, "RevertPreviews");

            var mustRestore = new[]
            {
                "_uiScaleInitial",
                "_showPatternVisualsInitial",
                "_visualEarconsInitial",
                "_colorVisionSafeInitial",
                "_hollowUpCandlesInitial",
                "_selectedThemeInitial",
            };
            foreach (var field in mustRestore)
                Assert.True(revert.Contains(field, StringComparison.Ordinal),
                    $"RevertPreviews never reads {field}, so that preview survives a Cancel.");

            // And Cancel actually calls it — a revert method nothing invokes reverts nothing.
            Assert.Contains("RevertPreviews()", MethodBody(src, "Cancel"), StringComparison.Ordinal);
        }

        /// <summary>
        /// Both outcomes announce. For a blind user the sentence IS the evidence the button did
        /// anything, and silence after Escape is indistinguishable from a dialog that did not
        /// close. Cancel's is the one that was missing.
        /// </summary>
        [Fact]
        public void Save_and_cancel_both_say_what_they_did()
        {
            string src = Source();
            Assert.Contains("Settings saved.", MethodBody(src, "Save"), StringComparison.Ordinal);
            Assert.Contains("Settings discarded", MethodBody(src, "Cancel"), StringComparison.Ordinal);
        }

        /// <summary>
        /// The three store-backed controls are dispatched only on a real difference.
        /// <c>ToggleSpeechAction</c> is a TOGGLE, not a setter: dispatching it on every Save would
        /// turn speech off for a user who opened the dialog to change the sound theme.
        /// </summary>
        [Fact]
        public void The_toggle_actions_are_dispatched_only_when_the_staged_value_differs()
        {
            string save = MethodBody(Source(), "Save");

            foreach (var (action, guard) in new[]
            {
                ("ToggleSpeechAction",       "_speechEnabled != Store.State.IsSpeechEnabled"),
                ("ToggleSonificationAction", "_sonificationEnabled != Store.State.IsSonificationEnabled"),
                ("AdjustGranularityAction",  "_panningGranularity != Store.State.PanningGranularity"),
            })
            {
                Assert.True(save.Contains(action, StringComparison.Ordinal),
                    $"Save no longer dispatches {action}; the staged control writes nowhere.");
                Assert.True(save.Contains(guard, StringComparison.Ordinal),
                    $"{action} is dispatched without the guard `{guard}`. A toggle applied "
                    + "unconditionally on Save flips a setting the user never touched.");
            }
        }

        /// <summary>
        /// Every staged field reaches Save. This is the check that catches the real mistake in
        /// this pattern — moving a control off its immediate write and forgetting to write it at
        /// all, which looks exactly like a working dialog until you reopen it.
        /// </summary>
        [Fact]
        public void Every_staged_setting_is_written_by_save()
        {
            string save = MethodBody(Source(), "Save");

            var staged = new[]
            {
                "App.HoverSonification",
                "App.MagnetSnap",
                "App.MarketStructureOnByDefault",
                "App.SoundTheme",
                "App.TouchNavBarMode",
                "App.BackgroundMonitoring",
                "App.MonitorPollSeconds",
                "App.LiveBackgroundTabs",
                "App.ResumeLastSession",
                "App.PaperTradingMode",
                "App.BrailleEnabled",
                "App.MuteIncludesOrderEvents",
                "App.TimestampReadLocation",
                "App.SpeechOrder",
            };
            var missing = staged.Where(x => !save.Contains(x, StringComparison.Ordinal)).ToList();

            Assert.True(missing.Count == 0,
                "Staged in the dialog and written nowhere — these settings are dropped on Save: "
                + string.Join(", ", missing));

            // The side effects that used to fire per keystroke now follow the write, once.
            foreach (var effect in new[] { "Monitoring.Reconcile()", "TabFeeds.Reconcile()",
                                           "TouchNavBarModeChangedEvent", "ApplySpeechOutputMode()" })
                Assert.True(save.Contains(effect, StringComparison.Ordinal),
                    $"Save no longer performs {effect}, so its setting persists without taking effect.");
        }

        /// <summary>
        /// ResetLocal is what seeds the dialog, and a field it does not read shows the user a
        /// control that disagrees with the setting it controls. "Draw chart formations" was
        /// exactly that until 2026-09-04: applied, persisted, and always rendered unchecked.
        /// </summary>
        [Fact]
        public void Reset_local_seeds_every_staged_and_preview_field()
        {
            string reset = MethodBody(Source(), "ResetLocal");

            foreach (var field in new[]
            {
                "_hoverSonification", "_magnetSnap", "_marketStructureDefault", "_soundTheme",
                "_touchNavMode", "_bgMonitoring", "_bgPollSeconds", "_liveBgTabs", "_resumeSession",
                "_speechEnabled", "_sonificationEnabled", "_panningGranularity",
                "_showPatternVisuals", "_visualEarcons", "_colorVisionSafe", "_hollowUpCandles",
                "_uiScale",
            })
                Assert.True(Regex.IsMatch(reset, @"\b" + Regex.Escape(field) + @"\s*="),
                    $"ResetLocal never assigns {field}, so reopening Settings shows a stale value for it.");
        }
    }
}
