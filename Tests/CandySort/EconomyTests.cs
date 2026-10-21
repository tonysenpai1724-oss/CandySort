using CandySort;

namespace CandySortVerification;

internal static class EconomyTests
{
    private const string ProgressKey = "CandySort.Boosters.HighestReachedLevel";
    private static readonly CandySortBooster[] Boosters = Enum.GetValues<CandySortBooster>();

    public static void Run()
    {
        Contracts();
        Thresholds();
        SkippedLevels();
        Persistence();
        ExhaustionAndPurchase();
        CorruptData();
        RewardAttempts();
        Console.WriteLine("Economy coverage: unlock thresholds/skips; persisted stock/progression; exhausted grants; purchases; clamped data/overflow; per-attempt win claims.");
    }

    private static void Contracts()
    {
        Check.That(CandySortBoosterInventory.PurchasePrice == 10 && CandySortBoosterInventory.WinReward == 40,
            "Booster price/win reward changed.");
        Check.That(Boosters.SequenceEqual(new[] { CandySortBooster.Magnet, CandySortBooster.Undo, CandySortBooster.Shuffle }),
            "Unexpected booster identities/order.");
        var store = new Preferences();
        Check.Throws<ArgumentNullException>(() => new CandySortBoosterInventory(null, store.Write, store.Save));
        Check.Throws<ArgumentNullException>(() => new CandySortBoosterInventory(store.Read, null, store.Save));
        Check.Throws<ArgumentNullException>(() => new CandySortBoosterInventory(store.Read, store.Write, null));
        Check.That(store.ReadCount == 0 && store.WriteCount == 0 && store.SaveCount == 0,
            "Invalid constructor touched persistence.");
        var inventory = store.Inventory();
        foreach (int value in new[] { int.MinValue, -1, 3, int.MaxValue })
        {
            var invalid = (CandySortBooster)value;
            Check.Throws<ArgumentOutOfRangeException>(() => CandySortBoosterInventory.UnlockLevel(invalid));
            Check.Throws<ArgumentOutOfRangeException>(() => inventory.IsUnlocked(invalid));
            Check.Throws<ArgumentOutOfRangeException>(() => inventory.Count(invalid));
            Check.Throws<ArgumentOutOfRangeException>(() => inventory.TryConsume(invalid));
            Check.Throws<ArgumentOutOfRangeException>(() => inventory.AddPurchasedUse(invalid));
        }
        Check.That(store.WriteCount == 0 && store.SaveCount == 0, "Invalid booster mutated preferences.");
    }

    private static void Thresholds()
    {
        var store = new Preferences();
        var inventory = store.Inventory();
        Check.That(store.WriteCount == 0 && store.SaveCount == 0, "Fresh construction needlessly saved.");
        for (int level = 0; level <= 8; level++)
        {
            inventory.GrantUnlocked(level);
            foreach (CandySortBooster booster in Boosters)
            {
                int threshold = 3 + 2 * (int)booster;
                bool unlocked = level >= threshold;
                Check.That(CandySortBoosterInventory.UnlockLevel(booster) == threshold, "Wrong unlock threshold.");
                Check.That(inventory.IsUnlocked(booster) == unlocked && inventory.Count(booster) == (unlocked ? 2 : 0),
                    $"Threshold/initial gift mismatch at level {level} for {booster}.");
                if (!unlocked)
                {
                    int writes = store.WriteCount, saves = store.SaveCount;
                    Check.That(!inventory.TryConsume(booster) && !inventory.AddPurchasedUse(booster), "Locked booster could be used or purchased.");
                    Check.That(store.WriteCount == writes && store.SaveCount == saves, "Locked action saved.");
                }
            }
        }
        Check.That(store.Saved.Count == 4 && store.Saved[ProgressKey] == 8,
            "Persistence must use one progression key plus exactly one stock/grant key per booster.");
        Check.That(store.Saved.Keys.All(key => key.StartsWith("CandySort.Boosters.", StringComparison.Ordinal)),
            "Booster persistence keys must be namespaced.");
        int finalWrites = store.WriteCount, finalSaves = store.SaveCount;
        foreach (int level in new[] { int.MinValue, -1, 0, 1, 3, 7, 8 }) inventory.GrantUnlocked(level);
        foreach (CandySortBooster booster in Boosters)
        {
            Check.That(inventory.IsUnlocked(booster) && inventory.Count(booster) == 2, "Replay relocked or regranted a booster.");
        }
        Check.That(store.Saved[ProgressKey] == 8 && store.WriteCount == finalWrites && store.SaveCount == finalSaves,
            "Replay/duplicate progression mutated persistence.");
    }

    private static void SkippedLevels()
    {
        foreach (int level in new[] { 3, 4, 5, 6, 7, 30, int.MaxValue })
        {
            var store = new Preferences();
            var inventory = store.Inventory();
            inventory.GrantUnlocked(level);
            Check.That(store.SaveCount == 1 && store.Saved[ProgressKey] == level, "Skipped progression was not saved in one batch.");
            foreach (CandySortBooster booster in Boosters)
            {
                bool unlocked = level >= CandySortBoosterInventory.UnlockLevel(booster);
                Check.That(inventory.IsUnlocked(booster) == unlocked && inventory.Count(booster) == (unlocked ? 2 : 0),
                    "Skipping levels lost or prematurely granted an unlock.");
            }
            store.Restart();
            var restarted = store.Inventory();
            restarted.GrantUnlocked(1);
            foreach (CandySortBooster booster in Boosters)
                Check.That(restarted.IsUnlocked(booster) == inventory.IsUnlocked(booster) && restarted.Count(booster) == inventory.Count(booster),
                    "Skipped unlocks did not survive restart and replay.");
        }
    }

    private static void Persistence()
    {
        var store = new Preferences();
        var inventory = store.Inventory();
        inventory.GrantUnlocked(7);
        Check.That(inventory.TryConsume(CandySortBooster.Magnet) && inventory.TryConsume(CandySortBooster.Magnet), "Could not exhaust magnet.");
        Check.That(inventory.TryConsume(CandySortBooster.Undo) && inventory.AddPurchasedUse(CandySortBooster.Shuffle), "Could not create distinct persistent counts.");
        int writes = store.WriteCount, saves = store.SaveCount;
        store.Restart();
        var restarted = store.Inventory();
        restarted.GrantUnlocked(1);
        Check.That(restarted.Count(CandySortBooster.Magnet) == 0 && restarted.IsUnlocked(CandySortBooster.Magnet) &&
            restarted.Count(CandySortBooster.Undo) == 1 && restarted.Count(CandySortBooster.Shuffle) == 3,
            "Restart lost exhausted/unlocked or purchased stock.");
        Check.That(store.WriteCount == writes && store.SaveCount == saves, "Reading/replaying persisted stock caused a save or grant.");

        // If progression was persisted without its stock updates, construction finishes
        // pending gifts, but never replenishes a stock key that already records zero.
        var interrupted = new Preferences(new Dictionary<string, int>
        {
            [ProgressKey] = 7,
            [Key(CandySortBooster.Magnet)] = 0,
            [Key(CandySortBooster.Undo)] = -1
        });
        var recovered = interrupted.Inventory();
        Check.That(recovered.IsUnlocked(CandySortBooster.Magnet) && recovered.Count(CandySortBooster.Magnet) == 0 &&
            recovered.Count(CandySortBooster.Undo) == 2 && recovered.Count(CandySortBooster.Shuffle) == 2,
            "Persistent highest progression did not finish pending grants or replenished exhausted stock.");
        Check.That(interrupted.SaveCount == 1 && interrupted.WriteCount == 2,
            "Pending grants need exactly one write each and one save, without separate grant flags.");
        interrupted.Restart();
        _ = interrupted.Inventory();
        Check.That(interrupted.SaveCount == 1, "Recovered grants repeated after another restart.");
    }

    private static void ExhaustionAndPurchase()
    {
        foreach (CandySortBooster booster in Boosters)
        {
            var store = new Preferences();
            var inventory = store.Inventory();
            inventory.GrantUnlocked(7);
            foreach (int count in new[] { 1, 0 })
            {
                int writes = store.WriteCount, saves = store.SaveCount;
                Check.That(inventory.TryConsume(booster) && inventory.Count(booster) == count, "Consumption did not remove exactly one use.");
                Check.That(store.WriteCount == writes + 1 && store.SaveCount == saves + 1 && store.Saved[Key(booster)] == count,
                    "Consumption did not atomically persist stock/grant in one key.");
            }
            int exhaustedWrites = store.WriteCount, exhaustedSaves = store.SaveCount;
            for (int attempt = 0; attempt < 4; attempt++)
            {
                Check.That(!inventory.TryConsume(booster), "Empty inventory went negative.");
                inventory.GrantUnlocked(7);
                inventory.GrantUnlocked(30);
            }
            Check.That(inventory.IsUnlocked(booster) && inventory.Count(booster) == 0, "Exhaustion caused a relock or welcome regrant.");
            Check.That(store.WriteCount == exhaustedWrites + 1 && store.SaveCount == exhaustedSaves + 1,
                "Exhaustion/repeated grant attempts saved beyond the sole progression update.");
            store.Restart();
            inventory = store.Inventory();
            inventory.GrantUnlocked(1);
            Check.That(inventory.IsUnlocked(booster) && inventory.Count(booster) == 0, "Restart replenished a spent welcome gift.");
            int purchaseWrites = store.WriteCount, purchaseSaves = store.SaveCount;
            Check.That(inventory.AddPurchasedUse(booster) && inventory.Count(booster) == 1, "Purchase did not add exactly one use.");
            Check.That(store.WriteCount == purchaseWrites + 1 && store.SaveCount == purchaseSaves + 1 && store.Saved[Key(booster)] == 1,
                "Purchase did not save exactly one stock update.");
            Check.That(inventory.TryConsume(booster) && inventory.Count(booster) == 0, "Purchased stock could not be consumed.");
            foreach (CandySortBooster other in Boosters.Where(other => other != booster))
                Check.That(inventory.Count(other) == 2, "An operation changed another booster inventory.");
        }
    }

    private static void CorruptData()
    {
        var corrupt = new Preferences(new Dictionary<string, int>
        {
            [ProgressKey] = int.MinValue,
            [Key(CandySortBooster.Magnet)] = int.MinValue,
            [Key(CandySortBooster.Undo)] = -2,
            [Key(CandySortBooster.Shuffle)] = -1
        });
        var inventory = corrupt.Inventory();
        Check.That(corrupt.Saved[ProgressKey] == 0 && corrupt.SaveCount == 1 && corrupt.WriteCount == 3,
            "Negative progression/invalid negative stock was not normalized and saved once.");
        foreach (CandySortBooster booster in new[] { CandySortBooster.Magnet, CandySortBooster.Undo })
            Check.That(inventory.IsUnlocked(booster) && inventory.Count(booster) == 0 && !inventory.TryConsume(booster),
                "Invalid negative stock did not clamp to exhausted/unlocked without regranting.");
        Check.That(!inventory.IsUnlocked(CandySortBooster.Shuffle) && inventory.Count(CandySortBooster.Shuffle) == 0,
            "The valid locked sentinel was treated as corrupt unlocked data.");
        inventory.GrantUnlocked(int.MinValue);
        inventory.GrantUnlocked(7);
        Check.That(inventory.Count(CandySortBooster.Magnet) == 0 && inventory.Count(CandySortBooster.Undo) == 0 &&
            inventory.Count(CandySortBooster.Shuffle) == 2, "Progression regranted corrupt exhausted stock.");
        corrupt.Restart();
        inventory = corrupt.Inventory();
        Check.That(inventory.Count(CandySortBooster.Magnet) == 0 && inventory.Count(CandySortBooster.Undo) == 0,
            "Normalized stock did not survive restart.");

        foreach (CandySortBooster booster in Boosters)
        {
            var maximum = new Preferences(new Dictionary<string, int> { [Key(booster)] = int.MaxValue });
            var full = maximum.Inventory();
            Check.That(full.IsUnlocked(booster) && full.Count(booster) == int.MaxValue && !full.AddPurchasedUse(booster),
                "Maximum stock overflowed or accepted an unrepresentable purchase.");
            Check.That(maximum.SaveCount == 0 && maximum.WriteCount == 0, "Rejected overflow purchase saved.");
            Check.That(full.TryConsume(booster) && full.Count(booster) == int.MaxValue - 1 &&
                full.AddPurchasedUse(booster) && full.Count(booster) == int.MaxValue,
                "Upper boundary stock cannot be safely consumed/replenished.");
        }
    }

    private static void RewardAttempts()
    {
        int coins = 0;
        for (int replay = 0; replay < 3; replay++)
        {
            var reward = new CandySortWinReward();
            for (int check = 0; check < 4; check++)
                Check.That(!reward.TryClaim(false), "An unfinished/blocked board claimed its win reward.");
            Check.That(reward.TryClaim(true), "First valid win could not claim.");
            coins += CandySortBoosterInventory.WinReward;
            for (int duplicate = 0; duplicate < 5; duplicate++)
                Check.That(!reward.TryClaim(true), "Duplicate win callback claimed twice.");
            Check.That(!reward.TryClaim(false) && !reward.TryClaim(true), "Undo/replay reset the same attempt's win claim.");
            Check.That(coins == (replay + 1) * 40, "A fresh level-load/replay attempt did not receive exactly one reward.");
        }
        var firstAttempt = new CandySortWinReward();
        var secondAttempt = new CandySortWinReward();
        Check.That(firstAttempt.TryClaim(true) && secondAttempt.TryClaim(true) && !firstAttempt.TryClaim(true),
            "Separate level attempts share reward claim state.");
    }

    private static string Key(CandySortBooster booster) => $"CandySort.Boosters.{booster}";

    private sealed class Preferences
    {
        public Dictionary<string, int> Saved { get; private set; }
        private Dictionary<string, int> current;
        public int ReadCount { get; private set; }
        public int WriteCount { get; private set; }
        public int SaveCount { get; private set; }

        public Preferences(Dictionary<string, int> initial = null)
        {
            Saved = initial == null ? new Dictionary<string, int>() : new Dictionary<string, int>(initial);
            Restart();
        }

        public int Read(string key, int fallback)
        {
            ReadCount++;
            return current.TryGetValue(key, out int value) ? value : fallback;
        }

        public void Write(string key, int value)
        {
            WriteCount++;
            current[key] = value;
        }

        public void Save()
        {
            SaveCount++;
            Saved = new Dictionary<string, int>(current);
        }

        public void Restart() => current = new Dictionary<string, int>(Saved);
        public CandySortBoosterInventory Inventory() => new CandySortBoosterInventory(Read, Write, Save);
    }
}
