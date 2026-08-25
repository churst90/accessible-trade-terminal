using System.Globalization;
using AccessibleTrader.Sdk.Enums;
using SkiaSharp;

namespace AccessibleTrader.Sdk.Theming;

/// <summary>
/// A user-made theme: a built-in theme plus the colours they changed.
///
/// <para>
/// Stored as a SPARSE set of overrides rather than a full copy of every colour, for one reason
/// that matters more than file size — a theme saved today keeps working when a new themeable
/// colour is added tomorrow. A full snapshot would freeze whatever the palette looked like at the
/// moment it was saved, and every new field would arrive in every old theme as black.
/// </para>
///
/// <para>
/// Overrides are keyed by <see cref="ThemeField.Key"/>, which is why those keys are permanent:
/// they are on disk in every theme anyone has saved.
/// </para>
/// </summary>
/// <param name="Id">Stable identifier.</param>
/// <param name="Name">What the user called it.</param>
/// <param name="BasedOn">The built-in theme it starts from; anything not overridden comes from here.</param>
/// <param name="Overrides">Field key → colour, as "#rrggbb" or "#rrggbbaa". An explicit null value
/// means "clear this optional field" — the difference between "no gradient" and "not customised".</param>
public record ThemePreset(
    string Id,
    string Name,
    ThemeType BasedOn,
    IReadOnlyDictionary<string, string?> Overrides)
{
    /// <summary>A new preset carrying no changes yet.</summary>
    public static ThemePreset Create(string name, ThemeType basedOn) =>
        new(Guid.NewGuid().ToString("N"), name, basedOn, new Dictionary<string, string?>(StringComparer.Ordinal));

    /// <summary>
    /// Applies this preset's overrides to its base theme.
    ///
    /// <para>
    /// Unknown keys are IGNORED rather than treated as an error. A theme file written by a newer
    /// version, or one carrying a field since removed, should still load with everything the
    /// current version understands — refusing the whole file over one unrecognised line would
    /// lose the user's work for no benefit.
    /// </para>
    /// </summary>
    public ChartTheme ApplyTo(ChartTheme baseTheme)
    {
        ArgumentNullException.ThrowIfNull(baseTheme);
        var result = baseTheme;

        foreach (var (key, hex) in Overrides)
        {
            var field = ThemeFields.ByKey(key);
            if (field == null) continue;

            if (string.IsNullOrWhiteSpace(hex))
            {
                // An explicitly stored null clears an optional field — that is how a user says
                // "flat background", as distinct from never having touched it.
                if (field.Optional) result = field.Set(result, null);
                continue;
            }

            if (TryParseColor(hex!, out var colour)) result = field.Set(result, colour);
        }

        return result;
    }

    /// <summary>Returns a copy with one field changed. Null clears an optional field.</summary>
    public ThemePreset With(string fieldKey, SKColor? colour)
    {
        var next = new Dictionary<string, string?>(Overrides, StringComparer.Ordinal)
        {
            [fieldKey] = colour is { } c ? ToHex(c) : null,
        };
        return this with { Overrides = next };
    }

    /// <summary>Returns a copy with one field reverted to whatever the base theme says.</summary>
    public ThemePreset WithoutOverride(string fieldKey)
    {
        var next = new Dictionary<string, string?>(Overrides, StringComparer.Ordinal);
        next.Remove(fieldKey);
        return this with { Overrides = next };
    }

    /// <summary>
    /// "#rrggbb", or "#rrggbbaa" when the colour is translucent. Volume bars and value-area fills
    /// carry real alpha, and dropping it would make them opaque blocks over the price data.
    /// </summary>
    public static string ToHex(SKColor c) =>
        c.Alpha == 255
            ? $"#{c.Red:x2}{c.Green:x2}{c.Blue:x2}"
            : $"#{c.Red:x2}{c.Green:x2}{c.Blue:x2}{c.Alpha:x2}";

    /// <summary>
    /// Parses "#rgb", "#rrggbb" or "#rrggbbaa". Hand-written and not delegated to
    /// <c>SKColor.TryParse</c> because that treats a trailing pair as ARGB on some inputs, and a
    /// theme file is user-editable text where a silently-wrong alpha is hard to spot.
    /// </summary>
    public static bool TryParseColor(string hex, out SKColor colour)
    {
        colour = default;
        if (string.IsNullOrWhiteSpace(hex)) return false;

        string s = hex.Trim().TrimStart('#');
        if (s.Length == 3)
            s = string.Concat(s.Select(ch => new string(ch, 2)));

        if (s.Length != 6 && s.Length != 8) return false;
        if (!s.All(Uri.IsHexDigit)) return false;

        byte Byte(int i) => byte.Parse(s.AsSpan(i, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);

        colour = new SKColor(Byte(0), Byte(2), Byte(4), s.Length == 8 ? Byte(6) : (byte)255);
        return true;
    }
}
