using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Every <see cref="WorkspaceAction"/> subtype must be routed by
    /// <c>WorkspaceStore.Reduce</c>'s switch.
    ///
    /// This bug has now happened three times, each found only by a user pressing a key
    /// that did nothing:
    ///   • WheelZoomAction    — every mouse-wheel zoom silently ignored (2026-07-23)
    ///   • ToggleEventSpeechAction / ToggleEarconsAction — Shift+F2 / Shift+F3 dead (2026-07-23)
    ///   • RestoreAllComponentsAction — Shift+H / Shift+M dead, and SILENT, because the
    ///     announcement lives inside the reducer that was never reached (2026-08-24)
    ///
    /// In all three the action record existed, the reducer case existed, the dispatch
    /// site existed, and the reducer's own unit tests passed — because those tests call
    /// the reducer directly. The only missing piece was the type's name in the routing
    /// switch, which no test looked at. So this one does.
    ///
    /// It is a source scan, and per this repo's standing lesson a scan guard must check
    /// the PATH rather than the mere presence of a string. Here the switch expression IS
    /// the path: <c>Reduce</c> is a single expression with one fall-through arm
    /// (<c>_ =&gt; state</c>), so there is no earlier branch that can route around a
    /// matched type. A name present in the switch is genuinely reached.
    ///
    /// The allow-list is bidirectional on purpose — an entry that turns out to BE routed
    /// fails too, so the list cannot rot into a place where real gaps hide.
    /// </summary>
    public class ActionRoutingReachabilityTests
    {
        /// <summary>
        /// Actions deliberately not routed through <c>WorkspaceStore.Reduce</c>. Each needs
        /// a reason. Empty today: every action subtype is routed.
        /// </summary>
        private static readonly Dictionary<string, string> DeliberatelyUnrouted = new();

        /// <summary>
        /// Vacuity floor. If reflection stops finding action types (a namespace move, a
        /// rename of the base record) this test would otherwise pass by examining nothing.
        /// </summary>
        private const int MinimumExpectedActionTypes = 45;

        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AccessibleTrader.slnx")))
                dir = dir.Parent;
            Assert.NotNull(dir);
            return dir!.FullName;
        }

        private static string ReduceSwitchBody()
        {
            string path = Path.Combine(RepoRoot(), "AccessibleTrader.Core", "Services", "WorkspaceStore.cs");
            string src = File.ReadAllText(path);

            int start = src.IndexOf("private WorkspaceState Reduce(", StringComparison.Ordinal);
            Assert.True(start >= 0,
                "Could not find WorkspaceStore.Reduce — this guard is pinned to that method and " +
                "must be re-pointed if the routing switch moves.");

            // The switch expression ends at its fall-through arm. Anchoring on that rather
            // than on brace counting keeps the guard honest about what it read.
            int end = src.IndexOf("_ => state", start, StringComparison.Ordinal);
            Assert.True(end > start,
                "WorkspaceStore.Reduce no longer ends in a '_ => state' fall-through arm. If the " +
                "routing shape changed, re-derive this guard rather than deleting it.");

            return src[start..end];
        }

        private static List<Type> AllActionTypes() =>
            typeof(WorkspaceAction).Assembly
                .GetTypes()
                .Where(t => t.IsSubclassOf(typeof(WorkspaceAction)) && !t.IsAbstract)
                .OrderBy(t => t.Name, StringComparer.Ordinal)
                .ToList();

        [Fact]
        public void EveryWorkspaceActionSubtype_IsRoutedByTheReduceSwitch()
        {
            var actions = AllActionTypes();

            Assert.True(actions.Count >= MinimumExpectedActionTypes,
                $"Only {actions.Count} WorkspaceAction subtypes found via reflection, expected at " +
                $"least {MinimumExpectedActionTypes}. This guard is vacuous unless it is actually " +
                "enumerating the action set — check the base type and assembly.");

            string body = ReduceSwitchBody();

            var missing = actions
                .Where(t => !DeliberatelyUnrouted.ContainsKey(t.Name))
                .Where(t => !Regex.IsMatch(body, $@"\b{Regex.Escape(t.Name)}\b"))
                .Select(t => t.Name)
                .ToList();

            Assert.True(missing.Count == 0,
                "These WorkspaceAction subtypes are dispatched but never routed by " +
                "WorkspaceStore.Reduce, so they fall through to '_ => state' and do nothing — " +
                "silently, which for a keyboard-driven accessible terminal means a key that " +
                "appears dead:\n  " + string.Join("\n  ", missing) +
                "\n\nAdd each to the correct reducer arm, or to DeliberatelyUnrouted with a reason.");
        }

        [Fact]
        public void TheUnroutedAllowList_ContainsNothingThatIsActuallyRouted()
        {
            // Bidirectional half: an allow-list entry that has since been wired up must be
            // removed, or the list slowly becomes a place where real gaps can hide.
            if (DeliberatelyUnrouted.Count == 0) return;

            string body = ReduceSwitchBody();

            var nowRouted = DeliberatelyUnrouted.Keys
                .Where(name => Regex.IsMatch(body, $@"\b{Regex.Escape(name)}\b"))
                .ToList();

            Assert.True(nowRouted.Count == 0,
                "These actions are listed as deliberately unrouted but DO appear in the Reduce " +
                "switch. Remove them from DeliberatelyUnrouted:\n  " + string.Join("\n  ", nowRouted));
        }

        [Fact]
        public void EveryUnroutedEntry_NamesARealActionType()
        {
            // Stops the allow-list from silencing a gap via a typo or a renamed action.
            if (DeliberatelyUnrouted.Count == 0) return;

            var known = AllActionTypes().Select(t => t.Name).ToHashSet(StringComparer.Ordinal);
            var unknown = DeliberatelyUnrouted.Keys.Where(n => !known.Contains(n)).ToList();

            Assert.True(unknown.Count == 0,
                "DeliberatelyUnrouted names a type that is not a WorkspaceAction subtype (typo, or " +
                "the action was renamed/deleted):\n  " + string.Join("\n  ", unknown));
        }
    }
}
