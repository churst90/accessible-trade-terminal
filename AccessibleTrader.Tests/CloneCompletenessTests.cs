using System.Collections;
using System.Reflection;
using AccessibleTrader.Sdk.Models;
using Xunit;

namespace AccessibleTrader.Tests;

/// <summary>
/// One test for a whole class of defect: <b>a hand-written clone is a second place every field
/// has to be added, and nothing tells you when you forget.</b>
///
/// <para>
/// The evidence is this repo's own history. Component mute (M) and component narration (N) were
/// both undone by every restart, for as long as each had existed, because
/// <c>IndicatorModelFactory.CloneComponent</c> sat between the saved-state merge and the series
/// and did not copy them (2026-09-05). Each was found by a user report, fixed with a point test
/// naming that one field, and the next field to be added went the same way. Seven such defects,
/// seven point tests, and no reason to think the eighth was not already written.
/// </para>
///
/// <para>
/// This test does not name a field. It enumerates every settable property on every type that
/// declares a hand-written <c>Clone()</c>, sets each to a value that is <i>demonstrably</i>
/// different from the one already there, clones, and diffs. Add a property to any of those
/// types and this goes red until the clone copies it. That retires the point-test genre.
/// </para>
///
/// <para>
/// Three defects were live in the tree when it was written, and all three are the same shape:
/// <list type="bullet">
/// <item><c>ComponentConfig.Clone()</c> dropped <c>MarkerAnchor</c>.</item>
/// <item><c>IndicatorModelFactory.CloneComponent</c> dropped <c>MarkerAnchor</c>,
///       <c>IsUserStyled</c> and <c>SecondaryWaveform</c> — and it ran on EVERY series build,
///       not only a restore, so Market Structure's swing markers were drawn at the value
///       instead of above and below the bar always. That clone is now deleted.</item>
/// <item><c>SeriesConfig.Clone()</c> dropped <c>StringParameters</c> — the dictionary that
///       carries a comparison symbol, an MA type, a pivot period, a threshold mode. It is
///       reached through <c>ChartSeries.Clone()</c>, so a chart edit undone with Ctrl+Z came
///       back with four indicators' string parameters reset to their defaults.</item>
/// </list>
/// </para>
///
/// <para><b>Vacuity.</b> A reflection sweep that silently checks nothing is worse than no test.
/// Two defences: every generated value is asserted to differ from the value it replaces (so a
/// property compared against itself cannot pass), and <see cref="TheCloneableTypeListIsComplete"/>
/// rediscovers the type list from the assembly so a new cloneable type cannot slip past by not
/// being listed.</para>
/// </summary>
public class CloneCompletenessTests
{
    /// <summary>
    /// The types whose <c>Clone()</c> is hand-written and therefore has to be policed.
    /// Kept explicit so the sweep is readable in a failure message; kept honest by
    /// <see cref="TheCloneableTypeListIsComplete"/>, which rediscovers it.
    /// </summary>
    private static readonly Type[] Cloneable =
    {
        typeof(ComponentConfig),
        typeof(SeriesConfig),
        typeof(LevelConfig),
        typeof(CloudFillConfig),
        typeof(ZoneBandConfig),
        typeof(DrawingData),
        typeof(SoundPatch),
        typeof(OscillatorLayer),
        typeof(SeriesDataBuffer),
        typeof(ChartSeries),
    };

    /// <summary>
    /// Properties a clone is RIGHT not to copy, with the reason. A pinned exemption is worth
    /// re-reading before trusting it, so each says what the method is actually for.
    /// </summary>
    private static readonly Dictionary<Type, string[]> DeliberatelyNotCopied = new()
    {
        // SoundPatch.Clone() is "duplicate this patch" as offered by the Sound Designer, not a
        // faithful copy: the duplicate must not share an id with its original (the id is the
        // key components reference a patch by), and it is named "<name> (copy)" so the two are
        // tellable apart in the picker. Everything else about the patch is copied and IS swept.
        [typeof(SoundPatch)] = new[] { "Id", "Name" },
    };

    public static TheoryData<Type> CloneableTypes()
    {
        var d = new TheoryData<Type>();
        foreach (var t in Cloneable) d.Add(t);
        return d;
    }

    [Theory]
    [MemberData(nameof(CloneableTypes))]
    public void EverySettableProperty_SurvivesClone(Type type)
    {
        var clone = type.GetMethod("Clone", BindingFlags.Public | BindingFlags.Instance,
            Type.EmptyTypes);
        Assert.NotNull(clone);

        var original = Activator.CreateInstance(type)!;
        var props = ReflectionFixture.SettableProperties(type);

        // Vacuity floor: a sweep over an empty property list passes trivially. Every one of
        // these types has well over five settable properties; the smallest is OscillatorLayer.
        Assert.True(props.Count >= 5,
            $"{type.Name}: only {props.Count} settable properties discovered — the sweep is not " +
            "reading this type, so a dropped field would pass silently.");

        // Set every property to something demonstrably different from what is already there.
        //
        // ORDER MATTERS, and getting it wrong produces a false positive rather than a miss.
        // ChartSeries exposes Components and CloudFills as get-only pass-throughs to its
        // Config, so filling them before assigning a fresh Config throws the contents away and
        // the sweep reports a drop the clone never made. Assign the composed objects first,
        // then scalars, then fill what can only be filled in place.
        var expected = new Dictionary<string, object?>();
        var skipped = new List<string>();
        var ordered = props.Where(p => p.CanWrite && !ReflectionFixture.IsScalar(p.PropertyType))
            .Concat(props.Where(p => p.CanWrite && ReflectionFixture.IsScalar(p.PropertyType)))
            .Concat(props.Where(p => !p.CanWrite))
            .ToList();
        foreach (var p in ordered)
        {
            object? current = p.GetValue(original);
            object? distinct = ReflectionFixture.DistinctValue(p.PropertyType, current);
            if (distinct == null && p.PropertyType.IsValueType && Nullable.GetUnderlyingType(p.PropertyType) == null)
            {
                skipped.Add(p.Name);
                continue;
            }

            // The per-property vacuity check. If the "distinct" value equals what was already
            // there, this property is being compared against itself and proves nothing.
            Assert.False(ReflectionFixture.Equivalent(current, distinct),
                $"{type.Name}.{p.Name}: the fixture value is equal to the default, so this " +
                "property is not actually under test. Teach DistinctValue about its type.");

            if (p.CanWrite)
            {
                p.SetValue(original, distinct);
            }
            else
            {
                // A get-only collection (ChartSeries exposes several). It is still state a
                // clone has to carry, so fill the instance that is already there.
                if (!ReflectionFixture.PopulateInPlace(p.GetValue(original), distinct))
                {
                    skipped.Add(p.Name);
                    continue;
                }
                distinct = p.GetValue(original);
            }
            expected[p.Name] = distinct;
        }

        Assert.True(skipped.Count == 0,
            $"{type.Name}: no fixture value could be generated for {string.Join(", ", skipped)}. " +
            "Teach DistinctValue about those types rather than leaving them unchecked.");

        var copy = clone!.Invoke(original, null)!;

        var dropped = new List<string>();
        foreach (var p in props)
        {
            if (!expected.TryGetValue(p.Name, out var want)) continue;
            if (DeliberatelyNotCopied.TryGetValue(type, out var exempt) && exempt.Contains(p.Name))
                continue;
            if (!ReflectionFixture.Equivalent(want, p.GetValue(copy))) dropped.Add(p.Name);
        }

        Assert.True(dropped.Count == 0,
            $"{type.Name}.Clone() does not copy: {string.Join(", ", dropped)}. " +
            "A hand-written clone is a second place every field must be added — add these to it. " +
            "This is the defect that undid component mute and narration on every restart.");
    }

    /// <summary>
    /// The list above is a hand-written list, which is the very hazard this file exists to
    /// police. So rediscover it: anything in the SDK model assembly declaring a public
    /// parameterless <c>Clone()</c> that returns its own type must be in the swept list.
    /// </summary>
    [Fact]
    public void TheCloneableTypeListIsComplete()
    {
        var discovered = typeof(ComponentConfig).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false, IsPublic: true })
            .Where(t => t.GetMethod("Clone", BindingFlags.Public | BindingFlags.Instance, Type.EmptyTypes)
                         is { } m && m.ReturnType == t)
            .Where(t => t.GetConstructor(Type.EmptyTypes) != null)
            .ToList();

        // Vacuity floor — if discovery finds nothing, the assertion below is empty.
        Assert.True(discovered.Count >= Cloneable.Length,
            $"discovery found only {discovered.Count} cloneable types; the swept list has " +
            $"{Cloneable.Length}. Reflection is not reading the assembly.");

        var missing = discovered.Where(t => !Cloneable.Contains(t)).Select(t => t.Name).ToList();
        Assert.True(missing.Count == 0,
            $"these types declare a hand-written Clone() and are not swept: {string.Join(", ", missing)}. " +
            "Add them to CloneCompletenessTests.Cloneable.");
    }
}
