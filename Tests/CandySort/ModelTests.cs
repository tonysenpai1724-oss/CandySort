using CandySort;

namespace CandySortVerification;

internal static class ModelTests
{
    public static void Campaign()
    {
        Check.That(CandySortCampaign.LevelCount == 30, "Campaign must contain exactly 30 levels.");
        BubbleVariety();
        string[] kinds =
        {
            "SwirlLollipop", "PurpleJellybean", "LemonCandy", "GummyBearRed",
            "CandyCane", "PeppermintMint", "OrangeWrappedCandy", "BlueberryCandy",
            "ChocolateBar", "Marshmallow", "RainbowLollipop", "GreenJellybean",
            "ChocolateTart", "GummyWorm", "RockCandyStick", "StrawberryWrappedCandy",
            "SprinkleCandyBall", "CottonCandy", "CaramelCube", "GummyBearGreen",
            "CaramelToffee", "PeppermintSwirl", "PinkCandyBall", "BlackLicoriceTwist",
            "OrangeCandyBlueGum", "StripedLollipop", "ChocolateFudge", "GummyFish",
            "HeartCandy", "RedCandyBall", "WaferBar", "LemonWedge"
        };
        Check.That(CandySortModel.CandyKindCount == 32 && Enum.GetValues<CandyKind>().Length == 32, "Exactly 32 playable kinds required, with no Count sentinel.");
        Check.That(Enum.GetNames<CandyKind>().SequenceEqual(kinds) && Enum.GetValues<CandyKind>().Select(k => (int)k).SequenceEqual(Enumerable.Range(0, 32)), "Candy enum identities/sheet-order mapping changed.");
        Check.That(Check.State(new CandySortModel()) == Check.State(new CandySortModel(1)), "Default constructor is not campaign level one.");
        foreach (int invalid in new[] { int.MinValue, -1, 0, 31, int.MaxValue })
        {
            Check.Throws<ArgumentOutOfRangeException>(() => CandySortCampaign.GetLevel(invalid));
            Check.Throws<ArgumentOutOfRangeException>(() => new CandySortModel(invalid));
        }
        // Authored progression, independent of the campaign implementation. Seeds are
        // intentionally not frozen here: default boards are instead verified by playing them.
        int[] orders = { 10, 28, 29, 30, 40, 41, 42, 43, 44, 45, 46, 47, 48, 49, 50, 51, 52, 53, 54, 55, 56, 57, 58, 59, 60, 61, 62, 63, 64, 65 };
        int[] flavors = { 3, 7, 8, 8, 9, 9, 10, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31, 32 };
        int[] mixing = { 0, 15, 20, 30, 75, 25, 35, 40, 45, 80, 45, 50, 55, 60, 85, 55, 60, 65, 70, 90, 65, 70, 75, 80, 95, 75, 80, 85, 90, 95 };
        int[] hardOpenings = { 15, 16, 14, 16, 14, 15 };
        var fingerprints = new HashSet<string>();
        var campaignKinds = new HashSet<CandyKind>();
        for (int level = 1; level <= 30; level++)
        {
            var definition = CandySortCampaign.GetLevel(level);
            Check.That(definition.Level == level, $"Wrong definition level {level}.");
            if (level > 1) Check.That(definition.OrderCount > CandySortCampaign.GetLevel(level - 1).OrderCount, "Campaign candy count must strictly increase.");
            Check.That(definition.OrderCount == orders[level - 1] && definition.FlavorCount == flavors[level - 1] && definition.Mixing == mixing[level - 1], $"Authored generation settings changed at {level}.");
            Check.That(definition.IsHard == (level % 5 == 0), $"Hard stage cadence changed at {level}.");
            Check.That(definition.ActivePlates == (level <= 2 ? 2 : level <= 4 ? 3 : 4), $"Plate unlock progression changed at {level}.");
            Check.That(definition.MinBubbleSize == (level <= 4 ? 1 : 2), $"Minimum bubble size changed at {level}.");
            Check.That(definition.MaxBubbleSize == 5, $"Maximum bubble size changed at {level}.");
            string expectedLabel = level <= 3 ? "LÀM QUEN" : definition.IsHard ? "MÀN KHÓ" : level <= 8 ? "DỄ" : level <= 19 ? "TRUNG BÌNH" : "THỬ THÁCH";
            Check.That(definition.DifficultyLabel == expectedLabel, $"Difficulty label changed at {level}.");
            Check.That(definition.Seed != 0, $"Missing authored seed at {level}.");
            Check.That(definition.OpeningTrayCandies == (definition.IsHard ? hardOpenings[level / 5 - 1] : 0), $"Authored opening pressure changed at {level}.");
            Check.That(definition.OrderCount >= definition.FlavorCount && definition.FlavorCount >= 2 && definition.FlavorCount <= CandySortModel.CandyKindCount, $"Invalid counts at {level}.");
            Check.That(definition.ActivePlates >= 2 && definition.ActivePlates <= 4 && definition.ActivePlates <= definition.FlavorCount, $"Invalid plate configuration at {level}.");
            Check.That(definition.MinBubbleSize >= 1 && definition.MaxBubbleSize >= definition.MinBubbleSize && definition.MaxBubbleSize <= 5, $"Invalid bubble range at {level}.");
            Check.That(definition.Mixing >= 0 && definition.Mixing <= 100, $"Invalid mixing at {level}.");
            Check.That(!string.IsNullOrWhiteSpace(definition.DifficultyLabel) && !string.IsNullOrWhiteSpace(definition.HelpText), $"Missing level copy at {level}.");
            var type = definition.GetType();
            Check.That(type.GetProperties().All(p => p.SetMethod == null || !p.SetMethod.IsPublic), "Definition exposes public property mutation.");
            Check.That(type.GetFields().All(f => f.IsInitOnly || f.IsLiteral), "Definition exposes mutable public field.");
            var model = new CandySortModel(level);
            Opening(model);
            campaignKinds.UnionWith(model.Bubbles.SelectMany(b => b.Candies));
            if (level <= 4)
            {
                Check.That(model.Bubbles.Any(b => b.Candies.Count >= 4), $"Early board lacks larger bubbles at {level}.");
                Check.That(model.Bubbles.Select(b => b.Candies.Count).Distinct().Count() >= 2, $"Early board lacks size variety at {level}.");
            }
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
        Check.That(campaignKinds.SetEquals(Enum.GetValues<CandyKind>()), "Campaign does not generate all 32 candy kinds.");
        Check.That(new CandySortModel(1).TotalCandies == 30 && new CandySortModel(30).TotalCandies == 195, "Campaign candy-total boundaries changed.");
        Check.That(CandySortCampaign.GetLevel(29).FlavorCount == 31 && CandySortCampaign.GetLevel(30).FlavorCount == 32, "Finale must unlock the final candy and include the complete catalog.");
    }

    private static void BubbleVariety()
    {
        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var build = typeof(CandySortModel).GetMethod("BuildBubbles", flags);
        var randomType = typeof(CandySortModel).GetNestedType("DeterministicRandom", System.Reflection.BindingFlags.NonPublic);
        int[][] inventories = { new[] { 12 }, new[] { 7, 5 }, new[] { 10, 1, 1 }, new[] { 2, 1, 1, 1 }, new[] { 1, 1, 1, 1, 1 }, new[] { 1, 1, 1, 1, 1, 1 }, new[] { 4, 4, 4, 4, 4, 4 } };
        foreach (int level in new[] { 2, 5, 9, 15, 30 })
        foreach (int[] counts in inventories)
        foreach (uint seed in Enumerable.Range(1, 16).Select(seed => (uint)seed))
        {
            string previous = null;
            for (int repeat = 0; repeat < 2; repeat++)
            {
                var model = new CandySortModel(level);
                var groups = (List<CandySortModel.Bubble>)typeof(CandySortModel).GetField("bubbles", flags).GetValue(model);
                groups.Clear();
                var inventory = counts.SelectMany((count, kind) => Enumerable.Repeat((CandyKind)kind, count)).ToList();
                var expected = inventory.OrderBy(kind => kind).ToArray();
                build.Invoke(model, new[] { (object)inventory, Activator.CreateInstance(randomType, seed) });
                Check.That(inventory.Count == 0 && groups.Count > 0, "Varied bubble builder did not consume its inventory.");
                Check.That(groups.SelectMany(b => b.Candies).OrderBy(kind => kind).SequenceEqual(expected), "Varied bubble builder changed per-kind inventory.");
                Check.That(groups.All(b => b.Candies.Count >= model.Definition.MinBubbleSize && b.Candies.Count <= 5), "Varied bubble builder emitted an invalid tail.");
                Check.That(groups.Where(b => b.Candies.Count >= 4).All(b => b.Candies.Distinct().Count() >= 3 && b.Candies.Distinct().Count() <= 4), "Varied bubble builder failed its large-group diversity target.");
                SmallBubbleVariety(groups);
                string current = string.Join('|', groups.Select(b => $"{b.Id}/{b.Lane}:{string.Join(',', b.Candies)}"));
                Check.That(repeat == 0 || current == previous, "Varied bubble builder is not deterministic.");
                previous = current;
            }
        }
    }

    private static void SmallBubbleVariety(IReadOnlyList<CandySortModel.Bubble> bubbles)
    {
        var remaining = bubbles.SelectMany(b => b.Candies).GroupBy(kind => kind).ToDictionary(group => group.Key, group => group.Count());
        int small = 0;
        foreach (var bubble in bubbles)
        {
            int size = bubble.Candies.Count;
            if (size == 2 || size == 3)
            {
                bool mixed = small++ % 2 == 0;
                int kinds = bubble.Candies.Distinct().Count();
                if (mixed && remaining.Count >= 2)
                    Check.That(kinds >= 2, "Small mixed bubble contains only one kind despite available alternatives.");
                else if (!mixed && remaining.Values.Any(count => count >= size))
                    Check.That(kinds == 1, "Small matching bubble was unnecessarily mixed.");
            }
            foreach (CandyKind kind in bubble.Candies)
            {
                remaining[kind]--;
                if (remaining[kind] == 0) remaining.Remove(kind);
            }
        }
    }

    private static void Opening(CandySortModel model)
    {
        Check.That(model.Bubbles.SelectMany(b => b.Candies).Distinct().OrderBy(c => c).Select(c => (int)c).SequenceEqual(Enumerable.Range(0, model.Definition.FlavorCount)), $"Unlocked flavor pool is not exactly the intended enum prefix at level {model.Level}.");
        var remaining = model.RemainingByKind;
        Check.That(remaining.Count == 32 && remaining.Keys.OrderBy(k => k).SequenceEqual(Enum.GetValues<CandyKind>()), "RemainingByKind must include exactly all 32 playable kinds, including locked/zero kinds.");
        foreach (int invalidKind in new[] { int.MinValue, -1, 32, int.MaxValue })
        {
            Check.That(!remaining.ContainsKey((CandyKind)invalidKind), $"Inventory accepts out-of-range candy kind {invalidKind}.");
            Check.Throws<KeyNotFoundException>(() => { _ = remaining[(CandyKind)invalidKind]; });
        }
        int minimumPerKind = model.TargetOrders / model.Definition.FlavorCount * 3;
        foreach (CandyKind kind in Enum.GetValues<CandyKind>())
        {
            bool unlocked = (int)kind < model.Definition.FlavorCount;
            int inventory = remaining[kind];
            Check.That(unlocked ? inventory >= 3 && inventory % 3 == 0 : inventory == 0, $"Unlocked/locked inventory mismatch for {kind} at level {model.Level}.");
            if (unlocked)
                Check.That(inventory == minimumPerKind || inventory == minimumPerKind + 3, $"Unbalanced initial order inventory for {kind} at level {model.Level}.");
        }
        Check.That(model.PendingOrders.Concat(model.Plates.Where(p => p.Kind.HasValue).Select(p => p.Kind.Value)).Distinct().OrderBy(k => k).Select(k => (int)k).SequenceEqual(Enumerable.Range(0, model.Definition.FlavorCount)), $"Initial orders do not cover the unlocked prefix at level {model.Level}.");
        int available = model.Bubbles.Sum(b => b.Candies.Count(c => !model.Plates.Any(p => p.Kind == c)));
        int expected = Math.Min(model.Definition.OpeningTrayCandies, available);
        Check.That(available > 0 && model.Definition.FlavorCount > model.ActivePlates, $"Initial flavor pool must include candies without an active plate at level {model.Level}.");
        if (model.Level >= 2 && model.Level <= 6)
            Check.That(model.Definition.FlavorCount - model.ActivePlates <= model.Temp.Length, $"Early unmatched flavor count exceeds tray capacity at level {model.Level}.");
        if (model.Level == 2)
            Check.That(model.TotalCandies == 84 && remaining.Values.Where(count => count > 0).All(count => count == 12), "Level two must spread its 84 candies across seven balanced flavors.");
        int minimumBubbleSize = model.Level <= 4 ? 1 : 2;
        Check.That(model.Bubbles.All(b => b.Candies.Count >= minimumBubbleSize && b.Candies.Count <= 5), $"Initial bubble outside {minimumBubbleSize}..5 at level {model.Level}.");
        if (model.Level >= 2)
        {
            Check.That(model.Bubbles.Where(b => b.Candies.Count >= 4).All(b => b.Candies.Distinct().Count() >= 3 && b.Candies.Distinct().Count() <= 4), $"Large bubble must contain 3..4 distinct flavors at level {model.Level}.");
            int openingGroups = (expected + Math.Min(8, expected)) / 2;
            var regular = model.Bubbles.Skip(openingGroups).ToArray();
            SmallBubbleVariety(regular);
            var small = regular.Where(b => b.Candies.Count == 2 || b.Candies.Count == 3).ToArray();
            Check.That(small.Any(b => b.Candies.Distinct().Count() == 1) && small.Any(b => b.Candies.Distinct().Count() > 1), $"Level {model.Level} must include both mixed and matching small bubbles.");
        }
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

    public static void Boosters()
    {
        MagnetTargets();
        MagnetCascades();
        ShuffleBoards();
        BoosterNoops();
        int matches = 0;
        foreach (int level in Enumerable.Range(1, 30))
        foreach (int seed in new[] { 0, 1729, -17 })
        {
            MixedBoosters(new CandySortModel(level, seed), new CandySortModel(level, seed));
            matches++;
        }
        // A staged tray match also exercises the explicit temp action in mixed sequences.
        CandySortModel Staged() => BoosterFixture(new[] { new[] { 0, 1, 0, 4 }, new[] { 2, 3, 0, 1 },
            new[] { 4, 0, 2, 3 }, new[] { 0, 1, 2, 3 } },
            new[] { (0, 0), (1, 0), (2, 0), (3, 0) }, new[] { 4, 0 },
            new int?[] { 0, 4 }, validate: false);
        MixedBoosters(Staged(), Staged(), useTemp: true);
        Console.WriteLine($"Booster coverage: {matches + 1} mixed complete matches with deterministic replay and tray-only undo; 150 seeded shuffle boards; 128 two-slot shuffles; target/tie/cascade and no-op fixtures.");
    }

    private static void MagnetTargets()
    {
        var model = BoosterFixture(new[] { new[] { 2, 0, 2, 1 }, new[] { 3, 2, 1, 0 },
            new[] { 0, 3, 3 }, new[] { 2, 3 } },
            new[] { (0, 0), (1, 1), (2, 2), (3, 2) }, new[] { 2, 3 });
        Check.That(model.MagnetPlateIndex == 2, "Magnet did not break the most-filled tie by index.");
        var replay = BoosterFixture(new[] { new[] { 2, 0, 2, 1 }, new[] { 3, 2, 1, 0 },
            new[] { 0, 3, 3 }, new[] { 2, 3 } },
            new[] { (0, 0), (1, 1), (2, 2), (3, 2) }, new[] { 2, 3 });
        uint random = ShuffleState(model);
        Check.That(model.TryMagnet(), "Magnet rejected a completable partial plate.");
        Check.That(model.CompletedOrders == 1 && model.Plates[2].Kind == (CandyKind)2 && model.Plates[2].Count == 0, "Magnet continued into the replacement order.");
        Check.That(BoardKinds(model) == "0,2,1|3,2,1,0|0,3,3|2,3", "Magnet did not remove exactly the first needed board candy.");
        Check.That(model.Plates[3].Count == 2 && model.MagnetPlateIndex == 3, "Magnet changed an unchosen tied plate.");
        Check.That(ShuffleState(model) == random, "Magnet advanced shuffle randomness.");
        Check.Invariants(model);
        string after = BoosterState(model);
        Check.That(!model.CanUndo, "Magnet must not be undoable.");
        BoosterInvalid(model, model.Undo);
        Check.That(replay.TryMagnet() && BoosterState(replay) == after, "Magnet replay changed its target or removals.");
        Check.That(model.TryMagnet() && model.CompletedOrders == 2 && model.MagnetPlateIndex == 1, "Magnet ignored the next most-filled plate.");
        Check.That(model.TryMagnet() && BoardKinds(model) == "0,2|2,0|0,3,3|2,3", "Magnet skipped adjacent/index-shifting matches across bubbles.");
        Check.Invariants(model);

        var tied = BoosterFixture(new[] { new[] { 1, 0, 1, 0, 1 }, new[] { 0 } },
            new[] { (0, 0), (1, 0) });
        Check.That(tied.MagnetPlateIndex == 0 && tied.TryMagnet(), "Empty-plate tie did not use the lowest plate index.");
        Check.That(BoardKinds(tied) == "1,1,1" && tied.CompletedOrders == 1, "Magnet did not remove three forward matches and the emptied tail bubble.");
        Check.Invariants(tied);

        // Staged matching temp must not contribute to board-only completion availability.
        var insufficient = BoosterFixture(new[] { new[] { 0 }, new[] { 1, 1, 1 } },
            new[] { (0, 1), (1, 0) }, temp: new int?[] { 0 }, validate: false);
        Check.That(insufficient.MagnetPlateIndex == 1, "Magnet counted tray candies toward board completion.");
        Check.That(insufficient.TryMagnet() && insufficient.CompletedOrders == 1 && insufficient.Plates[0].Count == 2,
            "Magnet did not skip the more-filled but board-incomplete plate before draining temp.");
        Check.Invariants(insufficient);

        var inactive = BoosterFixture(new[] { new[] { 0, 1, 1, 1 } }, new[] { (0, 2), (1, 0) });
        inactive.Plates[0].Active = false;
        Check.That(inactive.MagnetPlateIndex == 1, "Magnet selected an inactive plate.");
        inactive.Plates[1].Kind = null;
        Check.That(inactive.MagnetPlateIndex == -1, "Magnet selected an unassigned plate.");
        BoosterInvalid(inactive, inactive.TryMagnet);
    }

    private static void MagnetCascades()
    {
        // A staged same-kind tray candy must wait until all three board candies are
        // delivered. Draining between them would feed a board zero into the new four order.
        var delayed = BoosterFixture(new[] { new[] { 0, 0, 0, 4 }, new[] { 0, 1, 0, 1, 1 } },
            new[] { (0, 0), (1, 0) }, new[] { 4, 0 }, new int?[] { 0, 4, 4 }, validate: false);
        var delayedReplay = BoosterFixture(new[] { new[] { 0, 0, 0, 4 }, new[] { 0, 1, 0, 1, 1 } },
            new[] { (0, 0), (1, 0) }, new[] { 4, 0 }, new int?[] { 0, 4, 4 }, validate: false);
        Check.That(delayed.MagnetPlateIndex == 0 && delayed.TryMagnet(), "Delayed tray drain magnet failed.");
        Check.That(delayed.CompletedOrders == 1 && delayed.Plates[0].Kind == (CandyKind)4 && delayed.Plates[0].Count == 2 &&
            delayed.Temp[0] == (CandyKind)0 && delayed.Temp.Skip(1).All(c => !c.HasValue),
            "Magnet drained temp before finishing its chosen board-only completion.");
        Check.That(BoardKinds(delayed) == "4|0,1,0,1,1", "Delayed-drain magnet consumed the wrong board inventory.");
        Check.Invariants(delayed);
        string delayedAfter = BoosterState(delayed);
        Check.That(!delayed.CanUndo, "Delayed tray drain magnet must not be undoable.");
        BoosterInvalid(delayed, delayed.Undo);
        Check.That(delayedReplay.TryMagnet() && BoosterState(delayedReplay) == delayedAfter, "Delayed tray drain replay diverged.");

        var model = BoosterFixture(new[] { new[] { 0 }, new[] { 1, 0, 0, 5 },
            new[] { 2, 0, 1, 3 }, new[] { 0, 1, 2, 3 }, new[] { 2, 3 } },
            new[] { (0, 1), (1, 0), (2, 0), (3, 0) }, new[] { 4, 5, 0 },
            new int?[] { 4, 5, 4, 5, 4 });
        var replay = BoosterFixture(new[] { new[] { 0 }, new[] { 1, 0, 0, 5 },
            new[] { 2, 0, 1, 3 }, new[] { 0, 1, 2, 3 }, new[] { 2, 3 } },
            new[] { (0, 1), (1, 0), (2, 0), (3, 0) }, new[] { 4, 5, 0 },
            new int?[] { 4, 5, 4, 5, 4 });
        int sorted = model.SortedCandies;
        Check.That(model.MagnetPlateIndex == 0 && model.TryMagnet(), "Cascade magnet failed.");
        Check.That(model.CompletedOrders == 2 && model.SortedCandies == sorted + 7, "Magnet did not complete its target plus the tray cascade.");
        Check.That(model.Plates[0].Kind == (CandyKind)5 && model.Plates[0].Count == 2 && model.Temp.All(c => !c.HasValue), "Tray did not drain through replacement orders and a second scan.");
        Check.That(model.PendingOrders.SequenceEqual(new[] { (CandyKind)0 }), "Cascade changed pending order sequence.");
        Check.That(BoardKinds(model) == "1,0,5|2,0,1,3|0,1,2,3|2,3" && model.Bubbles[0].Id == 13,
            "Magnet consumed extra target candies or failed to remove an emptied bubble.");
        Check.Invariants(model);
        string after = BoosterState(model);
        Check.That(!model.CanUndo, "Cascade magnet must not be undoable.");
        BoosterInvalid(model, model.Undo);
        Check.That(replay.TryMagnet() && BoosterState(replay) == after, "Cascade replay diverged.");
    }

    private static void ShuffleBoards()
    {
        var fingerprints = new HashSet<string>();
        foreach (int level in Enumerable.Range(1, 30))
        foreach (int seed in new[] { 0, 1, -17, int.MinValue, int.MaxValue })
        {
            var model = new CandySortModel(level, seed);
            // Include partial plates and a nonempty tray, neither of which may be touched.
            var unmatched = BoardSlots(model).First(c => !model.Plates.Any(p => p.Kind == c.Kind));
            Check.That(model.TryTake(unmatched.Id, unmatched.Index), "Shuffle tray setup failed.");
            var matching = BoardSlots(model).First(c => model.Plates.Any(p => p.Kind == c.Kind));
            Check.That(model.TryTake(matching.Id, matching.Index), "Shuffle plate setup failed.");
            var twin = new CandySortModel(level, seed);
            Check.That(twin.TryTake(unmatched.Id, unmatched.Index) && twin.TryTake(matching.Id, matching.Index), "Twin shuffle setup failed.");
            for (int repeat = 0; repeat < 4; repeat++)
            {
                CheckBoosterAvailability(model);
                string kinds = BoardKinds(model);
                string shape = BoardShape(model);
                string outside = OutsideBoard(model);
                var inventory = model.Bubbles.SelectMany(b => b.Candies).OrderBy(c => c).ToArray();
                var objects = model.Bubbles.ToArray();
                var lists = model.Bubbles.Select(b => b.Candies).ToArray();
                Check.That(model.CanShuffle && model.TryShuffle(), "Mixed board shuffle was unavailable.");
                Check.That(BoardKinds(model) != kinds, "Shuffle left a mixed board unchanged.");
                Check.That(BoardShape(model) == shape && OutsideBoard(model) == outside, "Shuffle changed bubble IDs/lanes/counts, plates, tray, orders or counters.");
                Check.That(model.Bubbles.SequenceEqual(objects) && model.Bubbles.Select(b => b.Candies).SequenceEqual(lists), "Shuffle replaced board slots instead of their kinds.");
                Check.That(model.Bubbles.SelectMany(b => b.Candies).OrderBy(c => c).SequenceEqual(inventory), "Shuffle changed per-kind board inventory.");
                Check.Invariants(model);
                Check.That(!model.CanUndo, "Shuffle must not be undoable.");
                BoosterInvalid(model, model.Undo);
                Check.That(twin.TryShuffle() && BoosterState(twin) == BoosterState(model), "Same level/seed/actions produced a different shuffle sequence or PRNG state.");
                fingerprints.Add(BoardKinds(model));
            }
        }
        Check.That(fingerprints.Count > 150, "Shuffle variants ignored their level/seed or random progression.");

        // PRNG state one produces an identity two-slot Fisher-Yates permutation,
        // so this seed specifically exercises the guaranteed-change fallback.
        int fallbackSeed = CandySortCampaign.GetLevel(5).Seed ^ 1;
        var fallback = BoosterFixture(new[] { new[] { 0, 1 } }, new[] { (0, 2), (1, 2) }, seed: fallbackSeed);
        Check.That(ShuffleState(fallback) == 1 && fallback.TryShuffle() && BoardKinds(fallback) == "1,0",
            "Identity random permutation did not use the guaranteed-change fallback.");
        var zeroSeed = BoosterFixture(new[] { new[] { 0, 1 } }, new[] { (0, 2), (1, 2) },
            seed: CandySortCampaign.GetLevel(5).Seed);
        Check.That(ShuffleState(zeroSeed) != 0 && zeroSeed.TryShuffle() && ShuffleState(zeroSeed) != 0,
            "A zero combined seed left the shuffle PRNG stuck at zero.");

        // Two unlike slots can only swap, including seeds whose random permutation is identity.
        for (int seed = 0; seed < 128; seed++)
        {
            var model = BoosterFixture(new[] { new[] { 0, 1 } }, new[] { (0, 2), (1, 2) }, seed: seed);
            var twin = BoosterFixture(new[] { new[] { 0, 1 } }, new[] { (0, 2), (1, 2) }, seed: seed);
            for (int repeat = 0; repeat < 4; repeat++)
            {
                string previous = BoardKinds(model);
                Check.That(model.CanShuffle && model.TryShuffle() && BoardKinds(model) != previous,
                    "Two-slot shuffle failed its guaranteed change.");
                Check.That(!model.CanUndo, "Two-slot shuffle created undo eligibility.");
                BoosterInvalid(model, model.Undo);
                Check.That(twin.TryShuffle() && BoosterState(twin) == BoosterState(model), "Two-slot shuffle replay changed randomness/state.");
                Check.Invariants(model);
            }
        }
    }

    private static void BoosterNoops()
    {
        foreach (int[][] board in new[] { Array.Empty<int[]>(), new[] { new[] { 0 } }, new[] { new[] { 0, 0 }, new[] { 0 } } })
        {
            var model = board.Length == 0
                ? BoosterFixture(board, Array.Empty<(int, int)>(), new[] { 0 }, new int?[] { 0, 0, 0 })
                : BoosterFixture(board, new[] { (0, 3 - board.Sum(b => b.Length)) });
            Check.That(!model.CanShuffle && (board.Length > 0 || model.MagnetPlateIndex == -1), "No-board/single-kind availability is incorrect.");
            for (int repeat = 0; repeat < 3; repeat++) BoosterInvalid(model, model.TryShuffle);
            if (board.Length == 0)
            {
                Check.That(!model.IsWon, "Empty non-winning fixture unexpectedly won.");
                BoosterInvalid(model, model.TryMagnet);
                BoosterInvalid(model, model.Undo);
                continue;
            }
            Check.That(model.TryTake(model.Bubbles[0].Id, 0), "Single-kind move failed.");
            for (int repeat = 0; repeat < 3; repeat++) BoosterInvalid(model, model.TryShuffle);
            Check.That(!model.CanUndo, "Direct plate take or failed shuffle created undo eligibility.");
            BoosterInvalid(model, model.Undo);
            Check.Invariants(model);
            var final = BoosterFixture(board, new[] { (0, 3 - board.Sum(b => b.Length)) });
            Check.That(final.TryMagnet() && final.IsWon && final.MagnetPlateIndex == -1 && !final.CanShuffle, "Final magnet did not win/disable boosters.");
            BoosterInvalid(final, final.TryMagnet);
            BoosterInvalid(final, final.TryShuffle);
            Check.That(!final.CanUndo, "Winning magnet or post-win no-op created undo eligibility.");
            BoosterInvalid(final, final.Undo);
            Check.Invariants(final);
        }

        // A full tray does not prevent either booster; the shuffle leaves its contents intact.
        var full = new CandySortModel(5);
        while (full.Temp.Any(c => !c.HasValue))
        {
            var slot = BoardSlots(full).First(c => !full.Plates.Any(p => p.Kind == c.Kind));
            Check.That(full.TryTake(slot.Id, slot.Index), "Full-tray booster setup failed.");
        }
        string outside = OutsideBoard(full);
        Check.That(full.CanShuffle && full.TryShuffle() && OutsideBoard(full) == outside, "Full tray prevented shuffle or was changed by it.");
        Check.That(full.MagnetPlateIndex >= 0 && full.TryMagnet(), "Full tray prevented a completable magnet.");
        Check.Invariants(full);

        // Failed magnet/shuffle must not poison the next successful random operation.
        var failed = BoosterFixture(new[] { new[] { 0, 0, 0 } }, new[] { (0, 0) });
        var control = BoosterFixture(new[] { new[] { 0, 0, 0 } }, new[] { (0, 0) });
        BoosterInvalid(failed, failed.TryShuffle);
        failed.Plates[0].Active = control.Plates[0].Active = false;
        BoosterInvalid(failed, failed.TryMagnet);
        failed.Plates[0].Active = control.Plates[0].Active = true;
        failed.Bubbles[0].Candies[1] = control.Bubbles[0].Candies[1] = (CandyKind)1;
        Check.That(failed.TryShuffle() && control.TryShuffle() && BoosterState(failed) == BoosterState(control),
            "Failed boosters changed the next deterministic shuffle or its history.");
    }

    private static void MixedBoosters(CandySortModel model, CandySortModel replay, bool useTemp = false)
    {
        var results = new List<string>();
        var actions = new List<(string Kind, int Id, int Index)>();
        Check.That(BoosterState(model) == BoosterState(replay), "Mixed replay fixture differs from its opening.");
        int step = 0;
        while (!model.IsWon)
        {
            CheckBoosterAvailability(model);
            (string Kind, int Id, int Index) action;
            if (step == 0 && useTemp) action = ("temp", 0, 0);
            else if (step % 7 == 0 && model.CanShuffle) action = ("shuffle", 0, 0);
            else if (step % 3 == 0 && model.MagnetPlateIndex >= 0) action = ("magnet", 0, 0);
            else
            {
                var slots = BoardSlots(model).ToArray();
                var unmatched = slots.Where(c => !model.Plates.Any(p => p.Active && p.Kind == c.Kind)).ToArray();
                bool buffer = step < 9 && step % 2 == 0 && unmatched.Length > 0 && model.Temp.Any(c => !c.HasValue);
                var candidates = buffer ? unmatched : slots.Where(c => model.Plates.Any(p => p.Active && p.Kind == c.Kind)).ToArray();
                Check.That(candidates.Length > 0, "Mixed booster match has no legal progress move.");
                var selected = candidates[(step + model.Level) % candidates.Length];
                action = ("take", selected.Id, selected.Index);
            }
            string before = BoosterState(model);
            bool trayPickup = action.Kind == "take" && !model.Plates.Any(p => p.Active && p.Kind ==
                model.Bubbles.First(b => b.Id == action.Id).Candies[action.Index]);
            actions.Add(action);
            Check.That(ApplyBoosterAction(model, action), $"Mixed {action.Kind} action failed.");
            Check.Invariants(model);
            CheckBoosterAvailability(model);
            string after = BoosterState(model);
            results.Add(after);
            Check.That(model.CanUndo == trayPickup, $"Mixed {action.Kind} left incorrect tray-only undo eligibility.");
            if (trayPickup)
            {
                Check.That(model.Undo() && BoosterState(model) == before, "Mixed tray pickup undo was not exact.");
                Check.That(ApplyBoosterAction(model, action) && BoosterState(model) == after, "Mixed tray pickup replay diverged.");
            }
            else BoosterInvalid(model, model.Undo);
            Check.That(++step <= model.TotalCandies * 3, "Mixed booster match failed to make progress.");
        }
        Check.That(actions.Any(a => a.Kind == "shuffle") && actions.Any(a => a.Kind == "magnet") && actions.Any(a => a.Kind == "take"), "Mixed sequence omitted a main action type.");
        if (useTemp) Check.That(actions.Any(a => a.Kind == "temp"), "Mixed sequence omitted the explicit tray action.");
        BoosterInvalid(model, model.TryMagnet);
        BoosterInvalid(model, model.TryShuffle);
        Check.That(!model.CanUndo, "Winning mixed sequence retained undo eligibility.");
        BoosterInvalid(model, model.Undo);
        for (int i = 0; i < actions.Count; i++)
            Check.That(ApplyBoosterAction(replay, actions[i]) && BoosterState(replay) == results[i], $"Mixed full replay diverged at {model.Level}/{i}.");
        Check.That(replay.IsWon, "Mixed full replay did not win.");
        Check.Invariants(replay);
    }

    private static bool ApplyBoosterAction(CandySortModel model, (string Kind, int Id, int Index) action)
    {
        switch (action.Kind)
        {
            case "take": return model.TryTake(action.Id, action.Index);
            case "temp": return model.TryUseTemp(action.Id);
            case "magnet": return model.TryMagnet();
            case "shuffle": return model.TryShuffle();
            default: throw new InvalidOperationException("Unknown mixed action.");
        }
    }

    private static void CheckBoosterAvailability(CandySortModel model)
    {
        var board = model.Bubbles.SelectMany(b => b.Candies).ToArray();
        int expected = model.IsWon ? -1 : model.Plates.Select((p, i) => (Plate: p, Index: i))
            .Where(p => p.Plate.Active && p.Plate.Kind.HasValue && board.Count(c => c == p.Plate.Kind) >= 3 - p.Plate.Count)
            .OrderByDescending(p => p.Plate.Count).ThenBy(p => p.Index).Select(p => p.Index).DefaultIfEmpty(-1).First();
        string before = BoosterState(model);
        for (int repeat = 0; repeat < 3; repeat++)
        {
            Check.That(model.MagnetPlateIndex == expected, "Magnet availability disagrees with independent plate ranking.");
            Check.That(model.CanShuffle == (!model.IsWon && board.Distinct().Count() > 1), "Shuffle availability disagrees with board kinds.");
        }
        Check.That(BoosterState(model) == before, "Reading booster availability changed state/randomness/history.");
    }

    private static IEnumerable<(int Id, int Index, CandyKind Kind)> BoardSlots(CandySortModel model)
    {
        return model.Bubbles.SelectMany(b => b.Candies.Select((kind, index) => (b.Id, index, kind)));
    }

    private static string BoardKinds(CandySortModel model)
    {
        return string.Join('|', model.Bubbles.Select(b => string.Join(',', b.Candies.Select(c => (int)c))));
    }

    private static string BoardShape(CandySortModel model)
    {
        return string.Join('|', model.Bubbles.Select(b => $"{b.Id}/{b.Lane}/{b.Candies.Count}"));
    }

    private static string OutsideBoard(CandySortModel model)
    {
        return $"{model.Level}/{model.TotalCandies}/{model.TargetOrders}/{model.ActivePlates}/{model.CompletedOrders}/{model.SortedCandies}/{model.IsWon}/{model.IsBlocked}|" +
            string.Join('|', model.Plates.Select(p => $"{p.Active}/{p.Kind}/{p.Count}")) + "|" +
            string.Join(',', model.PendingOrders) + "|" + string.Join(',', model.Temp) + "|" +
            string.Join(',', model.RemainingByKind.OrderBy(p => p.Key).Select(p => $"{p.Key}/{p.Value}"));
    }

    private static uint ShuffleState(CandySortModel model)
    {
        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        object random = typeof(CandySortModel).GetField("shuffleRandom", flags).GetValue(model);
        return (uint)random.GetType().GetProperty("State").GetValue(random);
    }

    private static string BoosterState(CandySortModel model)
    {
        return $"{Check.State(model)}|{ShuffleState(model)}";
    }

    private static void BoosterInvalid(CandySortModel model, Func<bool> action)
    {
        string before = BoosterState(model);
        Check.That(!action() && BoosterState(model) == before, "Unavailable booster/action changed state, randomness or history.");
    }

    private static CandySortModel BoosterFixture(int[][] board, (int Kind, int Count)[] assigned,
        int[] pending = null, int?[] temp = null, int seed = 0, bool validate = true)
    {
        var model = new CandySortModel(5, seed);
        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var bubbles = (List<CandySortModel.Bubble>)typeof(CandySortModel).GetField("bubbles", flags).GetValue(model);
        var orders = (List<CandyKind>)typeof(CandySortModel).GetField("pendingOrders", flags).GetValue(model);
        bubbles.Clear();
        for (int i = 0; i < board.Length; i++)
            bubbles.Add(new CandySortModel.Bubble { Id = 10 + i * 3, Lane = (i + 1) % 3,
                Candies = board[i].Select(kind => (CandyKind)kind).ToList() });
        orders.Clear();
        if (pending != null) orders.AddRange(pending.Select(kind => (CandyKind)kind));
        for (int i = 0; i < model.Plates.Count; i++)
        {
            model.Plates[i].Kind = i < assigned.Length && assigned[i].Kind >= 0 ? (CandyKind)assigned[i].Kind : null;
            model.Plates[i].Count = i < assigned.Length ? assigned[i].Count : 0;
        }
        if (temp != null)
            for (int i = 0; i < temp.Length; i++) model.Temp[i] = temp[i].HasValue ? (CandyKind)temp[i].Value : null;
        int targets = orders.Count + model.Plates.Count(p => p.Kind.HasValue);
        typeof(CandySortModel).GetProperty("TargetOrders").SetValue(model, targets);
        typeof(CandySortModel).GetProperty("TotalCandies").SetValue(model, targets * 3);
        if (validate) Check.Invariants(model);
        return model;
    }

    private static void TrayUndo()
    {
        // Select a non-leading candy, repeated kinds, and an entire middle bubble.
        // Rewind must restore original candy indices, bubble order, IDs and lanes.
        var model = BoosterFixture(new[] { new[] { 0, 0, 0 }, new[] { 4, 5, 4 },
            new[] { 5, 4, 5 }, new[] { 1, 1, 1 } },
            new[] { (0, 0), (1, 0) }, new[] { 4, 5 });
        var moves = new[] { (Id: 13, Index: 1), (Id: 13, Index: 1), (Id: 13, Index: 0),
            (Id: 16, Index: 2), (Id: 16, Index: 0) };
        var states = new List<string>();
        var results = new List<string>();
        foreach (var move in moves)
        {
            states.Add(BoosterState(model));
            Check.That(model.TryTake(move.Id, move.Index) && model.CanUndo, "Unmatched pickup did not enable Undo.");
            results.Add(BoosterState(model));
            Check.Invariants(model);
        }
        Check.That(model.Temp.All(c => c.HasValue) && model.Bubbles.All(b => b.Id != 13), "Tray/bubble undo setup failed.");
        for (int i = moves.Length - 1; i >= 0; i--)
        {
            Check.That(model.Undo() && BoosterState(model) == states[i], "Consecutive tray undo did not restore the exact prior state.");
            Check.Invariants(model);
        }
        BoosterInvalid(model, model.Undo);
        for (int i = 0; i < moves.Length; i++)
            Check.That(model.TryTake(moves[i].Id, moves[i].Index) && BoosterState(model) == results[i], "Consecutive tray replay diverged.");

        // Staged unassigned orders make both boosters unavailable without breaking
        // candy conservation. Every failed/no-op action must retain the whole stack.
        var noops = BoosterFixture(new[] { new[] { 4, 4, 4 }, new[] { 4, 4, 4 } },
            Array.Empty<(int, int)>(), new[] { 4, 4 });
        string initial = BoosterState(noops);
        for (int i = 0; i < noops.Temp.Length; i++)
            Check.That(noops.TryTake(noops.Bubbles[0].Id, 0), "No-op tray setup failed.");
        Check.That(noops.IsBlocked && noops.CanUndo, "Blocked full tray must still allow its latest pickup to be undone.");
        BoosterInvalid(noops, () => noops.TryTake(-1, 0));
        BoosterInvalid(noops, () => noops.TryTake(noops.Bubbles[0].Id, -1));
        BoosterInvalid(noops, () => noops.TryTake(noops.Bubbles[0].Id, noops.Bubbles[0].Candies.Count));
        BoosterInvalid(noops, () => noops.TryTake(noops.Bubbles[0].Id, 0));
        BoosterInvalid(noops, () => noops.TryUseTemp(-1));
        BoosterInvalid(noops, () => noops.TryUseTemp(noops.Temp.Length));
        BoosterInvalid(noops, () => noops.TryUseTemp(0));
        BoosterInvalid(noops, noops.TryMagnet);
        BoosterInvalid(noops, noops.TryShuffle);
        for (int i = 0; i < noops.Temp.Length; i++)
        {
            Check.That(noops.Undo() && !noops.IsBlocked, "Failed action consumed an eligible tray pickup.");
            Check.Invariants(noops);
        }
        Check.That(BoosterState(noops) == initial, "Failed/no-op actions changed history or randomness.");
        BoosterInvalid(noops, () => noops.TryUseTemp(0));
        BoosterInvalid(noops, noops.Undo);

        foreach (string action in new[] { "plate", "temp", "magnet", "shuffle" })
        {
            var superseded = BoosterFixture(new[] { new[] { 0, 0, 0 }, new[] { 4, 4, 4 }, new[] { 5, 5, 5 } },
                new[] { (0, 0) }, new[] { 4, 5 });
            Check.That(superseded.TryTake(13, 1) && superseded.TryTake(13, 0) && superseded.CanUndo,
                "Superseding action setup lost consecutive tray history.");
            if (action == "temp")
            {
                // Normally DrainTemp consumes matches immediately. Stage a newly
                // assigned plate to exercise successful explicit tray use separately.
                superseded.Plates[1].Kind = (CandyKind)4;
                var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                ((List<CandyKind>)typeof(CandySortModel).GetField("pendingOrders", flags).GetValue(superseded)).RemoveAt(0);
            }
            bool succeeded = action switch
            {
                "plate" => superseded.TryTake(10, 0),
                "temp" => superseded.TryUseTemp(0),
                "magnet" => superseded.TryMagnet(),
                "shuffle" => superseded.TryShuffle(),
                _ => false
            };
            Check.That(succeeded && !superseded.CanUndo, $"Successful {action} did not supersede tray Undo.");
            BoosterInvalid(superseded, superseded.Undo);
            Check.Invariants(superseded);
            string after = BoosterState(superseded);
            var next = BoardSlots(superseded).First(c => !superseded.Plates.Any(p => p.Active && p.Kind == c.Kind));
            Check.That(superseded.TryTake(next.Id, next.Index) && superseded.CanUndo, $"Pickup after {action} did not start a new undo chain.");
            Check.That(superseded.Undo() && BoosterState(superseded) == after && !superseded.CanUndo,
                $"Undo after {action} revived superseded history or changed its effects/randomness.");
            BoosterInvalid(superseded, superseded.Undo);
        }

        var cascade = BoosterFixture(new[] { new[] { 0 }, new[] { 4, 4, 4 }, new[] { 1, 1, 1 } },
            new[] { (0, 2), (1, 0) }, new[] { 4 });
        for (int i = 0; i < 3; i++) Check.That(cascade.TryTake(13, 0), "Completion cascade tray setup failed.");
        Check.That(cascade.CanUndo && cascade.TryTake(10, 0) && cascade.CompletedOrders == 2 && cascade.Temp.All(c => !c.HasValue),
            "Direct plate completion did not perform its automatic tray cascade.");
        Check.That(!cascade.CanUndo, "Direct plate completion/cascade retained obsolete tray Undo.");
        BoosterInvalid(cascade, cascade.Undo);
        Check.Invariants(cascade);
    }

    public static void Matches()
    {
        TrayUndo();
        int matches = 0, buffered = 0, undoChecks = 0, blockedMatches = 0;
        var collectedKinds = new HashSet<CandyKind>();
        var undoneKinds = new HashSet<CandyKind>();
        for (int level = 1; level <= 30; level++)
        foreach (int seed in new[] { 0, 1, 1729, -17, int.MinValue, int.MaxValue })
        {
            var model = new CandySortModel(level, seed);
            Opening(model);
            Check.That(Check.State(model) == Check.State(new CandySortModel(level, seed)), $"Seed unstable at {level}/{seed}.");
            Check.That(Check.State(model) != Check.State(new CandySortModel(level, seed == 1 ? 2 : 1)), $"Seed ignored at {level}/{seed}.");
            Check.Invalid(model, model.Undo);
            // The complete-match policy buffers only a few early candies. Probe
            // newly encountered unmatched kinds explicitly, never undo plate moves
            // merely to satisfy the catalog-wide restoration coverage requirement.
            var probes = BoardSlots(model).Where(c => !model.Plates.Any(p => p.Active && p.Kind == c.Kind))
                .GroupBy(c => c.Kind).Select(group => group.First()).Where(c => !undoneKinds.Contains(c.Kind)).ToArray();
            foreach (var probe in probes)
            {
                string beforeProbe = BoosterState(model);
                Check.That(model.TryTake(probe.Id, probe.Index) && model.CanUndo, $"Unmatched {probe.Kind} probe failed.");
                string afterProbe = BoosterState(model);
                Check.That(model.Undo() && BoosterState(model) == beforeProbe, $"Unmatched {probe.Kind} probe did not restore its original state.");
                Check.That(model.TryTake(probe.Id, probe.Index) && BoosterState(model) == afterProbe && model.Undo() && BoosterState(model) == beforeProbe,
                    $"Unmatched {probe.Kind} probe replay diverged.");
                undoneKinds.Add(probe.Kind);
                undoChecks++;
            }
            int step = 0;
            var results = new List<string>();
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
                actions.Add((move.Bubble, move.Index));
                Check.That(model.TryTake(move.Bubble, move.Index), "Chosen legal move rejected.");
                if (useBuffer) buffered++;
                collectedKinds.Add(move.Kind);
                Check.Invariants(model);
                if (model.Bubbles.All(b => b.Id != move.Bubble))
                    Check.Invalid(model, () => model.TryTake(move.Bubble, 0));
                string after = Check.State(model);
                results.Add(after);
                Check.That(model.CanUndo == useBuffer, "Only unmatched board-to-tray pickups may be undone.");
                if (useBuffer)
                {
                    Check.That(model.Undo() && before == Check.State(model), $"Undo did not exactly restore pre-move state for {move.Kind}.");
                    Check.That(model.TryTake(move.Bubble, move.Index) && after == Check.State(model), $"Replaying undone move was not deterministic for {move.Kind}.");
                    undoneKinds.Add(move.Kind);
                    undoChecks++;
                }
                else Check.Invalid(model, model.Undo);
                Check.That(++step <= model.TotalCandies, "Match exceeded available candies.");
            }
            Check.Invariants(model);
            Check.Invalid(model, () => model.TryTake(actions[0].Bubble, 0));
            Check.Invalid(model, () => model.TryUseTemp(0));
            Check.That(!model.CanUndo, "Winning direct plate move retained tray undo eligibility.");
            Check.Invalid(model, model.Undo);
            var replay = new CandySortModel(level, seed);
            for (int i = 0; i < actions.Count; i++)
                Check.That(replay.TryTake(actions[i].Bubble, actions[i].Index) && Check.State(replay) == results[i], $"Full replay mismatch at {level}/{seed}/{i}.");
            Check.That(replay.IsWon, "Full replay did not win.");
            Check.That(model.RemainingByKind.Count == 32 && model.RemainingByKind.Values.All(count => count == 0), "Winning inventory must retain all 32 kinds with zero remaining.");
            matches++;

            // Independently consume only unmatched candies until a genuinely full tray blocks.
            var obstruction = new CandySortModel(level, seed);
            while (obstruction.Temp.Any(c => !c.HasValue))
            {
                var candidate = obstruction.Bubbles.SelectMany(b => b.Candies.Select((c, i) => (Bubble: b.Id, Index: i, Kind: c))).FirstOrDefault(c => !obstruction.Plates.Any(p => p.Kind == c.Kind), (Bubble: -1, Index: -1, Kind: CandyKind.SwirlLollipop));
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
        Check.That(collectedKinds.SetEquals(Enum.GetValues<CandyKind>()), "Complete matches must collect all 32 kinds.");
        Check.That(undoneKinds.SetEquals(Enum.GetValues<CandyKind>()), "Unmatched pickup probes and buffered moves must cover exact tray Undo/replay for all 32 kinds.");
        Console.WriteLine($"Model coverage: {matches} complete matches; {collectedKinds.Count} collected kinds; {undoneKinds.Count} kinds undone/replayed; {buffered} buffered moves; {undoChecks} exact undo/replay checks; {blockedMatches} full-tray direct matches.");
    }
}
