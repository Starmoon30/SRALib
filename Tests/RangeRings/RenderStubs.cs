// CPU-side test doubles only. Actual Unity rendering is checked by the game build/manual testing.
namespace UnityEngine
{
    public class Object
    {
        public bool Destroyed;
        public static void Destroy(Object obj) { obj.Destroyed = true; }
    }
    public enum TextureWrapMode { Clamp }
    public class Texture2D { public TextureWrapMode wrapMode; }
    public class Material { public Texture2D mainTexture = new Texture2D(); }
    public struct Color
    {
        public static Color white => default;
        public static Color red => default;
    }
    public struct Vector2
    {
        public float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
    }
    public struct Vector3
    {
        public static Vector3 up => new Vector3(0f, 1f, 0f);
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
    }
    public struct Quaternion { public static Quaternion identity => default; }
    public static class Time { public static int frameCount; }
    public class Mesh : Object
    {
        public string name;
        public Vector3[] Vertices;
        public Vector2[] UVs;
        public int[] Triangles;
        public static readonly List<Mesh> Created = new List<Mesh>();
        public Mesh() { Created.Add(this); }
        public void SetVertices(List<Vector3> value) { Vertices = value.ToArray(); }
        public void SetNormals(List<Vector3> value) { }
        public void SetUVs(int channel, List<Vector2> value) { UVs = value.ToArray(); }
        public void SetTriangles(List<int> value, int submesh) { Triangles = value.ToArray(); }
        public void RecalculateBounds() { }
        public void UploadMeshData(bool unreadable) { }
    }
    public static class Graphics
    {
        public static readonly List<Mesh> Drawn = new List<Mesh>();
        public static void DrawMesh(Mesh mesh, Vector3 position, Quaternion rotation, Material material, int layer)
        {
            if (mesh.Destroyed) throw new Exception("Drawing a destroyed mesh");
            Drawn.Add(mesh);
        }
    }
}

namespace Verse
{
    using UnityEngine;
    public class StaticConstructorOnStartup : Attribute { }
    public struct IntVec3
    {
        public int x, z;
        public IntVec3(int x, int z) { this.x = x; this.z = z; }
        public bool InBounds(Map map) => x >= 0 && z >= 0 && x < map.Size.x && z < map.Size.z;
        public Vector3 ToVector3ShiftedWithAltitude(AltitudeLayer layer) => new Vector3(x + 0.5f, 1f, z + 0.5f);
    }
    public class Map { public IntVec3 Size; }
    public static class Find { public static Map CurrentMap; }
    public enum AltitudeLayer { MetaOverlays }
    public static class ShaderDatabase { public static readonly object Transparent = new object(); }
    public static class MaterialPool
    {
        private static readonly Material Material = new Material();
        public static Material MatFrom(Texture2D texture, object shader, Color color, int queue) => Material;
    }
    public static class MatLoader { public static Material LoadMat(string path, int queue) => new Material(); }
    public static class Rand { public static float ValueSeeded(int seed) => 0; }
    public static class ColorExtensions { public static Color ToOpaque(this Color color) => color; }
}
