using RimWorld;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace SRA
{
    public class GameComponent_DropPodInterceptor : GameComponent
    {
        private const string DefaultAircraftDefName = "SRA_HiveCorvette_Entity";
        private const string DefaultInterceptFlyOverDefName = "SRA_HiveCorvetteB_Fake";
        
        // 配置参数
        private const int BASE_INTERCEPT_COOLDOWN = 0; // 基础冷却时间（一天）
        private const int AIRCRAFT_COOLDOWN_FACTOR = 0; // 每架飞机的额外冷却时间（约33秒）
        private const float INTERCEPT_CHANCE_PER_AIRCRAFT = 0.15f; // 每架飞机增加15%拦截概率
        private const float MIN_INTERCEPT_CHANCE = 1f; // 最小拦截概率（即使飞机很少）
        private const float MAX_INTERCEPT_CHANCE = 1f; // 最大拦截概率
        private const int MIN_AIRCRAFT_REQUIRED = 1; // 最小需要飞机数
        private const int MAX_INTERCEPT_COUNT = 9999; // 最大拦截人数
        
        private bool interceptEnabled;

        public bool IsInterceptEnabled => interceptEnabled;

        public GameComponent_DropPodInterceptor(Game game)
        {
        }

        public bool ToggleIntercept()
        {
            interceptEnabled = !interceptEnabled;
            SRALog.Debug($"DropPodInterceptor toggled: {interceptEnabled}");
            return interceptEnabled;
        }

        /// <summary>
        /// 获取可用飞机数量
        /// </summary>
        public int GetAvailableAircraftCount(ThingDef requiredAircraftDef = null)
        {
            WorldComponent_AircraftManager manager = Find.World?.GetComponent<WorldComponent_AircraftManager>();
            if (manager == null || Faction.OfPlayer == null)
            {
                return 0;
            }

            ThingDef aircraftDef = requiredAircraftDef ?? DefDatabase<ThingDef>.GetNamedSilentFail(DefaultAircraftDefName);
            if (aircraftDef == null)
            {
                SRALog.Debug($"DropPodInterceptor: missing aircraft def {DefaultAircraftDefName}");
                return 0;
            }

            return manager.GetAvailableAircraftCount(aircraftDef, Faction.OfPlayer);
        }

        /// <summary>
        /// 计算拦截概率（基于飞机数量）
        /// </summary>
        private float CalculateInterceptChance(int aircraftCount)
        {
            if (aircraftCount < MIN_AIRCRAFT_REQUIRED)
                return 0f;
            
            float baseChance = MIN_INTERCEPT_CHANCE;
            float additionalChance = Mathf.Min(
                aircraftCount * INTERCEPT_CHANCE_PER_AIRCRAFT,
                MAX_INTERCEPT_CHANCE - baseChance
            );
            
            return Mathf.Clamp(baseChance + additionalChance, 0f, MAX_INTERCEPT_CHANCE);
        }

        /// <summary>
        /// 计算可拦截的最大人数（基于飞机数量）
        /// </summary>
        private int CalculateMaxInterceptCount(int aircraftCount, int totalPawns)
        {
            if (aircraftCount < MIN_AIRCRAFT_REQUIRED)
                return 0;
            
            // 基本拦截能力：每架飞机可以拦截3个目标
            int baseIntercept = Mathf.Min(aircraftCount*3, totalPawns - 1);
            
            // 额外拦截能力：飞机数量超过5架后，每1架飞机增加1个拦截名额
            if (aircraftCount > 5)
            {
                int extraIntercept = (aircraftCount - 5);
                baseIntercept += extraIntercept;
            }
            
            // 确保至少留下1个敌人，且不超过最大限制
            return Mathf.Clamp(baseIntercept, 1, Mathf.Min(MAX_INTERCEPT_COUNT, totalPawns - 1));
        }

        /// <summary>
        /// 计算冷却时间（基于使用的飞机数量）
        /// </summary>
        private int CalculateCooldownTicks(int aircraftUsed)
        {
            // 基础冷却 + 每架飞机的额外冷却
            return BASE_INTERCEPT_COOLDOWN + (aircraftUsed * AIRCRAFT_COOLDOWN_FACTOR);
        }

        /// <summary>
        /// 尝试拦截空投舱
        /// </summary>
        public bool TryInterceptDropPods(List<Pawn> pawns, IncidentParms parms, out List<Pawn> interceptedPawns)
        {
            interceptedPawns = new List<Pawn>();

            if (pawns == null || pawns.Count <= 1 || !interceptEnabled)
            {
                return false;
            }

            if (parms == null || parms.faction == null || Faction.OfPlayer == null || !parms.faction.HostileTo(Faction.OfPlayer))
            {
                return false;
            }

            // 获取可用飞机数量
            int aircraftCount = GetAvailableAircraftCount();
            if (aircraftCount < MIN_AIRCRAFT_REQUIRED)
            {
                return false;
            }

            // 计算拦截概率
            float interceptChance = CalculateInterceptChance(aircraftCount);
            if (!Rand.Chance(interceptChance))
            {
                SRALog.Debug($"Intercept failed due to chance: {interceptChance:P0}");
                return false;
            }

            Map map = parms.target as Map;
            if (map == null)
            {
                SRALog.Debug("DropPodInterceptor: target map missing.");
                return false;
            }

            int validPawnCount = pawns.Count(p => p != null);
            if (validPawnCount <= 1)
            {
                return false;
            }

            // 计算最大可拦截人数
            int maxInterceptCount = CalculateMaxInterceptCount(aircraftCount, validPawnCount);
            if (maxInterceptCount <= 0)
            {
                return false;
            }

            // 实际拦截数量（根据飞机数量和敌人数量决定）
            int interceptCount = Rand.RangeInclusive(
                Mathf.Min(1, maxInterceptCount), // 至少拦截1个
                maxInterceptCount
            );

            // 尝试消耗飞机
            if (!TryUseAircraftForIntercept(interceptCount))
            {
                SRALog.Debug("Failed to use aircraft for intercept");
                return false;
            }

            List<Pawn> selected = pawns.Where(p => p != null).InRandomOrder().Take(interceptCount).ToList();
            if (selected.Count == 0)
            {
                return false;
            }

            List<Thing> corpses = new List<Thing>();
            foreach (Pawn pawn in selected)
            {
                if (!pawns.Remove(pawn))
                {
                    continue;
                }

                interceptedPawns.Add(pawn);

                if (!pawn.Dead)
                {
                    pawn.Kill(new DamageInfo(DamageDefOf.Bite, 9999f));
                }

                Corpse corpse = pawn.Corpse;
                if (corpse != null && !corpse.Spawned && !corpse.Destroyed)
                {
                    corpses.Add(corpse);
                }
            }

            if (interceptedPawns.Count == 0)
            {
                return false;
            }

            IntVec3 dropCenter = parms.spawnCenter.IsValid ? parms.spawnCenter : map.Center;
            if (corpses.Count > 0)
            {
                DropPodUtility.DropThingsNear(dropCenter, map, corpses, leaveSlag: true);
            }

            SpawnInterceptionFlyOver(map, dropCenter, interceptedPawns.Count);
            SendInterceptionLetter(map, interceptedPawns.Count, dropCenter, aircraftCount);

            SRALog.Debug($"DropPodInterceptor: intercepted {interceptedPawns.Count} raid pawns using {interceptCount} aircraft.");
            return true;
        }

        /// <summary>
        /// 为拦截行动使用飞机
        /// </summary>
        private bool TryUseAircraftForIntercept(int interceptCount)
        {
            WorldComponent_AircraftManager manager = Find.World?.GetComponent<WorldComponent_AircraftManager>();
            if (manager == null || Faction.OfPlayer == null)
            {
                return false;
            }

            ThingDef aircraftDef = DefDatabase<ThingDef>.GetNamedSilentFail(DefaultAircraftDefName);
            if (aircraftDef == null)
            {
                return false;
            }

            // 计算需要使用的飞机数量（1个飞机可以处理1-2个目标）
            int aircraftToUse = Mathf.CeilToInt(interceptCount / 2f);
            aircraftToUse = Mathf.Max(1, aircraftToUse); // 至少使用1架
            
            // 计算冷却时间
            int cooldownTicks = CalculateCooldownTicks(aircraftToUse);
            
            // 尝试使用飞机
            bool success = manager.TryUseAircraft(aircraftDef, aircraftToUse, Faction.OfPlayer, cooldownTicks);
            
            if (success)
            {
                SRALog.Debug($"Using {aircraftToUse} aircraft for intercept, cooldown: {cooldownTicks} ticks");
            }
            
            return success;
        }

        /// <summary>
        /// 生成拦截飞越效果（数量影响视觉效果）
        /// </summary>
        private void SpawnInterceptionFlyOver(Map map, IntVec3 dropCenter, int interceptCount)
        {
            ThingDef flyOverDef = DefDatabase<ThingDef>.GetNamedSilentFail(DefaultInterceptFlyOverDefName);
            if (flyOverDef == null)
            {
                SRALog.Debug($"DropPodInterceptor: missing fly over def {DefaultInterceptFlyOverDefName}.");
                return;
            }

            // 根据拦截数量决定飞越飞机数量
            int flyOverCount = Mathf.Clamp(interceptCount / 3 + 1, 1, 5);
            
            for (int i = 0; i < flyOverCount; i++)
            {
                IntVec3 start = GetRandomMapEdgeCell(map);
                IntVec3 end = dropCenter.IsValid && dropCenter.InBounds(map) ? dropCenter : map.Center;
                
                // 添加随机偏移，使飞越更有层次感
                if (i > 0)
                {
                    end.x += Rand.Range(-5, 5);
                    end.z += Rand.Range(-5, 5);
                    end = end.ClampInsideMap(map);
                }
                
                FlyOver.MakeFlyOver(flyOverDef, start, end, map, 
                    speed: 5f + Rand.Range(0f, 2f), 
                    height: 12f + Rand.Range(0f, 5f));
            }
        }

        private static IntVec3 GetRandomMapEdgeCell(Map map)
        {
            int edge = Rand.RangeInclusive(0, 3);
            switch (edge)
            {
                case 0:
                    return new IntVec3(Rand.RangeInclusive(0, map.Size.x - 1), 0, 0);
                case 1:
                    return new IntVec3(map.Size.x - 1, 0, Rand.RangeInclusive(0, map.Size.z - 1));
                case 2:
                    return new IntVec3(Rand.RangeInclusive(0, map.Size.x - 1), 0, map.Size.z - 1);
                default:
                    return new IntVec3(0, 0, Rand.RangeInclusive(0, map.Size.z - 1));
            }
        }

        /// <summary>
        /// 发送拦截通知
        /// </summary>
        private void SendInterceptionLetter(Map map, int interceptedCount, IntVec3 dropCenter, int aircraftCount)
        {
            string label = "SRA_InterceptDropPod_LetterLabel".Translate();
            string text = "SRA_InterceptDropPod_LetterText".Translate(
                interceptedCount,
                aircraftCount
            );

            Find.LetterStack.ReceiveLetter(
                label,
                text,
                LetterDefOf.PositiveEvent,
                new TargetInfo(dropCenter, map));
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref interceptEnabled, "interceptEnabled", false);
        }
    }
}
