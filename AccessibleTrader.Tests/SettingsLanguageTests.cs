using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Settings labels stay short; explanations live in hints.
    ///
    /// <para>
    /// A label is read in full every time you tab past it. For a screen-reader user working down a
    /// dialog, "Live-stream background tabs (tick-fresh, instant tab switch; exchanges that support
    /// it, first 4 tabs)" is not a label — it is a paragraph in the way of the next control, and it
    /// is read again on every pass. A sighted user skims past the parenthetical; a listener cannot.
    /// </para>
    ///
    /// <para>
    /// The rule this enforces: <b>the label says what the setting does; the hint says why you would
    /// want it or what it costs.</b> Never both in the label. Shortening a label while DROPPING the
    /// explanation is not an improvement either — it just moves the cost from reading to guessing —
    /// so the second test checks that the dialogs still carry hints.
    /// </para>
    /// </summary>
    public class SettingsLanguageTests
    {
        /// <summary>
        /// Beyond this a label has stopped naming the setting and started explaining it. Chosen
        /// from the labels that were actually a problem: the shortest offender was 45 characters.
        /// </summary>
        private const int MaxLabelChars = 42;

        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AccessibleTrader.slnx")))
                dir = dir.Parent;
            Assert.NotNull(dir);
            return dir!.FullName;
        }

        private static string SettingsModal() => File.ReadAllText(Path.Combine(
            RepoRoot(), "AccessibleTrader.BlazorClient.Components", "SettingsModal.razor"));

        /// <summary>
        /// Labels whose length is the definition rather than an aside. Each needs a reason: the
        /// bar for staying long is that shortening would make the control ambiguous.
        /// </summary>
        private static readonly Dictionary<string, string> AllowedLong = new(StringComparer.Ordinal)
        {
            ["Add Market Structure (swing highs and lows) to new charts"] =
                "the parenthetical IS the definition — 'Market Structure' alone names nothing to someone meeting it for the first time",
            ["Blend the toolbars, chart and footer into one gradient"] =
                "naming all three regions is the setting; any shorter and it is unclear what gets blended",
        };

        [Fact]
        public void SettingLabels_nameTheSettingRatherThanExplainingIt()
        {
            var offenders = new List<string>();

            foreach (Match m in Regex.Matches(SettingsModal(), @"<label for=""s-[a-z0-9\-]+"">([^<]+)</label>"))
            {
                string label = m.Groups[1].Value.Trim();

                // Razor interpolation makes the rendered length unknowable from source; those are
                // judged by eye, not here.
                if (label.Contains('@')) continue;
                if (AllowedLong.ContainsKey(label)) continue;

                if (label.Length > MaxLabelChars)
                    offenders.Add($"({label.Length} chars) \"{label}\"");
            }

            Assert.True(offenders.Count == 0,
                $"Labels longer than {MaxLabelChars} characters. Move the explanation into a hint " +
                "below the control, or add the label to AllowedLong with a reason:\n  " +
                string.Join("\n  ", offenders));
        }

        [Fact]
        public void TheExplanationsDidNotSimplyDisappear()
        {
            // The failure mode on the other side: labels get trimmed, the meaning goes nowhere, and
            // the dialog is now terse AND opaque. Hints are the muted paragraphs under a row.
            string text = SettingsModal();

            int hints = Regex.Matches(text, @"color:var\(--text-muted\); font-size:0\.9").Count;

            Assert.True(hints >= 12,
                $"Only {hints} explanatory hints found in Settings. Short labels are only an " +
                "improvement while the explanation still exists somewhere.");
        }

        [Fact]
        public void EveryControlStillHasALabelBoundToIt()
        {
            // Shortening a label is worthless if the `for` no longer matches the control's id — the
            // screen reader then announces an unlabelled checkbox, which is worse than a long name.
            string text = SettingsModal();

            var labelled = Regex.Matches(text, @"<label for=""(s-[a-z0-9\-]+)""")
                                .Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);
            var controlIds = Regex.Matches(text, @"<(?:input|select|textarea) id=""(s-[a-z0-9\-]+)""")
                                  .Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);

            var unlabelled = controlIds.Except(labelled).ToList();

            Assert.True(unlabelled.Count == 0,
                "Controls in Settings with no <label for=…>:\n  " + string.Join("\n  ", unlabelled));
        }

        [Fact]
        public void NoLabelUsesTheParentheticalSAbbreviation()
        {
            // "component(s)" is read aloud as "component open paren s close paren".
            var offenders = Regex.Matches(SettingsModal(), @"<label[^>]*>([^<]*\(s\)[^<]*)</label>")
                                 .Select(m => m.Groups[1].Value.Trim()).ToList();

            Assert.True(offenders.Count == 0,
                "Labels using \"(s)\", which a screen reader reads literally:\n  " +
                string.Join("\n  ", offenders));
        }
    }
}
