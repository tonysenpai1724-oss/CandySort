// Editor-only verification: run the standalone geometry policies on Unity's actual Mono runtime.
// Deliberately does not reference the net10 fixture, mutate campaign definitions, or drive UI.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CandySort;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class CandySortUnityTrace
{
    private const float Width = 644f;
    private const float VisibleHeight = 662f;
    private const int PolicyLimit = 256;
    private const double SecondsLimit = 180;
    private static readonly string Root = Path.GetDirectoryName(Application.dataPath);
    private static readonly string RequestPath = Path.Combine(Root, "Temp", "CandySortUnityTrace.request");
    private static Task<Result> pending;
    private static CancellationTokenSource cancellation;
    private static int requestedLevel;

    static CandySortUnityTrace()
    {
        EditorApplication.update += Update;
        AssemblyReloadEvents.beforeAssemblyReload += Cancel;
        EditorApplication.quitting += Cancel;
    }

    private static void Cancel()
    {
        if (cancellation != null) cancellation.Cancel();
    }

    private static string OutputPath(int level)
    {
        return Path.Combine(Root, "Temp", "CandySortCampaignVerification", "unity-solutions-level-" + level + ".json");
    }

    private static string ReportPath(int level)
    {
        return Path.Combine(Root, "Temp", "CandySortCampaignVerification", "unity-solutions-level-" + level + ".report.txt");
    }

    private static void Update()
    {
        if (pending != null)
        {
            if (!pending.IsCompleted) return;
            try
            {
                Result result = pending.GetAwaiter().GetResult();
                Require(result.Trace.signature == Signature(requestedLevel), "Campaign changed during solving; refusing stale export.");
                // JsonUtility and editor API calls remain on the editor main thread.
                File.WriteAllText(OutputPath(requestedLevel), JsonUtility.ToJson(result.Trace, true));
                File.WriteAllText(ReportPath(requestedLevel), result.Report);
                UnityEngine.Debug.Log("CandySort Unity trace verified: " + OutputPath(requestedLevel) + "\n" + result.Report);
            }
            catch (Exception exception)
            {
                File.WriteAllText(ReportPath(requestedLevel), "FAILED Unity/Mono trace solve\n" + exception);
                UnityEngine.Debug.LogException(exception);
            }
            finally
            {
                pending = null;
                cancellation.Dispose();
                cancellation = null;
            }
            return;
        }
        if (EditorApplication.isCompiling || !File.Exists(RequestPath)) return;
        try
        {
            string request = File.ReadAllText(RequestPath).Trim();
            File.Delete(RequestPath); // Explicit one-shot trigger; never run on ordinary imports/play.
            int level;
            Require(int.TryParse(request, out level) && level >= 1 && level <= CandySortCampaign.LevelCount,
                "CandySortUnityTrace.request must contain one level number, 1..30.");
            requestedLevel = level;
            Directory.CreateDirectory(Path.GetDirectoryName(OutputPath(level)));
            if (File.Exists(OutputPath(level))) File.Delete(OutputPath(level));
            File.WriteAllText(ReportPath(level), "RUNNING Unity/Mono level " + level + "; max policies=" + PolicyLimit + ", max seconds=" + SecondsLimit + "\n");
            cancellation = new CancellationTokenSource();
            CancellationToken token = cancellation.Token;
            pending = Task.Run(() => Solve(level, token), token);
        }
        catch (Exception exception)
        {
            UnityEngine.Debug.LogException(exception);
        }
    }

    public static string Signature(int level)
    {
        CandySortLevelDefinition definition = CandySortCampaign.GetLevel(level);
        return string.Join(":", new[] { CandySortModel.CandyKindCount, 644, 662, definition.Seed,
            definition.OrderCount, definition.FlavorCount, definition.ActivePlates, definition.MinBubbleSize,
            definition.MaxBubbleSize, definition.Mixing, definition.OpeningTrayCandies });
    }

    private static Result Solve(int level, CancellationToken token)
    {
        Stopwatch watch = Stopwatch.StartNew();
        var report = new StringBuilder("Actual Unity/Mono geometry solve; canonical model seed; strict centers [0,662].\n");
        int bestMoves = 0;
        for (int policy = 0; policy < PolicyLimit && watch.Elapsed.TotalSeconds < SecondsLimit; policy++)
        {
            token.ThrowIfCancellationRequested();
            var board = new Board(level);
            Greedy(board, policy, token, watch);
            bestMoves = Math.Max(bestMoves, board.Trace.moves.Count);
            report.AppendFormat("p{0}: {1}, moves={2}/{3}, buffered={4}, peak={5}, temp={6}, visibleMatches={7}\n",
                policy, board.Model.IsWon ? "WIN" : "stuck", board.Trace.moves.Count, board.Model.TotalCandies,
                board.Trace.bufferedMoves, board.Trace.peakTemp, board.TempCount, board.Visible().Count(c => c.Matches));
            if (!board.Model.IsWon) continue;
            Require(board.Trace.moves.Count == board.Model.TotalCandies, "Winning trace did not consume every candy.");
            int pressure = board.Model.Definition.IsHard ? (level == 5 ? 1 : 2) : 0;
            Require(board.Trace.bufferedMoves >= pressure && board.Trace.peakTemp >= pressure,
                "Winning hard trace does not meet the existing temporary-tray pressure requirement.");
            Replay(level, board.Trace, token);
            board.Trace.policy = policy;
            report.AppendFormat("PASS exact fresh Mono replay; level={0}; policy={1}; seconds={2:F2}; signature={3}\n",
                level, policy, watch.Elapsed.TotalSeconds, board.Trace.signature);
            return new Result { Trace = board.Trace, Report = report.ToString() };
        }
        throw new InvalidOperationException("No winning Unity/Mono policy; best moves=" + bestMoves + "; seconds=" +
            watch.Elapsed.TotalSeconds.ToString("F2") + "\n" + report);
    }

    private static void Greedy(Board board, int policy, CancellationToken token, Stopwatch watch)
    {
        // p0..5 exactly reproduce the original matching/clearing priorities; p6..11 reverse index ties.
        // Later policies change only move ranking, never board seeds, rules, capacities or visibility.
        var random = new System.Random(unchecked(7919 * (policy + 1) + board.Model.Level * 104729));
        while (!board.Model.IsWon && watch.Elapsed.TotalSeconds < SecondsLimit)
        {
            token.ThrowIfCancellationRequested();
            List<Candidate> visible = board.Visible();
            IEnumerable<Candidate> matches = visible.Where(c => c.Matches);
            Candidate chosen;
            if (matches.Any())
            {
                if (policy >= 12)
                {
                    double remainingWeight = (policy % 8) * 22;
                    double heightWeight = 0.25 + ((policy / 8) % 4) * 0.5;
                    double noiseWeight = 20 + ((policy / 32) % 4) * 50;
                    chosen = matches.Select(c => new { Candy = c, Score = c.Remaining * remainingWeight +
                        c.BodyY * heightWeight + random.NextDouble() * noiseWeight })
                        .OrderBy(c => c.Score).ThenBy(c => c.Candy.BubbleId).ThenBy(c => c.Candy.Index).First().Candy;
                }
                else
                {
                    IOrderedEnumerable<Candidate> ranked;
                    switch (policy % 6)
                    {
                        case 0: ranked = matches.OrderBy(c => c.Remaining).ThenBy(c => c.BodyY).ThenBy(c => c.BubbleId); break;
                        case 1: ranked = matches.OrderBy(c => c.Remaining / (double)c.Initial).ThenBy(c => c.BodyY).ThenBy(c => c.BubbleId); break;
                        case 2: ranked = matches.OrderBy(c => c.BodyY).ThenBy(c => c.Remaining).ThenBy(c => c.BubbleId); break;
                        case 3: ranked = matches.OrderByDescending(c => board.Model.Plates.First(p => p.Kind == c.Kind).Count).ThenBy(c => c.Remaining).ThenBy(c => c.BodyY); break;
                        case 4: ranked = matches.OrderBy(c => c.BodyY + c.Remaining * 45).ThenBy(c => c.BubbleId); break;
                        default: ranked = matches.OrderBy(c => c.Remaining).ThenByDescending(c => c.BodyY).ThenBy(c => c.BubbleId); break;
                    }
                    chosen = ranked.ThenBy(c => policy < 6 ? c.Index : -c.Index).First();
                }
            }
            else
            {
                int free = 5 - board.TempCount;
                List<Candidate> clearable = visible.GroupBy(c => c.BubbleId)
                    .Where(group => group.Count() == group.First().Remaining && group.Count(c => !c.Matches) <= free)
                    .Select(group => group.First()).ToList();
                if (clearable.Count == 0) return;
                Candidate target;
                if (policy >= 12)
                    target = clearable.Select(c => new { Candy = c, Score = c.Remaining * (policy % 4) * 45 +
                        c.BodyY + random.NextDouble() * (50 + (policy % 5) * 75) })
                        .OrderBy(c => c.Score).ThenBy(c => c.Candy.BubbleId).First().Candy;
                else
                {
                    switch (policy % 3)
                    {
                        case 0: target = clearable.OrderBy(c => c.Remaining).ThenBy(c => c.BodyY).First(); break;
                        case 1: target = clearable.OrderBy(c => c.BodyY).ThenBy(c => c.Remaining).First(); break;
                        default: target = clearable.OrderBy(c => c.Remaining / (double)c.Initial).ThenBy(c => c.BodyY).First(); break;
                    }
                }
                chosen = visible.Where(c => c.BubbleId == target.BubbleId).OrderBy(c => policy < 6 ? c.Index : -c.Index).First();
            }
            board.Take(chosen);
        }
    }

    private static void Replay(int level, Trace trace, CancellationToken token)
    {
        var board = new Board(level);
        foreach (Move move in trace.moves)
        {
            token.ThrowIfCancellationRequested();
            Candidate candy = board.Visible().SingleOrDefault(c => c.BubbleId == move.bubbleId && c.Index == move.candyIndex);
            Require(candy != null, "Fresh Mono replay clicks a clipped/nonexistent candy at move " + board.Trace.moves.Count);
            board.Take(candy);
        }
        Require(board.Model.IsWon, "Fresh Mono replay did not win.");
        Require(board.Trace.bufferedMoves == trace.bufferedMoves && board.Trace.peakTemp == trace.peakTemp,
            "Fresh Mono replay metrics changed.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static bool Finite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }
    private static float Top(CandyBubbleLayout layout)
    {
        return layout.Bodies.Count == 0 ? 0 : layout.Bodies.Max(b => b.Position.Y + b.Radius);
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
        public readonly Trace Trace;
        private readonly Dictionary<int, List<int>> slots;
        private readonly Dictionary<int, int> initial;
        private readonly Dictionary<int, float> radii;
        public int TempCount { get { return Model.Temp.Count(c => c.HasValue); } }

        public Board(int level)
        {
            Model = new CandySortModel(level); // No seed override or definition mutation.
            initial = Model.Bubbles.ToDictionary(b => b.Id, b => b.Candies.Count);
            slots = initial.ToDictionary(p => p.Key, p => Enumerable.Range(0, p.Value).ToList());
            radii = initial.ToDictionary(p => p.Key, p => (144 + p.Value * 16) / 2f);
            Layout = new CandyBubbleLayout(Width, level);
            Layout.Reset(Model.Bubbles.Select(b => new CandyBubbleLayout.Body(b.Id, radii[b.Id])));
            Trace = new Trace { level = level, signature = Signature(level) };
            Validate();
        }

        public List<Candidate> Visible()
        {
            var result = new List<Candidate>();
            var positions = Layout.Bodies.ToDictionary(b => b.Id, b => b.Position);
            foreach (CandySortModel.Bubble bubble in Model.Bubbles)
            for (int index = 0; index < bubble.Candies.Count; index++)
            {
                int count = initial[bubble.Id];
                float diameter = radii[bubble.Id] * 2;
                float angle = (slots[bubble.Id][index] * 360f / count + 90) * Mathf.Deg2Rad;
                float offsetY = count == 1 ? 0 : Mathf.Sin(angle) * diameter * (count <= 3 ? .22f : .28f);
                float centerY = positions[bubble.Id].Y - offsetY;
                if (centerY < 0 || centerY > VisibleHeight) continue;
                result.Add(new Candidate { BubbleId = bubble.Id, Index = index, Initial = count,
                    Remaining = bubble.Candies.Count, Kind = bubble.Candies[index],
                    Matches = Model.Plates.Any(p => p.Active && p.Kind == bubble.Candies[index]), BodyY = positions[bubble.Id].Y });
            }
            return result;
        }

        public void Take(Candidate candy)
        {
            Require(Visible().Any(c => c.BubbleId == candy.BubbleId && c.Index == candy.Index), "Attempt to take invisible candy.");
            Require(candy.Matches || TempCount < 5, "Would overflow temporary tray.");
            if (!candy.Matches) Trace.bufferedMoves++;
            Trace.peakTemp = Math.Max(Trace.peakTemp, TempCount + (candy.Matches ? 0 : 1));
            Require(Model.TryTake(candy.BubbleId, candy.Index), "Geometry solver's legal move was rejected.");
            slots[candy.BubbleId].RemoveAt(candy.Index);
            if (slots[candy.BubbleId].Count == 0)
            {
                float oldTop = Top(Layout);
                Layout.Remove(candy.BubbleId);
                Require(Top(Layout) <= oldTop + .3f, "Removing an emptied bubble grew the campaign queue.");
            }
            Trace.moves.Add(new Move { bubbleId = candy.BubbleId, candyIndex = candy.Index });
            Validate();
        }

        private void Validate()
        {
            ValidateModel(Model);
            Require(Layout.Bodies.Select(b => b.Id).Distinct().Count() == Layout.Bodies.Count, "Duplicate layout bodies.");
            foreach (CandyBubbleLayout.Body body in Layout.Bodies)
            {
                Require(Finite(body.Position.X) && Finite(body.Position.Y), "Nonfinite body position.");
                Require(body.Radius == radii[body.Id], "Body radius changed after candy removal.");
                Require(Math.Abs(body.Position.X) + body.Radius <= 322.01f && body.Position.Y >= body.Radius - .01f,
                    "Body escaped walls/floor.");
            }
            for (int i = 0; i < Layout.Bodies.Count; i++)
            for (int j = i + 1; j < Layout.Bodies.Count; j++)
            {
                CandyBubbleLayout.Body a = Layout.Bodies[i], b = Layout.Bodies[j];
                double dx = a.Position.X - b.Position.X, dy = a.Position.Y - b.Position.Y;
                Require(Math.Sqrt(dx * dx + dy * dy) >= a.Radius + b.Radius - .251, "Layout bodies overlap: " + a.Id + "/" + b.Id);
            }
            Require(Model.Bubbles.Select(b => b.Id).OrderBy(id => id).SequenceEqual(Layout.Bodies.Select(b => b.Id).OrderBy(id => id)),
                "Model/layout body membership diverged.");
            foreach (CandySortModel.Bubble bubble in Model.Bubbles)
                Require(slots[bubble.Id].Count == bubble.Candies.Count && slots[bubble.Id].Distinct().Count() == bubble.Candies.Count,
                    "Fixed original candy slots diverged.");
            Require(Trace.peakTemp <= 5, "Geometry match overflowed temporary tray.");
        }
    }

    // Same strong model invariants as the package-free standalone fixture; no console/JSON dependency.
    private static void ValidateModel(CandySortModel model)
    {
        Require(model.TotalCandies == model.TargetOrders * 3, "Order/candy total mismatch.");
        Require(model.Temp.Length == 5 && model.Plates.Count == 4, "Board capacity changed.");
        Require(model.Plates.Count(p => p.Active) == model.ActivePlates, "Active plate count mismatch.");
        Require(model.CompletedOrders >= 0 && model.CompletedOrders <= model.TargetOrders, "Invalid completion count.");
        Require(model.Bubbles.Select(b => b.Id).Distinct().Count() == model.Bubbles.Count, "Duplicate bubble IDs.");
        Require(model.Bubbles.All(b => b.Candies.Count > 0 && b.Lane >= 0 && b.Lane < 3), "Empty bubble or invalid lane.");
        Require(model.Plates.All(p => p.Count >= 0 && p.Count < 3 && (p.Kind.HasValue || p.Count == 0)), "Invalid plate state.");
        Require(model.Plates.Where(p => !p.Active).All(p => !p.Kind.HasValue && p.Count == 0), "Inactive plate holds an order.");
        CandyKind[] assigned = model.Plates.Where(p => p.Kind.HasValue).Select(p => p.Kind.Value).ToArray();
        Require(assigned.Distinct().Count() == assigned.Length, "Active plates have duplicate kinds.");
        Require(model.CompletedOrders + model.PendingOrders.Count + assigned.Length == model.TargetOrders, "Order conservation failed.");
        int inBubbles = model.Bubbles.Sum(b => b.Candies.Count), temp = model.Temp.Count(c => c.HasValue);
        Require(inBubbles + temp + model.SortedCandies == model.TotalCandies, "Candy conservation failed.");
        Require(model.SortedCandies == 3 * model.CompletedOrders + model.Plates.Sum(p => p.Count), "Sorted counter incorrect.");
        for (int i = 0; i < CandySortModel.CandyKindCount; i++)
        {
            CandyKind kind = (CandyKind)i;
            int supply = model.Bubbles.Sum(b => b.Candies.Count(c => c == kind)) + model.Temp.Count(c => c == kind) +
                model.Plates.Where(p => p.Kind == kind).Sum(p => p.Count);
            int demand = 3 * (model.PendingOrders.Count(c => c == kind) + model.Plates.Count(p => p.Kind == kind));
            Require(supply == demand, "Per-kind order conservation failed for " + kind);
            Require(model.RemainingByKind[kind] == supply, "Remaining counter incorrect for " + kind);
            Require(!model.Temp.Any(c => c == kind) || !model.Plates.Any(p => p.Active && p.Kind == kind), "DrainTemp left routable candy behind.");
        }
        Require(model.RemainingByKind.Values.Sum() == model.TotalCandies - model.CompletedOrders * 3, "Remaining total incorrect.");
        bool legal = model.Bubbles.Any(b => b.Candies.Any(c => model.Temp.Any(t => !t.HasValue) || model.Plates.Any(p => p.Active && p.Kind == c))) ||
            model.Temp.Any(c => c.HasValue && model.Plates.Any(p => p.Active && p.Kind == c));
        Require(model.IsBlocked == (!model.IsWon && !legal), "Blocked flag disagrees with legal moves.");
        if (model.IsWon)
            Require(model.Bubbles.Count == 0 && temp == 0 && model.PendingOrders.Count == 0 && model.Plates.All(p => !p.Kind.HasValue && p.Count == 0),
                "Win has leftover state.");
    }

    private sealed class Result { public Trace Trace; public string Report; }
    [Serializable] public sealed class Trace
    {
        public int level;
        public string signature;
        public int bufferedMoves;
        public int peakTemp;
        public int policy;
        public List<Move> moves = new List<Move>();
    }
    [Serializable] public sealed class Move { public int bubbleId; public int candyIndex; }
}
