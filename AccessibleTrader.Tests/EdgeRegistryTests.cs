using AccessibleTrader.Sdk.Strategies;
using AccessibleTrader.StrategyLab;
using AccessibleTrader.StrategyLab.Catalogue;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// The edge registry — the research programme's memory.
    ///
    /// <para>
    /// These tests guard the properties that make the registry worth having rather than the
    /// contents of any one record. The load-bearing ones: a scorable edge must name the controls it
    /// beat (otherwise "ControlTested" is a claim, not a test), nulls must survive in the file
    /// (a recorded negative is what stops the next hopeful re-run), and overlaps must resolve
    /// (an unresolvable overlap silently permits double-counting, which is the failure mode this
    /// project has repeated most).
    /// </para>
    /// </summary>
    public class EdgeRegistryTests
    {
        private static EdgeRegistry Registry() => EdgeRegistry.Load();

        [Fact]
        public void TheRegistryShipsBesideTheBinaryAndLoads()
        {
            Assert.True(File.Exists(EdgeRegistry.DefaultPath()),
                $"edges.json is not at {EdgeRegistry.DefaultPath()} — check the csproj copies it to the output.");

            var reg = Registry();

            Assert.Equal(1, reg.SchemaVersion);
            Assert.NotEmpty(reg.Edges);
        }

        [Fact]
        public void TheRegistryIsStructurallySound()
        {
            var problems = Registry().Validate();

            Assert.True(problems.Count == 0,
                "edges.json has structural problems:\n  " + string.Join("\n  ", problems));
        }

        [Fact]
        public void OnlyControlTestedEdgesMayScore()
        {
            var reg = Registry();

            Assert.All(reg.Scorable, e => Assert.Equal(StrategyEvidenceLevel.ControlTested, e.Evidence));
            Assert.All(reg.Edges.Where(e => e.Evidence != StrategyEvidenceLevel.ControlTested),
                e => Assert.False(e.CanScore, $"{e.Id} is {e.Evidence} and must not be scorable."));
            Assert.NotEmpty(reg.Scorable);
        }

        [Fact]
        public void EveryScorableEdgeNamesTheControlsItBeat()
        {
            // The difference between an edge and an opinion is the control arm. An edge marked
            // ControlTested with nothing in `controls` is the latter wearing the former's label.
            var thin = Registry().Scorable.Where(e => e.Controls.Count < 2).Select(e => e.Id).ToList();

            Assert.True(thin.Count == 0,
                "Marked ControlTested without naming at least two controls:\n  " + string.Join("\n  ", thin));
        }

        [Fact]
        public void TheNullsAreKept()
        {
            // Ten of the records are falsified. If this collapses, check that a negative result was
            // not quietly deleted because it was "not useful" — the negatives are the cheapest
            // thing in the registry and they save the most time.
            var falsified = Registry().Edges.Where(e => e.Evidence == StrategyEvidenceLevel.Falsified).ToList();

            Assert.True(falsified.Count >= 8,
                $"Only {falsified.Count} recorded nulls — a negative result may have been dropped.");
            Assert.All(falsified, e => Assert.NotEmpty(e.Controls));
        }

        [Fact]
        public void EveryEdgeIsScopedToAnAssetClass()
        {
            // "It works everywhere" has been wrong every single time it was tested here: volume
            // reverses between crypto and equities, exits only work in crypto, POC reversion only
            // in equities. An unscoped edge is an untested generalisation.
            var unscoped = Registry().Edges.Where(e => e.Scope.AssetClasses.Count == 0).Select(e => e.Id).ToList();

            Assert.True(unscoped.Count == 0, "Unscoped edges: " + string.Join(", ", unscoped));
        }

        [Fact]
        public void RecordedOverlapsResolve_AndAreReportedOnce()
        {
            var reg = Registry();

            foreach (var e in reg.Edges)
                foreach (var link in e.CorrelatesWith)
                    Assert.True(reg[link.Id] != null, $"{e.Id} overlaps unknown edge '{link.Id}'.");

            // Symmetric relationships recorded at both ends must still surface as one pair.
            var pairs = reg.KnownOverlaps()
                .Select(p => string.Join("|", new[] { p.A.Id, p.B.Id }.OrderBy(x => x, StringComparer.Ordinal)))
                .ToList();
            Assert.Equal(pairs.Count, pairs.Distinct().Count());
        }

        [Fact]
        public void TheStrongestEdgeIsRecordedWithItsNumbers()
        {
            // A spot-check that the registry carries the actual measurement and not a summary of a
            // summary: cross-sectional momentum is the project's best result and its p-value and
            // effect size must survive the trip into the file.
            var xs = Registry()["xs-momentum-equities"];

            Assert.NotNull(xs);
            Assert.Equal(StrategyEvidenceLevel.ControlTested, xs!.Evidence);
            // 0.0069, not the 0.0045 recorded until 2026-08-27. The old number was the winner of a
            // 16-cell grid tested against a fixed-configuration null; the re-run reports it against
            // the null of the MAXIMUM over the grid, which is the statistic that was actually
            // computed. The effect size is unchanged — selection inflated the confidence, not the
            // measurement.
            Assert.Equal(0.0069, xs.Effect.P);
            Assert.Equal(0.0037, xs.Effect.Value);
            // The whole point of the re-run: an edge that cannot say how many hypotheses it tried
            // has not earned ControlTested. Sixteen grid cells, all of them reported.
            Assert.Equal(16, xs.Effect.VariantsTried);
            Assert.Contains("equities", xs.Scope.AssetClasses);
            Assert.False(xs.AppliesTo("crypto"));   // the crypto arm is underpowered, not established
        }

        [Fact]
        public void EverySourceDocumentExists()
        {
            // A source that does not resolve makes a verdict unauditable, which is how a finding
            // decays into a rumour.
            var root = RepoRoot();
            var missing = Registry().Edges
                .Where(e => e.Evidence != StrategyEvidenceLevel.Untested)   // queued claims have no verdict yet
                .Where(e => !File.Exists(Path.Combine(root, e.Source)))
                .Select(e => $"{e.Id} → {e.Source}")
                .ToList();

            Assert.True(missing.Count == 0, "Edge sources that do not exist:\n  " + string.Join("\n  ", missing));
        }

        [Fact]
        public void EveryFindingsDocumentIsRepresentedInTheRegistry()
        {
            // The registry's whole purpose is that nothing stays trapped in prose. If a new findings
            // document lands without an edge record, this fails and says which one.
            var root = RepoRoot();
            var docs = Directory.GetFiles(Path.Combine(root, "docs"), "*_FINDINGS.md")
                .Select(f => "docs/" + Path.GetFileName(f))
                .ToList();
            var cited = Registry().Edges.Select(e => e.Source).ToHashSet(StringComparer.OrdinalIgnoreCase);

            var orphans = docs.Where(d => !cited.Contains(d)).ToList();

            Assert.True(orphans.Count == 0,
                "Findings documents with no edge record — the verdict is still trapped in prose:\n  " +
                string.Join("\n  ", orphans));
        }

        [Fact]
        public void TheCliReadsTheRegistry()
        {
            var stdout = Console.Out;
            var stderr = Console.Error;
            Console.SetOut(TextWriter.Null);
            Console.SetError(TextWriter.Null);
            try
            {
                Assert.Equal(0, EdgesCommand.Run(new[] { "validate" }));
                Assert.Equal(0, EdgesCommand.Run(new[] { "list" }));
                Assert.Equal(0, EdgesCommand.Run(new[] { "list", "--class", "crypto" }));
                Assert.Equal(0, EdgesCommand.Run(new[] { "list", "--evidence", "ControlTested" }));
                Assert.Equal(0, EdgesCommand.Run(new[] { "scorable" }));
                Assert.Equal(0, EdgesCommand.Run(new[] { "overlaps" }));
                Assert.Equal(0, EdgesCommand.Run(new[] { "stale", "--days", "30" }));
                Assert.Equal(0, EdgesCommand.Run(new[] { "show", "xs-momentum-equities" }));

                Assert.NotEqual(0, EdgesCommand.Run(new[] { "show", "no-such-edge" }));
                Assert.NotEqual(0, EdgesCommand.Run(new[] { "list", "--evidence", "Excellent" }));
                Assert.NotEqual(0, EdgesCommand.Run(new[] { "nonsense" }));
            }
            finally
            {
                Console.SetOut(stdout);
                Console.SetError(stderr);
            }
        }

        [Fact]
        public void QueuedClaimsAreTraceableAndHaveAProposedTest()
        {
            // The registry holds two kinds of record. A MEASURED edge carries controls, a source and
            // dates. A QUEUED claim (Untested) carries none of those yet — so it must instead carry
            // where the claim came from and what test would settle it. Without the origin an
            // ambiguous result is unresolvable because nobody can re-read what was actually claimed;
            // without a proposed test it is an idea rather than a plan.
            var queued = Registry().Edges.Where(e => e.Evidence == StrategyEvidenceLevel.Untested).ToList();

            Assert.NotEmpty(queued);
            Assert.All(queued, e =>
            {
                Assert.NotNull(e.Origin);
                Assert.False(string.IsNullOrWhiteSpace(e.Origin!.Who), $"{e.Id}: origin names nobody.");
                Assert.False(string.IsNullOrWhiteSpace(e.Origin!.Quote), $"{e.Id}: no quote — the claim cannot be re-read in its own words.");
                Assert.Matches(@"^\d{4}-\d{2}-\d{2}$", e.Origin!.CapturedOn ?? "");
                Assert.NotNull(e.ReMeasure?.Command);
                Assert.False(e.CanScore, $"{e.Id} is queued and must not be scorable.");
            });
        }

        [Fact]
        public void AQueuedClaimThatContradictsAMeasuredOneSaysSo()
        {
            // The 1:3-target claim from the 2026-08-01 video contradicts our own exit finding. That
            // is exactly why it is worth running — but the contradiction has to be recorded, or the
            // test gets designed as though the question were open when half of it is already answered.
            var target = Registry()["fixed-1to3-target"];

            Assert.NotNull(target);
            Assert.Contains(target!.CorrelatesWith, l => l.Id == "fixed-percent-scale-outs");
        }

        [Fact]
        public void EveryClaimedReMeasureCommandActuallyExists()
        {
            // The registry told a lie about itself on 2026-08-01: nineteen edges claimed
            // "implemented: false" when every one of those studies was already a wired command, and
            // two named commands that did not exist at all. A registry whose self-description is
            // wrong is worse than no registry, because the whole point is that it can be trusted
            // without re-deriving it. So the dispatcher is parsed and the claims checked against it.
            string program = File.ReadAllText(Path.Combine(
                RepoRoot(), "AccessibleTrader.StrategyLab", "Program.cs"));
            var wired = new HashSet<string>(
                System.Text.RegularExpressions.Regex
                    .Matches(program, @"""([a-z0-9-]+)""\s*(?:or\s*""[a-z0-9-]+""\s*)?=>")
                    .Select(m => m.Groups[1].Value),
                StringComparer.OrdinalIgnoreCase);

            Assert.NotEmpty(wired);

            var broken = Registry().Edges
                .Where(e => e.ReMeasure?.Implemented == true)
                .Select(e => (e.Id, Verb: e.ReMeasure!.Command?.Split(' ')[0] ?? ""))
                .Where(x => !wired.Contains(x.Verb))
                .Select(x => $"{x.Id} → '{x.Verb}'")
                .ToList();

            Assert.True(broken.Count == 0,
                "Edges claiming a re-measurement command that is not in the lab's dispatcher:\n  " +
                string.Join("\n  ", broken));
        }

        [Fact]
        public void TheFlagshipEdgeRecordsItsReMeasurement()
        {
            // Re-measuring the strongest edge and finding it unchanged is a result, and the decay
            // series is where that result lives. An empty series on the flagship means nobody has
            // checked it since it was found.
            var xs = Registry()["xs-momentum-equities"];

            Assert.NotEmpty(xs!.Decay);
            Assert.All(xs.Decay, d => Assert.Matches(@"^\d{4}-\d{2}-\d{2}$", d.AsOf));
            // The flags matter: the bare verb reproduces a different study on a 3.2-year window.
            Assert.Contains("--universe equity", xs.ReMeasure!.Command);
        }

        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AccessibleTrader.slnx")))
                dir = dir.Parent;
            Assert.NotNull(dir);
            return dir!.FullName;
        }

        // ── Breadth ────────────────────────────────────────────────────────────

        /// <summary>
        /// <b>Every scorable edge must say how widely it held.</b>
        ///
        /// <para>
        /// A p-value answers "could this pooled number have arisen by chance". Breadth answers a
        /// different question that catches a different failure: <b>did it show up in more than one
        /// place?</b> A result significant across thirty symbols but driven by two of them is one
        /// instrument's behaviour wearing a statistic — and "per-symbol and per-era breakdown" is
        /// already a standing control here precisely because that shape has caught us before.
        /// </para>
        ///
        /// <para>
        /// Required only of ControlTested edges, because those are the ones the application is
        /// willing to act on. A falsified edge needs no breadth; it is already dead.
        /// </para>
        /// </summary>
        [Fact]
        public void EveryScorableEdgeRecordsItsBreadth()
        {
            var missing = Registry().Scorable
                .Where(e => e.Breadth == null)
                .Select(e => e.Id)
                .ToList();

            Assert.True(missing.Count == 0,
                "These edges may contribute to a score but do not say on how many independent "
              + "instruments they held: " + string.Join(", ", missing));
        }

        /// <summary>Held cannot exceed tested, and tested cannot be zero if anything held.</summary>
        [Fact]
        public void BreadthCountsAreInternallyConsistent()
        {
            foreach (var e in Registry().Edges.Where(x => x.Breadth != null))
            {
                var b = e.Breadth!;
                Assert.True(b.Held >= 0, $"{e.Id}: negative breadth.");
                Assert.True(b.Tested > 0, $"{e.Id}: {b.Held} held but nothing tested.");
                Assert.True(b.Held <= b.Tested, $"{e.Id}: held {b.Held} of {b.Tested}.");
            }
        }

        /// <summary>
        /// A bare count is not enough. "2 of 4" is a weakness or an asset-class fork depending on
        /// WHICH two, and only the note can say which — the signal-reversed exit held on BTC and ETH
        /// and failed on SPY and QQQ, which is the polarity split rather than noise.
        /// </summary>
        [Fact]
        public void EveryBreadthRecordExplainsItself()
        {
            foreach (var e in Registry().Edges.Where(x => x.Breadth != null))
                Assert.False(string.IsNullOrWhiteSpace(e.Breadth!.Notes),
                    $"{e.Id}: breadth {e.Breadth.Summary} with no note saying which instruments and why.");
        }

        /// <summary>The validator must actually enforce the rule, not merely document it.</summary>
        [Fact]
        public void ValidationRejectsAScorableEdgeWithNoBreadth()
        {
            var reg = new EdgeRegistry
            {
                SchemaVersion = 1,
                RegistryVersion = "test",
                Edges = new[]
                {
                    new Edge
                    {
                        Id = "probe",
                        Title = "Probe",
                        Claim = "A claim long enough to satisfy the falsifiability length check in the validator.",
                        Family = "test",
                        Scope = new EdgeScope { AssetClasses = new[] { "equities" } },
                        Controls = new[] { "one", "two" },
                        Evidence = StrategyEvidenceLevel.ControlTested,
                        FirstMeasured = "2026-01-01",
                        LastMeasured = "2026-01-01",
                        Source = "docs/PROBE.md",
                        Breadth = null,
                    }
                }
            };

            Assert.Contains(reg.Validate(), p => p.Contains("breadth", StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Share is the ranking key for `edges breadth`, so it must be right.</summary>
        [Fact]
        public void ShareIsHeldOverTested()
        {
            Assert.Equal(0.5, new EdgeBreadth { Held = 2, Tested = 4 }.Share!.Value, 6);
            Assert.Equal(1.0, new EdgeBreadth { Held = 51, Tested = 51 }.Share!.Value, 6);
            Assert.Null(new EdgeBreadth { Held = 0, Tested = 0 }.Share);
        }
    }
}
