using SRA;
using UnityEngine;
using Verse;
using System.Diagnostics;

internal static class Program
{
    private static int comparisons;

    private static void Main()
    {
        // Reference uses the original float-distance membership and four-neighbour border rules.
        foreach (float radius in new[] { 0.01f, 1f, 1.5f, 2f, 20.5f, 79f, 79.81228f, 80f, 200f, 499f, 500f, 1200f, float.MaxValue })
        {
            Compare(radius, 123, 87, 0, 0);
            Compare(radius, 123, 87, 122, 86);
            Compare(radius, 123, 87, 50, 40);
            Compare(radius, 1, 100, 0, 40);
        }
        Compare(500f, 1200, 1200, 600, 600);
        Compare(1200f, 2500, 100, 1250, 10);
        for (int square = 1; square <= 300; square++)
        {
            float radius = MathF.Sqrt(square);
            Compare(MathF.BitDecrement(radius), 50, 60, 25, 30);
            Compare(radius, 50, 60, 25, 30);
            Compare(MathF.BitIncrement(radius), 50, 60, 25, 30);
        }
        Random random = new Random(12345);
        for (int i = 0; i < 2000; i++)
        {
            int w = random.Next(1, 120), h = random.Next(1, 120);
            Compare((float)random.NextDouble() * 170 + 0.001f, w, h, random.Next(w), random.Next(h));
        }
        Console.WriteLine($"PASS: {comparisons} geometry/reference + normalized-cache-bound comparisons");
        TestRenderer();
        Benchmark();
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private static void Compare(float radius, int width, int height, int cx, int cz)
    {
        var expected = new HashSet<(int, int, int)>();
        bool Inside(int x, int z) => (float)Math.Sqrt((float)((double)x * x + (double)z * z)) <= radius;
        for (int x = -cx; x < width - cx; x++)
        for (int z = -cz; z < height - cz; z++)
        {
            if (!Inside(x, z)) continue;
            if (z < height - cz - 1 && !Inside(x, z + 1)) expected.Add((x, z, 0));
            if (x < width - cx - 1 && !Inside(x + 1, z)) expected.Add((x, z, 1));
            if (z > -cz && !Inside(x, z - 1)) expected.Add((x, z, 2));
            if (x > -cx && !Inside(x - 1, z)) expected.Add((x, z, 3));
        }
        var edges = new List<CircleOutlineGeometry.Edge>();
        CircleOutlineGeometry.Build(radius, -cx, width - cx - 1, -cz, height - cz - 1, edges);
        Verify();
        int reach = radius >= int.MaxValue - 2d ? int.MaxValue - 1 : (int)Math.Ceiling(radius) + 1;
        CircleOutlineGeometry.Build(radius, Math.Max(-cx, -reach), Math.Min(width - cx - 1, reach),
            Math.Max(-cz, -reach), Math.Min(height - cz - 1, reach), edges);
        Verify();
        comparisons++;

        void Verify()
        {
            var actual = edges.Select(e => (e.X, e.Z, e.Direction)).ToHashSet();
            Check(actual.Count == edges.Count, "Duplicate edges");
            Check(actual.SetEquals(expected), $"Boundary mismatch: r={radius}, map={width}x{height}, center={cx},{cz}");
        }
    }

    private static void TestRenderer()
    {
        Find.CurrentMap = new Map { Size = new IntVec3(3000, 3000) };
        IntVec3 center = new IntVec3(1500, 1500);
        RangeRingDrawer.Draw(center, 500f);
        RangeRingDrawer.Draw(center, float.NaN);
        RangeRingDrawer.Draw(center, float.PositiveInfinity);
        RangeRingDrawer.Draw(center, -1f);
        RangeRingDrawer.Draw(center, 10f, map: new Map { Size = new IntVec3(3000, 3000) });
        Check(Mesh.Created.Count == 0, "Hidden/invalid/off-map rings allocated geometry");
        RangeRingDrawer.Draw(center, 500f, hideAtOrAboveRadius: 0f);
        Check(Graphics.Drawn.Count == 1, "500-cell ring should render when hiding disabled");
        Mesh shared = Graphics.Drawn[0];
        RangeRingDrawer.Draw(new IntVec3(1600, 1600), 500f, hideAtOrAboveRadius: 0f);
        Check(Mesh.Created.Count == 1 && Graphics.Drawn.Last() == shared, "Interior rings did not share mesh");
        RangeRingDrawer.Draw(new IntVec3(0, 0), 500f, hideAtOrAboveRadius: 0f);
        Check(Mesh.Created.Count == 2, "Clipped ring should have a separate cache entry");
        RangeRingDrawer.Draw(center, 1200f, hideAtOrAboveRadius: -1f);
        Check(Mesh.Created.Count == 5, "Large ring should use three 16-bit-safe mesh chunks");
        foreach (Mesh mesh in Mesh.Created)
        {
            Check(mesh.Vertices.Length <= 16384, "Mesh exceeds chunk vertex limit");
            Check(mesh.Triangles.All(i => i >= 0 && i < mesh.Vertices.Length), "Invalid triangle indices");
            for (int i = 0; i < mesh.Triangles.Length; i += 3)
            {
                Vector3 a = mesh.Vertices[mesh.Triangles[i]], b = mesh.Vertices[mesh.Triangles[i + 1]], c = mesh.Vertices[mesh.Triangles[i + 2]];
                Check((b.z - a.z) * (c.x - a.x) - (b.x - a.x) * (c.z - a.z) > 0f, "Triangle winding is not upward");
            }
        }
        RangeRingDrawer.ClearCache();
        Check(Mesh.Created.All(m => m.Destroyed), "ClearCache leaked meshes");
        Mesh.Created.Clear();
        Graphics.Drawn.Clear();
        Time.frameCount++;
        for (int i = 0; i < 150; i++) RangeRingDrawer.Draw(center, i + 1f);
        Check(Mesh.Created.All(m => !m.Destroyed), "Evicted a mesh submitted this frame");
        Time.frameCount++;
        RangeRingDrawer.Draw(center, 150f);
        Check(Mesh.Created.Count(m => !m.Destroyed) <= 128, "Cache entry budget not enforced next frame");
        Check(Mesh.Created.Where(m => !m.Destroyed).Sum(m => m.Vertices.Length) <= 262144, "Cache vertex budget not enforced");
        RangeRingDrawer.ClearCache();
        Console.WriteLine("PASS: hiding threshold, invalid inputs, map isolation, mesh reuse/clipping/chunking, winding, deferred eviction and cleanup");
    }

    private static void Benchmark()
    {
        // CPU geometry generation only, not a claim about Unity frame rate or GPU cost.
        var edges = new List<CircleOutlineGeometry.Edge>();
        foreach (float radius in new[] { 79f, 200f, 499f })
        {
            for (int i = 0; i < 1000; i++) CircleOutlineGeometry.Build(radius, -600, 600, -600, 600, edges);
            Stopwatch watch = new Stopwatch();
            long before = GC.GetAllocatedBytesForCurrentThread();
            watch.Start();
            for (int i = 0; i < 10000; i++) CircleOutlineGeometry.Build(radius, -600, 600, -600, 600, edges);
            watch.Stop();
            long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
            Console.WriteLine($"Cold-boundary calculation r={radius}: {edges.Count} edges, {watch.Elapsed.TotalMilliseconds / 10:F2} us/build, ~{bytes} bytes/10000 builds with reused buffers");
        }
    }
}
