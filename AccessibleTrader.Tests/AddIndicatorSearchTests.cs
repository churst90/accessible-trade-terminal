using System;
using System.IO;
using System.Linq;
using Xunit;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// The Add Indicator dialog's search and description.
    ///
    /// <para>
    /// Before this, the dialog was a category dropdown and an alphabetical list of about a hundred
    /// indicators. Finding one required knowing both its exact name and which category somebody
    /// else had filed it under — and selecting one told you nothing whatsoever about what it does,
    /// even though every indicator carries a description in its metadata. It was the weakest
    /// dialog in the application and the one a new user meets earliest.
    /// </para>
    ///
    /// <para>
    /// Rendering it needs the whole indicator registry, so these check the wiring at source level.
    /// The behaviour that matters and is easy to lose: search covers the DESCRIPTION as well as the
    /// name, the match count is a live region, and the description is bound to the list for a
    /// screen reader.
    /// </para>
    /// </summary>
    public class AddIndicatorSearchTests
    {
        private static string Modal()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AccessibleTrader.slnx")))
                dir = dir.Parent;
            Assert.NotNull(dir);
            return File.ReadAllText(Path.Combine(dir!.FullName,
                "AccessibleTrader.BlazorClient.Components", "AddIndicatorModal.razor"));
        }

        [Fact]
        public void SearchMatchesTheDescriptionAndNotOnlyTheName()
        {
            // This is the whole point of the search. Matching names alone helps only people who
            // already know what the thing is called; "volatility" should find ATR and Bollinger
            // Bands for someone who knows what they want but not what it is named.
            string text = Modal();

            Assert.Contains("i.Name.Contains(needle", text);
            Assert.Contains("i.Description?.Contains(needle", text);
            Assert.Contains("i.Code.Contains(needle", text);
        }

        [Fact]
        public void TheMatchCountIsSpokenAsItChanges()
        {
            // A screen-reader user typing into the box otherwise types into silence and finds out
            // what happened only after tabbing to the list.
            string text = Modal();

            Assert.Contains("id=\"indicator-count\"", text);
            Assert.Contains("aria-live=\"polite\"", text);
            Assert.Contains("MatchSummary", text);
        }

        [Fact]
        public void TheSelectedIndicatorIsDescribed()
        {
            // Every indicator carries a Description in its metadata, and none of it reached the one
            // dialog where a user is deciding whether they want the thing.
            string text = Modal();

            Assert.Contains("id=\"indicator-description\"", text);
            Assert.Contains("meta.Description", text);

            // Bound to the list so the description is announced with the selection rather than
            // being something you have to go and find. The reference is conditional: the
            // description div is @if-gated on a selection, and a dangling aria-describedby is
            // itself a defect (AriaValueScanTests scans the rendered tree for exactly that).
            Assert.Contains("aria-describedby=\"@(_selectedMeta is null ? null : \"indicator-description\")\"", text);
        }

        [Fact]
        public void FilteringKeepsTheCurrentSelectionWhenItStillMatches()
        {
            // Otherwise every keystroke resets the selection to the first result and yanks the
            // description away from under someone reading it.
            string text = Modal();

            Assert.Contains("if (!_filteredIndicators.Any(i => i.Code == _selectedIndicatorCode))", text);
        }

        [Fact]
        public void AnEmptyResultSaysWhatToDoAboutIt()
        {
            // "0 indicators" is a dead end. The user needs to know which of the two filters to
            // relax, and there are two of them.
            Assert.Contains("Clear the search or pick another category", Modal());
        }

        [Fact]
        public void BothFiltersAreLabelled()
        {
            string text = Modal();

            Assert.Contains("<label for=\"indicator-search\">", text);
            Assert.Contains("<label for=\"indicator-category\">", text);
            Assert.Contains("<label for=\"indicator-name\">", text);
        }
    }
}
