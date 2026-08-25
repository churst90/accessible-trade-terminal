using AccessibleTrader.Core.Services.Theming;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Theming;
using SkiaSharp;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// User-made themes: the field catalogue, sparse overrides, and round-tripping through a file.
    ///
    /// <para>
    /// The design decision under test is that a saved theme records only what the user CHANGED,
    /// against a named built-in. A full snapshot would be simpler to write and would quietly rot:
    /// every colour added to <see cref="ChartTheme"/> afterwards would arrive in every previously
    /// saved theme as black, because a snapshot cannot know about a field that did not exist when
    /// it was taken.
    /// </para>
    /// </summary>
    public class ThemeCustomizationTests
    {
        // ── The field catalogue ──────────────────────────────────────────

        [Fact]
        public void EveryThemeableColourOnChartTheme_appearsInTheCatalogue()
        {
            // The catalogue is what generates the editor. A colour missing from it is themeable in
            // the renderer and invisible in the UI — which is the exact gap this whole effort
            // started from, so it should not be possible to reintroduce quietly.
            var colourProperties = typeof(ChartTheme).GetProperties()
                .Where(p => p.PropertyType == typeof(SKColor) || p.PropertyType == typeof(SKColor?))
                .Select(p => p.Name)
                .ToHashSet(StringComparer.Ordinal);

            var baseTheme = BaseThemeResolver.Resolve(ThemeType.SteelGray);

            // A field covers a property if setting it changes that property and nothing else.
            var covered = new HashSet<string>(StringComparer.Ordinal);
            foreach (var field in ThemeFields.All)
            {
                var probe = new SKColor(1, 2, 3);
                var after = field.Set(baseTheme, probe);

                foreach (var name in colourProperties)
                {
                    var prop = typeof(ChartTheme).GetProperty(name)!;
                    var before = prop.GetValue(baseTheme);
                    var now = prop.GetValue(after);
                    if (!Equals(before, now)) covered.Add(name);
                }
            }

            var missing = colourProperties.Except(covered).ToList();
            Assert.True(missing.Count == 0,
                "ChartTheme colours with no entry in ThemeFields.All — themeable in the renderer, " +
                "unreachable in the editor:\n  " + string.Join("\n  ", missing));
        }

        [Fact]
        public void EveryFieldHasAKeyALabelAndADescriptionThatSaysWhatItAffects()
        {
            foreach (var f in ThemeFields.All)
            {
                Assert.False(string.IsNullOrWhiteSpace(f.Key), $"{f.Label} has no key");
                Assert.False(string.IsNullOrWhiteSpace(f.Label), $"{f.Key} has no label");

                // The description is not decoration. "Gridlines, minor" means nothing on its own,
                // and it is read aloud to someone who cannot see the result of changing it.
                Assert.True(f.Description.Length > 20,
                    $"{f.Key} has no useful description — it is the only explanation a screen-reader " +
                    "user gets of what this colour does.");
            }
        }

        [Fact]
        public void FieldKeysAreUniqueBecauseTheyAreOnDisk()
        {
            var dupes = ThemeFields.All.GroupBy(f => f.Key).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            Assert.True(dupes.Count == 0, "Duplicate theme field keys: " + string.Join(", ", dupes));
        }

        [Fact]
        public void EveryGroupHasAHumanLabelRatherThanItsEnumName()
        {
            // The failure to catch is a group falling through the switch to ToString(), which
            // renders a heading like "TextAndChrome". A label that HAPPENS to match its enum name
            // — "Dialogs" — is perfectly good English and not the bug; comparing against the enum
            // name flagged it, which is why this checks the shape instead.
            foreach (var group in ThemeFields.All.Select(f => f.Group).Distinct())
            {
                string label = ThemeFields.GroupLabel(group);

                Assert.False(string.IsNullOrWhiteSpace(label));
                Assert.DoesNotContain(label.Skip(1), char.IsUpper);   // PascalCase leaked through
            }
        }

        // ── Sparse overrides ─────────────────────────────────────────────

        [Fact]
        public void AnUntouchedFieldFollowsTheBaseTheme()
        {
            // The whole reason for storing a base plus changes: a theme built on Steel Gray picks
            // up a later improvement to Steel Gray instead of being frozen at whatever it was.
            var preset = ThemePreset.Create("Mine", ThemeType.SteelGray)
                .With("accent", new SKColor(0xFF, 0x00, 0xFF));

            var applied = preset.ApplyTo(BaseThemeResolver.Resolve(ThemeType.SteelGray));
            var baseline = BaseThemeResolver.Resolve(ThemeType.SteelGray);

            Assert.Equal(new SKColor(0xFF, 0x00, 0xFF), applied.Accent);
            Assert.Equal(baseline.Background, applied.Background);
            Assert.Equal(baseline.GridLine, applied.GridLine);
        }

        [Fact]
        public void AnOverriddenFieldWins()
        {
            var preset = ThemePreset.Create("Mine", ThemeType.Blackout)
                .With("chartTop", new SKColor(0x12, 0x34, 0x56));

            Assert.Equal(new SKColor(0x12, 0x34, 0x56),
                preset.ApplyTo(BaseThemeResolver.Resolve(ThemeType.Blackout)).Background);
        }

        [Fact]
        public void ClearingAnOptionalFieldIsDifferentFromNeverTouchingIt()
        {
            // Steel Gray has a chart gradient. "No gradient" has to be expressible, and it is a
            // different thing from "I did not change the gradient".
            var baseline = BaseThemeResolver.Resolve(ThemeType.SteelGray);
            Assert.NotNull(baseline.BackgroundGradientEnd);

            var untouched = ThemePreset.Create("A", ThemeType.SteelGray).ApplyTo(baseline);
            var cleared = ThemePreset.Create("B", ThemeType.SteelGray)
                .With("chartBottom", null).ApplyTo(baseline);

            Assert.NotNull(untouched.BackgroundGradientEnd);
            Assert.Null(cleared.BackgroundGradientEnd);
        }

        [Fact]
        public void ClearingIsRefusedForFieldsWhereNullHasNoMeaning()
        {
            // Nulling a required colour would have to mean something, and there is nothing it
            // could sensibly mean — so it leaves the value alone rather than inventing black.
            var baseline = BaseThemeResolver.Resolve(ThemeType.SteelGray);
            var applied = ThemePreset.Create("A", ThemeType.SteelGray)
                .With("accent", null).ApplyTo(baseline);

            Assert.Equal(baseline.Accent, applied.Accent);
        }

        [Fact]
        public void RevertingAFieldRemovesItRatherThanStoringTheBaseValue()
        {
            // Storing the base value would look identical today and freeze it forever.
            var preset = ThemePreset.Create("Mine", ThemeType.SteelGray)
                .With("accent", new SKColor(1, 2, 3))
                .WithoutOverride("accent");

            Assert.DoesNotContain("accent", preset.Overrides.Keys);
        }

        [Fact]
        public void AnUnknownKeyIsIgnoredRatherThanRejectingTheWholeTheme()
        {
            // A theme file written by a newer version, or carrying a field since removed, should
            // still load with everything this version understands. Refusing it entirely would
            // lose the user's work over one line.
            var preset = new ThemePreset("id", "Mine", ThemeType.SteelGray,
                new Dictionary<string, string?> { ["accent"] = "#ff00ff", ["fromTheFuture"] = "#123456" });

            var applied = preset.ApplyTo(BaseThemeResolver.Resolve(ThemeType.SteelGray));

            Assert.Equal(new SKColor(0xFF, 0x00, 0xFF), applied.Accent);
        }

        // ── Colour parsing ───────────────────────────────────────────────

        [Theory]
        [InlineData("#3A4048", 0x3A, 0x40, 0x48, 255)]
        [InlineData("3A4048", 0x3A, 0x40, 0x48, 255)]
        [InlineData("#abc", 0xAA, 0xBB, 0xCC, 255)]
        [InlineData("#3A404880", 0x3A, 0x40, 0x48, 0x80)]
        public void HexIsParsedInEveryFormAUserMightType(string hex, int r, int g, int b, int a)
        {
            Assert.True(ThemePreset.TryParseColor(hex, out var c));
            Assert.Equal(new SKColor((byte)r, (byte)g, (byte)b, (byte)a), c);
        }

        [Theory]
        [InlineData("")]
        [InlineData("nonsense")]
        [InlineData("#12345")]
        [InlineData("#gggggg")]
        public void NonsenseIsRejectedRatherThanSilentlyBecomingBlack(string hex)
        {
            Assert.False(ThemePreset.TryParseColor(hex, out _));
        }

        [Fact]
        public void AlphaSurvivesTheRoundTrip()
        {
            // Volume bars and value-area fills carry real transparency. Dropping it would turn
            // them into opaque blocks sitting over the price data.
            var translucent = new SKColor(0x77, 0xFF, 0x77, 120);

            Assert.True(ThemePreset.TryParseColor(ThemePreset.ToHex(translucent), out var back));
            Assert.Equal(translucent, back);
        }

        [Fact]
        public void AnOpaqueColourDoesNotGrowAPointlessAlphaPair()
        {
            Assert.Equal("#77ff77", ThemePreset.ToHex(new SKColor(0x77, 0xFF, 0x77)));
        }

        // ── Whole-theme round trip ───────────────────────────────────────

        [Fact]
        public void ATheme_survivesEveryFieldBeingSetAndReadBack()
        {
            // Exercises the catalogue end to end: set every field to a distinct colour, apply,
            // and confirm each one landed where it was supposed to.
            var preset = ThemePreset.Create("Everything", ThemeType.Blackout);
            var expected = new Dictionary<string, SKColor>(StringComparer.Ordinal);

            byte i = 10;
            foreach (var f in ThemeFields.All)
            {
                var colour = new SKColor(i, (byte)(255 - i), (byte)(i * 2), 200);
                expected[f.Key] = colour;
                preset = preset.With(f.Key, colour);
                i += 3;
            }

            var applied = preset.ApplyTo(BaseThemeResolver.Resolve(ThemeType.Blackout));

            foreach (var f in ThemeFields.All)
                Assert.Equal(expected[f.Key], f.Get(applied));
        }

        [Fact]
        public void BaseThemeResolver_returnsTheThemeWithoutTheUsersPreferences()
        {
            // The editor edits the THEME. Folding in the user's own up/down colours or background
            // override would show those as if they belonged to the theme, and then save them into
            // it — baking a personal preference into something meant to be shared.
            var resolved = BaseThemeResolver.Resolve(ThemeType.SteelGray);

            Assert.Equal(ThemeType.SteelGray, resolved.ThemeType);
            Assert.Equal(new SKColor(0x77, 0xFF, 0x77), resolved.CandleBullishBody);
        }

        [Fact]
        public void BaseThemeResolver_isStableAcrossCalls()
        {
            Assert.Equal(BaseThemeResolver.Resolve(ThemeType.Walnut).Background,
                         BaseThemeResolver.Resolve(ThemeType.Walnut).Background);
        }
    }
}
