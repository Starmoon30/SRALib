using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace SRA
{
    /// <summary>
    /// 原版格子外缘风格的圆形范围提示。仅在主线程绘制阶段调用。
    /// 缓存只持有相对坐标网格；不同圆心可以复用同一网格。
    /// </summary>
    [StaticConstructorOnStartup]
    public static class RangeRingDrawer
    {
        public const float DefaultHideAtOrAboveRadius = 500f;
        private const int MaxCachedRings = 128;
        private const int MaxCachedVertices = 262144;
        private const int EdgesPerMesh = 4096;
        // 与 GenDraw 的私有 TargetSquareMatSide 使用同一个原版资源。
        private static readonly Texture2D EdgeTexture = (Texture2D)MatLoader.LoadMat("Misc/FieldEdge", -1).mainTexture;
        private static readonly Dictionary<CacheKey, LinkedListNode<CachedRing>> Cache = new Dictionary<CacheKey, LinkedListNode<CachedRing>>();
        private static readonly LinkedList<CachedRing> Recent = new LinkedList<CachedRing>();
        private static readonly List<CircleOutlineGeometry.Edge> Edges = new List<CircleOutlineGeometry.Edge>();
        private static readonly List<Vector3> Vertices = new List<Vector3>();
        private static readonly List<Vector3> Normals = new List<Vector3>();
        private static readonly List<Vector2> UVs = new List<Vector2>();
        private static readonly List<int> Triangles = new List<int>();
        private static readonly Vector3[] Corners =
        {
            new Vector3(-0.5f, 0f, -0.5f), new Vector3(-0.5f, 0f, 0.5f),
            new Vector3(0.5f, 0f, 0.5f), new Vector3(0.5f, 0f, -0.5f)
        };
        private static readonly Vector2[] CornerUVs =
        {
            new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f)
        };
        private static int cachedVertices;

        static RangeRingDrawer()
        {
            EdgeTexture.wrapMode = TextureWrapMode.Clamp;
        }

        /// <param name="center">圆心所在地图格，与原版 DrawRadiusRing 一致。</param>
        /// <param name="radius">实际显示半径，不受 GenRadial.MaxRadialPatternRadius 限制。</param>
        /// <param name="color">默认白色；使用原版透明外缘贴图。</param>
        /// <param name="hideAtOrAboveRadius">达到此半径则不绘制；默认 500，设为 0 或负数可禁用隐藏。</param>
        /// <param name="map">用于地图边缘裁剪。</param>
        public static void Draw(IntVec3 center, float radius, Color? color = null,
            float hideAtOrAboveRadius = DefaultHideAtOrAboveRadius, Map map = null)
        {
            map = map ?? Find.CurrentMap;
            if (map == null || map != Find.CurrentMap || !center.InBounds(map)
                || !(radius > 0f) || float.IsInfinity(radius)
                || (hideAtOrAboveRadius > 0f && radius >= hideAtOrAboveRadius)) return;

            int farX = Math.Max(center.x, map.Size.x - 1 - center.x);
            int farZ = Math.Max(center.z, map.Size.z - 1 - center.z);
            if (CircleOutlineGeometry.Contains(farX, farZ, radius)) return;

            TrimCache();

            // 圆外多保留一格用于判断相邻格。远离地图边缘时，所有圆心的缓存键相同。
            int reach = radius >= int.MaxValue - 2d ? int.MaxValue - 1 : (int)Math.Ceiling(radius) + 1;
            CacheKey key = new CacheKey(radius,
                Math.Max(-center.x, -reach), Math.Min(map.Size.x - 1 - center.x, reach),
                Math.Max(-center.z, -reach), Math.Min(map.Size.z - 1 - center.z, reach));
            if (!Cache.TryGetValue(key, out LinkedListNode<CachedRing> node))
            {
                CachedRing ring = Build(key);
                node = Recent.AddFirst(ring);
                Cache.Add(key, node);
                cachedVertices += ring.VertexCount;
            }
            else if (node != Recent.First)
            {
                Recent.Remove(node);
                Recent.AddFirst(node);
            }
            node.Value.LastDrawFrame = Time.frameCount;
            TrimCache();

            Color tint = color ?? Color.white;
            Material material = MaterialPool.MatFrom(EdgeTexture, ShaderDatabase.Transparent, tint, 2900);
            // 与原版相同的 MetaOverlays 层级和按颜色固定的微小高度偏移。
            Vector3 position = center.ToVector3ShiftedWithAltitude(AltitudeLayer.MetaOverlays);
            position.y += Rand.ValueSeeded(tint.ToOpaque().GetHashCode()) * 0.03658537f / 10f;
            Mesh[] meshes = node.Value.Meshes;
            for (int i = 0; i < meshes.Length; i++)
            {
                Graphics.DrawMesh(meshes[i], position, Quaternion.identity, material, 0);
            }
        }

        /// <summary>在主线程、尚未提交本帧圆环绘制时调用，释放原生 Mesh 与工作缓冲区。</summary>
        public static void ClearCache()
        {
            foreach (CachedRing ring in Recent) Destroy(ring);
            Recent.Clear();
            Cache.Clear();
            cachedVertices = 0;
            Edges.Clear(); Edges.TrimExcess();
            Vertices.Clear(); Vertices.TrimExcess();
            Normals.Clear(); Normals.TrimExcess();
            UVs.Clear(); UVs.TrimExcess();
            Triangles.Clear(); Triangles.TrimExcess();
        }

        private static void Destroy(CachedRing ring)
        {
            for (int i = 0; i < ring.Meshes.Length; i++) UnityEngine.Object.Destroy(ring.Meshes[i]);
        }

        private static void TrimCache()
        {
            // 同时限制条目数和几何量。Unity 会延后执行 DrawMesh。
            // 极端多选时允许暂时超预算，在后续绘制帧回收；单个超大圆只保留自身。
            while (Recent.Count > 1 && (Recent.Count > MaxCachedRings || cachedVertices > MaxCachedVertices))
            {
                LinkedListNode<CachedRing> oldest = Recent.Last;
                if (oldest.Value.LastDrawFrame == Time.frameCount) break;
                Recent.RemoveLast();
                Cache.Remove(oldest.Value.Key);
                cachedVertices -= oldest.Value.VertexCount;
                Destroy(oldest.Value);
            }
        }

        private static CachedRing Build(CacheKey key)
        {
            CircleOutlineGeometry.Build(key.Radius, key.MinX, key.MaxX, key.MinZ, key.MaxZ, Edges);
            Mesh[] meshes = new Mesh[(Edges.Count + EdgesPerMesh - 1) / EdgesPerMesh];
            for (int part = 0; part < meshes.Length; part++)
            {
                Vertices.Clear();
                Normals.Clear();
                UVs.Clear();
                Triangles.Clear();
                int end = Math.Min(Edges.Count, (part + 1) * EdgesPerMesh);
                for (int i = part * EdgesPerMesh; i < end; i++)
                {
                    CircleOutlineGeometry.Edge edge = Edges[i];
                    int first = Vertices.Count;
                    for (int corner = 0; corner < 4; corner++)
                    {
                        // 等价于原版 plane10 + Rot4 的变换，不在构建循环里计算四元数。
                        Vector3 p = Corners[corner];
                        float x = p.x, z = p.z;
                        switch (edge.Direction)
                        {
                            case 1: x = p.z; z = -p.x; break;
                            case 2: x = -p.x; z = -p.z; break;
                            case 3: x = -p.z; z = p.x; break;
                        }
                        Vertices.Add(new Vector3(edge.X + x, 0f, edge.Z + z));
                        Normals.Add(Vector3.up);
                        UVs.Add(CornerUVs[corner]);
                    }
                    Triangles.Add(first); Triangles.Add(first + 1); Triangles.Add(first + 2);
                    Triangles.Add(first); Triangles.Add(first + 2); Triangles.Add(first + 3);
                }
                Mesh mesh = new Mesh { name = "SRA range ring" };
                mesh.SetVertices(Vertices);
                mesh.SetNormals(Normals);
                mesh.SetUVs(0, UVs);
                mesh.SetTriangles(Triangles, 0);
                mesh.RecalculateBounds();
                mesh.UploadMeshData(true);
                meshes[part] = mesh;
            }
            return new CachedRing { Key = key, Meshes = meshes, VertexCount = Edges.Count * 4 };
        }

        private sealed class CachedRing
        {
            public CacheKey Key;
            public Mesh[] Meshes;
            public int VertexCount;
            public int LastDrawFrame = -1;
        }

        private readonly struct CacheKey : IEquatable<CacheKey>
        {
            public readonly float Radius;
            public readonly int MinX, MaxX, MinZ, MaxZ;

            public CacheKey(float radius, int minX, int maxX, int minZ, int maxZ)
            {
                Radius = radius;
                MinX = minX; MaxX = maxX; MinZ = minZ; MaxZ = maxZ;
            }

            public bool Equals(CacheKey other) => Radius == other.Radius && MinX == other.MinX
                && MaxX == other.MaxX && MinZ == other.MinZ && MaxZ == other.MaxZ;
            public override bool Equals(object obj) => obj is CacheKey other && Equals(other);
            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = Radius.GetHashCode();
                    hash = hash * 397 ^ MinX; hash = hash * 397 ^ MaxX;
                    hash = hash * 397 ^ MinZ;
                    return hash * 397 ^ MaxZ;
                }
            }
        }
    }
}
