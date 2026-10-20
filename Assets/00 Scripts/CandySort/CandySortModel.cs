using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace CandySort
{
    public enum CandyKind
    {
        SwirlLollipop,
        PurpleJellybean,
        LemonCandy,
        GummyBearRed,
        CandyCane,
        PeppermintMint,
        OrangeWrappedCandy,
        BlueberryCandy,
        ChocolateBar,
        Marshmallow,
        RainbowLollipop,
        GreenJellybean,
        ChocolateTart,
        GummyWorm,
        RockCandyStick,
        StrawberryWrappedCandy,
        SprinkleCandyBall,
        CottonCandy,
        CaramelCube,
        GummyBearGreen,
        CaramelToffee,
        PeppermintSwirl,
        PinkCandyBall,
        BlackLicoriceTwist,
        OrangeCandyBlueGum,
        StripedLollipop,
        ChocolateFudge,
        GummyFish,
        HeartCandy,
        RedCandyBall,
        WaferBar,
        LemonWedge
    }

    /// <summary>
    /// Deterministic, Unity-independent rules for a three-candy order puzzle.
    /// Collection contents are view data: callers must use the move methods to change state.
    /// Every candy in every bubble is selectable; lanes are presentation hints, not blockers.
    /// </summary>
    public sealed class CandySortModel
    {
        public const int CandyKindCount = 32;

        public sealed class Plate
        {
            public CandyKind? Kind { get; internal set; }
            public int Count { get; internal set; }
            public bool Active { get; internal set; }
        }

        public sealed class Bubble
        {
            public int Id { get; internal set; }
            public int Lane { get; internal set; }
            public List<CandyKind> Candies { get; internal set; }
        }

        private readonly List<Plate> plates = new List<Plate>(4);
        private readonly List<Bubble> bubbles = new List<Bubble>();
        private readonly List<CandyKind> pendingOrders = new List<CandyKind>();
        private readonly Stack<Snapshot> history = new Stack<Snapshot>();

        public IReadOnlyList<Plate> Plates { get; private set; }
        public IReadOnlyList<Bubble> Bubbles { get; private set; }
        public IReadOnlyList<CandyKind> PendingOrders { get; private set; }
        public CandyKind?[] Temp { get; private set; }
        public int Level { get; private set; }
        public CandySortLevelDefinition Definition { get; }
        public int TotalCandies { get; private set; }
        public int CompletedOrders { get; private set; }
        public int TargetOrders { get; private set; }
        public int ActivePlates { get; private set; }
        public bool CanUndo { get { return history.Count > 0; } }
        public bool IsWon { get { return CompletedOrders == TargetOrders; } }

        /// <summary>All candies delivered to plates, including partially filled orders.</summary>
        public int SortedCandies
        {
            get
            {
                int count = CompletedOrders * 3;
                for (int i = 0; i < plates.Count; i++) count += plates[i].Count;
                return count;
            }
        }

        /// <summary>Candies not yet part of a completed order (including partial plates).</summary>
        public IReadOnlyDictionary<CandyKind, int> RemainingByKind
        {
            get
            {
                var remaining = new Dictionary<CandyKind, int>();
                for (int i = 0; i < CandyKindCount; i++) remaining[(CandyKind)i] = 0;
                for (int i = 0; i < bubbles.Count; i++)
                    for (int j = 0; j < bubbles[i].Candies.Count; j++)
                        remaining[bubbles[i].Candies[j]]++;
                for (int i = 0; i < Temp.Length; i++)
                    if (Temp[i].HasValue) remaining[Temp[i].Value]++;
                for (int i = 0; i < plates.Count; i++)
                    if (plates[i].Kind.HasValue) remaining[plates[i].Kind.Value] += plates[i].Count;
                return new ReadOnlyDictionary<CandyKind, int>(remaining);
            }
        }

        /// <summary>A full tray is not blocked if any bubble candy can still fill a plate.</summary>
        public bool IsBlocked
        {
            get
            {
                if (IsWon) return false;
                for (int i = 0; i < Temp.Length; i++)
                    if (Temp[i].HasValue && FindPlate(Temp[i].Value) >= 0) return false;
                for (int i = 0; i < bubbles.Count; i++)
                {
                    if (bubbles[i].Candies.Count > 0 && FindFreeTemp() >= 0) return false;
                    for (int j = 0; j < bubbles[i].Candies.Count; j++)
                        if (FindPlate(bubbles[i].Candies[j]) >= 0) return false;
                }
                return true;
            }
        }

        /// <summary>
        /// Builds an authored campaign level (1..30). Seed zero uses its canonical
        /// arrangement; other seeds provide deterministic variants of the same parameters.
        /// </summary>
        public CandySortModel(int level = 1, int seed = 0)
        {
            Definition = CandySortCampaign.GetLevel(level);
            Level = Definition.Level;
            ActivePlates = Definition.ActivePlates;
            TargetOrders = Definition.OrderCount;
            TotalCandies = TargetOrders * 3;
            Temp = new CandyKind?[5];
            Plates = plates.AsReadOnly();
            Bubbles = bubbles.AsReadOnly();
            PendingOrders = pendingOrders.AsReadOnly();

            var random = new DeterministicRandom(unchecked((uint)Definition.Seed ^ (uint)seed));
            var kinds = new List<CandyKind>();
            // Unlock an intentional enum prefix before shuffling; later flavors
            // cannot appear early merely because a seed shuffled a global pool.
            for (int i = 0; i < Definition.FlavorCount; i++) kinds.Add((CandyKind)i);
            Shuffle(kinds, random);
            for (int i = 0; i < TargetOrders; i++)
                pendingOrders.Add(kinds[i % Definition.FlavorCount]);
            Shuffle(pendingOrders, random);

            // Orders are the source of truth: every order contributes exactly three candies.
            var candies = new List<CandyKind>(TotalCandies);
            for (int i = 0; i < pendingOrders.Count; i++)
                for (int j = 0; j < 3; j++) candies.Add(pendingOrders[i]);
            BuildOpeningBubbles(candies, random);
            BuildBubbles(candies, random);
            for (int i = 0; i < 4; i++) plates.Add(new Plate { Active = i < ActivePlates });
            for (int i = 0; i < plates.Count; i++) AssignOrder(i);
        }

        private void BuildOpeningBubbles(List<CandyKind> inventory, DeterministicRandom random)
        {
            if (Definition.OpeningTrayCandies == 0) return;
            var initialKinds = new HashSet<CandyKind>();
            for (int i = 0; i < pendingOrders.Count && initialKinds.Count < ActivePlates; i++)
                initialKinds.Add(pendingOrders[i]);

            var available = new List<CandyKind>();
            for (int i = 0; i < inventory.Count; i++)
                if (!initialKinds.Contains(inventory[i])) available.Add(inventory[i]);
            int count = Math.Min(Definition.OpeningTrayCandies, available.Count);
            var unserved = new List<CandyKind>(count);
            for (int i = 0; i < count; i++)
            {
                int selected = random.Next(available.Count);
                CandyKind kind = available[selected];
                available.RemoveAt(selected);
                inventory.Remove(kind);
                unserved.Add(kind);
            }

            // Two candies per opening plate cannot finish its triple by themselves.
            // Pair them with unserved flavors: taking a match leaves a real mixed
            // bubble to clear, instead of manufacturing rows of singleton bubbles.
            var opening = new List<CandyKind>();
            var served = new List<CandyKind>();
            for (int i = 0; i < pendingOrders.Count && served.Count < ActivePlates; i++)
                if (!served.Contains(pendingOrders[i])) served.Add(pendingOrders[i]);
            int offset = 0;
            for (int round = 0; round < 2; round++)
                for (int i = 0; i < served.Count && offset < unserved.Count; i++)
                {
                    opening.Add(unserved[offset++]);
                    opening.Add(served[i]);
                    inventory.Remove(served[i]);
                }
            for (; offset < unserved.Count; offset++) opening.Add(unserved[offset]);
            for (int i = 0; i < opening.Count;)
            {
                int size = opening.Count - i == 3 ? 3 : 2;
                int bubbleId = bubbles.Count;
                bubbles.Add(new Bubble { Id = bubbleId, Lane = bubbleId % 3,
                    Candies = opening.GetRange(i, size) });
                i += size;
            }
        }

        private void BuildBubbles(List<CandyKind> inventory, DeterministicRandom random)
        {
            while (inventory.Count > 0)
            {
                int size = inventory.Count <= Definition.MaxBubbleSize ? inventory.Count :
                    Definition.MinBubbleSize +
                    random.Next(Definition.MaxBubbleSize - Definition.MinBubbleSize + 1);
                // Leave enough candies for the last group rather than emitting a tiny tail.
                int left = inventory.Count - size;
                if (left > 0 && left < Definition.MinBubbleSize)
                    size -= Definition.MinBubbleSize - left;

                CandyKind leadingKind = inventory[random.Next(inventory.Count)];
                var group = new List<CandyKind>(size);
                for (int i = 0; i < size; i++)
                {
                    bool different = i > 0 && random.Next(100) < Definition.Mixing;
                    int index = ChooseCandy(inventory, leadingKind, different, random);
                    group.Add(inventory[index]);
                    inventory.RemoveAt(index);
                }
                int bubbleId = bubbles.Count;
                bubbles.Add(new Bubble { Id = bubbleId, Lane = bubbleId % 3, Candies = group });
            }
        }

        private static int ChooseCandy(List<CandyKind> inventory, CandyKind leadingKind,
            bool different, DeterministicRandom random)
        {
            int candidates = 0;
            for (int i = 0; i < inventory.Count; i++)
                if ((inventory[i] != leadingKind) == different) candidates++;
            // Exhausted flavors never add or discard inventory: use another remaining candy.
            if (candidates == 0) return random.Next(inventory.Count);
            int selected = random.Next(candidates);
            for (int i = 0; i < inventory.Count; i++)
                if ((inventory[i] != leadingKind) == different && selected-- == 0) return i;
            throw new InvalidOperationException("Candy inventory changed during generation.");
        }

        /// <summary>Routes the selected candy to a matching plate, otherwise to the first free slot.</summary>
        public bool TryTake(int bubbleId, int candyIndex)
        {
            int index = bubbles.FindIndex(bubble => bubble.Id == bubbleId);
            if (IsWon || index < 0 || candyIndex < 0 || candyIndex >= bubbles[index].Candies.Count)
                return false;
            CandyKind kind = bubbles[index].Candies[candyIndex];
            int plateIndex = FindPlate(kind);
            int tempIndex = plateIndex < 0 ? FindFreeTemp() : -1;
            if (plateIndex < 0 && tempIndex < 0) return false;

            history.Push(Capture());
            bubbles[index].Candies.RemoveAt(candyIndex);
            if (bubbles[index].Candies.Count == 0) bubbles.RemoveAt(index);
            if (plateIndex >= 0) PutOnPlate(plateIndex);
            else Temp[tempIndex] = kind;
            DrainTemp();
            return true;
        }

        /// <summary>Uses a stored candy only if it matches an assigned plate. Failed moves do nothing.</summary>
        public bool TryUseTemp(int slot)
        {
            if (IsWon || slot < 0 || slot >= Temp.Length || !Temp[slot].HasValue) return false;
            int plateIndex = FindPlate(Temp[slot].Value);
            if (plateIndex < 0) return false;
            history.Push(Capture());
            Temp[slot] = null;
            PutOnPlate(plateIndex);
            DrainTemp();
            return true;
        }

        /// <summary>Restores the whole last move, including all automatic completion cascades.</summary>
        public bool Undo()
        {
            if (!CanUndo) return false;
            Snapshot snapshot = history.Pop();
            CompletedOrders = snapshot.CompletedOrders;
            for (int i = 0; i < plates.Count; i++)
            {
                plates[i].Kind = snapshot.PlateKinds[i];
                plates[i].Count = snapshot.PlateCounts[i];
            }
            Array.Copy(snapshot.Temp, Temp, Temp.Length);
            pendingOrders.Clear();
            pendingOrders.AddRange(snapshot.PendingOrders);
            bubbles.Clear();
            bubbles.AddRange(snapshot.Bubbles);
            return true;
        }

        private int FindPlate(CandyKind kind)
        {
            for (int i = 0; i < plates.Count; i++)
                if (plates[i].Active && plates[i].Kind == kind && plates[i].Count < 3) return i;
            return -1;
        }

        private int FindFreeTemp()
        {
            for (int i = 0; i < Temp.Length; i++) if (!Temp[i].HasValue) return i;
            return -1;
        }

        private void PutOnPlate(int index)
        {
            Plate plate = plates[index];
            plate.Count++;
            if (plate.Count != 3) return;
            CompletedOrders++;
            plate.Count = 0;
            plate.Kind = null;
            AssignOrder(index);
        }

        private void AssignOrder(int plateIndex)
        {
            Plate plate = plates[plateIndex];
            if (!plate.Active || plate.Kind.HasValue || pendingOrders.Count == 0) return;
            int selected = -1;
            // Take the earliest pending kind not already assigned, preserving the other orders.
            // Rest when all pending kinds are assigned; their plates process repeats sequentially.
            for (int i = 0; i < pendingOrders.Count; i++)
            {
                if (FindPlate(pendingOrders[i]) >= 0) continue;
                selected = i;
                break;
            }
            if (selected < 0) return;
            plate.Kind = pendingOrders[selected];
            pendingOrders.RemoveAt(selected);
        }

        private void DrainTemp()
        {
            bool moved;
            do
            {
                moved = false;
                for (int i = 0; i < Temp.Length; i++)
                {
                    if (!Temp[i].HasValue) continue;
                    int plateIndex = FindPlate(Temp[i].Value);
                    if (plateIndex < 0) continue;
                    Temp[i] = null;
                    PutOnPlate(plateIndex);
                    moved = true;
                }
            } while (moved);
        }

        private Snapshot Capture()
        {
            var snapshot = new Snapshot
            {
                CompletedOrders = CompletedOrders,
                Temp = (CandyKind?[])Temp.Clone(),
                PendingOrders = new List<CandyKind>(pendingOrders)
            };
            for (int i = 0; i < plates.Count; i++)
            {
                snapshot.PlateKinds[i] = plates[i].Kind;
                snapshot.PlateCounts[i] = plates[i].Count;
            }
            for (int i = 0; i < bubbles.Count; i++)
                snapshot.Bubbles.Add(new Bubble
                {
                    Id = bubbles[i].Id,
                    Lane = bubbles[i].Lane,
                    Candies = new List<CandyKind>(bubbles[i].Candies)
                });
            return snapshot;
        }

        private sealed class Snapshot
        {
            public int CompletedOrders;
            public CandyKind?[] Temp;
            public List<CandyKind> PendingOrders;
            public readonly CandyKind?[] PlateKinds = new CandyKind?[4];
            public readonly int[] PlateCounts = new int[4];
            public readonly List<Bubble> Bubbles = new List<Bubble>();
        }

        private static void Shuffle<T>(List<T> values, DeterministicRandom random)
        {
            for (int i = values.Count - 1; i > 0; i--)
            {
                int other = random.Next(i + 1);
                T swap = values[i];
                values[i] = values[other];
                values[other] = swap;
            }
        }

        // Fixed PRNG, rather than System.Random, keeps seeds stable across Unity/.NET versions.
        private sealed class DeterministicRandom
        {
            private uint state;
            public DeterministicRandom(uint seed) { state = seed == 0 ? 1831565813u : seed; }
            public int Next(int maximum)
            {
                uint value = state;
                value ^= value << 13;
                value ^= value >> 17;
                value ^= value << 5;
                state = value;
                return (int)(value % (uint)maximum);
            }
        }
    }
}
