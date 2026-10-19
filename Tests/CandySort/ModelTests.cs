using CandySort;

namespace CandySortVerification;

internal static class ModelTests
{
    public static void Campaign()
    {
        Check.That(CandySortCampaign.LevelCount == 30, "Campaign must contain exactly 30 levels.");
        Check.That(Check.State(new CandySortModel()) == Check.State(new CandySortModel(1)), "Default constructor is not campaign level one.");
        foreach (int invalid in new[] { int.MinValue, -1, 0, 31, int.MaxValue })
        {
            Check.Throws<ArgumentOutOfRangeException>(() => CandySortCampaign.GetLevel(invalid));
            Check.Throws<ArgumentOutOfRangeException>(() => new CandySortModel(invalid));
        }
        // Authored progression, independent of the campaign implementation. Seeds are
        // intentionally not frozen here: default boards are instead verified by playing them.
        int[] orders = { 27, 28, 29, 30, 40, 41, 42, 43, 44, 45, 46, 47, 48, 49, 50, 51, 52, 53, 54, 55, 56, 57, 58, 59, 60, 61, 62, 63, 64, 65 };
        int[] flavors = { 2, 3, 3, 4, 6, 4, 5, 5, 5, 6, 5, 5, 5, 5, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6 };
        int[] mixing = { 0, 15, 20, 30, 75, 25, 35, 40, 45, 80, 45, 50, 55, 60, 85, 55, 60, 65, 70, 90, 65, 70, 75, 80, 95, 75, 80, 85, 90, 95 };
        int[] hardOpenings = { 15, 15, 17, 14, 16, 14 };
        var fingerprints = new HashSet<string>();
        for (int level = 1; level <= 30; level++)
        {
            var definition = CandySortCampaign.GetLevel(level);
            Check.That(definition.Level == level, $"Wrong definition level {level}.");
            if (level > 1) Check.That(definition.OrderCount > CandySortCampaign.GetLevel(level - 1).OrderCount, "Campaign candy count must strictly increase.");
            Check.That(definition.OrderCount == orders[level - 1] && definition.FlavorCount == flavors[level - 1] && definition.Mixing == mixing[level - 1], $"Authored generation settings changed at {level}.");
            Check.That(definition.IsHard == (level % 5 == 0), $"Hard stage cadence changed at {level}.");
            Check.That(definition.ActivePlates == (level <= 2 ? 2 : level <= 4 ? 3 : 4), $"Plate unlock progression changed at {level}.");
            Check.That(definition.MinBubbleSize == (level == 1 ? 3 : 2), $"Minimum bubble size changed at {level}.");
            Check.That(definition.MaxBubbleSize == (level <= 2 ? 3 : level == 3 ? 4 : 5), $"Maximum bubble size changed at {level}.");
            string expectedLabel = level <= 3 ? "LÀM QUEN" : definition.IsHard ? "MÀN KHÓ" : level <= 8 ? "DỄ" : level <= 19 ? "TRUNG BÌNH" : "THỬ THÁCH";
            Check.That(definition.DifficultyLabel == expectedLabel, $"Difficulty label changed at {level}.");
            Check.That(definition.Seed != 0, $"Missing authored seed at {level}.");
            Check.That(definition.OpeningTrayCandies == (definition.IsHard ? hardOpenings[level / 5 - 1] : 0), $"Authored opening pressure changed at {level}.");
            Check.That(definition.OrderCount > 0 && definition.FlavorCount >= 2 && definition.FlavorCount <= 6, $"Invalid counts at {level}.");
            Check.That(definition.ActivePlates >= 2 && definition.ActivePlates <= 4 && definition.ActivePlates <= definition.FlavorCount, $"Invalid plate configuration at {level}.");
            Check.That(definition.MinBubbleSize >= 1 && definition.MaxBubbleSize >= definition.MinBubbleSize && definition.MaxBubbleSize <= 5, $"Invalid bubble range at {level}.");
            Check.That(definition.Mixing >= 0 && definition.Mixing <= 100, $"Invalid mixing at {level}.");
            Check.That(!string.IsNullOrWhiteSpace(definition.DifficultyLabel) && !string.IsNullOrWhiteSpace(definition.HelpText), $"Missing level copy at {level}.");
            var type = definition.GetType();
            Check.That(type.GetProperties().All(p => p.SetMethod == null || !p.SetMethod.IsPublic), "Definition exposes public property mutation.");
            Check.That(type.GetFields().All(f => f.IsInitOnly || f.IsLiteral), "Definition exposes mutable public field.");
            var model = new CandySortModel(level);
            Opening(model);
            if (definition.IsHard)
            {
                Check.That(model.Bubbles.Any(b => b.Candies.Distinct().Count() == 1) && model.Bubbles.Any(b => b.Candies.Distinct().Count() > 1), $"Hard board must mix pure and mixed groups at {level}.");
                Check.That(model.Bubbles.Select(b => b.Candies.Count).Distinct().Count() >= 2, $"Hard board lacks size variety at {level}.");
            }
            Check.That(model.Definition.Level == level && model.TargetOrders == definition.OrderCount && model.ActivePlates == definition.ActivePlates, $"Model ignored definition at {level}.");
            Check.That(model.Bubbles.SelectMany(b => b.Candies).Distinct().Count() == definition.FlavorCount, $"Flavor count mismatch at {level}.");
            Check.That(model.Bubbles.All(b => b.Candies.Count <= definition.MaxBubbleSize), $"Bubble exceeds configured maximum at {level}.");
            Check.That(model.Bubbles.Take(Math.Max(0, model.Bubbles.Count - 1)).All(b => b.Candies.Count >= definition.MinBubbleSize), $"Non-tail bubble below minimum at {level}.");
            Check.Invariants(model);
            string fingerprint = Check.State(model);
            Check.That(fingerprints.Add(fingerprint), $"Duplicate campaign board at {level}.");
            Check.That(fingerprint == Check.State(new CandySortModel(level)), $"Default seed unstable at {level}.");
            Check.That(fingerprint == Check.State(new CandySortModel(level, 0)), $"Explicit zero does not select default seed at {level}.");
        }
    }

    private static void Opening(CandySortModel model)
    {
        int available = model.Bubbles.Sum(b => b.Candies.Count(c => !model.Plates.Any(p => p.Kind == c)));
        int expected = Math.Min(model.Definition.OpeningTrayCandies, available);
        Check.That(model.Bubbles.All(b => b.Candies.Count >= 2 && b.Candies.Count <= 5), $"Initial bubble outside 2..5 at level {model.Level}.");
        if (expected == 0) return;
        int served = Math.Min(8, expected);
        int openingCandies = expected + served;
        int groups = openingCandies / 2;
        var opening = model.Bubbles.Take(groups).ToArray();
        Check.That(opening.Sum(b => b.Candies.Count) == openingCandies, "Opening group size conservation failed.");
        Check.That(opening.All(b => b.Candies.Count >= 2 && b.Candies.Count <= 3), "Opening groups must contain 2..3 candies.");
        Check.That(opening.Sum(b => b.Candies.Count(c => !model.Plates.Any(p => p.Kind == c))) == expected, "Opening unserved inventory mismatch.");
        Check.That(model.Plates.All(p => !p.Kind.HasValue || opening.Sum(b => b.Candies.Count(c => c == p.Kind)) <= 2), "Opening contains a complete served triple.");
        Check.That(opening.Any(b => b.Candies.Distinct().Count() > 1), "Hard opening has no mixed groups.");
    }

    public static void Matches()
    {
        int matches = 0, buffered = 0, undoChecks = 0, blockedMatches = 0;
        for (int level = 1; level <= 30; level++)
        foreach (int seed in new[] { 0, 1, 1729, -17, int.MinValue, int.MaxValue })
        {
            var model = new CandySortModel(level, seed);
            Opening(model);
            Check.That(Check.State(model) == Check.State(new CandySortModel(level, seed)), $"Seed unstable at {level}/{seed}.");
            Check.That(Check.State(model) != Check.State(new CandySortModel(level, seed == 1 ? 2 : 1)), $"Seed ignored at {level}/{seed}.");
            string initial = Check.State(model);
            Check.Invalid(model, model.Undo);
            int step = 0;
            var states = new List<string>();
            var actions = new List<(int Bubble, int Index)>();
            while (!model.IsWon)
            {
                Check.Invariants(model);
                Check.Invalid(model, () => model.TryTake(-1, 0));
                Check.Invalid(model, () => model.TryTake(int.MaxValue, 0));
                Check.Invalid(model, () => model.TryTake(model.Bubbles[0].Id, -1));
                Check.Invalid(model, () => model.TryTake(model.Bubbles[0].Id, model.Bubbles[0].Candies.Count));
                Check.Invalid(model, () => model.TryUseTemp(-1));
                Check.Invalid(model, () => model.TryUseTemp(model.Temp.Length));
                for (int slot = 0; slot < model.Temp.Length; slot++)
                {
                    int capturedSlot = slot;
                    Check.Invalid(model, () => model.TryUseTemp(capturedSlot));
                }
                var matching = model.Bubbles.SelectMany(b => b.Candies.Select((c, i) => (Bubble: b.Id, Index: i, Kind: c))).Where(c => model.Plates.Any(p => p.Active && p.Kind == c.Kind)).ToArray();
                Check.That(matching.Length > 0, $"No model-only matching move at {level}/{seed} step {step}.");
                var unmatched = model.Bubbles.SelectMany(b => b.Candies.Select((c, i) => (Bubble: b.Id, Index: i, Kind: c))).Where(c => !model.Plates.Any(p => p.Active && p.Kind == c.Kind)).ToArray();
                // Fill the tray early, then prove matching candies still route through a full tray.
                bool useBuffer = step < 8 && unmatched.Length > 0 && model.Temp.Any(c => !c.HasValue);
                var move = useBuffer ? unmatched[(step + level) % unmatched.Length] : matching[(step + level) % matching.Length];
                if (model.Temp.All(c => c.HasValue) && unmatched.Length > 0)
                {
                    var rejected = unmatched[0];
                    Check.Invalid(model, () => model.TryTake(rejected.Bubble, rejected.Index));
                }
                string before = Check.State(model);
                states.Add(before);
                actions.Add((move.Bubble, move.Index));
                Check.That(model.TryTake(move.Bubble, move.Index), "Chosen legal move rejected.");
                if (useBuffer) buffered++;
                Check.Invariants(model);
                if (model.Bubbles.All(b => b.Id != move.Bubble))
                    Check.Invalid(model, () => model.TryTake(move.Bubble, 0));
                string after = Check.State(model);
                if (step % 7 == 0 || model.CompletedOrders > 0 && step % 11 == 0)
                {
                    Check.That(model.Undo() && before == Check.State(model), "Undo did not exactly restore pre-move state.");
                    Check.That(model.TryTake(move.Bubble, move.Index) && after == Check.State(model), "Replaying undone move was not deterministic.");
                    undoChecks++;
                }
                Check.That(++step <= model.TotalCandies, "Match exceeded available candies.");
            }
            Check.Invariants(model);
            Check.Invalid(model, () => model.TryTake(actions[0].Bubble, 0));
            Check.Invalid(model, () => model.TryUseTemp(0));
            for (int i = states.Count - 1; i >= 0; i--)
                Check.That(model.Undo() && Check.State(model) == states[i], $"Full rewind mismatch at {level}/{seed}/{i}.");
            Check.That(Check.State(model) == initial, "Full rewind did not restore initial board.");
            Check.Invalid(model, model.Undo);
            foreach (var move in actions) Check.That(model.TryTake(move.Bubble, move.Index), "Full replay failed.");
            Check.That(model.IsWon, "Full replay did not win.");
            matches++;

            // Independently consume only unmatched candies until a genuinely full tray blocks.
            var obstruction = new CandySortModel(level, seed);
            while (obstruction.Temp.Any(c => !c.HasValue))
            {
                var candidate = obstruction.Bubbles.SelectMany(b => b.Candies.Select((c, i) => (Bubble: b.Id, Index: i, Kind: c))).FirstOrDefault(c => !obstruction.Plates.Any(p => p.Kind == c.Kind), (Bubble: -1, Index: -1, Kind: CandyKind.Red));
                if (candidate.Bubble < 0) break;
                Check.That(obstruction.TryTake(candidate.Bubble, candidate.Index), "Buffer fill failed.");
                Check.Invariants(obstruction);
            }
            if (obstruction.Temp.All(c => c.HasValue))
            {
                var legal = obstruction.Bubbles.SelectMany(b => b.Candies.Select((c, i) => (Bubble: b.Id, Index: i, Kind: c))).First(c => obstruction.Plates.Any(p => p.Kind == c.Kind));
                Check.That(!obstruction.IsBlocked && obstruction.TryTake(legal.Bubble, legal.Index), "Full temp tray blocked a direct plate match.");
                Check.Invariants(obstruction);
                blockedMatches++;
            }
        }
        Check.That(buffered > 0 && undoChecks > 0 && blockedMatches > 0, "Missing buffer/full-tray/undo coverage.");
        Console.WriteLine($"Model coverage: {matches} complete matches; {buffered} buffered moves; {undoChecks} exact undo/replay checks; {blockedMatches} full-tray direct matches.");
    }
}
