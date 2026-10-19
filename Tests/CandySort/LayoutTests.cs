using CandySort;

namespace CandySortVerification;

internal static class LayoutTests
{
    public static void Run()
    {
        foreach (float width in new[] { 0f, -1f, float.NaN, float.PositiveInfinity })
            Check.Throws<ArgumentOutOfRangeException>(() => new CandyBubbleLayout(width, 1));
        var layout = new CandyBubbleLayout(644, 11);
        Check.Throws<ArgumentNullException>(() => layout.Reset(null));
        foreach (float radius in new[] { 0f, -1f, 323f, float.NaN, float.PositiveInfinity })
            Check.Throws<ArgumentOutOfRangeException>(() => layout.Reset(new[] { new CandyBubbleLayout.Body(0, radius) }));
        Check.Throws<ArgumentException>(() => layout.Reset(new[] { new CandyBubbleLayout.Body(0, 80), new CandyBubbleLayout.Body(0, 80) }));
        var seeds = Enumerable.Range(0, 18).Select(id => new CandyBubbleLayout.Body(id, 80 + (id % 5) * 8)).ToArray();
        layout.Reset(seeds);
        var radii = layout.Bodies.ToDictionary(b => b.Id, b => b.Radius);
        var initial = layout.Capture();
        Validate(layout, radii);
        var duplicate = new CandyBubbleLayout(644, 11);
        duplicate.Reset(seeds.Select(b => new CandyBubbleLayout.Body(b.Id, b.Radius) { Position = new CandyBubbleLayout.Point(9999, -9999) }));
        Check.That(Same(initial, duplicate.Capture()), "Reset is not deterministic or used incoming positions.");
        layout.Remove(-1);
        Check.That(Same(initial, layout.Capture()), "Removing unknown body mutated layout.");
        float oldTop = Top(layout);
        foreach (int id in new[] { 1, 3, 8, 0, 17, 4 })
        {
            layout.Remove(id);
            duplicate.Remove(id);
            Check.That(layout.Bodies.All(b => b.Id != id), "Removed body still present.");
            Check.That(Same(layout.Capture(), duplicate.Capture()), "Removal settling is nondeterministic.");
            Check.That(Top(layout) <= oldTop + .3f, "Removing a body grew the queue.");
            oldTop = Top(layout);
            Validate(layout, radii);
        }
        var reduced = layout.Capture();
        layout.Restore(initial.Reverse().ToDictionary(p => p.Key, p => p.Value), radii);
        Check.That(Same(initial, layout.Capture()), "Restore failed exact target restoration.");
        layout.Remove(1);
        duplicate.Restore(initial, radii);
        duplicate.Remove(1);
        Check.That(Same(layout.Capture(), duplicate.Capture()), "Restored order changed subsequent physics.");
        Check.Throws<ArgumentNullException>(() => layout.Restore(null, radii));
        Check.Throws<ArgumentNullException>(() => layout.Restore(initial, null));
        Check.Throws<ArgumentException>(() => layout.Restore(initial, new Dictionary<int, float>()));
        Check.Throws<ArgumentException>(() => layout.Restore(new Dictionary<int, CandyBubbleLayout.Point> { [0] = new(float.NaN, 0) }, radii));
        layout.Restore(reduced, radii);
        foreach (int id in reduced.Keys) layout.Remove(id);
        Check.That(layout.Bodies.Count == 0, "Empty layout retained bodies.");
    }

    public static float Top(CandyBubbleLayout layout) => layout.Bodies.Count == 0 ? 0 : layout.Bodies.Max(b => b.Position.Y + b.Radius);

    public static void Validate(CandyBubbleLayout layout, IReadOnlyDictionary<int, float> radii)
    {
        Check.That(layout.Bodies.Select(b => b.Id).Distinct().Count() == layout.Bodies.Count, "Duplicate layout bodies.");
        foreach (var body in layout.Bodies)
        {
            Check.That(float.IsFinite(body.Position.X) && float.IsFinite(body.Position.Y), "Nonfinite body position.");
            Check.That(body.Radius == radii[body.Id], "Body radius changed after candy removal.");
            Check.That(Math.Abs(body.Position.X) + body.Radius <= 322.01f && body.Position.Y >= body.Radius - .01f, "Body escaped walls/floor.");
        }
        for (int i = 0; i < layout.Bodies.Count; i++)
        for (int j = i + 1; j < layout.Bodies.Count; j++)
        {
            var a = layout.Bodies[i];
            var b = layout.Bodies[j];
            double dx = a.Position.X - b.Position.X, dy = a.Position.Y - b.Position.Y;
            Check.That(Math.Sqrt(dx * dx + dy * dy) >= a.Radius + b.Radius - .251, $"Layout bodies overlap: {a.Id}/{b.Id}.");
        }
    }

    private static bool Same(Dictionary<int, CandyBubbleLayout.Point> a, Dictionary<int, CandyBubbleLayout.Point> b) =>
        a.Count == b.Count && a.All(p => b.TryGetValue(p.Key, out var point) && p.Value.X == point.X && p.Value.Y == point.Y);
}
