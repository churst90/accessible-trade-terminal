using AccessibleTrader.Core.Models;

namespace AccessibleTrader.Core.Services.GoBar
{
    /// <summary>What a Go bar entry is allowed to do.</summary>
    public enum GoBarKind
    {
        /// <summary>Runs the command.</summary>
        Normal,

        /// <summary>
        /// Moves money. Matching is exact-code or exact-name only: a typo must never arm,
        /// size or place an order.
        /// </summary>
        Money,

        /// <summary>
        /// A key, not a command: navigation, closing, cancelling and the anchor nudges. It is
        /// still named and matched, and choosing it says which key it is.
        /// </summary>
        KeyOnly,
    }

    /// <summary>
    /// One action the terminal can perform, as the Go bar knows it: a spoken name, Bloomberg
    /// style short codes, plain aliases, one line of help and whether it is safe to run from
    /// a text box.
    /// </summary>
    public sealed record GoBarEntry(
        SystemCommand Command,
        string Name,
        IReadOnlyList<string> Codes,
        IReadOnlyList<string> Aliases,
        string Help,
        GoBarKind Kind);

    /// <summary>
    /// Every <see cref="SystemCommand"/> except <see cref="SystemCommand.None"/>, in the
    /// enum's own order so the file can be read beside it. A guard test fails if one is
    /// missing, so a new action cannot skip the Go bar.
    ///
    /// <para>Help lines carry no key. The key comes from the live
    /// <see cref="IShortcutManager"/> profile at run time, because users rebind.</para>
    /// </summary>
    public static class GoBarRegistry
    {
        public static IReadOnlyList<GoBarEntry> All { get; } = Array.Empty<GoBarEntry>();
    }
}
