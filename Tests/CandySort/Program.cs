using System.Text;
using CandySort;

namespace CandySortVerification;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Contains("--tune-hard")) return GeometryTests.TuneHard(args);
        if (args.Contains("--tune-opening")) return GeometryTests.TuneOpening(args);
        if (args.Contains("--tune-board")) return GeometryTests.TuneBoard(args);
        var failures = new List<string>();
        Run("campaign definitions and boundaries", ModelTests.Campaign, failures);
        Run("model invariants, seeds, invalid moves, undo and complete matches", ModelTests.Matches, failures);
        Run("layout validation, determinism, conservation and restoration", LayoutTests.Run, failures);
        Run("all 30 default boards through actual viewport geometry", () => GeometryTests.Run(args), failures);
        Console.WriteLine($"\n{4 - failures.Count}/4 suites passed.");
        foreach (string failure in failures) Console.Error.WriteLine(failure);
        return failures.Count == 0 ? 0 : 1;
    }

    private static void Run(string name, Action test, List<string> failures)
    {
        try { test(); Console.WriteLine($"PASS {name}"); }
        catch (Exception exception)
        {
            string failure = $"FAIL {name}: {exception}";
            failures.Add(failure);
            Console.Error.WriteLine(failure);
        }
    }
}

internal static class Check
{
    public static void That(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    public static void Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }

    public static string State(CandySortModel model)
    {
        var result = new StringBuilder();
        result.Append($"{model.Level}/{model.TotalCandies}/{model.TargetOrders}/{model.ActivePlates}/{model.CompletedOrders}/{model.SortedCandies}/{model.IsWon}/{model.IsBlocked}/{model.CanUndo}|");
        foreach (var plate in model.Plates) result.Append($"{plate.Active}:{plate.Kind}:{plate.Count},");
        result.Append('|');
        foreach (var kind in model.PendingOrders) result.Append($"{kind},");
        result.Append('|');
        foreach (var kind in model.Temp) result.Append($"{kind},");
        result.Append('|');
        foreach (var bubble in model.Bubbles) result.Append($"{bubble.Id}:{bubble.Lane}:{string.Join(',', bubble.Candies)},");
        result.Append('|');
        foreach (var pair in model.RemainingByKind.OrderBy(pair => pair.Key)) result.Append($"{pair.Key}:{pair.Value},");
        return result.ToString();
    }

    public static void Invalid(CandySortModel model, Func<bool> move)
    {
        string before = State(model);
        That(!move(), "Invalid move succeeded.");
        That(before == State(model), "Invalid move mutated state.");
    }

    public static void Invariants(CandySortModel model)
    {
        That(model.TotalCandies == model.TargetOrders * 3, "Order/candy total mismatch.");
        That(model.Temp.Length == 5 && model.Plates.Count == 4, "Board capacity changed.");
        That(model.Plates.Count(p => p.Active) == model.ActivePlates, "Active plate count mismatch.");
        That(model.CompletedOrders >= 0 && model.CompletedOrders <= model.TargetOrders, "Invalid completion count.");
        That(model.Bubbles.Select(b => b.Id).Distinct().Count() == model.Bubbles.Count, "Duplicate bubble IDs.");
        That(model.Bubbles.All(b => b.Candies.Count > 0 && b.Lane >= 0 && b.Lane < 3), "Empty bubble or invalid lane.");
        That(model.Plates.All(p => p.Count >= 0 && p.Count < 3 && (p.Kind.HasValue || p.Count == 0)), "Invalid plate state.");
        That(model.Plates.Where(p => !p.Active).All(p => !p.Kind.HasValue && p.Count == 0), "Inactive plate holds an order.");
        var assigned = model.Plates.Where(p => p.Kind.HasValue).Select(p => p.Kind.Value).ToArray();
        That(assigned.Distinct().Count() == assigned.Length, "Active plates have duplicate kinds.");
        That(model.CompletedOrders + model.PendingOrders.Count + assigned.Length == model.TargetOrders, "Order conservation failed.");
        int inBubbles = model.Bubbles.Sum(b => b.Candies.Count);
        int temp = model.Temp.Count(c => c.HasValue);
        That(inBubbles + temp + model.SortedCandies == model.TotalCandies, "Candy conservation failed.");
        That(model.SortedCandies == 3 * model.CompletedOrders + model.Plates.Sum(p => p.Count), "Sorted counter incorrect.");
        var remaining = model.RemainingByKind;
        var kinds = Enum.GetValues<CandyKind>();
        That(remaining.Count == 32 && remaining.Keys.OrderBy(k => k).SequenceEqual(kinds), "Remaining inventory must contain exactly the 32 playable candy kinds.");
        var presentKinds = model.Bubbles.SelectMany(b => b.Candies)
            .Concat(model.Temp.Where(c => c.HasValue).Select(c => c.Value))
            .Concat(assigned).Concat(model.PendingOrders);
        That(presentKinds.All(k => (uint)(int)k < (uint)model.Definition.FlavorCount), "Board/order state contains an invalid or locked candy kind.");
        foreach (CandyKind kind in kinds)
        {
            int supply = model.Bubbles.Sum(b => b.Candies.Count(c => c == kind)) + model.Temp.Count(c => c == kind) + model.Plates.Where(p => p.Kind == kind).Sum(p => p.Count);
            int demand = 3 * (model.PendingOrders.Count(c => c == kind) + model.Plates.Count(p => p.Kind == kind));
            That(supply == demand, $"Per-kind order conservation failed for {kind}: {supply}!={demand}.");
            That(remaining[kind] == supply, $"Remaining counter incorrect for {kind}.");
            That(!model.Temp.Any(c => c == kind) || !model.Plates.Any(p => p.Active && p.Kind == kind), "DrainTemp left a routable candy behind.");
        }
        That(remaining.Values.Sum() == model.TotalCandies - model.CompletedOrders * 3, "Remaining total incorrect.");
        bool legal = model.Bubbles.Any(b => b.Candies.Any(c => model.Temp.Any(t => !t.HasValue) || model.Plates.Any(p => p.Active && p.Kind == c))) || model.Temp.Any(c => c.HasValue && model.Plates.Any(p => p.Active && p.Kind == c));
        That(model.IsBlocked == (!model.IsWon && !legal), "Blocked flag disagrees with legal move enumeration.");
        if (model.IsWon) That(model.Bubbles.Count == 0 && temp == 0 && model.PendingOrders.Count == 0 && model.Plates.All(p => !p.Kind.HasValue && p.Count == 0), "Win has leftover state.");
    }
}
