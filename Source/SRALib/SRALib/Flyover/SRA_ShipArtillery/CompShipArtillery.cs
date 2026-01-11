using RimWorld;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace SRA
{
    public class CompShipArtillery : ThingComp
    {
        public CompProperties_ShipArtillery Props => (CompProperties_ShipArtillery)props;

        // 状态变量
        private int ticksUntilNextAttack = 0;
        private int attackTicksRemaining = 0;
        private int warmupTicksRemaining = 0;
        private bool isAttacking = false;
        private bool isWarmingUp = false;
        private IntVec3 currentTarget;
        private Effecter warmupEffecter;
        private Effecter attackEffecter;
        
        // 目标跟踪
        private List<IntVec3> previousTargets = new List<IntVec3>();

        // 优化：缓存目标列表，避免每帧重新计算
        private List<LocalTargetInfo> cachedTargets = new List<LocalTargetInfo>();
        private List<float> cachedTargetWeights = new List<float>();
        private int lastTargetUpdateTick = -9999;
        private const int TARGET_UPDATE_INTERVAL = 60; // 每60 ticks更新一次目标列表

        // 优化：一轮炮击的目标缓存
        private IntVec3 currentVolleyCenter;
        private List<IntVec3> currentVolleyTargets = new List<IntVec3>();
        private int currentVolleyIndex = 0;

        // 目标类型权重配置
        private const float PAWN_WEIGHT = 5.0f;
        private const float OWNED_BUILDING_WEIGHT = 1.0f;
        private const float UNOWNED_BUILDING_WEIGHT = 0.01f;
        private const float WALL_WEIGHT = 0.001f; // 墙的权重极低
        private const float OTHER_WEIGHT = 1.0f;

        public override void Initialize(CompProperties props)
        {
            base.Initialize(props);
            ticksUntilNextAttack = Props.ticksBetweenAttacks;
            
            Log.Message($"Ship Artillery initialized: {Props.ticksBetweenAttacks} ticks between attacks, {Props.attackRadius} radius");
            Log.Message($"Faction Discrimination: {Props.useFactionDiscrimination}, Target Faction: {Props.targetFaction?.defName ?? "None"}, Micro Tracking: {Props.useMicroTracking}");
        }

        public override void CompTick()
        {
            base.CompTick();

            if (parent is not FlyOver flyOver || !flyOver.Spawned || flyOver.Map == null)
                return;

            // 优化：减少目标更新频率
            if (Props.useMicroTracking && Props.useFactionDiscrimination)
            {
                if (Find.TickManager.TicksGame - lastTargetUpdateTick > TARGET_UPDATE_INTERVAL)
                {
                    UpdateTargetCache(flyOver);
                    lastTargetUpdateTick = Find.TickManager.TicksGame;
                }
            }

            // 更新预热状态
            if (isWarmingUp)
            {
                UpdateWarmup(flyOver);
                return;
            }

            // 更新攻击状态
            if (isAttacking)
            {
                UpdateAttack(flyOver);
                return;
            }

            // 检查是否开始攻击
            if (ticksUntilNextAttack <= 0)
            {
                StartAttack(flyOver);
            }
            else
            {
                ticksUntilNextAttack--;
            }
        }

        // 优化：缓存目标列表
        private void UpdateTargetCache(FlyOver flyOver)
        {
            cachedTargets.Clear();
            cachedTargetWeights.Clear();
            
            Faction targetFaction = GetTargetFaction(flyOver);
            if (targetFaction == null) return;

            IntVec3 center = GetFlyOverPosition(flyOver);
            
            // 优化：使用更高效的目标搜索
            var potentialTargets = GenRadial.RadialDistinctThingsAround(center, flyOver.Map, Props.attackRadius, true)
                .Where(thing => IsValidMicroTrackingTarget(thing, targetFaction))
                .Distinct(); // 避免重复

            foreach (Thing thing in potentialTargets)
            {
                cachedTargets.Add(new LocalTargetInfo(thing));
                cachedTargetWeights.Add(GetTargetWeight(thing));
            }

            if (DebugSettings.godMode && cachedTargets.Count > 0)
            {
                Log.Message($"Target Cache Updated: Found {cachedTargets.Count} targets");
                var stats = GetTargetStatistics();
                Log.Message($"Target Statistics - Pawns: {stats.pawnCount}, Owned Buildings: {stats.ownedBuildingCount}, Unowned Buildings: {stats.unownedBuildingCount}, Walls: {stats.wallCount}, Others: {stats.otherCount}");
            }
        }

        // 优化：改进的目标有效性检查
        private bool IsValidMicroTrackingTarget(Thing thing, Faction targetFaction)
        {
            if (thing == null || thing.Destroyed) return false;

            // 修复1：无主建筑总是被排除
            if (thing is Building building && building.Faction == null)
                return false;

            // 修复2：isWall的建筑总是不考虑
            if (thing.def?.building?.isWall == true)
                return false;

            // 检查派系关系
            if (thing.Faction != null)
            {
                if (thing.Faction == targetFaction) return false;
                if (thing.Faction.RelationKindWith(targetFaction) == FactionRelationKind.Ally) return false;
            }

            // 检查保护范围
            if (Props.avoidPlayerAssets && IsNearPlayerAssets(thing.Position, thing.Map))
                return false;

            // 避免击中飞越物体本身
            if (Props.avoidHittingFlyOver && thing.Position.DistanceTo(parent.Position) < 10f)
                return false;

            return true;
        }

        // 优化：获取目标权重
        private float GetTargetWeight(Thing thing)
        {
            if (thing is Pawn)
                return PAWN_WEIGHT;
            else if (thing is Building building)
            {
                // 修复2：墙的权重极低
                if (building.def?.building?.isWall == true)
                    return WALL_WEIGHT;
                    
                if (building.Faction == null)
                    return UNOWNED_BUILDING_WEIGHT;
                else
                    return OWNED_BUILDING_WEIGHT;
            }
            else
                return OTHER_WEIGHT;
        }

        // 新增：获取目标统计信息
        private (int pawnCount, int ownedBuildingCount, int unownedBuildingCount, int wallCount, int otherCount) GetTargetStatistics()
        {
            int pawnCount = 0;
            int ownedBuildingCount = 0;
            int unownedBuildingCount = 0;
            int wallCount = 0;
            int otherCount = 0;

            foreach (var target in cachedTargets)
            {
                Thing thing = target.Thing;
                if (thing == null) continue;

                if (thing is Pawn)
                {
                    pawnCount++;
                }
                else if (thing is Building building)
                {
                    if (building.def?.building?.isWall == true)
                    {
                        wallCount++;
                    }
                    else if (building.Faction == null)
                    {
                        unownedBuildingCount++;
                    }
                    else
                    {
                        ownedBuildingCount++;
                    }
                }
                else
                {
                    otherCount++;
                }
            }

            return (pawnCount, ownedBuildingCount, unownedBuildingCount, wallCount, otherCount);
        }

        private Faction GetTargetFaction(FlyOver flyOver)
        {
            if (!Props.useFactionDiscrimination)
                return null;

            if (Props.targetFaction != null)
            {
                Faction faction = Find.FactionManager.FirstFactionOfDef(Props.targetFaction);
                if (faction != null) return faction;
            }

            return Faction.OfPlayer;
        }

        private void StartAttack(FlyOver flyOver)
        {
            if (!CanAttack(flyOver))
                return;

            // 选择目标区域
            currentTarget = SelectTarget(flyOver);
            
            if (!currentTarget.IsValid || !currentTarget.InBounds(flyOver.Map))
            {
                Log.Warning("Ship Artillery: Invalid target selected, skipping attack");
                ticksUntilNextAttack = Props.ticksBetweenAttacks;
                return;
            }

            Log.Message($"Ship Artillery starting attack on target area: {currentTarget} (attack radius: {Props.attackRadius})");

            // 修复3：在一轮炮击中，只进行一次目标选择
            currentVolleyCenter = currentTarget;
            currentVolleyTargets.Clear();
            currentVolleyIndex = 0;

            // 预热阶段
            isWarmingUp = true;
            warmupTicksRemaining = Props.warmupTicks;

            if (Props.warmupEffect != null)
            {
                warmupEffecter = Props.warmupEffect.Spawn();
                warmupEffecter.Trigger(new TargetInfo(currentTarget, flyOver.Map), new TargetInfo(currentTarget, flyOver.Map));
            }
        }

        private void UpdateWarmup(FlyOver flyOver)
        {
            warmupTicksRemaining--;

            if (warmupEffecter != null)
            {
                warmupEffecter.EffectTick(new TargetInfo(currentTarget, flyOver.Map), new TargetInfo(currentTarget, flyOver.Map));
            }

            if (Props.warmupFleck != null && Rand.MTBEventOccurs(0.1f, 1f, 1f))
            {
                FleckMaker.Static(currentTarget.ToVector3Shifted(), flyOver.Map, Props.warmupFleck);
            }

            if (warmupTicksRemaining <= 0)
            {
                StartFiring(flyOver);
            }
        }

        private void StartFiring(FlyOver flyOver)
        {
            isWarmingUp = false;
            isAttacking = true;
            attackTicksRemaining = Props.attackDurationTicks;

            warmupEffecter?.Cleanup();
            warmupEffecter = null;

            if (Props.attackEffect != null)
            {
                attackEffecter = Props.attackEffect.Spawn();
            }

            Log.Message($"Ship Artillery started firing at area {currentTarget}");
            
            // 发送攻击通知
            if (Props.sendAttackLetter)
            {
                SendAttackLetter(flyOver);
            }

            // 立即执行第一轮齐射
            ExecuteVolley(flyOver);
        }

        private void UpdateAttack(FlyOver flyOver)
        {
            attackTicksRemaining--;

            if (attackEffecter != null)
            {
                attackEffecter.EffectTick(new TargetInfo(currentTarget, flyOver.Map), new TargetInfo(currentTarget, flyOver.Map));
            }

            // 在攻击期间定期发射炮弹
            if (attackTicksRemaining % 60 == 0)
            {
                ExecuteVolley(flyOver);
            }

            if (Props.attackFleck != null && Rand.MTBEventOccurs(0.2f, 1f, 1f))
            {
                Vector3 randomOffset = new Vector3(Rand.Range(-3f, 3f), 0f, Rand.Range(-3f, 3f));
                FleckMaker.Static((currentTarget.ToVector3Shifted() + randomOffset), flyOver.Map, Props.attackFleck);
            }

            if (attackTicksRemaining <= 0)
            {
                EndAttack(flyOver);
            }
        }

        private void ExecuteVolley(FlyOver flyOver)
        {
            // 修复3：为这一轮炮击生成所有目标
            if (currentVolleyTargets.Count == 0)
            {
                GenerateVolleyTargets(flyOver);
            }

            for (int i = 0; i < Props.shellsPerVolley; i++)
            {
                if (currentVolleyIndex < currentVolleyTargets.Count)
                {
                    FireShell(flyOver, currentVolleyTargets[currentVolleyIndex]);
                    currentVolleyIndex++;
                }
                else
                {
                    // 如果目标用完了，重新生成（对于持续攻击）
                    GenerateVolleyTargets(flyOver);
                    if (currentVolleyTargets.Count > 0)
                    {
                        FireShell(flyOver, currentVolleyTargets[0]);
                        currentVolleyIndex = 1;
                    }
                }
            }
        }

        // 修复3：生成一轮炮击的所有目标
        private void GenerateVolleyTargets(FlyOver flyOver)
        {
            currentVolleyTargets.Clear();
            currentVolleyIndex = 0;

            for (int i = 0; i < Props.shellsPerVolley * 3; i++) // 生成足够的目标
            {
                IntVec3 target;
                if (Props.useMicroTracking && Props.useFactionDiscrimination && cachedTargets.Count > 0)
                {
                    target = SelectTargetFromCache(flyOver);
                }
                else
                {
                    target = SelectRandomTargetInRadius(currentVolleyCenter, flyOver.Map, Props.attackRadius);
                }
                
                if (target.IsValid && target.InBounds(flyOver.Map))
                {
                    currentVolleyTargets.Add(target);
                }
            }

            if (DebugSettings.godMode)
            {
                Log.Message($"Generated {currentVolleyTargets.Count} targets for volley around {currentVolleyCenter}");
            }
        }

        private void FireShell(FlyOver flyOver, IntVec3 shellTarget)
        {
            try
            {
                ThingDef shellDef = SelectShellDef();
                if (shellDef == null)
                {
                    Log.Error("Ship Artillery: No valid shell def found");
                    return;
                }

                SkyfallerMaker.SpawnSkyfaller(shellDef, shellTarget, flyOver.Map);

                float distanceFromCenter = shellTarget.DistanceTo(currentVolleyCenter);
                if (DebugSettings.godMode)
                {
                    Log.Message($"Ship Artillery fired shell at {shellTarget} (distance from center: {distanceFromCenter:F1})");
                }

                if (Props.attackSound != null)
                {
                    Props.attackSound.PlayOneShot(new TargetInfo(shellTarget, flyOver.Map));
                }
            }
            catch (System.Exception ex)
            {
                Log.Error($"Error firing ship artillery shell: {ex}");
            }
        }

        // 优化：从缓存中选择目标
        private IntVec3 SelectTargetFromCache(FlyOver flyOver)
        {
            if (cachedTargets.Count == 0)
            {
                Log.Warning("MicroTracking: No targets available, falling back to random target");
                return SelectRandomTargetInRadius(currentVolleyCenter, flyOver.Map, Props.attackRadius);
            }

            LocalTargetInfo selectedTarget = SelectTargetByWeight();
            IntVec3 targetCell = selectedTarget.Cell;

            // 在目标周围添加随机偏移，避免过于精确
            float offsetDistance = Rand.Range(0f, 2f);
            float angle = Rand.Range(0f, 360f);
            
            IntVec3 offsetTarget = targetCell;
            offsetTarget.x += Mathf.RoundToInt(Mathf.Cos(angle * Mathf.Deg2Rad) * offsetDistance);
            offsetTarget.z += Mathf.RoundToInt(Mathf.Sin(angle * Mathf.Deg2Rad) * offsetDistance);

            if (!offsetTarget.InBounds(flyOver.Map))
            {
                offsetTarget = targetCell;
            }

            if (DebugSettings.godMode)
            {
                Thing selectedThing = selectedTarget.Thing;
                string targetType = selectedThing is Pawn ? "Pawn" : 
                                  selectedThing is Building building ? 
                                  (building.Faction == null ? "Unowned Building" : "Owned Building") : "Other";
                
                Log.Message($"MicroTracking: Targeting {selectedThing?.Label ?? "unknown"} ({targetType}) at {targetCell}, final target: {offsetTarget}");
            }

            return offsetTarget;
        }

        // 基于权重的目标选择
        private LocalTargetInfo SelectTargetByWeight()
        {
            if (cachedTargets.Count == 0) 
                return LocalTargetInfo.Invalid;
                
            if (cachedTargets.Count == 1)
                return cachedTargets[0];

            float totalWeight = 0f;
            foreach (float weight in cachedTargetWeights)
            {
                totalWeight += weight;
            }

            float randomValue = Rand.Range(0f, totalWeight);
            float currentSum = 0f;

            for (int i = 0; i < cachedTargets.Count; i++)
            {
                currentSum += cachedTargetWeights[i];
                if (randomValue <= currentSum)
                {
                    return cachedTargets[i];
                }
            }

            return cachedTargets[cachedTargets.Count - 1];
        }

        private ThingDef SelectShellDef()
        {
            if (Props.skyfallerDefs != null && Props.skyfallerDefs.Count > 0)
            {
                if (Props.useDifferentShells)
                {
                    return Props.skyfallerDefs.RandomElement();
                }
                else
                {
                    return Props.skyfallerDefs[0];
                }
            }
            
            return Props.skyfallerDef;
        }

        private IntVec3 GetLaunchPosition(FlyOver flyOver)
        {
            // 从飞越物体的位置发射
            IntVec3 launchPos = flyOver.Position;
            
            // 确保发射位置在地图边界内
            if (!launchPos.InBounds(flyOver.Map))
            {
                launchPos = flyOver.Map.Center;
            }

            return launchPos;
        }

        private IntVec3 SelectTarget(FlyOver flyOver)
        {
            IntVec3 center = GetFlyOverPosition(flyOver) + Props.targetOffset;
            return FindRandomTargetInRadius(center, flyOver.Map, Props.attackRadius);
        }

        // 简化的目标选择 - 每次直接随机选择目标
        private IntVec3 SelectRandomTargetInRadius(IntVec3 center, Map map, float radius)
        {
            return FindRandomTargetInRadius(center, map, radius);
        }

        private IntVec3 GetFlyOverPosition(FlyOver flyOver)
        {
            // 优先使用 DrawPos，因为它反映实际视觉位置
            Vector3 drawPos = flyOver.DrawPos;
            IntVec3 result = new IntVec3(
                Mathf.RoundToInt(drawPos.x),
                0,
                Mathf.RoundToInt(drawPos.z)
            );

            // 如果 DrawPos 无效，回退到 Position
            if (!result.InBounds(flyOver.Map))
            {
                result = flyOver.Position;
            }

            return result;
        }

        // 目标查找逻辑 - 基于攻击半径
        private IntVec3 FindRandomTargetInRadius(IntVec3 center, Map map, float radius)
        {
            if (radius <= 0)
                return center;

            bool ignoreProtectionForThisTarget = Rand.Value < Props.ignoreProtectionChance;
            
            // 优化：减少尝试次数
            for (int i = 0; i < 15; i++)
            {
                // 在圆形区域内随机选择
                float angle = Rand.Range(0f, 360f);
                float distance = Rand.Range(0f, radius);
                
                IntVec3 potentialTarget = center;
                potentialTarget.x += Mathf.RoundToInt(Mathf.Cos(angle * Mathf.Deg2Rad) * distance);
                potentialTarget.z += Mathf.RoundToInt(Mathf.Sin(angle * Mathf.Deg2Rad) * distance);

                if (potentialTarget.InBounds(map) && IsValidTarget(potentialTarget, map, ignoreProtectionForThisTarget))
                {
                    // 避免重复攻击同一位置
                    if (!previousTargets.Contains(potentialTarget) || previousTargets.Count > 10)
                    {
                        if (previousTargets.Count > 10)
                            previousTargets.RemoveAt(0);
                        
                        previousTargets.Add(potentialTarget);
                        
                        if (DebugSettings.godMode)
                        {
                            float actualDistance = potentialTarget.DistanceTo(center);
                            Log.Message($"Found valid target at {potentialTarget} (distance from center: {actualDistance:F1})");
                            
                            if (ignoreProtectionForThisTarget)
                            {
                                Log.Warning($"Protection ignored for target selection! May target player assets.");
                            }
                        }
                        
                        return potentialTarget;
                    }
                }
            }

            // 回退：使用地图随机位置
            Log.Warning("Could not find valid target in radius, using fallback");
            CellRect mapRect = CellRect.WholeMap(map);
            for (int i = 0; i < 5; i++)
            {
                IntVec3 fallbackTarget = mapRect.RandomCell;
                if (IsValidTarget(fallbackTarget, map, ignoreProtectionForThisTarget))
                {
                    return fallbackTarget;
                }
            }

            // 最终回退：使用中心
            return center;
        }

        // 检查是否靠近玩家资产
        private bool IsNearPlayerAssets(IntVec3 cell, Map map)
        {
            if (!Props.avoidPlayerAssets)
                return false;

            // 如果启用了派系甄别，检查目标派系
            if (Props.useFactionDiscrimination)
            {
                Faction targetFaction = GetTargetFaction(parent as FlyOver);
                if (targetFaction != null)
                {
                    foreach (IntVec3 checkCell in GenRadial.RadialCellsAround(cell, Props.playerAssetAvoidanceRadius, true))
                    {
                        if (!checkCell.InBounds(map))
                            continue;

                        // 检查目标派系建筑
                        var building = checkCell.GetEdifice(map);
                        if (building != null && building.Faction == targetFaction)
                            return true;

                        // 检查目标派系殖民者
                        var pawn = map.thingGrid.ThingAt<Pawn>(checkCell);
                        if (pawn != null && pawn.Faction == targetFaction && pawn.RaceProps.Humanlike)
                            return true;

                        // 检查目标派系动物
                        var animal = map.thingGrid.ThingAt<Pawn>(checkCell);
                        if (animal != null && animal.Faction == targetFaction && animal.RaceProps.Animal)
                            return true;

                        // 检查目标派系物品
                        var items = checkCell.GetThingList(map);
                        foreach (var item in items)
                        {
                            if (item.Faction == targetFaction && item.def.category == ThingCategory.Item)
                                return true;
                        }
                    }
                    return false;
                }
            }

            // 默认行为：检查玩家资产
            foreach (IntVec3 checkCell in GenRadial.RadialCellsAround(cell, Props.playerAssetAvoidanceRadius, true))
            {
                if (!checkCell.InBounds(map))
                    continue;

                // 检查玩家建筑
                var building = checkCell.GetEdifice(map);
                if (building != null && building.Faction == Faction.OfPlayer)
                    return true;

                // 检查玩家殖民者
                var pawn = map.thingGrid.ThingAt<Pawn>(checkCell);
                if (pawn != null && pawn.Faction == Faction.OfPlayer && pawn.RaceProps.Humanlike)
                    return true;

                // 检查玩家动物
                var animal = map.thingGrid.ThingAt<Pawn>(checkCell);
                if (animal != null && animal.Faction == Faction.OfPlayer && animal.RaceProps.Animal)
                    return true;

                // 检查玩家物品
                var items = checkCell.GetThingList(map);
                foreach (var item in items)
                {
                    if (item.Faction == Faction.OfPlayer && item.def.category == ThingCategory.Item)
                        return true;
                }
            }

            return false;
        }

        private bool IsValidTarget(IntVec3 target, Map map, bool ignoreProtection = false)
        {
            if (!target.InBounds(map))
                return false;

            // 避开玩家资产（除非无视保护机制）
            if (Props.avoidPlayerAssets && !ignoreProtection && IsNearPlayerAssets(target, map))
            {
                return false;
            }

            // 避免击中飞越物体本身
            if (Props.avoidHittingFlyOver)
            {
                float distanceToFlyOver = target.DistanceTo(parent.Position);
                if (distanceToFlyOver < 10f) // 增加安全距离
                {
                    return false;
                }
            }

            return true;
        }

        private bool CanAttack(FlyOver flyOver)
        {
            if (flyOver.Map == null)
                return false;

            if (flyOver.hasCompleted)
                return false;

            return true;
        }

        private void EndAttack(FlyOver flyOver)
        {
            isAttacking = false;
            
            // 清理效果
            attackEffecter?.Cleanup();
            attackEffecter = null;

            // 清理缓存
            currentVolleyTargets.Clear();
            currentVolleyIndex = 0;

            // 重置计时器
            if (Props.continuousAttack && !flyOver.hasCompleted)
            {
                ticksUntilNextAttack = 0;
            }
            else
            {
                ticksUntilNextAttack = Props.ticksBetweenAttacks;
            }

            Log.Message($"Ship Artillery attack ended");
        }

        private void SendAttackLetter(FlyOver flyOver)
        {
            try
            {
                string label = Props.customLetterLabel ?? "ShipArtilleryAttack".Translate();
                string text = Props.customLetterText ?? "ShipArtilleryAttackDesc".Translate();

                Find.LetterStack.ReceiveLetter(
                    label,
                    text,
                    Props.letterDef,
                    new TargetInfo(currentTarget, flyOver.Map)
                );
            }
            catch (System.Exception ex)
            {
                Log.Error($"Error sending ship artillery letter: {ex}");
            }
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref ticksUntilNextAttack, "ticksUntilNextAttack", 0);
            Scribe_Values.Look(ref attackTicksRemaining, "attackTicksRemaining", 0);
            Scribe_Values.Look(ref warmupTicksRemaining, "warmupTicksRemaining", 0);
            Scribe_Values.Look(ref isAttacking, "isAttacking", false);
            Scribe_Values.Look(ref isWarmingUp, "isWarmingUp", false);
            Scribe_Values.Look(ref currentTarget, "currentTarget");
            Scribe_Values.Look(ref currentVolleyCenter, "currentVolleyCenter");
            Scribe_Values.Look(ref currentVolleyIndex, "currentVolleyIndex");
            Scribe_Values.Look(ref lastTargetUpdateTick, "lastTargetUpdateTick", -9999);
            Scribe_Collections.Look(ref previousTargets, "previousTargets", LookMode.Value);
            Scribe_Collections.Look(ref cachedTargets, "cachedTargets", LookMode.LocalTargetInfo);
            Scribe_Collections.Look(ref cachedTargetWeights, "cachedTargetWeights", LookMode.Value);
            Scribe_Collections.Look(ref currentVolleyTargets, "currentVolleyTargets", LookMode.Value);
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            if (DebugSettings.ShowDevGizmos && parent is FlyOver)
            {
                yield return new Command_Action
                {
                    defaultLabel = "Dev: Trigger Artillery Attack",
                    action = () => StartAttack(parent as FlyOver)
                };

                yield return new Command_Action
                {
                    defaultLabel = "Dev: Fire Single Shell",
                    action = () => 
                    {
                        if (parent is FlyOver flyOver)
                        {
                            IntVec3 target = SelectRandomTargetInRadius(GetFlyOverPosition(flyOver), flyOver.Map, Props.attackRadius);
                            FireShell(flyOver, target);
                        }
                    }
                };

                yield return new Command_Action
                {
                    defaultLabel = $"Dev: Status - Next: {ticksUntilNextAttack}, Attacking: {isAttacking}",
                    action = () => {}
                };

                yield return new Command_Action
                {
                    defaultLabel = $"Dev: Debug Position Info",
                    action = () => 
                    {
                        if (parent is FlyOver flyOver)
                        {
                            IntVec3 flyOverPos = GetFlyOverPosition(flyOver);
                            Log.Message($"FlyOver - DrawPos: {flyOver.DrawPos}, Position: {flyOver.Position}, Calculated: {flyOverPos}");
                            Log.Message($"Current Target: {currentTarget}, Distance: {flyOverPos.DistanceTo(currentTarget):F1}");
                            
                            // 显示派系甄别信息
                            Faction targetFaction = GetTargetFaction(flyOver);
                            Log.Message($"Faction Discrimination: {Props.useFactionDiscrimination}, Target Faction: {targetFaction?.def.defName ?? "None"}");
                            Log.Message($"Micro Tracking: {Props.useMicroTracking}, Targets Found: {cachedTargets.Count}");
                            
                            // 显示目标统计
                            var stats = GetTargetStatistics();
                            Log.Message($"Target Stats - Pawns: {stats.pawnCount}, Owned Buildings: {stats.ownedBuildingCount}, Unowned Buildings: {stats.unownedBuildingCount}, Walls: {stats.wallCount}, Others: {stats.otherCount}");
                            
                            // 显示炮击信息
                            Log.Message($"Volley - Center: {currentVolleyCenter}, Targets: {currentVolleyTargets.Count}, Index: {currentVolleyIndex}");
                        }
                    }
                };

                // 显示微追踪目标信息
                if (Props.useMicroTracking && Props.useFactionDiscrimination)
                {
                    yield return new Command_Action
                    {
                        defaultLabel = $"Dev: Show Cached Targets ({cachedTargets.Count})",
                        action = () => 
                        {
                            if (parent is FlyOver flyOver)
                            {
                                for (int i = 0; i < cachedTargets.Count; i++)
                                {
                                    var target = cachedTargets[i];
                                    float weight = cachedTargetWeights[i];
                                    Thing thing = target.Thing;
                                    string type = thing is Pawn ? "Pawn" : 
                                                thing is Building building ? 
                                                (building.Faction == null ? "Unowned Building" : "Owned Building") : "Other";
                                    
                                    Log.Message($"Cached Target: {thing?.Label ?? "Unknown"} ({type}) at {target.Cell}, Weight: {weight:F2}");
                                }
                            }
                        }
                    };
                }

                // 显示当前炮击目标
                if (currentVolleyTargets.Count > 0)
                {
                    yield return new Command_Action
                    {
                        defaultLabel = $"Dev: Show Volley Targets ({currentVolleyTargets.Count})",
                        action = () => 
                        {
                            for (int i = 0; i < currentVolleyTargets.Count; i++)
                            {
                                Log.Message($"Volley Target {i}: {currentVolleyTargets[i]} ({(i == currentVolleyIndex ? "NEXT" : "queued")})");
                            }
                        }
                    };
                }

                // 强制更新目标缓存
                yield return new Command_Action
                {
                    defaultLabel = "Dev: Force Update Target Cache",
                    action = () => 
                    {
                        if (parent is FlyOver flyOver)
                        {
                            UpdateTargetCache(flyOver);
                            Log.Message($"Force updated target cache: {cachedTargets.Count} targets found");
                        }
                    }
                };
            }
        }

        public void TriggerAttack()
        {
            if (parent is FlyOver flyOver)
            {
                StartAttack(flyOver);
            }
        }

        public void SetTarget(IntVec3 target)
        {
            currentTarget = target;
        }

        // 新增：获取当前状态信息
        public string GetStatusString()
        {
            if (parent is not FlyOver flyOver)
                return "Invalid parent";

            string status = isWarmingUp ? $"Warming up ({warmupTicksRemaining} ticks)" :
                         isAttacking ? $"Attacking ({attackTicksRemaining} ticks)" :
                         $"Next attack in {ticksUntilNextAttack} ticks";

            string targetInfo = currentTarget.IsValid ? $"Target: {currentTarget}" : "No target";
            string volleyInfo = currentVolleyTargets.Count > 0 ? $", Volley: {currentVolleyIndex}/{currentVolleyTargets.Count}" : "";

            return $"{status}, {targetInfo}{volleyInfo}";
        }
    }
}
