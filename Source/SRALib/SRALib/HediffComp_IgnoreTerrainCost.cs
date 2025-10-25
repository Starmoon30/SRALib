using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI;

namespace SRA
{
    public static class TerrainIgnoreCache
    {
        private static Dictionary<Pawn, int> ignoreCounts = new Dictionary<Pawn, int>();

        public static bool PawnIgnoresTerrainCost(Pawn pawn)
        {
            return pawn != null && ignoreCounts.TryGetValue(pawn, out int count) && count > 0;
        }

        public static void AddIgnoreTerrain(Pawn pawn)
        {
            if (pawn == null) return;

            if (ignoreCounts.ContainsKey(pawn))
            {
                ignoreCounts[pawn]++;
            }
            else
            {
                ignoreCounts[pawn] = 1;
            }
        }

        public static void RemoveIgnoreTerrain(Pawn pawn)
        {
            if (pawn == null) return;

            if (ignoreCounts.ContainsKey(pawn))
            {
                ignoreCounts[pawn]--;
                if (ignoreCounts[pawn] <= 0)
                {
                    ignoreCounts.Remove(pawn);
                }
            }
        }

        // 清理已销毁的 pawn
        public static void Cleanup()
        {
            var toRemove = new List<Pawn>();
            foreach (var kvp in ignoreCounts)
            {
                if (kvp.Key.Destroyed || !kvp.Key.Spawned)
                {
                    toRemove.Add(kvp.Key);
                }
            }

            foreach (var pawn in toRemove)
            {
                ignoreCounts.Remove(pawn);
            }
        }
    }

    public class HediffComp_IgnoreTerrainCost : HediffComp
    {
        public override void CompPostPostAdd(DamageInfo? dinfo)
        {
            base.CompPostPostAdd(dinfo);
            TerrainIgnoreCache.AddIgnoreTerrain(Pawn);
        }

        public override void CompPostPostRemoved()
        {
            base.CompPostPostRemoved();
            TerrainIgnoreCache.RemoveIgnoreTerrain(Pawn);
        }

        public override void CompExposeData()
        {
            base.CompExposeData();

            // 在加载后重新添加缓存
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                LongEventHandler.ExecuteWhenFinished(() => {
                    TerrainIgnoreCache.AddIgnoreTerrain(Pawn);
                });
            }
        }
        public override string CompTipStringExtra
        {
            get
            {
                return "SRA_IgnoreTerrainCostTipExtra".Translate();
            }
        }
    }

    // HediffCompProperties 保持不变
    public class HediffCompProperties_IgnoreTerrainCost : HediffCompProperties
    {
        public HediffCompProperties_IgnoreTerrainCost()
        {
            this.compClass = typeof(HediffComp_IgnoreTerrainCost);
        }
    }
    [HarmonyPatch(typeof(Pawn_PathFollower), "CostToMoveIntoCell", new Type[] { typeof(Pawn), typeof(IntVec3) })]
    public static class Pawn_PathFollower_CostToMoveIntoCell_Patch
    {
        public static bool Prefix(Pawn pawn, IntVec3 c, ref float __result)
        {
            if (TerrainIgnoreCache.PawnIgnoresTerrainCost(pawn))
            {
                if (c.x == pawn.Position.x || c.z == pawn.Position.z)
                {
                    __result = pawn.TicksPerMoveCardinal;
                }
                else
                {
                    __result = pawn.TicksPerMoveDiagonal;
                }
                return false; // 跳过原始方法
            }
            return true; // 继续执行原始方法
        }
    }
    [HarmonyPatch(typeof(Pawn_PathFollower))]
    [HarmonyPatch("GetPawnCellBaseCostOverride")]
    public static class Pawn_PathFollower_GetPawnCellBaseCostOverride_Patch
    {
        public static bool Prefix(Pawn pawn, IntVec3 c, ref int? __result)
        {
            if (TerrainIgnoreCache.PawnIgnoresTerrainCost(pawn))
            {
                __result = 0;
                return false; // 跳过原始方法
            }
            return true; // 继续执行原始方法
        }
    }
}
