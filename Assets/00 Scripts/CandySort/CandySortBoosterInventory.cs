using System;

namespace CandySort
{
    public enum CandySortBooster
    {
        Magnet,
        Undo,
        Shuffle
    }

    /// <summary>Persistent booster stock, independent of Unity and the current board.</summary>
    public sealed class CandySortBoosterInventory
    {
        public const int PurchasePrice = 10;
        public const int WinReward = 40;

        private const int Locked = -1;
        private const int InitialFreeUses = 2;
        private const string ProgressKey = "CandySort.Boosters.HighestReachedLevel";
        private static readonly string[] StockKeys =
        {
            "CandySort.Boosters.Magnet",
            "CandySort.Boosters.Undo",
            "CandySort.Boosters.Shuffle"
        };

        private readonly Action<string, int> write;
        private readonly Action save;
        private readonly int[] stock = new int[3];
        private int highestReachedLevel;

        public CandySortBoosterInventory(Func<string, int, int> read,
            Action<string, int> write, Action save)
        {
            if (read == null) throw new ArgumentNullException(nameof(read));
            this.write = write ?? throw new ArgumentNullException(nameof(write));
            this.save = save ?? throw new ArgumentNullException(nameof(save));

            int storedLevel = read(ProgressKey, 0);
            highestReachedLevel = Math.Max(0, storedLevel);
            bool changed = storedLevel != highestReachedLevel;
            if (changed) write(ProgressKey, highestReachedLevel);
            for (int i = 0; i < stock.Length; i++)
            {
                int stored = read(StockKeys[i], Locked);
                // Only -1 means not yet granted. Other negative data is exhausted,
                // not a reason to grant a potentially spent welcome gift again.
                stock[i] = stored < Locked ? 0 : stored;
                if (stored == stock[i]) continue;
                write(StockKeys[i], stock[i]);
                changed = true;
            }
            if (GrantPendingUnlocks()) changed = true;
            if (changed) save();
        }

        public static int UnlockLevel(CandySortBooster booster)
        {
            return 3 + 2 * Index(booster);
        }

        /// <summary>Remember the furthest reached level and grant each welcome gift once.</summary>
        public void GrantUnlocked(int highestReachedLevel)
        {
            bool changed = false;
            if (highestReachedLevel > this.highestReachedLevel)
            {
                this.highestReachedLevel = highestReachedLevel;
                write(ProgressKey, this.highestReachedLevel);
                changed = true;
            }
            if (GrantPendingUnlocks()) changed = true;
            if (changed) save();
        }

        public bool IsUnlocked(CandySortBooster booster)
        {
            return stock[Index(booster)] != Locked;
        }

        public int Count(CandySortBooster booster)
        {
            return Math.Max(0, stock[Index(booster)]);
        }

        public bool TryConsume(CandySortBooster booster)
        {
            int index = Index(booster);
            if (stock[index] <= 0) return false;
            SetStock(index, stock[index] - 1);
            return true;
        }

        /// <summary>Add one paid use; the caller handles the separate coin transaction.</summary>
        public bool AddPurchasedUse(CandySortBooster booster)
        {
            int index = Index(booster);
            if (stock[index] == Locked || stock[index] == int.MaxValue) return false;
            SetStock(index, stock[index] + 1);
            return true;
        }

        private bool GrantPendingUnlocks()
        {
            bool changed = false;
            for (int i = 0; i < stock.Length; i++)
            {
                if (stock[i] != Locked || highestReachedLevel < UnlockLevel((CandySortBooster)i)) continue;
                // Grant state and stock share this key: exhausted stock (0) stays
                // distinguishable from a gift that has never been granted (-1).
                stock[i] = InitialFreeUses;
                write(StockKeys[i], stock[i]);
                changed = true;
            }
            return changed;
        }

        private void SetStock(int index, int count)
        {
            stock[index] = count;
            write(StockKeys[index], count);
            save();
        }

        private static int Index(CandySortBooster booster)
        {
            int index = (int)booster;
            if ((uint)index >= (uint)StockKeys.Length)
                throw new ArgumentOutOfRangeException(nameof(booster));
            return index;
        }
    }

    /// <summary>One reward claim per level-load attempt, including after undo/replay.</summary>
    public sealed class CandySortWinReward
    {
        private bool claimed;

        public bool TryClaim(bool isWon)
        {
            if (!isWon || claimed) return false;
            claimed = true;
            return true;
        }
    }
}
