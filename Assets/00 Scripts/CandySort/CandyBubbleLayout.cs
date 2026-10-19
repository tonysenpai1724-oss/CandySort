using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace CandySort
{
    /// <summary>
    /// Deterministic, Unity-independent circle targets in an open-topped chamber.
    /// X is centered on the chamber; Y increases upwards from its floor.
    /// </summary>
    public sealed class CandyBubbleLayout
    {
        public struct Point
        {
            public float X;
            public float Y;

            public Point(float x, float y)
            {
                X = x;
                Y = y;
            }
        }

        public struct Body
        {
            public int Id;
            public float Radius;
            public Point Position;

            public Body(int id, float radius)
            {
                Id = id;
                Radius = radius;
                Position = new Point();
            }
        }

        private struct Particle
        {
            public float X, Y, OldX, OldY, VelocityX, VelocityY, Radius, Ceiling;
        }

        private Particle[] particles = new Particle[0];
        private Point[] bestPositions = new Point[0];
        private readonly float halfWidth;
        private readonly uint seed;
        private readonly List<Body> bodies = new List<Body>();
        private readonly ReadOnlyCollection<Body> view;

        public IReadOnlyList<Body> Bodies { get { return view; } }

        public CandyBubbleLayout(float width, int seed)
        {
            if (!Finite(width) || width <= 0f)
                throw new ArgumentOutOfRangeException(nameof(width));
            halfWidth = width * 0.5f;
            this.seed = unchecked((uint)seed);
            view = bodies.AsReadOnly();
        }

        /// <summary>Ignores incoming positions and packs circles in enumeration order.</summary>
        public void Reset(IEnumerable<Body> seeds)
        {
            if (seeds == null) throw new ArgumentNullException(nameof(seeds));
            var incoming = new List<Body>();
            var ids = new HashSet<int>();
            foreach (Body body in seeds)
            {
                ValidateRadius(body.Radius);
                if (!ids.Add(body.Id)) throw new ArgumentException("Duplicate body ID.", nameof(seeds));
                incoming.Add(body);
            }

            bodies.Clear();
            uint random = seed;
            foreach (Body source in incoming)
            {
                Body body = source;
                float limit = halfWidth - body.Radius;
                float preferredX = (Next(ref random) * 2f - 1f) * limit;
                float bestX = preferredX;
                float bestY = Envelope(bestX, body.Radius);
                float bestScore = bestY;
                // Sample the continuous lower envelope, then refine around its minimum.
                // The seed only breaks near-ties: there are no rows, columns or slots.
                const int samples = 48;
                float spacing = 2f * limit / samples;
                for (int i = 0; i <= samples; i++)
                    TryPlacement(-limit + spacing * i, body.Radius, preferredX,
                        ref bestX, ref bestY, ref bestScore);
                float step = spacing;
                for (int i = 0; i < 10; i++)
                {
                    step *= 0.5f;
                    float center = bestX;
                    TryPlacement(Math.Max(-limit, center - step), body.Radius, preferredX,
                        ref bestX, ref bestY, ref bestScore);
                    TryPlacement(Math.Min(limit, center + step), body.Radius, preferredX,
                        ref bestX, ref bestY, ref bestScore);
                }
                body.Position = new Point(bestX, bestY);
                bodies.Add(body);
            }
            // Canonical solver order makes a restored snapshot behave like its source.
            bodies.Sort((a, b) => a.Id.CompareTo(b.Id));
            Settle();
        }

        /// <summary>Settles the remaining circles from their current targets, not a new packing.</summary>
        public void Remove(int id)
        {
            for (int i = 0; i < bodies.Count; i++)
            {
                if (bodies[i].Id != id) continue;
                bodies.RemoveAt(i);
                Settle();
                return;
            }
        }

        public Dictionary<int, Point> Capture()
        {
            var snapshot = new Dictionary<int, Point>(bodies.Count);
            foreach (Body body in bodies) snapshot.Add(body.Id, body.Position);
            return snapshot;
        }

        /// <summary>Restores exact targets without running physics or changing original radii.</summary>
        public void Restore(Dictionary<int, Point> snapshot, IReadOnlyDictionary<int, float> originalRadii)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            if (originalRadii == null) throw new ArgumentNullException(nameof(originalRadii));
            var restored = new List<Body>(snapshot.Count);
            foreach (KeyValuePair<int, Point> entry in snapshot)
            {
                float radius;
                if (!originalRadii.TryGetValue(entry.Key, out radius))
                    throw new ArgumentException("Missing original radius for a restored body.", nameof(originalRadii));
                ValidateRadius(radius);
                if (!Finite(entry.Value.X) || !Finite(entry.Value.Y))
                    throw new ArgumentException("Snapshot positions must be finite.", nameof(snapshot));
                restored.Add(new Body(entry.Key, radius) { Position = entry.Value });
            }
            // Stable physics order even when snapshots were serialized/reordered externally.
            restored.Sort((a, b) => a.Id.CompareTo(b.Id));
            bodies.Clear();
            bodies.AddRange(restored);
        }

        private void TryPlacement(float x, float radius, float preferredX,
            ref float bestX, ref float bestY, ref float bestScore)
        {
            float y = Envelope(x, radius);
            float score = y + Math.Abs(x - preferredX) * 0.025f;
            if (score >= bestScore) return;
            bestX = x;
            bestY = y;
            bestScore = score;
        }

        private float Envelope(float x, float radius)
        {
            float y = radius;
            foreach (Body other in bodies)
            {
                float dx = x - other.Position.X;
                float distance = radius + other.Radius;
                if (Math.Abs(dx) >= distance) continue;
                float top = other.Position.Y + (float)Math.Sqrt(distance * distance - dx * dx);
                y = Math.Max(y, top);
            }
            return y;
        }

        private void Settle()
        {
            // Warm-start the current positions. The temporary ceiling is the old pile
            // height, not the viewport: removing a circle must not grow the queue.
            int count = bodies.Count;
            if (count == 0) return;
            if (particles.Length < count)
            {
                particles = new Particle[count];
                bestPositions = new Point[count];
            }
            float top = 0f;
            double bestEnergy = 0;
            for (int i = 0; i < count; i++)
            {
                top = Math.Max(top, bodies[i].Position.Y + bodies[i].Radius);
                bestPositions[i] = bodies[i].Position;
                bestEnergy += bodies[i].Position.Y;
            }
            for (int i = 0; i < count; i++)
            {
                Body body = bodies[i];
                particles[i] = new Particle { X = body.Position.X, Y = body.Position.Y,
                    Radius = body.Radius, Ceiling = top - body.Radius,
                    VelocityX = ((unchecked((uint)body.Id) ^ seed) & 1u) == 0 ? 0.15f : -0.15f };
            }
            int quiet = 0;
            for (int step = 0; step < 900; step++)
            {
                for (int i = 0; i < count; i++)
                {
                    Particle p = particles[i];
                    p.OldX = p.X;
                    p.OldY = p.Y;
                    p.VelocityY = Math.Max(-12f, p.VelocityY - 0.5f);
                    p.X += p.VelocityX;
                    p.Y += p.VelocityY;
                    particles[i] = p;
                }
                for (int pass = 0; pass < 12; pass++) Separate(count, (pass & 1) != 0);
                float movement = 0f;
                for (int i = 0; i < count; i++)
                {
                    Particle p = particles[i];
                    p.VelocityX = (p.X - p.OldX) * 0.94f;
                    p.VelocityY = (p.Y - p.OldY) * 0.94f;
                    movement = Math.Max(movement, Math.Abs(p.X - p.OldX) + Math.Abs(p.Y - p.OldY));
                    particles[i] = p;
                }
                if ((step & 31) == 31) KeepIfBetter(count, ref bestEnergy);
                quiet = movement < 0.01f ? quiet + 1 : 0;
                if (quiet >= 16) break;
            }
            // With gravity switched off, eliminate residual compression. Keep only
            // separated, lower-energy candidates; a jam never invalidates existing targets.
            for (int pass = 0; pass < 4096; pass++)
                if (Separate(count, (pass & 1) != 0) < 0.005f) break;
            KeepIfBetter(count, ref bestEnergy);
            for (int i = 0; i < count; i++)
            {
                Body body = bodies[i];
                body.Position = bestPositions[i];
                bodies[i] = body;
            }
            FinishGravity();
        }

        private void FinishGravity()
        {
            // Exact downhill casts finish any unobstructed falls even if a crowded PBD
            // candidate was rejected. Every accepted move is downward and collision-safe.
            float stride = Math.Min(2f, halfWidth * 0.01f);
            for (int pass = 0; pass < 400 && stride >= 0.03125f; pass++)
            {
                bool moved = false;
                for (int i = 0; i < bodies.Count; i++)
                {
                    Body body = bodies[i];
                    Point best = Landing(i, body.Position.X);
                    float limit = halfWidth - body.Radius;
                    Point left = Landing(i, Math.Max(-limit, body.Position.X - stride));
                    Point right = Landing(i, Math.Min(limit, body.Position.X + stride));
                    if (left.Y < best.Y - 0.0001f) best = left;
                    if (right.Y < best.Y - 0.0001f) best = right;
                    if (best.Y >= body.Position.Y - 0.0001f) continue;
                    body.Position = best;
                    bodies[i] = body;
                    moved = true;
                }
                if (!moved) stride *= 0.25f;
            }
        }

        private Point Landing(int index, float x)
        {
            Body body = bodies[index];
            float y = body.Radius;
            for (int j = 0; j < bodies.Count; j++)
            {
                if (j == index) continue;
                Body other = bodies[j];
                if (other.Position.Y > body.Position.Y) continue;
                float dx = x - other.Position.X;
                float radius = body.Radius + other.Radius;
                if (Math.Abs(dx) >= radius) continue;
                y = Math.Max(y, other.Position.Y + (float)Math.Sqrt(radius * radius - dx * dx));
            }
            if (y > body.Position.Y) return body.Position;
            for (int j = 0; j < bodies.Count; j++)
            {
                if (j == index) continue;
                Body other = bodies[j];
                float dx = x - other.Position.X;
                float dy = y - other.Position.Y;
                float radius = Math.Max(0f, body.Radius + other.Radius - 0.25f);
                if (dx * dx + dy * dy < radius * radius) return body.Position;
            }
            return new Point(x, y);
        }

        private void KeepIfBetter(int count, ref double bestEnergy)
        {
            double energy = 0;
            for (int i = 0; i < count; i++) energy += particles[i].Y;
            if (energy >= bestEnergy) return;
            for (int i = 0; i < count; i++)
            {
                for (int j = i + 1; j < count; j++)
                {
                    float dx = particles[i].X - particles[j].X;
                    float dy = particles[i].Y - particles[j].Y;
                    float radius = Math.Max(0f, particles[i].Radius + particles[j].Radius - 0.25f);
                    if (dx * dx + dy * dy < radius * radius) return;
                }
            }
            bestEnergy = energy;
            for (int i = 0; i < count; i++) bestPositions[i] = new Point(particles[i].X, particles[i].Y);
        }

        private float Separate(int count, bool reverse)
        {
            float overlap = 0f;
            for (int index = 0; index < count; index++)
            {
                int i = reverse ? count - 1 - index : index;
                Particle a = particles[i];
                for (int j = i + 1; j < count; j++)
                {
                    Particle b = particles[j];
                    float dx = b.X - a.X;
                    float dy = b.Y - a.Y;
                    float radius = a.Radius + b.Radius;
                    float squared = dx * dx + dy * dy;
                    if (squared >= radius * radius) continue;
                    float distance = (float)Math.Sqrt(squared);
                    float depth = radius - distance;
                    overlap = Math.Max(overlap, depth);
                    if (distance < 0.0001f)
                    {
                        dx = ((i + j) & 1) == 0 ? 1f : -1f;
                        dy = 0f;
                        distance = 1f;
                    }
                    float correction = depth * 0.5f / distance;
                    a.X -= dx * correction;
                    a.Y -= dy * correction;
                    b.X += dx * correction;
                    b.Y += dy * correction;
                    particles[j] = b;
                }
                float limit = halfWidth - a.Radius;
                a.X = Math.Max(-limit, Math.Min(limit, a.X));
                a.Y = Math.Max(a.Radius, Math.Min(a.Ceiling, a.Y));
                particles[i] = a;
            }
            for (int i = 0; i < count; i++)
            {
                Particle p = particles[i];
                float limit = halfWidth - p.Radius;
                p.X = Math.Max(-limit, Math.Min(limit, p.X));
                p.Y = Math.Max(p.Radius, Math.Min(p.Ceiling, p.Y));
                particles[i] = p;
            }
            return overlap;
        }

        private void ValidateRadius(float radius)
        {
            if (!Finite(radius) || radius <= 0f || radius > halfWidth)
                throw new ArgumentOutOfRangeException(nameof(radius), "Radius must be positive and fit inside the chamber.");
        }

        private static bool Finite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static float Next(ref uint state)
        {
            // Explicit integer PRNG keeps layouts independent of System.Random/runtime versions.
            state = unchecked(state * 1664525u + 1013904223u);
            return (state >> 8) / 16777216f;
        }
    }
}
