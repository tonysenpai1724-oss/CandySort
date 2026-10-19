using System.Text.Json;
using CandySort;

namespace CandySortVerification;

internal static class GeometryTests
{
    // These are the live board's chamber, clipping and immutable bubble-size rules.
    private const float Width = 644;
    private const float VisibleHeight = 662;

    public static void Run(string[] args)
    {
        var solutions = new List<LevelTrace>();
        var failures = new List<string>();
        int onlyLevel = 0;
        int index = Array.IndexOf(args, "--level");
        if (index >= 0) onlyLevel = int.Parse(args[index + 1]);
        Console.WriteLine("Geometry: level | exposed per kind (R/O/Y/G/B/P) | matching/visible | buffered | peak temp | moves | overflow px | policy");
        for (int level = 1; level <= 30; level++)
        {
            if (onlyLevel != 0 && level != onlyLevel) continue;
            var initial = new Board(level);
            var exposed = initial.Visible();
            string mix = string.Join('/', Enum.GetValues<CandyKind>().Select(k => exposed.Count(c => c.Kind == k)));
            string exposure = $"{exposed.Count(c => c.Matches)}/{exposed.Count}";
            float overflow = Math.Max(0, LayoutTests.Top(initial.Layout) - VisibleHeight);
            Board winner = null, best = null;
            int winningPolicy = -1;
            var hardMetrics = new List<(int Policy, int Buffered, int Peak)>();
            bool hard = initial.Model.Definition.IsHard;
            for (int policy = 0; policy < (hard ? 6 : 12); policy++)
            {
                var board = policy == 0 ? initial : new Board(level);
                Greedy(board, policy);
                if (best == null || board.Trace.moves.Count > best.Trace.moves.Count) best = board;
                if (!board.Model.IsWon) continue;
                if (hard) hardMetrics.Add((policy, board.Trace.bufferedMoves, board.Trace.peakTemp));
                if (winner == null || board.Trace.bufferedMoves < winner.Trace.bufferedMoves || board.Trace.bufferedMoves == winner.Trace.bufferedMoves && board.Trace.peakTemp < winner.Trace.peakTemp)
                {
                    winner = board;
                    winningPolicy = policy;
                }
                if (!hard) break;
            }
            if (winner == null)
            {
                string reason = $"L{level:D2} UNSOLVED after {(hard ? 6 : 12)} deterministic policies: best {best.Trace.moves.Count}/{best.Model.TotalCandies} moves, completed {best.Model.CompletedOrders}/{best.Model.TargetOrders}, temp {best.TempCount}/5, visible matches {best.Visible().Count(c => c.Matches)}, modelBlocked={best.Model.IsBlocked}; {best.DescribeVisible()}";
                failures.Add(reason);
                Console.Error.WriteLine(reason);
                Console.WriteLine($"L{level:D2} | {mix} | {exposure} | {best.Trace.bufferedMoves} | {best.Trace.peakTemp} | {best.Trace.moves.Count}/{best.Model.TotalCandies} | {overflow:F1} | FAIL");
                continue;
            }
            if (hard)
            {
                int target = level == 5 ? 1 : 2;
                string metrics = string.Join(' ', hardMetrics.Select(m => $"p{m.Policy}:{m.Buffered}/{m.Peak}"));
                Console.WriteLine($"Hard L{level:D2}: successful policies buffered/peak {metrics}");
                if (hardMetrics.Any(m => m.Buffered < target || m.Peak < target))
                {
                    string reason = $"L{level:D2} INSUFFICIENT HARD PRESSURE: required >={target} buffered and peak temp across successful policies; {metrics}";
                    failures.Add(reason);
                    Console.Error.WriteLine(reason);
                }
            }
            Check.That(winner.Trace.moves.Count == winner.Model.TotalCandies, "Winning geometry trace did not consume every candy.");
            Replay(level, winner.Trace);
            solutions.Add(winner.Trace);
            Console.WriteLine($"L{level:D2} | {mix} | {exposure} | {winner.Trace.bufferedMoves} | {winner.Trace.peakTemp} | {winner.Trace.moves.Count} | {overflow:F1} | {winningPolicy}");
        }
        string root = FindRoot();
        string output = Path.Combine(root, "Temp", "CandySortCampaignVerification", onlyLevel == 0 ? "solutions.json" : $"solutions-level-{onlyLevel}.json");
        Directory.CreateDirectory(Path.GetDirectoryName(output));
        File.WriteAllText(output, JsonSerializer.Serialize(new SolutionFile { levels = solutions }, new JsonSerializerOptions { IncludeFields = true, WriteIndented = true }));
        Console.WriteLine($"Verified winning traces: {output} ({solutions.Count} levels). Geometry failures: {failures.Count}.");
        Check.That(failures.Count == 0, string.Join(Environment.NewLine, failures));
        Check.That(solutions.Count == (onlyLevel == 0 ? 30 : 1), "Missing campaign solutions.");
    }

    public static int TuneOpening(string[] args)
    {
        int selectedLevel = 0;
        int index = Array.IndexOf(args, "--level");
        if (index >= 0) selectedLevel = int.Parse(args[index + 1]);
        Check.That(selectedLevel == 0 || selectedLevel >= 5 && selectedLevel <= 30 && selectedLevel % 5 == 0, "Tuning level must be a hard milestone.");
        var definitions = (CandySortLevelDefinition[])typeof(CandySortCampaign)
            .GetField("levels", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static).GetValue(null);
        int missing = 0;
        foreach (int level in selectedLevel == 0 ? new[] { 5, 10, 15, 20, 25, 30 } : new[] { selectedLevel })
        {
            var original = definitions[level - 1];
            int target = level == 5 ? 1 : 2;
            string best = null;
            int tested = 0;
            var watch = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                for (int variant = 0; variant <= 63 && watch.Elapsed.TotalSeconds < 60; variant++)
                {
                    for (int opening = 14; opening <= 18; opening++)
                    {
                        tested++;
                        // Test-process-only override; never edit campaign source or serialize this definition.
                        definitions[level - 1] = new CandySortLevelDefinition(original.Level, original.OrderCount,
                            original.FlavorCount, original.ActivePlates, original.Seed, original.MinBubbleSize,
                            original.MaxBubbleSize, original.Mixing, original.DifficultyLabel, original.HelpText, opening);
                        try
                        {
                            var first = new Board(level, variant);
                            string sizes = string.Join('/', Enumerable.Range(2, 4).Select(size => first.Model.Bubbles.Count(b => b.Candies.Count == size)));
                            int mixed = first.Model.Bubbles.Count(b => b.Candies.Distinct().Count() > 1);
                            int pure = first.Model.Bubbles.Count - mixed;
                            if (pure == 0 || mixed == 0) continue;
                            Greedy(first, 0);
                            if (!first.Model.IsWon || first.Trace.bufferedMoves < target || first.Trace.peakTemp < target) continue;
                            int minBuffered = first.Trace.bufferedMoves, minPeak = first.Trace.peakTemp;
                            var metrics = new List<string> { $"p0:{minBuffered}/{minPeak}" };
                            int wins = 1;
                            bool qualifies = true;
                            for (int policy = 1; policy < 6; policy++)
                            {
                                var board = new Board(level, variant);
                                Greedy(board, policy);
                                metrics.Add($"p{policy}:{(board.Model.IsWon ? "win" : "stuck")}:{board.Trace.bufferedMoves}/{board.Trace.peakTemp}");
                                if (!board.Model.IsWon) continue;
                                wins++;
                                minBuffered = Math.Min(minBuffered, board.Trace.bufferedMoves);
                                minPeak = Math.Min(minPeak, board.Trace.peakTemp);
                                if (minBuffered >= target && minPeak >= target) continue;
                                qualifies = false;
                                break;
                            }
                            if (!qualifies) continue;
                            best = $"L{level:D2} opening={opening}, variant={variant}, canonicalSeed={original.Seed ^ variant}; minBuffer/Peak={minBuffered}/{minPeak}; wins={wins}/6; sizes2/3/4/5={sizes}, mixed={mixed}, pure={pure}; {string.Join(' ', metrics)}";
                            goto Complete;
                        }
                        finally { definitions[level - 1] = original; }
                    }
                }
            Complete:
                if (best == null) { missing++; Console.WriteLine($"NO OPENING RECOMMENDATION L{level:D2}; candidates={tested}, seconds={watch.Elapsed.TotalSeconds:F1}"); }
                else Console.WriteLine($"OPENING RECOMMEND {best}; candidates={tested}, seconds={watch.Elapsed.TotalSeconds:F1}");
            }
            finally { definitions[level - 1] = original; }
        }
        Console.WriteLine($"Opening tuning complete; missing={missing}. Test-process-only overrides restored; no campaign files or traces changed.");
        return missing == 0 ? 0 : 1;
    }

    public static int TuneHard(string[] args)
    {
        int maximumCandidates = 512;
        int selectedLevel = 0;
        int index = Array.IndexOf(args, "--candidates");
        if (index >= 0) maximumCandidates = int.Parse(args[index + 1]);
        index = Array.IndexOf(args, "--level");
        if (index >= 0) selectedLevel = int.Parse(args[index + 1]);
        Check.That(maximumCandidates > 0 && maximumCandidates <= 10000, "Candidate limit must be 1..10000.");
        Check.That(selectedLevel == 0 || selectedLevel >= 5 && selectedLevel <= 30 && selectedLevel % 5 == 0, "Tuning level must be a hard milestone.");
        int missing = 0;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        foreach (int level in new[] { 5, 10, 15, 20, 25, 30 })
        {
            if (selectedLevel != 0 && level != selectedLevel) continue;
            int target = level == 5 ? 1 : 2;
            int baseSeed = CandySortCampaign.GetLevel(level).Seed;
            var stageWatch = System.Diagnostics.Stopwatch.StartNew();
            bool found = false;
            int considered = 0, firstPolicyQualified = 0;
            for (int variant = 1; variant <= maximumCandidates && stageWatch.Elapsed.TotalSeconds < 100; variant++)
            {
                considered++;
                var primary = new Board(level, variant);
                Greedy(primary, 0);
                if (!primary.Model.IsWon || primary.Trace.bufferedMoves < target || primary.Trace.peakTemp < target) continue;
                firstPolicyQualified++;
                var metrics = new List<string> { $"p0:{primary.Trace.bufferedMoves}/{primary.Trace.peakTemp}" };
                bool qualifies = true;
                int minimumBuffered = primary.Trace.bufferedMoves, minimumPeak = primary.Trace.peakTemp;
                for (int policy = 1; policy < 6; policy++)
                {
                    var alternative = new Board(level, variant);
                    Greedy(alternative, policy);
                    metrics.Add($"p{policy}:{(alternative.Model.IsWon ? "win" : "stuck")}:{alternative.Trace.bufferedMoves}/{alternative.Trace.peakTemp}");
                    if (!alternative.Model.IsWon) continue;
                    minimumBuffered = Math.Min(minimumBuffered, alternative.Trace.bufferedMoves);
                    minimumPeak = Math.Min(minimumPeak, alternative.Trace.peakTemp);
                    if (minimumBuffered >= target && minimumPeak >= target) continue;
                    qualifies = false;
                    break;
                }
                if (!qualifies) continue;
                int canonical = baseSeed ^ variant;
                Console.WriteLine($"RECOMMEND L{level:D2}: variant={variant}, canonicalSeed={canonical} (base {baseSeed} XOR {variant}); minBuffer={minimumBuffered}, minPeak={minimumPeak}; {string.Join(' ', metrics)}; candidates={considered}, seconds={stageWatch.Elapsed.TotalSeconds:F1}");
                found = true;
                break;
            }
            if (!found)
            {
                missing++;
                Console.WriteLine($"NO RECOMMENDATION L{level:D2}: candidates={considered}, primaryQualified={firstPolicyQualified}, requiredBuffer/Peak={target}/{target}, seconds={stageWatch.Elapsed.TotalSeconds:F1}.");
            }
        }
        Console.WriteLine($"Tuning finished in {watch.Elapsed.TotalSeconds:F1}s; milestones without a recommendation: {missing}. Campaign sources and normal traces were not changed.");
        return missing == 0 ? 0 : 1;
    }

    private static void Greedy(Board board, int policy)
    {
        while (!board.Model.IsWon)
        {
            var visible = board.Visible();
            var matches = visible.Where(c => c.Matches);
            Candidate chosen;
            if (matches.Any())
            {
                var ranked = (policy % 6) switch
                {
                    0 => matches.OrderBy(c => c.Remaining).ThenBy(c => c.BodyY).ThenBy(c => c.BubbleId),
                    1 => matches.OrderBy(c => c.Remaining / (double)c.Initial).ThenBy(c => c.BodyY).ThenBy(c => c.BubbleId),
                    2 => matches.OrderBy(c => c.BodyY).ThenBy(c => c.Remaining).ThenBy(c => c.BubbleId),
                    3 => matches.OrderByDescending(c => board.Model.Plates.First(p => p.Kind == c.Kind).Count).ThenBy(c => c.Remaining).ThenBy(c => c.BodyY),
                    4 => matches.OrderBy(c => c.BodyY + c.Remaining * 45).ThenBy(c => c.BubbleId),
                    _ => matches.OrderBy(c => c.Remaining).ThenByDescending(c => c.BodyY).ThenBy(c => c.BubbleId)
                };
                chosen = ranked.ThenBy(c => policy < 6 ? c.Index : -c.Index).First();
            }
            else
            {
                int free = 5 - board.TempCount;
                var clearable = visible.GroupBy(c => c.BubbleId)
                    .Where(group => group.Count() == group.First().Remaining && group.Count(c => !c.Matches) <= free)
                    .Select(group => group.First());
                if (!clearable.Any()) return;
                var target = (policy % 3) switch
                {
                    0 => clearable.OrderBy(c => c.Remaining).ThenBy(c => c.BodyY).First(),
                    1 => clearable.OrderBy(c => c.BodyY).ThenBy(c => c.Remaining).First(),
                    _ => clearable.OrderBy(c => c.Remaining / (double)c.Initial).ThenBy(c => c.BodyY).First()
                };
                chosen = visible.Where(c => c.BubbleId == target.BubbleId).OrderBy(c => policy < 6 ? c.Index : -c.Index).First();
            }
            board.Take(chosen);
        }
    }

    private static void Replay(int level, LevelTrace trace)
    {
        var board = new Board(level);
        foreach (var move in trace.moves)
        {
            var candidate = board.Visible().SingleOrDefault(c => c.BubbleId == move.bubbleId && c.Index == move.candyIndex);
            Check.That(candidate != null, $"Trace clicks a clipped/nonexistent candy at level {level}, move {board.Trace.moves.Count}.");
            board.Take(candidate);
        }
        Check.That(board.Model.IsWon, $"Saved trace does not win level {level}.");
        Check.That(board.Trace.bufferedMoves == trace.bufferedMoves && board.Trace.peakTemp == trace.peakTemp, "Trace replay metrics changed.");
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Assets", "00 Scripts", "CandySort", "CandySortModel.cs"))) return directory.FullName;
            directory = directory.Parent;
        }
        throw new InvalidOperationException("Cannot find project root for solution trace output.");
    }

    private sealed class Candidate
    {
        public int BubbleId, Index, Initial, Remaining;
        public CandyKind Kind;
        public bool Matches;
        public float BodyY;
    }

    private sealed class Board
    {
        public readonly CandySortModel Model;
        public readonly CandyBubbleLayout Layout;
        public readonly LevelTrace Trace;
        private readonly Dictionary<int, List<int>> slots;
        private readonly Dictionary<int, int> initial;
        private readonly Dictionary<int, float> radii;
        public int TempCount => Model.Temp.Count(c => c.HasValue);

        public Board(int level, int variantSeed = 0)
        {
            // Normal verification always leaves variantSeed at zero. Only opt-in tuning varies it.
            Model = new CandySortModel(level, variantSeed);
            initial = Model.Bubbles.ToDictionary(b => b.Id, b => b.Candies.Count);
            slots = initial.ToDictionary(p => p.Key, p => Enumerable.Range(0, p.Value).ToList());
            radii = initial.ToDictionary(p => p.Key, p => (144 + p.Value * 16) / 2f);
            Layout = new CandyBubbleLayout(Width, level);
            Layout.Reset(Model.Bubbles.Select(b => new CandyBubbleLayout.Body(b.Id, radii[b.Id])));
            Trace = new LevelTrace { level = level };
            Validate();
        }

        public List<Candidate> Visible()
        {
            var result = new List<Candidate>();
            var positions = Layout.Bodies.ToDictionary(b => b.Id, b => b.Position);
            foreach (var bubble in Model.Bubbles)
            for (int index = 0; index < bubble.Candies.Count; index++)
            {
                int count = initial[bubble.Id];
                float diameter = radii[bubble.Id] * 2;
                // Match Unity's float arithmetic; do not reindex after taking a candy.
                float angle = (slots[bubble.Id][index] * 360f / count + 90) * (MathF.PI / 180f);
                float offsetY = count == 1 ? 0 : MathF.Sin(angle) * diameter * (count <= 3 ? .22f : .28f);
                float centerY = positions[bubble.Id].Y - offsetY;
                if (centerY < 0 || centerY > VisibleHeight) continue;
                result.Add(new Candidate
                {
                    BubbleId = bubble.Id, Index = index, Initial = count, Remaining = bubble.Candies.Count,
                    Kind = bubble.Candies[index], Matches = Model.Plates.Any(p => p.Active && p.Kind == bubble.Candies[index]),
                    BodyY = positions[bubble.Id].Y
                });
            }
            return result;
        }

        public void Take(Candidate candy)
        {
            Check.That(Visible().Any(c => c.BubbleId == candy.BubbleId && c.Index == candy.Index), "Attempt to take invisible candy.");
            Check.That(candy.Matches || TempCount < 5, "Would overflow temporary tray.");
            if (!candy.Matches) Trace.bufferedMoves++;
            // Account for the transient inserted candy even if DrainTemp subsequently drains it.
            Trace.peakTemp = Math.Max(Trace.peakTemp, TempCount + (candy.Matches ? 0 : 1));
            Check.That(Model.TryTake(candy.BubbleId, candy.Index), "Geometry solver's legal move was rejected.");
            slots[candy.BubbleId].RemoveAt(candy.Index);
            if (slots[candy.BubbleId].Count == 0)
            {
                float oldTop = LayoutTests.Top(Layout);
                Layout.Remove(candy.BubbleId);
                Check.That(LayoutTests.Top(Layout) <= oldTop + .3f, "Removing an emptied bubble grew the campaign queue.");
            }
            Trace.moves.Add(new MoveTrace { bubbleId = candy.BubbleId, candyIndex = candy.Index });
            Validate();
        }

        private void Validate()
        {
            Check.Invariants(Model);
            LayoutTests.Validate(Layout, radii);
            Check.That(Model.Bubbles.Select(b => b.Id).Order().SequenceEqual(Layout.Bodies.Select(b => b.Id).Order()), "Model/layout body membership diverged.");
            foreach (var bubble in Model.Bubbles)
                Check.That(slots[bubble.Id].Count == bubble.Candies.Count && slots[bubble.Id].Distinct().Count() == bubble.Candies.Count, "Fixed original candy slots diverged.");
            Check.That(Trace.peakTemp <= 5, "Geometry match overflowed temp tray.");
        }

        public string DescribeVisible() => "visible bubbles " + string.Join(";", Visible().GroupBy(c => c.BubbleId).Select(g => $"{g.Key}:{g.Count()}/{g.First().Remaining} [{string.Join(',', g.Select(c => c.Kind))}]"));
    }

    // Public fields and arrays/lists deliberately match Unity JsonUtility's serialization rules.
    [Serializable] public sealed class SolutionFile { public List<LevelTrace> levels = new(); }
    [Serializable] public sealed class LevelTrace
    {
        public int level;
        public int bufferedMoves;
        public int peakTemp;
        public List<MoveTrace> moves = new();
    }
    [Serializable] public sealed class MoveTrace { public int bubbleId; public int candyIndex; }
}
