namespace AccessibleTrader.StrategyLab
{
    /// <summary>
    /// <c>StrategyLab capabilities</c> — what every trading provider declares
    /// against what it implements, offline and without credentials.
    ///
    /// <para>
    /// This is the static half. It answers "does our code do what our code claims".
    /// It cannot answer "does the venue support this" or "is this account eligible",
    /// because eligibility for margin and derivatives is decided per customer — that
    /// needs the live probe and real keys, and no documentation page substitutes for
    /// it.
    /// </para>
    /// </summary>
    public static class ProbeCommand
    {
        public static int Run(string[] args)
        {
            string? outPath = GetFlag(args, "--out");
            string root = GetFlag(args, "--root") ?? FindRepoRoot();

            var audits = ProviderCapabilityAudit.Run(root);
            string md = ProviderCapabilityAudit.ToMarkdown(audits);

            if (outPath != null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))!);
                File.WriteAllText(outPath, md);
                Console.WriteLine($"Wrote {outPath} ({audits.Count} trading providers audited).");
            }
            else Console.WriteLine(md);

            int mismatches = audits.SelectMany(a => a.Findings)
                .Count(f => f.Verdict is Verdict.DeclaredNotBacked or Verdict.BackedNotDeclared
                                      or Verdict.DeclaredPartial);
            int contradictions = audits.Sum(a => a.Contradictions.Count);
            int unparsed = audits.Count(a => !a.CapabilitiesParsed);

            Console.Error.WriteLine(
                $"{audits.Count} providers · {mismatches} claim mismatches · "
              + $"{contradictions} self-contradictions · {unparsed} unparsed");

            // Non-zero only when the audit could not do its job. Findings are the
            // point of the report, not a build failure — the conformance TESTS are
            // what hold the line once each finding is triaged.
            return unparsed > 0 ? 1 : 0;
        }

        private static string? GetFlag(string[] args, string name)
        {
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        private static string FindRepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AccessibleTrader.slnx")))
                dir = dir.Parent;
            return dir?.FullName
                ?? throw new InvalidOperationException("Repo root not found; pass --root explicitly.");
        }
    }
}
