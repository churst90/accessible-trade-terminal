using AccessibleTrader.Core.Services.Audio;
using AccessibleTrader.Core.Services.Rendering;
using AccessibleTrader.Sdk.Models;
using SkiaSharp;

namespace AccessibleTrader.Core.Services.Accessibility
{
    /// <summary>
    /// What a signal marker looks like on the chart, in words: "Large green dot." And what colour a
    /// line is: "Orange line."
    ///
    /// <para>
    /// Cody, 2026-10-22, asked for the descriptors so the signals can be talked about with people who
    /// trade by looking: they say "the big green dot" and "the red triangle above the candle", and a
    /// signal heard only by its name ("Buy") cannot be matched to that. Opt-in under Settings, Speech,
    /// "Say what signals look like" (<see cref="WorkspaceState.DescribeSignalLooks"/>), because it
    /// lengthens every signal it is added to.
    /// </para>
    ///
    /// <para>
    /// Four parts, each read from what the chart draws rather than from what the provider declared:
    /// the shape (<see cref="ComponentConfig.DisplayType"/>; an Arrow's direction is its sign), the
    /// size (<see cref="ComponentConfig.Thickness"/>, which sizes every marker), the colour PAINTED
    /// at that bar (<see cref="StandardRenderers.MarkerColorAt"/>, the renderers' own choice, so a
    /// colour rule or a user's recolour is what is said), and where it sits when it is pinned to the
    /// candle rather than to a value (<see cref="ComponentConfig.MarkerAnchor"/>).
    /// </para>
    ///
    /// <para>
    /// Cody, 2026-10-23: the "Drawn as" in front is gone (it said nothing the description did not),
    /// and every line says its colour too, "so when someone says orange or blue line they can tell
    /// by ear" (<see cref="LineSentence"/>).
    /// </para>
    /// </summary>
    public static class MarkerLook
    {
        /// <summary>Below this a marker is drawn noticeably smaller than the shipped signals
        /// (the default line thickness, 2, is "small").</summary>
        internal const float SmallBelow = 2.5f;
        /// <summary>From here a marker is drawn noticeably larger: Cipher B's big green and red dots
        /// (5) are "large", its ordinary dots (3 and 3.5) carry no size word.</summary>
        internal const float LargeFrom = 4.5f;

        /// <summary>"Large green dot." when the setting is on and a marker is drawn on this bar;
        /// empty otherwise.</summary>
        public static string Sentence(WorkspaceState state, ChartSeries series, ComponentConfig comp, int dataIndex)
            => state.DescribeSignalLooks ? Sentence(series, comp, dataIndex) : "";

        /// <summary>"Large green dot." for a marker drawn on this bar; empty when the component is
        /// not a marker, is hidden, or has nothing on this bar.</summary>
        public static string Sentence(ChartSeries series, ComponentConfig comp, int dataIndex)
        {
            string look = Describe(series, comp, dataIndex);
            return look.Length == 0 ? "" : Capitalise(look) + ".";
        }

        /// <summary>
        /// The display types drawn as a line or an area, which say their colour. Not bars: volume
        /// and histograms are painted by direction on every bar, so their colour word would only
        /// restate up or down, on nearly every bar of a sweep.
        /// </summary>
        private static readonly HashSet<ComponentDisplayType> LineTypes = new()
        {
            ComponentDisplayType.Line, ComponentDisplayType.StepLine, ComponentDisplayType.Oscillator,
            ComponentDisplayType.Area, ComponentDisplayType.ZeroArea,
        };

        /// <summary>
        /// "Orange line." for a visible line or area with a value on this bar. Said on arriving at
        /// the component (<paramref name="fromIndex"/> below zero: the user just moved onto it), and
        /// otherwise only when the colour's NAME differs from the one at <paramref name="fromIndex"/>,
        /// the bar the user was on before this move. So a sweep along an orange line is silent
        /// about it, and a line that turns from green to red says so on the first bar heard in the
        /// new colour, in either direction and after a jump. Empty otherwise.
        ///
        /// <para>
        /// Cody, 2026-11-17: "sometimes I may hear 'faint green line' when I'm far away from a
        /// transition." It compared with the bar to the LEFT, not the bar the user came from, so
        /// arrowing leftward said the old colour one bar before the change and nothing on the
        /// change itself, and a jump said it wherever the landing bar's left neighbour differed.
        /// </para>
        /// </summary>
        public static string LineSentence(ChartSeries series, ComponentConfig comp, int dataIndex, int fromIndex)
        {
            string look = DescribeLine(series, comp, dataIndex);
            if (look.Length == 0) return "";
            if (fromIndex >= 0 && DescribeLine(series, comp, fromIndex) == look) return "";
            return Capitalise(look) + ".";
        }

        /// <summary>"orange line", "orange area" when a fill is drawn under it
        /// (<see cref="StandardRenderers.DrawsFill"/>), or empty. Cody, 2026-11-17: one word for
        /// what is drawn; Cipher B's waves, money flow and wave gap are filled, so they are areas.</summary>
        public static string DescribeLine(ChartSeries series, ComponentConfig comp, int dataIndex)
        {
            if (!comp.IsVisible || !LineTypes.Contains(comp.DisplayType)) return "";
            var data = series.GetComponentData(comp.Name);
            if (data == null || dataIndex < 0 || dataIndex >= data.Length || double.IsNaN(data[dataIndex])) return "";
            string colour = ColorName(StandardRenderers.LineColorAt(series, comp, dataIndex));
            return StandardRenderers.DrawsFill(comp) ? $"{colour} area" : $"{colour} line";
        }

        /// <summary>"large green dot below the candle", or empty.</summary>
        public static string Describe(ChartSeries series, ComponentConfig comp, int dataIndex)
        {
            if (!comp.IsVisible || !AudioConstants.MarkerDisplayTypes.Contains(comp.DisplayType)) return "";
            var data = series.GetComponentData(comp.Name);
            if (data == null || dataIndex < 0 || dataIndex >= data.Length || double.IsNaN(data[dataIndex])) return "";

            string shape = Shape(comp, data[dataIndex]);
            string size = comp.Thickness < SmallBelow ? "small " : comp.Thickness >= LargeFrom ? "large " : "";
            string colour = ColorName(StandardRenderers.MarkerColorAt(series, comp, dataIndex));
            string where = comp.MarkerAnchor switch
            {
                MarkerAnchor.BelowBar => " below the candle",
                MarkerAnchor.AboveBar => " above the candle",
                _ => "",
            };
            return $"{size}{colour} {shape}{where}";
        }

        private static string Shape(ComponentConfig comp, double value) => comp.DisplayType switch
        {
            ComponentDisplayType.Dot or ComponentDisplayType.ZeroDot => "dot",
            // RenderArrow points up at or above zero, down below it.
            ComponentDisplayType.Arrow => value >= 0 ? "up arrow" : "down arrow",
            ComponentDisplayType.TriangleUp => "up triangle",
            ComponentDisplayType.TriangleDown => "down triangle",
            ComponentDisplayType.TriangleUpRing => string.IsNullOrWhiteSpace(comp.MarkerText)
                ? "up triangle with a ring"
                : $"up triangle with a ring marked {comp.MarkerText.Trim()}",
            ComponentDisplayType.Diamond => "diamond",
            ComponentDisplayType.Square => "square",
            // RenderCross draws the two diagonals: an X, not a plus.
            ComponentDisplayType.Cross => "X",
            ComponentDisplayType.CheckBox => "square with a check mark",
            ComponentDisplayType.RefreshBox => "square with circling arrows",
            _ => "marker",
        };

        /// <summary>
        /// The everyday name of a colour: one of white, gray, black, brown, red, orange, yellow, green,
        /// teal, cyan, blue, purple, pink or magenta, with "dark" or "light" in front at the ends of
        /// the lightness range. Tuned on every colour the built-in signal markers use. By hue and lightness rather than nearest-RGB, because a hue boundary is
        /// where people actually change the word: #3FFF00 and #00E676 are both "green" to the eye
        /// although they are far apart in RGB.
        ///
        /// <para>
        /// "faint" in front when the colour is drawn less than <see cref="FaintBelowAlpha"/> opaque.
        /// Cody, 2026-11-15: Cipher A's Red Diamond and Blood Diamond were both "red diamond". They
        /// ARE the same red, #F23645, as Market Cipher A draws them, but the Red Diamond at 55%
        /// opacity, so on the dark chart it is a dimmer, darker red, and smaller; the Blood Diamond
        /// is solid. The hue and lightness above are of the colour itself, not of what shows
        /// through it, so the word for that is "faint", not a darker shade that would change with
        /// the theme behind it.
        /// </para>
        /// </summary>
        public static string ColorName(SKColor c)
        {
            string name = HueName(c);
            return c.Alpha < FaintBelowAlpha ? "faint " + name : name;
        }

        /// <summary>Below this alpha (60%) a colour is said as "faint": Cipher A's Yellow Diamond (30%)
        /// and Red Diamond (55%). Not Cipher B's cross dots (80% and 65%), drawn as plain dots.</summary>
        internal const byte FaintBelowAlpha = 153;

        private static string HueName(SKColor c)
        {
            c.ToHsl(out float h, out float s, out float l);   // h 0–360, s and l 0–100
            if (l >= 94) return "white";
            if (l <= 8) return "black";
            // Below 25% saturation a colour reads as a gray: Cipher C's pending box (#78909C, a
            // "blue gray" at 15%) is gray to the eye, not cyan; #CCCCCC is light gray, not white.
            if (s < 25)
                return l >= 90 ? "white" : l <= 15 ? "black" : l >= 65 ? "light gray" : l <= 35 ? "dark gray" : "gray";

            string hue =
                // #FF3D00 (hue 14), the gradient dot's overbought end, is red to the eye.
                h < 15 || h >= 345 ? "red" :
                h < 42  ? "orange" :
                h < 70  ? "yellow" :
                h < 160 ? "green" :
                h < 182 ? "teal" :
                h < 200 ? "cyan" :
                h < 255 ? "blue" :
                h < 300 ? "purple" :
                h < 320 ? "magenta" :
                // #E91E63 (Material pink, hue 340) is "pink" to the eye, not red.
                "pink";
            // Dark orange reads as brown to most people; say so rather than "dark orange".
            if (hue == "orange" && l < 35) return "brown";   // #8B4513 (saddle brown) is at 31%
            if (l < 30) return "dark " + hue;
            if (l > 75 && hue != "pink") return "light " + hue;   // #EF9A9A at 77%: light red
            return hue;
        }

        private static string Capitalise(string phrase) => char.ToUpperInvariant(phrase[0]) + phrase[1..];
    }
}
