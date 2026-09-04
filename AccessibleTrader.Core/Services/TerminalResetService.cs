using AccessibleTrader.Core.Services.Theming;
using Microsoft.Extensions.Logging;

namespace AccessibleTrader.Core.Services
{
    /// <summary>
    /// What a factory reset actually erases, and — just as importantly — what it does not.
    ///
    /// <para>
    /// One place, because the alternative is a confirmation dialog whose wording and the code
    /// behind it drift apart. <see cref="WhatIsErased"/> and <see cref="WhatSurvives"/> are the
    /// sentences the dialog reads out, and they are written here beside the calls they describe.
    /// </para>
    /// </summary>
    public interface ITerminalResetService
    {
        /// <summary>
        /// Returns every preference, rebind, theme, patch and indicator style to the shipped
        /// defaults. Returns the number of subsystems that failed, which is zero on a clean run —
        /// a reset that only half-worked must not be announced as done.
        /// </summary>
        int ResetEverything();

        /// <summary>The plain-language list of what goes, for the confirmation.</summary>
        IReadOnlyList<string> WhatIsErased { get; }

        /// <summary>The plain-language list of what stays, for the same confirmation.</summary>
        IReadOnlyList<string> WhatSurvives { get; }
    }

    /// <inheritdoc />
    public sealed class TerminalResetService : ITerminalResetService
    {
        private readonly ISettingsManager _settings;
        private readonly IShortcutManager _shortcuts;
        private readonly IThemeLibrary _themes;
        private readonly ISoundPatchLibrary _patches;
        private readonly IIndicatorPreferencesService _indicatorPrefs;
        private readonly ILogger<TerminalResetService>? _logger;

        public TerminalResetService(
            ISettingsManager settings,
            IShortcutManager shortcuts,
            IThemeLibrary themes,
            ISoundPatchLibrary patches,
            IIndicatorPreferencesService indicatorPrefs,
            ILogger<TerminalResetService>? logger = null)
        {
            _settings = settings;
            _shortcuts = shortcuts;
            _themes = themes;
            _patches = patches;
            _indicatorPrefs = indicatorPrefs;
            _logger = logger;
        }

        public IReadOnlyList<string> WhatIsErased { get; } = new[]
        {
            "every setting in this dialog",
            "every keyboard rebinding",
            "your own themes",
            "your sound patches and earcon assignments",
            "the colours and sounds you gave individual indicators",
        };

        // ── WHAT A FACTORY RESET DELIBERATELY LEAVES ALONE ───────────────────────────
        //
        // Three things, and each is excluded for a different reason.
        //
        // API KEYS are credentials, not preferences. They live in the platform secure store
        // rather than in settings.json, they cannot be reconstructed from anything on this
        // machine, and a user reaching for "reset my settings" because the terminal is speaking
        // too much is not asking to be locked out of their broker. Deleting them would also be
        // the one part of this that a support conversation could not undo.
        //
        // THE PAPER ACCOUNT is a trading record — weeks of proving a strategy — and it already
        // has its own two-step reset a few rows up this same tab. Two buttons that erase it, one
        // of them not saying so, is how a user loses it by accident.
        //
        // SAVED WORKSPACES are documents. The user named them and can delete them by name; a
        // settings reset that quietly took them is the same class of surprise as a preferences
        // reset deleting your files.
        public IReadOnlyList<string> WhatSurvives { get; } = new[]
        {
            "your API keys",
            "your paper trading account and its history",
            "your saved workspaces",
        };

        public int ResetEverything()
        {
            int failures = 0;

            // Each subsystem is reset independently and its failure counted rather than thrown,
            // because a half-reset that stops at the first exception is the worst outcome
            // available: the user is left with a keyboard from one era and preferences from
            // another, and no way to tell which. Every one is attempted; the count is what the
            // caller announces.
            failures += Try("settings",             () => _settings.ResetToDefaults());
            failures += Try("keyboard shortcuts",   () => _shortcuts.ResetToDefaults());
            failures += Try("themes",               ResetThemes);
            failures += Try("sound patches",        () => _patches.ResetToDefaults());
            failures += Try("indicator preferences",() => _indicatorPrefs.ClearAllPreferences());

            return failures;
        }

        private void ResetThemes()
        {
            // IThemeLibrary has no bulk remove, and adding one for a single caller would put a
            // second way to empty it next to the one that already works. Snapshot first: Remove
            // mutates the list All is a view over.
            foreach (var id in _themes.All.Select(t => t.Id).ToList()) _themes.Remove(id);
            _themes.Save();
        }

        private int Try(string what, Action action)
        {
            try { action(); return 0; }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Factory reset: {What} could not be reset.", what);
                return 1;
            }
        }
    }
}
