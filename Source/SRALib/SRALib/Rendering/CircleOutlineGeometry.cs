using System;
using System.Collections.Generic;

namespace SRA
{
    /// <summary>
    /// 计算圆形格子区域的外缘。
    /// 坐标相对圆心；方向与 Rot4 一致。独立于 Unity，便于与逐格参考算法做一致性测试。
    /// </summary>
    internal static class CircleOutlineGeometry
    {
        internal struct Edge
        {
            public int X, Z, Direction;

            public Edge(int x, int z, int direction)
            {
                X = x;
                Z = z;
                Direction = direction;
            }
        }

        // 保留原版以 float 距离判断边界的语义，例如 sqrt(2) 附近的半径。
        // 平方使用 double，避免大半径时整数乘法溢出（根本没有这样的半径）。
        internal static bool Contains(int x, int z, float radius)
        {
            return (float)Math.Sqrt((float)((double)x * x + (double)z * z)) <= radius;
        }

        internal static void Build(float radius, int minX, int maxX, int minZ, int maxZ, List<Edge> edges)
        {
            edges.Clear();
            if (!(radius > 0f) || float.IsInfinity(radius) || minX > maxX || minZ > maxZ)
            {
                return;
            }

            int farX = Math.Max(Math.Abs(minX), Math.Abs(maxX));
            int farZ = Math.Max(Math.Abs(minZ), Math.Abs(maxZ));
            if (Contains(farX, farZ, radius))
            {
                // 整张地图在圆内时没有可见圆弧那就不绘制。
                return;
            }

            int reach = radius >= int.MaxValue - 2d ? int.MaxValue - 1 : (int)Math.Ceiling(radius) + 1;
            for (int z = Math.Max(minZ, -reach); z <= Math.Min(maxZ, reach); z++)
            {
                int x = Extent(z, radius, farX + 1);
                if (x < 0) continue;
                if (x >= minX && x < maxX) edges.Add(new Edge(x, z, 1));
                if (-x > minX && -x <= maxX) edges.Add(new Edge(-x, z, 3));
            }

            for (int x = Math.Max(minX, -reach); x <= Math.Min(maxX, reach); x++)
            {
                int z = Extent(x, radius, farZ + 1);
                if (z < 0) continue;
                if (z >= minZ && z < maxZ) edges.Add(new Edge(x, z, 0));
                if (-z > minZ && -z <= maxZ) edges.Add(new Edge(x, -z, 2));
            }
        }

        private static int Extent(int row, float radius, int limit)
        {
            if (!Contains(0, row, radius)) return -1;
            // 地图之外的端点无需精确求值。
            if (Contains(limit, row, radius)) return limit;
            double square = Math.Max(0d, (double)radius * radius - (double)row * row);
            int result = (int)Math.Min(limit, Math.Floor(Math.Sqrt(square)));
            while (result < limit && Contains(result + 1, row, radius)) result++;
            while (result >= 0 && !Contains(result, row, radius)) result--;
            return result;
        }
    }
}
