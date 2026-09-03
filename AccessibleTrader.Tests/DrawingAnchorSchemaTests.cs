// The anchor schema is only worth anything if it agrees with the calculators.
//
// It is data describing which AnchorPriceN / AnchorDateN each DrawingType actually uses, and the
// properties dialog renders its coordinate editors from it. A schema that drifts from the code
// does not fail loudly: it silently offers a control that changes nothing, or silently withholds
// the one control that would repair a drawing the user can no longer see.
//
// So the census is RE-DERIVED here from the calculator sources rather than restated. That is the
// one shape of check that is not a mirror of the thing it guards — the schema is a table written
// by hand, the census is `grep AnchorPrice2` over sixteen files, and they were produced by
// different means. If a new calculator starts reading a slot, or stops, this goes red.

using System.Text.RegularExpressions;
using AccessibleTrader.Core.Services.Drawing;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Tests;

public class DrawingAnchorSchemaTests
{
    private static string CalculatorsDir()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !Directory.Exists(Path.Combine(dir, "AccessibleTrader.Core")))
            dir = Path.GetDirectoryName(dir);
        Assert.NotNull(dir);
        var path = Path.Combine(dir!, "AccessibleTrader.Core", "Services", "Drawing", "Calculators");
        Assert.True(Directory.Exists(path), $"calculator directory not found at {path}");
        return path;
    }

    /// <summary>
    /// Which (slot, axis) pairs each calculator file dereferences, keyed by the DrawingType its
    /// <c>DrawingType</c> property declares. Read from source, not from the schema.
    /// </summary>
    private static Dictionary<DrawingType, HashSet<(int Slot, DrawingAnchorAxis Axis)>> CensusFromSource()
    {
        var census = new Dictionary<DrawingType, HashSet<(int, DrawingAnchorAxis)>>();
        foreach (var file in Directory.EnumerateFiles(CalculatorsDir(), "*.cs"))
        {
            var src = File.ReadAllText(file);
            var declared = Regex.Match(src, @"DrawingType\s+DrawingType\s*=>\s*DrawingType\.(\w+)");
            if (!declared.Success) continue;                       // the shared helper declares none
            if (!Enum.TryParse<DrawingType>(declared.Groups[1].Value, out var type)) continue;

            var used = new HashSet<(int, DrawingAnchorAxis)>();
            foreach (Match m in Regex.Matches(src, @"Anchor(Price|Date)([123])"))
            {
                var axis = m.Groups[1].Value == "Price" ? DrawingAnchorAxis.Price : DrawingAnchorAxis.Date;
                used.Add((int.Parse(m.Groups[2].Value), axis));
            }
            census[type] = used;
        }
        return census;
    }

    [Fact]
    public void TheSchemaMatchesWhatTheCalculatorsActuallyRead()
    {
        var census = CensusFromSource();

        // Vacuity floor: fifteen calculators declare a type (DrawingCalculatorHelper declares none).
        Assert.True(census.Count >= 15,
            $"only {census.Count} calculators were parsed — the DrawingType declaration regex has "
            + "stopped matching, so this comparison is running against almost nothing.");

        var problems = new List<string>();
        foreach (var (type, used) in census.OrderBy(k => k.Key.ToString(), StringComparer.Ordinal))
        {
            var declared = DrawingAnchorSchema.For(type)
                .Select(f => (f.Slot, f.Axis)).ToHashSet();

            foreach (var missing in used.Except(declared).OrderBy(x => x.Item1))
                problems.Add($"{type}: the calculator reads Anchor{missing.Item2}{missing.Item1} but the "
                             + "schema does not declare it — that coordinate has NO keyboard editor");
            foreach (var extra in declared.Except(used).OrderBy(x => x.Slot))
                problems.Add($"{type}: the schema declares Anchor{extra.Axis}{extra.Slot} but no calculator "
                             + "reads it — that editor would change nothing");
        }

        Assert.True(problems.Count == 0, string.Join("\n  ", problems));
    }

    [Fact]
    public void EveryDrawingTypeIsDeclared()
    {
        // A new DrawingType with no schema entry renders no coordinate editors at all, which is
        // exactly the state seven of the sixteen were in before the schema existed. Enumerating
        // the enum means adding a value is what fails, not forgetting to update a list here.
        var missing = Enum.GetValues<DrawingType>()
            .Where(t => !DrawingAnchorSchema.DeclaredTypes.Contains(t))
            .ToList();

        Assert.True(missing.Count == 0,
            "DrawingTypes with no anchor schema — their coordinates cannot be reached from the "
            + "keyboard:\n  " + string.Join("\n  ", missing));
    }

    [Fact]
    public void EveryFieldIsNamedInTheVocabularyOfItsOwnDrawing()
    {
        // The label is the only thing a screen reader gives a user in a form-field list, so a
        // generic one is a defect and not a cosmetic issue. Two rules: no label may be blank, and
        // within one drawing type no two labels may collide — three fields all called
        // "Price" is a dialog you cannot fill in without counting.
        var problems = new List<string>();
        foreach (var type in DrawingAnchorSchema.DeclaredTypes)
        {
            var fields = DrawingAnchorSchema.For(type);
            foreach (var f in fields)
            {
                if (string.IsNullOrWhiteSpace(f.Label))
                    problems.Add($"{type} slot {f.Slot} {f.Axis} has no label");
                // "Anchor date" is right for an Anchored VWAP — the tool is named after it.
                // "Anchor 2 price" never is: that is the field's index in DrawingData leaking
                // into the only text a screen-reader user gets.
                if (Regex.IsMatch(f.Label, @"anchor\s*[123]", RegexOptions.IgnoreCase))
                    problems.Add($"{type} slot {f.Slot} {f.Axis} is labelled \"{f.Label}\" — "
                                 + "\"anchor N\" is the data model's word, not the user's");
            }
            var dupes = fields.GroupBy(f => f.Label, StringComparer.OrdinalIgnoreCase)
                              .Where(g => g.Count() > 1).Select(g => g.Key);
            foreach (var d in dupes)
                problems.Add($"{type} has more than one field labelled \"{d}\"");
        }

        Assert.True(problems.Count == 0, string.Join("\n  ", problems));
    }

    [Theory]
    [InlineData(DrawingType.RiskReward, 3, DrawingAnchorAxis.Price)]
    [InlineData(DrawingType.FibExtension, 3, DrawingAnchorAxis.Price)]
    [InlineData(DrawingType.AndrewsPitchfork, 3, DrawingAnchorAxis.Date)]
    [InlineData(DrawingType.TextLabel, 1, DrawingAnchorAxis.Price)]
    [InlineData(DrawingType.AnchoredVwap, 1, DrawingAnchorAxis.Date)]
    [InlineData(DrawingType.GannFan, 2, DrawingAnchorAxis.Date)]
    public void TheCoordinatesThatHadNoKeyboardEditorAreDeclared(
        DrawingType type, int slot, DrawingAnchorAxis axis)
    {
        // Named individually rather than left to the census, because these are the specific
        // coordinates the 2026-09-01 audit's "anchors can only be moved with a mouse drag"
        // finding came down to. If the schema is ever narrowed, these say what was lost.
        Assert.True(DrawingAnchorSchema.Uses(type, slot, axis),
            $"{type} slot {slot} {axis} is not editable from the keyboard");
    }
}
