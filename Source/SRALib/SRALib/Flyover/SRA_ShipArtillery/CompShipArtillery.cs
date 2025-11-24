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

        // 新增：微追踪目标列表
        private List<LocalTargetInfo> microTrackingTargets = new List<LocalTargetInfo>();
        private List<float> microTrackingWeights = new List<float>(); // 新增：权重列表

        // 新增：目标类型权重配置
        private const float PAWN_WEIGHT = 5.0f;        // Pawn权重：5倍
        private const float OWNED_BUILDING_WEIGHT = 1.0f; // 有主建筑权重：1倍
        private const float UNOWNED_BUILDING_WEIGHT = 0.01f; // 无主建筑权重：0.01倍
        private const float OTHER_WEIGHT = 1.0f;       // 其他目标权重：1倍

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

            // 更新微追踪目标列表（如果需要）
            if (Props.useMicroTracking && Props.useFactionDiscrimination)
            {
                UpdateMicroTrackingTargets(flyOver);
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

        // 新增：更新微追踪目标列表
        private void UpdateMicroTrackingTargets(FlyOver flyOver)
        {
            microTrackingTargets.Clear();
            microTrackingWeights.Clear();
            
            Faction targetFaction = GetTargetFaction(flyOver);
            if (targetFaction == null) return;

            // 获取飞越物体当前位置
            IntVec3 center = GetFlyOverPosition(flyOver);
            
            // 搜索范围内的所有潜在目标
            foreach (IntVec3 cell in GenRadial.RadialCellsAround(center, Props.attackRadius, true))
            {
                if (!cell.InBounds(flyOver.Map)) continue;

                // 检查建筑
                Building building = cell.GetEdifice(flyOver.Map);
                if (building != null && IsValidMicroTrackingTarget(building, targetFaction))
                {
                    microTrackingTargets.Add(new LocalTargetInfo(building));
                    float weight = GetTargetWeight(building);
                    microTrackingWeights.Add(weight);
                }

                // 检查生物
                List<Thing> thingList = cell.GetThingList(flyOver.Map);
                foreach (Thing thing in thingList)
                {
                    if (thing is Pawn pawn && IsValidMicroTrackingTarget(pawn, targetFaction))
                    {
                        microTrackingTargets.Add(new LocalTargetInfo(pawn));
                        float weight = GetTargetWeight(pawn);
                        microTrackingWeights.Add(weight);
                    }
                }
            }

            // 移除重复目标（基于位置）
            for (int i = microTrackingTargets.Count - 1; i >= 0; i--)
            {
                for (int j = 0; j < i; j++)
                {
                    if (microTrackingTargets[i].Cell == microTrackingTargets[j].Cell)
                    {
                        microTrackingTargets.RemoveAt(i);
                        microTrackingWeights.RemoveAt(i);
                        break;
                    }
                }
            }

            if (DebugSettings.godMode)
            {
                Log.Message($"MicroTracking: Found {microTrackingTargets.Count} targets for faction {targetFaction.def.defName}");
                // 输出目标统计信息
                var targetStats = GetTargetStatistics();
                Log.Message($"Target Statistics - Pawns: {targetStats.pawnCount}, Owned Buildings: {targetStats.ownedBuildingCount}, Unowned Buildings: {targetStats.unownedBuildingCount}, Others: {targetStats.otherCount}");
            }
        }

        // 新增：获取目标权重
        private float GetTargetWeight(Thing thing)
        {
            if (thing is Pawn)
            {
                return PAWN_WEIGHT;
            }
            else if (thing is Building building)
            {
                if (building.Faction == null)
                {
                    return UNOWNED_BUILDING_WEIGHT;
                }
                else
                {
                    return OWNED_BUILDING_WEIGHT;
                }
            }
            else
            {
                return OTHER_WEIGHT;
            }
        }

        // 新增：获取目标统计信息
        private (int pawnCount, int ownedBuildingCount, int unownedBuildingCount, int otherCount) GetTargetStatistics()
        {
            int pawnCount = 0;
            int ownedBuildingCount = 0;
            int unownedBuildingCount = 0;
            int otherCount = 0;

            for (int i = 0; i < microTrackingTargets.Count; i++)
            {
                Thing thing = microTrackingTargets[i].Thing;
                if (thing == null) continue;

                if (thing is Pawn)
                {
                    pawnCount++;
                }
                else if (thing is Building building)
                {
                    if (building.Faction == null)
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

            return (pawnCount, ownedBuildingCount, unownedBuildingCount, otherCount);
        }

        // 新增：检查是否为有效的微追踪目标
        private bool IsValidMicroTrackingTarget(Thing thing, Faction targetFaction)
        {
            if (thing == null || thing.Destroyed) return false;

            // 检查派系关系：目标派系的友军不应该被攻击
            if (thing.Faction != null)
            {
                if (thing.Faction == targetFaction) return false;
                if (thing.Faction.RelationKindWith(targetFaction) == FactionRelationKind.Ally) return false;
            }

            // 检查是否在保护范围内
            if (Props.avoidPlayerAssets && IsNearPlayerAssets(thing.Position, thing.Map))
            {
                return false;
            }

            // 避免击中飞越物体本身
            if (Props.avoidHittingFlyOver && thing.Position.DistanceTo(parent.Position) < 10f)
            {
                return false;
            }

            return true;
        }

        // 新增：获取目标派系
        private Faction GetTargetFaction(FlyOver flyOver)
        {
            if (!Props.useFactionDiscrimination)
                return null;

            // 如果指定了目标派系，使用指定的派系
            if (Props.targetFaction != null)
            {
                Faction faction = Find.FactionManager.FirstFactionOfDef(Props.targetFaction);
                if (faction != null) return faction;
            }

            // 否则使用玩家当前派系
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

            // 开始预热
            isWarmingUp = true;
            warmupTicksRemaining = Props.warmupTicks;

            // 启动预热效果
            if (Props.warmupEffect != null)
            {
                warmupEffecter = Props.warmupEffect.Spawn();
                warmupEffecter.Trigger(new TargetInfo(currentTarget, flyOver.Map), new TargetInfo(currentTarget, flyOver.Map));
            }
        }

        private void UpdateWarmup(FlyOver flyOver)
        {
            warmupTicksRemaining--;

            // 维持预热效果
            if (warmupEffecter != null)
            {
                warmupEffecter.EffectTick(new TargetInfo(currentTarget, flyOver.Map), new TargetInfo(currentTarget, flyOver.Map));
            }

            // 生成预热粒子
            if (Props.warmupFleck != null && Rand.MTBEventOccurs(0.1f, 1f, 1f))
            {
                FleckMaker.Static(currentTarget.ToVector3Shifted(), flyOver.Map, Props.warmupFleck);
            }

            // 预热完成，开始攻击
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

            // 清理预热效果
            warmupEffecter?.Cleanup();
            warmupEffecter = null;

            // 启动攻击效果
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

            // 维持攻击效果
            if (attackEffecter != null)
            {
                attackEffecter.EffectTick(new TargetInfo(currentTarget, flyOver.Map), new TargetInfo(currentTarget, flyOver.Map));
            }

            // 在攻击期间定期发射炮弹
            if (attackTicksRemaining % 60 == 0) // 每秒发射一次
            {
                ExecuteVolley(flyOver);
            }

            // 生成攻击粒子
            if (Props.attackFleck != null && Rand.MTBEventOccurs(0.2f, 1f, 1f))
            {
                Vector3 randomOffset = new Vector3(Rand.Range(-3f, 3f), 0f, Rand.Range(-3f, 3f));
                FleckMaker.Static((currentTarget.ToVector3Shifted() + randomOffset), flyOver.Map, Props.attackFleck);
            }

            // 攻击结束
            if (attackTicksRemaining <= 0)
            {
                EndAttack(flyOver);
            }
        }

        private void ExecuteVolley(FlyOver flyOver)
        {
            for (int i = 0; i < Props.shellsPerVolley; i++)
            {
                FireShell(flyOver);
            }
        }

        private void FireShell(FlyOver flyOver)
        {
            try
            {
                // 选择炮弹类型
                ThingDef shellDef = SelectShellDef();
                if (shellDef == null)
                {
                    Log.Error("Ship Artillery: No valid shell def found");
                    return;
                }

                // 选择目标
                IntVec3 shellTarget;
                if (Props.useMicroTracking && Props.useFactionDiscrimination && microTrackingTargets.Count > 0)
                {
                    shellTarget = SelectMicroTrackingTarget(flyOver);
                }
                else
                {
                    shellTarget = SelectRandomTarget(flyOver);
                }

                // 关键修复：使用 SkyfallerMaker 创建并立即生成 Skyfaller
                SkyfallerMaker.SpawnSkyfaller(shellDef, shellTarget, flyOver.Map);

                float distanceFromCenter = shellTarget.DistanceTo(currentTarget);
                Log.Message($"Ship Artillery fired shell at {shellTarget} (distance from center: {distanceFromCenter:F1})");

                // 播放音效
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

        // 修改：微追踪目标选择 - 现在使用权重系统
        private IntVec3 SelectMicroTrackingTarget(FlyOver flyOver)
        {
            if (microTrackingTargets.Count == 0)
            {
                Log.Warning("MicroTracking: No targets available, falling back to random target");
                return SelectRandomTarget(flyOver);
            }

            // 使用权重系统选择目标
            LocalTargetInfo selectedTarget = SelectTargetByWeight();
            IntVec3 targetCell = selectedTarget.Cell;

            // 在目标周围添加随机偏移，避免过于精确
            float offsetDistance = Rand.Range(0f, 2f);
            float angle = Rand.Range(0f, 360f);
            
            IntVec3 offsetTarget = targetCell;
            offsetTarget.x += Mathf.RoundToInt(Mathf.Cos(angle * Mathf.Deg2Rad) * offsetDistance);
            offsetTarget.z += Mathf.RoundToInt(Mathf.Sin(angle * Mathf.Deg2Rad) * offsetDistance);

            // 确保目标在地图内
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

        // 新增：基于权重的目标选择
        private LocalTargetInfo SelectTargetByWeight()
        {
            if (microTrackingTargets.Count == 0) 
                return LocalTargetInfo.Invalid;
                
            if (microTrackingTargets.Count == 1)
                return microTrackingTargets[0];

            // 计算总权重
            float totalWeight = 0f;
            foreach (float weight in microTrackingWeights)
            {
                totalWeight += weight;
            }

            // 随机选择
            float randomValue = Rand.Range(0f, totalWeight);
            float currentSum = 0f;

            for (int i = 0; i < microTrackingTargets.Count; i++)
            {
                currentSum += microTrackingWeights[i];
                if (randomValue <= currentSum)
                {
                    return microTrackingTargets[i];
                }
            }

            // 回退到最后一个目标
            return microTrackingTargets[microTrackingTargets.Count - 1];
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

        // 简化的目标选择 - 每次直接随机选择目标
        private IntVec3 SelectRandomTarget(FlyOver flyOver)
        {
            IntVec3 center = GetFlyOverPosition(flyOver) + Props.targetOffset;
            return FindRandomTargetInRadius(center, flyOver.Map, Props.attackRadius);
        }

        private IntVec3 SelectTarget(FlyOver flyOver)
        {
            // 获取飞越物体当前位置作为基础中心
            IntVec3 flyOverPos = GetFlyOverPosition(flyOver);
            IntVec3 center = flyOverPos + Props.targetOffset;

            Log.Message($"FlyOver position: {flyOverPos}, Center for targeting: {center}");

            // 在攻击半径内选择随机目标
            return FindRandomTargetInRadius(center, flyOver.Map, Props.attackRadius);
        }

        // 改进的飞越物体位置获取
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
            Log.Message($"Finding target around {center} with radius {radius}");

            // 如果半径为0，直接返回中心
            if (radius <= 0)
                return center;

            bool ignoreProtectionForThisTarget = Rand.Value < Props.ignoreProtectionChance;
            
            for (int i = 0; i < 30; i++)
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
                        
                        float actualDistance = potentialTarget.DistanceTo(center);
                        Log.Message($"Found valid target at {potentialTarget} (distance from center: {actualDistance:F1})");
                        
                        if (ignoreProtectionForThisTarget)
                        {
                            Log.Warning($"Protection ignored for target selection! May target player assets.");
                        }
                        
                        return potentialTarget;
                    }
                }
            }

            // 回退：使用地图随机位置
            Log.Warning("Could not find valid target in radius, using fallback");
            CellRect mapRect = CellRect.WholeMap(map);
            for (int i = 0; i < 10; i++)
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
            Scribe_Collections.Look(ref previousTargets, "previousTargets", LookMode.Value);
            Scribe_Collections.Look(ref microTrackingTargets, "microTrackingTargets", LookMode.LocalTargetInfo);
            Scribe_Collections.Look(ref microTrackingWeights, "microTrackingWeights", LookMode.Value);
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
                    action = () => FireShell(parent as FlyOver)
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
                            Log.Message($"Micro Tracking: {Props.useMicroTracking}, Targets Found: {microTrackingTargets.Count}");
                            
                            // 显示目标统计
                            var stats = GetTargetStatistics();
                            Log.Message($"Target Stats - Pawns: {stats.pawnCount}, Owned Buildings: {stats.ownedBuildingCount}, Unowned Buildings: {stats.unownedBuildingCount}, Others: {stats.otherCount}");
                        }
                    }
                };

                // 显示微追踪目标信息
                if (Props.useMicroTracking && Props.useFactionDiscrimination)
                {
                    yield return new Command_Action
                    {
                        defaultLabel = $"Dev: Show Micro Targets ({microTrackingTargets.Count})",
                        action = () => 
                        {
                            if (parent is FlyOver flyOver)
                            {
                                for (int i = 0; i < microTrackingTargets.Count; i++)
                                {
                                    var target = microTrackingTargets[i];
                                    float weight = microTrackingWeights[i];
                                    Thing thing = target.Thing;
                                    string type = thing is Pawn ? "Pawn" : 
                                                thing is Building building ? 
                                                (building.Faction == null ? "Unowned Building" : "Owned Building") : "Other";
                                    
                                    Log.Message($"Micro Target: {thing?.Label ?? "Unknown"} ({type}) at {target.Cell}, Weight: {weight:F2}");
                                }
                            }
                        }
                    };
                }
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
    }
}
