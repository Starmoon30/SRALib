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

        public override void Initialize(CompProperties props)
        {
            base.Initialize(props);
            
            ticksUntilNextAttack = Props.ticksBetweenAttacks;
            
            SRALog.Debug($"Ship Artillery initialized: {Props.ticksBetweenAttacks} ticks between attacks, {Props.attackRadius} radius");
        }

        public override void CompTick()
        {
            base.CompTick();

            if (parent is not FlyOver flyOver || !flyOver.Spawned || flyOver.Map == null)
                return;

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

        private void StartAttack(FlyOver flyOver)
        {
            if (!CanAttack(flyOver))
                return;

            // 选择目标区域
            currentTarget = SelectTarget(flyOver);
            
            if (!currentTarget.IsValid || !currentTarget.InBounds(flyOver.Map))
            {
                SRALog.Debug("Ship Artillery: Invalid target selected, skipping attack");
                ticksUntilNextAttack = Props.ticksBetweenAttacks;
                return;
            }

            SRALog.Debug($"Ship Artillery starting attack on target area: {currentTarget} (attack radius: {Props.attackRadius})");

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

            SRALog.Debug($"Ship Artillery started firing at area {currentTarget}");
            
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
                    SRALog.Debug("Ship Artillery: No valid shell def found");
                    return;
                }

                // 直接选择随机目标
                IntVec3 shellTarget = SelectRandomTarget(flyOver);

                // 关键修复：使用 SkyfallerMaker 创建并立即生成 Skyfaller
                SkyfallerMaker.SpawnSkyfaller(shellDef, shellTarget, flyOver.Map);

                float distanceFromCenter = shellTarget.DistanceTo(currentTarget);
                SRALog.Debug($"Ship Artillery fired shell at {shellTarget} (distance from center: {distanceFromCenter:F1})");

                // 播放音效
                if (Props.attackSound != null)
                {
                    Props.attackSound.PlayOneShot(new TargetInfo(shellTarget, flyOver.Map));
                }
            }
            catch (System.Exception ex)
            {
                SRALog.Debug($"Error firing ship artillery shell: {ex}");
            }
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

            SRALog.Debug($"FlyOver position: {flyOverPos}, Center for targeting: {center}");

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
            SRALog.Debug($"Finding target around {center} with radius {radius}");

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
                        SRALog.Debug($"Found valid target at {potentialTarget} (distance from center: {actualDistance:F1})");
                        
                        if (ignoreProtectionForThisTarget)
                        {
                            SRALog.Debug($"Protection ignored for target selection! May target player assets.");
                        }
                        
                        return potentialTarget;
                    }
                }
            }

            // 回退：使用地图随机位置
            SRALog.Debug("Could not find valid target in radius, using fallback");
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

            SRALog.Debug($"Ship Artillery attack ended");
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
                SRALog.Debug($"Error sending ship artillery letter: {ex}");
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
                            SRALog.Debug($"FlyOver - DrawPos: {flyOver.DrawPos}, Position: {flyOver.Position}, Calculated: {flyOverPos}");
                            SRALog.Debug($"Current Target: {currentTarget}, Distance: {flyOverPos.DistanceTo(currentTarget):F1}");
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
    }
}
