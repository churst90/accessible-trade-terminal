// Can a keyboard user move a drawing they have already placed?
//
// The 2026-09-01 audit recorded "an existing drawing's anchors can still only be moved with a
// 10-pixel mouse drag". That was PARTLY stale when written — PropertiesModal has had absolute
// price and date fields since 2026-04-27 — and the part that was true was worse than the
// sentence: the fields were driven by four hand-written per-type lists, and measured against
// what the calculators actually read, seven of the sixteen drawing types appeared in none of
// them. A GannFan, a RiskReward, an Anchored VWAP, a Measure Tool, a Gann Box, an Andrews
// Pitchfork and an Angle Fib could be created from the keyboard and then never corrected from
// it. Slot 3 was offered to nobody, and a TextLabel offered its date but not its price.
//
// These tests walk every DrawingType, render the properties dialog on a drawing of that type,
// and require one editor per coordinate the type's calculator reads — the list coming from
// DrawingAnchorSchema, which DrawingAnchorSchemaTests independently checks against the
// calculator sources. So this file asks "is the editor there", that file asks "is the schema
// true", and neither is the other's mirror.

using System.Collections.Immutable;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Drawing;
using AccessibleTrader.Sdk.Models;
using AngleSharp.Dom;
using Bunit;
using NSubstitute;

namespace AccessibleTrader.Tests.Blazor;

public class DrawingCoordinateEditorTests
{
    private static ChartSeries DrawingOfType(DrawingType type)
    {
        var config = new SeriesConfig { Id = "dw", Name = type.ToString(), FriendlyName = type.ToString() };
        // Every slot filled, so a value-derived renderer and a schema-derived one would agree
        // here — the difference between them shows up on the types with fewer slots, which the
        // "no editor a calculator does not read" half of the assertion covers.
        return new ChartSeries(config, new SeriesDataBuffer { SeriesId = "dw" })
        {
            Drawing = new DrawingData
            {
                Type = type,
                AnchorPrice1 = 100, AnchorDate1 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                AnchorPrice2 = 110, AnchorDate2 = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc),
                AnchorPrice3 = 120, AnchorDate3 = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            },
        };
    }

    private static IRenderedFragment OpenOn(BlazorTestHarness h, ChartSeries series)
    {
        var state = WorkspaceState.Initial with
        {
            Identity = new ChartIdentity("Crypto", "kraken", "BTC/USD", "1h"),
            ActiveSeries = ImmutableList.Create(series),
            FocusedSeriesId = series.Id,
        };
        h.WorkspaceStore.State.Returns(_ => state);
        return h.OpenModal<AccessibleTrader.BlazorClient.Components.PropertiesModal>(
            b => b.Publish(new OpenPropertiesEvent()));
    }

    public static TheoryData<DrawingType> RealDrawingTypes
    {
        get
        {
            var d = new TheoryData<DrawingType>();
            foreach (var t in Enum.GetValues<DrawingType>())
                if (t != DrawingType.None) d.Add(t);
            return d;
        }
    }

    [Theory]
    [MemberData(nameof(RealDrawingTypes))]
    public void EveryCoordinateItsCalculatorReadsHasAnEditor(DrawingType type)
    {
        using var h = new BlazorTestHarness();
        var cut = OpenOn(h, DrawingOfType(type));

        var expected = DrawingAnchorSchema.For(type);
        var missing = new List<string>();
        foreach (var f in expected)
        {
            var id = f.Axis == DrawingAnchorAxis.Price ? $"drawing-price-{f.Slot}" : $"drawing-date-{f.Slot}";
            if (cut.FindAll($"#{id}").Count == 0)
                missing.Add($"{f.Label} (#{id})");
        }

        Assert.True(missing.Count == 0,
            $"A {type} renders no editor for: {string.Join(", ", missing)}. Its calculator reads "
            + "those anchors, so the drawing has a coordinate that can only be changed with a mouse.");
    }

    [Theory]
    [MemberData(nameof(RealDrawingTypes))]
    public void NoEditorIsOfferedForACoordinateNothingReads(DrawingType type)
    {
        // The other half, and it is not symmetry for its own sake. The placement fallback in
        // DrawingInteractionManager writes slot 3 on EVERY drawing type, so a renderer driven by
        // "which anchors are non-null" would put a third price box on a horizontal line: a
        // control that announces itself, takes a value, and changes nothing.
        using var h = new BlazorTestHarness();
        var cut = OpenOn(h, DrawingOfType(type));

        var spurious = new List<string>();
        foreach (var slot in new[] { 1, 2, 3 })
        {
            if (cut.FindAll($"#drawing-price-{slot}").Count > 0
                && !DrawingAnchorSchema.Uses(type, slot, DrawingAnchorAxis.Price))
                spurious.Add($"#drawing-price-{slot}");
            if (cut.FindAll($"#drawing-date-{slot}").Count > 0
                && !DrawingAnchorSchema.Uses(type, slot, DrawingAnchorAxis.Date))
                spurious.Add($"#drawing-date-{slot}");
        }

        Assert.True(spurious.Count == 0,
            $"A {type} renders coordinate editors nothing reads: {string.Join(", ", spurious)}.");
    }

    [Theory]
    [InlineData(DrawingType.HorizontalLine, 1)]
    [InlineData(DrawingType.VerticalLine, 1)]
    [InlineData(DrawingType.AnchoredVwap, 1)]
    [InlineData(DrawingType.FibRetracement, 2)]
    [InlineData(DrawingType.TextLabel, 2)]
    [InlineData(DrawingType.FibExtension, 3)]
    [InlineData(DrawingType.RiskReward, 3)]
    [InlineData(DrawingType.TrendLine, 4)]
    [InlineData(DrawingType.Channel, 4)]
    [InlineData(DrawingType.Rectangle, 4)]
    [InlineData(DrawingType.MeasureTool, 4)]
    [InlineData(DrawingType.GannFan, 4)]
    [InlineData(DrawingType.GannBox, 4)]
    [InlineData(DrawingType.AngleFib, 4)]
    [InlineData(DrawingType.AndrewsPitchfork, 6)]
    public void TheCoordinateCountIsWhatTheDrawingActuallyHas(DrawingType type, int expected)
    {
        // Hard numbers, on purpose. The two theories above read DrawingAnchorSchema and the
        // markup renders from DrawingAnchorSchema, so between them they can only prove that the
        // dialog follows the schema — widen the schema wrongly and both stay green. These counts
        // were taken from the calculators by hand and are the leg that does not move when the
        // schema does. Every anchor slot is filled in the fixture, so a renderer that keyed off
        // the live values instead would put six editors on all fifteen of these.
        //
        // All fifteen real drawing types are listed, not a sample. A review pointed out that the
        // first version covered seven — and two of the eight it left out, GannFan and
        // FibExtension, are among the types this whole change exists to fix, so the types with
        // the most to lose had only the mirror covering them.
        using var h = new BlazorTestHarness();
        var cut = OpenOn(h, DrawingOfType(type));

        int rendered = cut.FindAll("fieldset input[id^='drawing-price-'], fieldset input[id^='drawing-date-']").Count;
        Assert.Equal(expected, rendered);
    }

    [Fact]
    public void EveryCoordinateEditorIsNamedByItsOwnLabel()
    {
        // A RiskReward's three prices are Entry, Stop loss and Take profit. Three boxes all
        // called "Price" is a form a screen-reader user cannot fill in without counting.
        using var h = new BlazorTestHarness();
        var cut = OpenOn(h, DrawingOfType(DrawingType.RiskReward));

        foreach (var f in DrawingAnchorSchema.For(DrawingType.RiskReward))
        {
            var id = $"drawing-price-{f.Slot}";
            var label = cut.FindAll("label").FirstOrDefault(l => l.GetAttribute("for") == id);
            Assert.True(label is not null, $"#{id} has no <label for>");
            Assert.Equal(f.Label, label!.TextContent.Trim());
        }
    }

    /// <summary>
    /// The series the dialog edits, as it was handed to the store by Apply.
    ///
    /// <para>The dialog edits a <c>Clone()</c> so that Cancel discards — asserting on the
    /// series that was seeded would therefore report every edit as lost, which is what a
    /// review of this code predicted and it is not what happens. The edit lands when Apply
    /// dispatches, and that dispatch is what a user's change actually is.</para>
    /// </summary>
    private static ChartSeries AppliedSeries(BlazorTestHarness h, IRenderedFragment cut, string id)
    {
        cut.Find("button#props-save").Click();

        // WaitForAssertion, not a bare read: a DOM event's handler is queued on the renderer's
        // dispatcher and the synchronous Click() does not always outlive it. On a 24-core box it
        // had finished by the next statement every time; on two cores this file failed five
        // tests in three runs of four, and it took CI's four-core runner down on 2026-09-03.
        // Present before that date and reproducible on the commit CI called green, so it is a
        // latent race in the test rather than a regression in the dialog.
        UpdateSeriesAction? dispatched = null;
        cut.WaitForAssertion(() =>
        {
            dispatched = h.WorkspaceStore.ReceivedCalls()
                .Where(c => c.GetMethodInfo().Name == nameof(IWorkspaceStore.Dispatch))
                .Select(c => c.GetArguments()[0])
                .OfType<UpdateSeriesAction>()
                .LastOrDefault();
            Assert.True(dispatched is not null, "Apply dispatched no UpdateSeriesAction");
        });
        return dispatched!.Series.Single(s => s.Id == id);
    }

    [Fact]
    public void EditingASlotThreeDateIsNotSilentlyDropped()
    {
        // UpdateDrawingPrice had an `anchor == 3` branch and UpdateDrawingDate did not, so the
        // moment a date-3 editor rendered — Andrews Pitchfork is the one type that needs it —
        // the price would save and the date would vanish with no error and no announcement.
        using var h = new BlazorTestHarness();
        var series = DrawingOfType(DrawingType.AndrewsPitchfork);
        var cut = OpenOn(h, series);

        cut.Find("#drawing-price-3").Change("133.25");
        cut.Find("#drawing-date-3").Change("2026-06-15T09:30");

        var applied = AppliedSeries(h, cut, series.Id);
        Assert.Equal(133.25, applied.Drawing!.AnchorPrice3);
        // The field is a wall clock in the USER's zone; the stamp is stored in UTC, which is what
        // SpeechTimeFormatter converts back for every spoken date in the app. Asserting the
        // instant rather than the literal keeps this test honest on a machine in any zone.
        Assert.Equal(
            DateTime.SpecifyKind(new DateTime(2026, 6, 15, 9, 30, 0), DateTimeKind.Local).ToUniversalTime(),
            applied.Drawing!.AnchorDate3!.Value.ToUniversalTime());
    }

    [Fact]
    public void EditingACoordinateOfATypeThatHadNoEditorReachesTheStore()
    {
        // A Gann Fan had no coordinate editors at all, so this whole path is new: the field has
        // to exist, take a value, and survive Apply. Asserting only that the input renders would
        // pass on a box wired to nothing.
        using var h = new BlazorTestHarness();
        var series = DrawingOfType(DrawingType.GannFan);
        var cut = OpenOn(h, series);

        cut.Find("#drawing-price-2").Change("142.75");

        Assert.Equal(142.75, AppliedSeries(h, cut, series.Id).Drawing!.AnchorPrice2);
    }

    [Fact]
    public void LeavingACoordinateSaysWhatItBecame_Once()
    {
        // A number typed into a box with no readback is a change a blind user has to take on
        // trust; every other edit in this app answers. The announcement also names the field,
        // because "105" alone does not say WHICH of three prices moved.
        //
        // It fires on BLUR, not on change, and the count is the assertion. Chromium raises
        // `change` on every arrow-key step of a number input, and AnnouncementEvent interrupts by
        // default — so a readback wired to `change` turns a fourteen-step walk into thirteen
        // clipped fragments and one sentence. This is the narrator's flood defect (2026-09-02) in
        // a new place, and a test that only asserted "something was said" would not see it.
        using var h = new BlazorTestHarness();
        var series = DrawingOfType(DrawingType.RiskReward);
        var cut = OpenOn(h, series);

        var spoken = new List<string>();
        h.EventBus.Subscribe<AnnouncementEvent>(e => spoken.Add(e.Message));

        var field = cut.Find("#drawing-price-2");
        field.Change("98.1");
        field.Change("98.3");
        field.Change("98.5");
        Assert.Empty(spoken);          // three steps, nothing said yet

        field.Blur();

        // Wait for the FIRST utterance, then assert there is exactly one. The "once" half cannot
        // be waited for — a WaitForAssertion around it would pass on its first poll and prove
        // nothing — so it is checked after the positive has settled, and the Assert.Empty above
        // is what actually pins the change-versus-blur distinction this test is named for.
        cut.WaitForAssertion(() => Assert.NotEmpty(spoken));
        var only = Assert.Single(spoken);
        Assert.Contains("Stop loss price", only, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("98.5", only, StringComparison.Ordinal);
    }

    [Fact]
    public void ARejectedCoordinateSaysSoAndMarksTheFieldInvalid()
    {
        // The browser hands a number input's value over as the EMPTY STRING when its content will
        // not parse, so quoting the raw text back produced "Start price unchanged — is not a
        // number" with a silent gap. And after the refusal the model is unchanged, so Blazor
        // emits no DOM update and the field keeps showing the rejected content while the drawing
        // holds the old coordinate. aria-invalid is the signal for exactly that state.
        using var h = new BlazorTestHarness();
        var series = DrawingOfType(DrawingType.RiskReward);
        var cut = OpenOn(h, series);

        var spoken = new List<string>();
        h.EventBus.Subscribe<AnnouncementEvent>(e => spoken.Add(e.Message));

        var field = cut.Find("#drawing-price-1");
        field.Change("");
        field.Blur();

        // The mark and the sentence both arrive on the dispatcher; see AppliedSeries.
        cut.WaitForAssertion(() =>
            Assert.Equal("true", cut.Find("#drawing-price-1").GetAttribute("aria-invalid")));
        cut.WaitForAssertion(() => Assert.NotEmpty(spoken));
        var only = Assert.Single(spoken);
        Assert.Contains("Entry price", only, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("not", only, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"\"", only, StringComparison.Ordinal);
        Assert.Equal(100, series.Drawing!.AnchorPrice1);   // untouched

        // ...and a good value clears the mark.
        field.Change("101.5");
        cut.WaitForAssertion(() => Assert.Null(cut.Find("#drawing-price-1").GetAttribute("aria-invalid")));
    }

    [Fact]
    public void ADateRoundTripsThroughTheFieldUnchanged()
    {
        // The field renders the stored stamp in the user's zone and parses it back out of it.
        // It used to render the stamp RAW while the readback ran through SpeechTimeFormatter,
        // which resolves an Unspecified kind as UTC — so on any machine not on UTC the field
        // showed one time and the spoken confirmation of that same field gave another. Measured
        // when it was found: typing 09:30 was announced as 04:30.
        using var h = new BlazorTestHarness();
        var series = DrawingOfType(DrawingType.TrendLine);
        var cut = OpenOn(h, series);

        var shown = cut.Find("#drawing-date-1").GetAttribute("value");
        Assert.NotNull(shown);

        // What is SPOKEN is the same wall clock the field is SHOWING. This is the half that was
        // wrong: the two came from different conversions, so they disagreed by the UTC offset.
        var spoken = new List<string>();
        h.EventBus.Subscribe<AnnouncementEvent>(e => spoken.Add(e.Message));
        cut.Find("#drawing-date-1").Blur();
        var shownTime = DateTime.Parse(shown!, System.Globalization.CultureInfo.InvariantCulture)
            .ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture);
        cut.WaitForAssertion(() =>
            Assert.Contains(spoken, m => m.Contains(shownTime, StringComparison.Ordinal)));

        // And writing back exactly what the field shows must not move the anchor. (Apply closes
        // the dialog, so this goes last.)
        var before = series.Drawing!.AnchorDate1;
        cut.Find("#drawing-date-1").Change(shown!);
        var applied = AppliedSeries(h, cut, series.Id);
        Assert.Equal(before!.Value.ToUniversalTime(), applied.Drawing!.AnchorDate1!.Value.ToUniversalTime());
    }
}
