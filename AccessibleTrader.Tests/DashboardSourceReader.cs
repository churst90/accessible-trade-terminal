namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Reads the trading dashboard's source for the guards that scan it.
    ///
    /// <para>
    /// Shared rather than copied, because two suites now scan this file
    /// (<see cref="MoneyPathSafetyTests"/> for the money path, <see cref="DashboardRefusalScanTests"/>
    /// for silent refusals) and a brace matcher that drifts between them would let one
    /// of them quietly read the wrong method body and pass.
    /// </para>
    /// </summary>
    internal static class DashboardSourceReader
    {
        public static string Source()
        {
            string path = Path.Combine(RepoPaths.RepoRoot(), "AccessibleTrader.BlazorClient.Components",
                                       "TradingDashboardModal.razor");
            Assert.True(File.Exists(path), $"Trading dashboard not found at {path}");
            return File.ReadAllText(path);
        }

        /// <summary>
        /// The source with comments and string literals removed, so a scan cannot be
        /// satisfied — or tripped — by a code shape quoted inside a comment explaining
        /// why it is gone. Several of this file's comments quote the exact defect.
        /// </summary>
        public static string Stripped() =>
            PipelineIdentityAndResilienceTests.StripCommentsAndStrings(Source());

        /// <summary>
        /// One method's body with comments and strings removed. Stripped AFTER the
        /// braces are matched, not before: the stripper rewrites interpolated-string
        /// braces, and matching against that would read the wrong body.
        ///
        /// <para>
        /// Needed because this file's comments deliberately quote the defects they
        /// replaced — "this used to read Store.State.Identity.Provider" is the most
        /// useful line in the method and must not be what a scan trips over.
        /// </para>
        /// </summary>
        public static string MethodStripped(string signature) =>
            PipelineIdentityAndResilienceTests.StripCommentsAndStrings(Method(signature));

        /// <summary>
        /// A member's body, whether it is brace-bodied or expression-bodied.
        ///
        /// <para>
        /// Brace matching alone silently reads the WRONG method for an
        /// <c>=&gt;</c> member: the first <c>{</c> after the signature belongs to
        /// whatever is declared next, so the guard would assert against a body it was
        /// never pointed at and pass or fail for unrelated reasons.
        /// </para>
        /// </summary>
        public static string Member(string signature)
        {
            string src = Source();
            int at = src.IndexOf(signature, StringComparison.Ordinal);
            Assert.True(at >= 0, $"The dashboard no longer declares `{signature}` — re-point this guard.");

            int brace = src.IndexOf('{', at);
            int arrow = src.IndexOf("=>", at, StringComparison.Ordinal);
            if (arrow >= 0 && (brace < 0 || arrow < brace))
                return src.Substring(at, src.IndexOf(';', arrow) - at + 1);

            return Method(signature);
        }

        /// <summary>Brace-matched body of a method in the dashboard's @code block.</summary>
        public static string Method(string signature)
        {
            string src = Source();
            int at = src.IndexOf(signature, StringComparison.Ordinal);
            Assert.True(at >= 0, $"The dashboard no longer declares `{signature}` — re-point this guard.");
            int open = src.IndexOf('{', at);
            Assert.True(open > 0, $"No body found for `{signature}`.");
            int depth = 0;
            for (int i = open; i < src.Length; i++)
            {
                if (src[i] == '{') depth++;
                else if (src[i] == '}' && --depth == 0) return src.Substring(open, i - open + 1);
            }
            throw new Xunit.Sdk.XunitException($"Unbalanced braces reading `{signature}`.");
        }
    }
}
