using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AccessibleTrader.Core.Services;
using Newtonsoft.Json.Linq;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Mocks;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AccessibleTrader.Tests;

/// <summary>
/// Every preference must survive a restart.
///
/// <para>
/// <b>The bug this exists to prevent, in the user's words: "I have to enable pattern detection every
/// time I open the terminal."</b> The settings dialog dispatched <c>DescribeChartPatterns</c> into
/// the workspace store, so it worked perfectly for the whole session — and
/// <see cref="PreferencePersistenceService"/>, which is the only bridge from the store to the
/// settings file, carried a hardcoded list of seven preferences that did not include it. It was
/// written nowhere and silently reset on every launch.
/// </para>
///
/// <para>
/// The shape of that defect is what makes it worth a reflection test rather than one more assertion.
/// Adding a preference means touching four places — the <c>Prefs</c> record, <c>FromState</c>, the
/// seed dispatch and the write-back — and nothing in the compiler or the type system makes you.
/// A test that enumerated preferences by hand would need the same fifth edit and would be forgotten
/// in the same way, so this one <b>derives</b> the list: any property that exists on BOTH
/// <see cref="WorkspaceState"/> and <see cref="IAppSettings"/> is by definition a persisted
/// preference, and must round-trip.
/// </para>
/// </summary>
public class PreferenceRoundTripTests
{
    /// <summary>
    /// Properties present on both the live state and the settings facade. That intersection IS the
    /// definition of "a preference that persists" — a value the app keeps in the store to read
    /// cheaply and in settings to survive a restart.
    /// </summary>
    private static List<PropertyInfo> PersistedPreferences()
    {
        var stateProps = typeof(WorkspaceState).GetProperties()
            .ToDictionary(p => p.Name, StringComparer.Ordinal);

        return typeof(IAppSettings).GetProperties()
            .Where(p => p.CanRead && p.CanWrite)
            .Where(p => stateProps.ContainsKey(p.Name))
            .Where(p => stateProps[p.Name].PropertyType == p.PropertyType)
            .OrderBy(p => p.Name, StringComparer.Ordinal)
            .ToList();
    }

    [Fact]
    public void TheIntersectionIsNotEmpty()
    {
        // Guards the guard: if a refactor renamed things apart, this test would silently pass by
        // checking nothing at all.
        Assert.NotEmpty(PersistedPreferences());
    }

    /// <summary>
    /// Set every preference to a non-default value, let the persistence service write, then seed a
    /// fresh store from the same settings and assert nothing was lost.
    /// </summary>
    [Fact]
    public void EveryPreferenceSurvivesASaveAndReload()
    {
        var prefs = PersistedPreferences();
        var settings = new AppSettings(new DictSettings());

        // 1. Write a distinctly non-default value for each preference.
        var written = new Dictionary<string, object?>();
        foreach (var p in prefs)
        {
            object? flipped = Flip(p.GetValue(settings), p.PropertyType);
            p.SetValue(settings, flipped);
            written[p.Name] = flipped;
        }

        // 2. Seed a fresh store exactly as startup does.
        var store = new MockWorkspaceStore();
        var svc = new PreferencePersistenceService(store, settings, NullLogger<PreferencePersistenceService>.Instance);
        svc.Initialize();

        // 3. Whatever the settings file held must now be in the state.
        var seeded = store.DispatchedActions.OfType<UpdateSettingsAction>().ToList();
        Assert.NotEmpty(seeded);

        var state = WorkspaceState.Initial;
        foreach (var action in seeded) state = action.Updater(state);

        var missed = new List<string>();
        foreach (var p in prefs)
        {
            var onState = typeof(WorkspaceState).GetProperty(p.Name)!.GetValue(state);
            if (!Equals(onState, written[p.Name])) missed.Add(p.Name);
        }

        Assert.True(missed.Count == 0,
            "These preferences are stored in settings but are NOT seeded back into the workspace "
          + "state on startup, so they reset every launch: " + string.Join(", ", missed)
          + ". Add them to PreferencePersistenceService (Prefs, FromState, the seed dispatch and "
          + "the write-back — all four).");
    }

    /// <summary>
    /// A value guaranteed to differ from whatever the default was, so "it round-tripped" cannot be
    /// satisfied by the value simply never having changed.
    /// </summary>
    private static object? Flip(object? current, Type t)
    {
        if (t == typeof(bool)) return !(bool)(current ?? false);
        if (t == typeof(int)) return (int)(current ?? 0) + 7;
        if (t == typeof(double)) return (double)(current ?? 0d) + 7d;
        if (t == typeof(string)) return "roundtrip-probe";
        return current;
    }

    /// <summary>
    /// The REAL <see cref="AppSettings"/> over a dictionary-backed settings manager.
    ///
    /// <para>
    /// Deliberately not a hand-written fake of <c>IAppSettings</c>. That interface is large, a fake
    /// would need updating every time it grew, and — more to the point — it would bypass the real
    /// key names and JSON conversion, which is exactly the layer where a preference can go missing.
    /// A dictionary in place of a file removes IO flakiness and nothing else.
    /// </para>
    /// </summary>
    private sealed class DictSettings : ISettingsManager
    {
        public readonly Dictionary<string, JToken> Store = new(StringComparer.Ordinal);
        public JToken? GetSetting(string keyPath, JToken? defaultValue = null)
            => Store.TryGetValue(keyPath, out var v) ? v : defaultValue;
        public void SetSetting(string keyPath, JToken value) => Store[keyPath] = value;
        public JObject GetEffectiveSettingsForSeries(string seriesId) => new();
        public void SaveSettings() { }
    }
}
