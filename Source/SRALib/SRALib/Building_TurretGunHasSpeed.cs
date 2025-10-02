using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace SRA
{
    public class ModExt_HasSpeedTurret : DefModExtension
    {
        public float speed = 1f;
    }
    /// <summary>
    /// 非瞬时瞄准的炮塔建筑类
    /// 继承自原版炮塔，增加了平滑旋转瞄准功能
    /// </summary>
    public class Building_TurretGunHasSpeed : Building_TurretGun
    {
        // 当前炮塔角度
        public float curAngle;

        /// <summary>
        /// 旋转速度属性
        /// 从Mod扩展配置中获取旋转速度，如果没有配置则使用默认值1f
        /// </summary>
        public float rotateSpeed
        {
            get
            {
                ModExt_HasSpeedTurret ext = this.ext;
                return ext.speed;
            }
        }
        /// <summary>
        /// Mod扩展配置属性
        /// 获取炮塔定义的Mod扩展配置
        /// </summary>
        public ModExt_HasSpeedTurret ext
        {
            get
            {
                return this.def.GetModExtension<ModExt_HasSpeedTurret>();
            }
        }

        /// <summary>
        /// 炮塔方向向量
        /// 根据当前角度计算炮塔的朝向向量
        /// </summary>
        public Vector3 turretOrientation
        {
            get
            {
                return Vector3.forward.RotatedBy(this.curAngle);
            }
        }

        /// <summary>
        /// 目标角度差
        /// 计算当前炮塔方向与目标方向之间的角度差
        /// </summary>
        public float deltaAngle
        {
            get
            {
                return (this.currentTargetInt == null) ? 0f : Vector3.SignedAngle(this.turretOrientation, (this.currentTargetInt.CenterVector3 - this.DrawPos).Yto0(), Vector3.up);
            }
        }

        /// <summary>
        /// 数据保存和加载
        /// 重写ExposeData以保存和加载当前角度数据
        /// </summary>
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look<float>(ref this.curAngle, "curAngle", 0f, false);
        }

        /// <summary>
        /// 检查是否可以攻击目标（LocalTargetInfo重载）
        /// </summary>
        /// <param name="t">目标信息</param>
        /// <returns>是否可以攻击</returns>
        private bool CanAttackTarget(LocalTargetInfo t)
        {
            return this.CanAttackTarget(t.CenterVector3);
        }

        /// <summary>
        /// 检查是否可以攻击目标（Thing重载）
        /// </summary>
        /// <param name="t">目标物体</param>
        /// <returns>是否可以攻击</returns>
        private bool CanAttackTarget(Thing t)
        {
            return this.CanAttackTarget(t.DrawPos);
        }

        /// <summary>
        /// 检查是否可以攻击目标（Vector3重载）
        /// 判断目标是否在当前炮塔的瞄准范围内
        /// </summary>
        /// <param name="t">目标位置</param>
        /// <returns>是否可以攻击</returns>
        private bool CanAttackTarget(Vector3 t)
        {
            return Vector3.Angle(this.turretOrientation, (t - this.DrawPos).Yto0()) <= this.rotateSpeed;
        }

        /// <summary>
        /// 每帧更新
        /// 处理炮塔的旋转逻辑
        /// </summary>
        protected override void Tick()
        {
            // 如果炮塔处于激活状态且有目标
            if (base.Active && this.currentTargetInt != null)
            {
                // 如果准备开火但角度差过大，延迟开火
                if (this.burstWarmupTicksLeft == 1 && Mathf.Abs(this.deltaAngle) > this.rotateSpeed)
                {
                    this.burstWarmupTicksLeft++;
                }

                // 根据角度差更新当前角度
                this.curAngle += ((Mathf.Abs(this.deltaAngle) - this.rotateSpeed > 0f) ?
                    (Mathf.Sign(this.deltaAngle) * this.rotateSpeed) : this.deltaAngle);
            }

            base.Tick();
            // 规范化角度值到0-360度范围
            this.curAngle = this.Trim(this.curAngle);
        }

        /// <summary>
        /// 角度规范化
        /// 将角度值限制在0-360度范围内
        /// </summary>
        /// <param name="angle">输入角度</param>
        /// <returns>规范化后的角度</returns>
        protected float Trim(float angle)
        {
            if (angle > 360f)
            {
                angle -= 360f;
            }
            if (angle < 0f)
            {
                angle += 360f;
            }
            return angle;
        }

        /// <summary>
        /// 绘制炮塔
        /// 设置炮塔顶部的旋转角度
        /// </summary>
        /// <param name="drawLoc">绘制位置</param>
        /// <param name="flip">是否翻转</param>
        protected override void DrawAt(Vector3 drawLoc, bool flip = false)
        {
            this.top.CurRotation = this.curAngle;
            base.DrawAt(drawLoc, flip);
        }

        /// <summary>
        /// 获取目标搜索器
        /// 如果有人操作则返回操作者，否则返回炮塔自身
        /// </summary>
        /// <returns>目标搜索器</returns>
        private IAttackTargetSearcher TargSearcher()
        {
            if (this.mannableComp != null && this.mannableComp.MannedNow)
            {
                return this.mannableComp.ManningPawn;
            }
            else
            {
                return this;
            }
        }

        /// <summary>
        /// 检查目标是否有效
        /// 过滤不适合攻击的目标
        /// </summary>
        /// <param name="t">目标物体</param>
        /// <returns>目标是否有效</returns>
        private bool IsValidTarget(Thing t)
        {
            Pawn pawn = t as Pawn;
            if (pawn != null)
            {
                // 玩家派系的炮塔不攻击囚犯
                if (base.Faction == Faction.OfPlayer && pawn.IsPrisoner)
                {
                    return false;
                }

                // 检查弹道是否会被厚屋顶阻挡
                if (this.AttackVerb.ProjectileFliesOverhead())
                {
                    RoofDef roofDef = base.Map.roofGrid.RoofAt(t.Position);
                    if (roofDef != null && roofDef.isThickRoof)
                    {
                        return false;
                    }
                }

                // 无人操作的机械炮塔不攻击友好机械单位
                if (this.mannableComp == null)
                {
                    return !GenAI.MachinesLike(base.Faction, pawn);
                }

                // 有人操作的炮塔不攻击玩家动物
                if (pawn.RaceProps.Animal && pawn.Faction == Faction.OfPlayer)
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// 尝试寻找新目标
        /// 重写目标选择逻辑，支持角度限制
        /// </summary>
        /// <returns>新的目标信息</returns>
        public override LocalTargetInfo TryFindNewTarget()
        {
            IAttackTargetSearcher attackTargetSearcher = this.TargSearcher();
            Faction faction = attackTargetSearcher.Thing.Faction;
            float range = this.AttackVerb.verbProps.range;

            Building t;
            // 50%概率优先攻击殖民者建筑（如果敌对且使用抛射武器）
            if (Rand.Value < 0.5f && this.AttackVerb.ProjectileFliesOverhead() &&
                faction.HostileTo(Faction.OfPlayer) &&
                base.Map.listerBuildings.allBuildingsColonist.Where(delegate (Building x)
                {
                    float minRange = this.AttackVerb.verbProps.EffectiveMinRange(x, this);
                    float distanceSquared = (float)x.Position.DistanceToSquared(this.Position);
                    return distanceSquared > minRange * minRange && distanceSquared < range * range;
                }).TryRandomElement(out t))
            {
                return t;
            }
            else
            {
                // 设置目标扫描标志
                TargetScanFlags targetScanFlags = TargetScanFlags.NeedThreat | TargetScanFlags.NeedAutoTargetable;

                if (!this.AttackVerb.ProjectileFliesOverhead())
                {
                    targetScanFlags |= TargetScanFlags.NeedLOSToAll;
                    targetScanFlags |= TargetScanFlags.LOSBlockableByGas;
                }

                if (this.AttackVerb.IsIncendiary_Ranged())
                {
                    targetScanFlags |= TargetScanFlags.NeedNonBurning;
                }

                if (this.def.building.IsMortar)
                {
                    targetScanFlags |= TargetScanFlags.NeedNotUnderThickRoof;
                }

                // 使用角度感知的目标查找器
                return (Thing)AttackTargetFinderAngle.BestShootTargetFromCurrentPosition(
                    attackTargetSearcher, targetScanFlags, this.turretOrientation,
                    new Predicate<Thing>(this.IsValidTarget), 0f, 9999f);
            }
        }
    }
    /// <summary>
    /// 攻击目标查找器（角度优化版）
    /// 提供基于角度优化的攻击目标选择功能
    /// </summary>
    public static class AttackTargetFinderAngle
    {
        // 友军误伤评分偏移量常量
        private const float FriendlyFireScoreOffsetPerHumanlikeOrMechanoid = 18f;  // 每人类或机械族的友军误伤分数偏移
        private const float FriendlyFireScoreOffsetPerAnimal = 7f;                 // 每动物的友军误伤分数偏移
        private const float FriendlyFireScoreOffsetPerNonPawn = 10f;               // 每非pawn单位的友军误伤分数偏移
        private const float FriendlyFireScoreOffsetSelf = 40f;                     // 对自己造成误伤的分数偏移
        // 临时目标列表，用于缓存计算过程中的目标
        private static List<IAttackTarget> tmpTargets = new List<IAttackTarget>(128);

        // 可用射击目标及其分数的列表
        private static List<Pair<IAttackTarget, float>> availableShootingTargets = new List<Pair<IAttackTarget, float>>();

        // 临时存储目标分数的列表
        private static List<float> tmpTargetScores = new List<float>();

        // 临时存储是否可以向目标射击的列表
        private static List<bool> tmpCanShootAtTarget = new List<bool>();
        /// <summary>
        /// 从当前位置寻找最佳射击目标
        /// </summary>
        /// <param name="searcher">搜索者（攻击目标搜索器）</param>
        /// <param name="flags">目标扫描标志</param>
        /// <param name="angle">射击角度</param>
        /// <param name="validator">目标验证器（可选）</param>
        /// <param name="minDistance">最小距离（默认0）</param>
        /// <param name="maxDistance">最大距离（默认9999）</param>
        /// <returns>最佳攻击目标，如果没有则返回null</returns>
        public static IAttackTarget BestShootTargetFromCurrentPosition(
            IAttackTargetSearcher searcher,
            TargetScanFlags flags,
            Vector3 angle,
            Predicate<Thing> validator = null,
            float minDistance = 0f,
            float maxDistance = 9999f)
        {
            // 获取当前有效动词（武器）
            Verb currentEffectiveVerb = searcher.CurrentEffectiveVerb;

            // 检查是否有攻击动词
            if (currentEffectiveVerb == null)
            {
                Log.Error("BestShootTargetFromCurrentPosition with " + searcher.ToStringSafe<IAttackTargetSearcher>() + " who has no attack verb.");
                return null;
            }

            // 计算实际的最小和最大距离，考虑武器的属性
            float actualMinDistance = Mathf.Max(minDistance, currentEffectiveVerb.verbProps.minRange);
            float actualMaxDistance = Mathf.Min(maxDistance, currentEffectiveVerb.verbProps.range);

            // 调用主要的目标查找方法
            return BestAttackTarget(
                searcher,
                flags,
                angle,
                validator,
                actualMinDistance,
                actualMaxDistance,
                default(IntVec3),
                float.MaxValue,
                false);
        }

        /// <summary>
        /// 查找最佳攻击目标（核心方法）
        /// </summary>
        /// <param name="searcher">搜索者</param>
        /// <param name="flags">目标扫描标志</param>
        /// <param name="angle">射击角度</param>
        /// <param name="validator">目标验证器</param>
        /// <param name="minDist">最小距离</param>
        /// <param name="maxDist">最大距离</param>
        /// <param name="locus">搜索中心点</param>
        /// <param name="maxTravelRadiusFromLocus">从中心点的最大移动半径</param>
        /// <param name="canTakeTargetsCloserThanEffectiveMinRange">是否可以攻击比有效最小距离更近的目标</param>
        /// <returns>最佳攻击目标</returns>
        public static IAttackTarget BestAttackTarget(
            IAttackTargetSearcher searcher,
            TargetScanFlags flags,
            Vector3 angle,
            Predicate<Thing> validator = null,
            float minDist = 0f,
            float maxDist = 9999f,
            IntVec3 locus = default(IntVec3),
            float maxTravelRadiusFromLocus = float.MaxValue,
            bool canTakeTargetsCloserThanEffectiveMinRange = true)
        {
            // 获取搜索者的Thing对象和当前有效动词
            Thing searcherThing = searcher.Thing;
            Verb verb = searcher.CurrentEffectiveVerb;

            // 验证攻击动词是否存在
            if (verb == null)
            {
                Log.Error("BestAttackTarget with " + searcher.ToStringSafe<IAttackTargetSearcher>() + " who has no attack verb.");
                return null;
            }

            // 初始化各种标志和参数
            bool onlyTargetMachines = verb.IsEMP();  // 是否只瞄准机械单位（EMP武器）
            float minDistSquared = minDist * minDist;  // 最小距离的平方（用于距离比较优化）

            // 计算从搜索中心点的最大距离平方
            float maxLocusDist = maxTravelRadiusFromLocus + verb.verbProps.range;
            float maxLocusDistSquared = maxLocusDist * maxLocusDist;

            // LOS（视线）验证器，用于检查是否被烟雾阻挡
            Predicate<IntVec3> losValidator = null;
            if ((flags & TargetScanFlags.LOSBlockableByGas) > TargetScanFlags.None)
            {
                losValidator = (IntVec3 vec3) => !vec3.AnyGas(searcherThing.Map, GasType.BlindSmoke);
            }

            // 获取潜在目标列表
            tmpTargets.Clear();
            tmpTargets.AddRange(searcherThing.Map.attackTargetsCache.GetPotentialTargetsFor(searcher));

            // 移除非战斗人员（根据标志）
            tmpTargets.RemoveAll(t => ShouldIgnoreNoncombatant(searcherThing, t, flags));

            // 内部验证器函数
            bool InnerValidator(IAttackTarget target, Predicate<IntVec3> losValidator)
            {
                Thing targetThing = target.Thing;
                if (target == searcher)
                {
                    return false;
                }

                if (minDistSquared > 0f && (float)(searcherThing.Position - targetThing.Position).LengthHorizontalSquared < minDistSquared)
                {
                    return false;
                }

                if (!canTakeTargetsCloserThanEffectiveMinRange)
                {
                    float num3 = verb.verbProps.EffectiveMinRange(targetThing, searcherThing);
                    if (num3 > 0f && (float)(searcherThing.Position - targetThing.Position).LengthHorizontalSquared < num3 * num3)
                    {
                        return false;
                    }
                }

                if (maxTravelRadiusFromLocus < 9999f && (float)(targetThing.Position - locus).LengthHorizontalSquared > maxLocusDistSquared)
                {
                    return false;
                }

                if (!searcherThing.HostileTo(targetThing))
                {
                    return false;
                }

                if (validator != null && !validator(targetThing))
                {
                    return false;
                }


                if ((flags & TargetScanFlags.NeedNotUnderThickRoof) != 0)
                {
                    RoofDef roof = targetThing.Position.GetRoof(targetThing.Map);
                    if (roof != null && roof.isThickRoof)
                    {
                        return false;
                    }
                }

                if ((flags & TargetScanFlags.NeedLOSToAll) != 0)
                {
                    if (losValidator != null && (!losValidator(searcherThing.Position) || !losValidator(targetThing.Position)))
                    {
                        return false;
                    }

                    if (!searcherThing.CanSee(targetThing))
                    {
                        if (target is Pawn)
                        {
                            if ((flags & TargetScanFlags.NeedLOSToPawns) != 0)
                            {
                                return false;
                            }
                        }
                        else if ((flags & TargetScanFlags.NeedLOSToNonPawns) != 0)
                        {
                            return false;
                        }
                    }
                }

                if (((flags & TargetScanFlags.NeedThreat) != 0 || (flags & TargetScanFlags.NeedAutoTargetable) != 0) && target.ThreatDisabled(searcher))
                {
                    return false;
                }

                if ((flags & TargetScanFlags.NeedAutoTargetable) != 0 && !AttackTargetFinder.IsAutoTargetable(target))
                {
                    return false;
                }

                if ((flags & TargetScanFlags.NeedActiveThreat) != 0 && !GenHostility.IsActiveThreatTo(target, searcher.Thing.Faction))
                {
                    return false;
                }

                Pawn pawn = target as Pawn;
                if (onlyTargetMachines && pawn != null && pawn.RaceProps.IsFlesh)
                {
                    return false;
                }

                if ((flags & TargetScanFlags.NeedNonBurning) != 0 && targetThing.IsBurning())
                {
                    return false;
                }

                if (searcherThing.def.race != null && (int)searcherThing.def.race.intelligence >= 2)
                {
                    CompExplosive compExplosive = targetThing.TryGetComp<CompExplosive>();
                    if (compExplosive != null && compExplosive.wickStarted)
                    {
                        return false;
                    }
                }

                // 距离验证
                if (!targetThing.Position.InHorDistOf(searcherThing.Position, maxDist))
                    return false;

                // 最小距离验证
                if (!canTakeTargetsCloserThanEffectiveMinRange &&
                    (float)(searcherThing.Position - targetThing.Position).LengthHorizontalSquared < minDistSquared)
                    return false;

                // 中心点距离验证
                if (locus.IsValid &&
                    (float)(locus - targetThing.Position).LengthHorizontalSquared > maxLocusDistSquared)
                    return false;

                // 自定义验证器
                if (validator != null && !validator(targetThing))
                    return false;

                return true;
            }

            // 检查是否有可以直接射击的目标
            bool hasDirectShootTarget = false;
            for (int i = 0; i < tmpTargets.Count; i++)
            {
                IAttackTarget attackTarget = tmpTargets[i];
                if (attackTarget.Thing.Position.InHorDistOf(searcherThing.Position, maxDist) &&
                    InnerValidator(attackTarget, losValidator) &&
                    CanShootAtFromCurrentPosition(attackTarget, searcher, verb))
                {
                    hasDirectShootTarget = true;
                    break;
                }
            }

            IAttackTarget bestTarget;

            if (hasDirectShootTarget)
            {
                // 如果有可以直接射击的目标，使用基于分数的随机选择
                tmpTargets.RemoveAll(x => !x.Thing.Position.InHorDistOf(searcherThing.Position, maxDist) || !InnerValidator(x, losValidator));
                bestTarget = GetRandomShootingTargetByScore(tmpTargets, searcher, verb, angle);
            }
            else
            {
                // 否则使用最近的目标选择策略
                bool needReachableIfCantHit = (flags & TargetScanFlags.NeedReachableIfCantHitFromMyPos) > TargetScanFlags.None;
                bool needReachable = (flags & TargetScanFlags.NeedReachable) > TargetScanFlags.None;

                Predicate<Thing> reachableValidator;
                if (!needReachableIfCantHit || needReachable)
                {
                    reachableValidator = (Thing t) => InnerValidator((IAttackTarget)t, losValidator);
                }
                else
                {
                    reachableValidator = (Thing t) => InnerValidator((IAttackTarget)t, losValidator) &&
                                                     CanShootAtFromCurrentPosition((IAttackTarget)t, searcher, verb);
                }

                bestTarget = (IAttackTarget)GenClosest.ClosestThing_Global(
                    searcherThing.Position,
                    tmpTargets,
                    maxDist,
                    reachableValidator,
                    null,
                    false);
            }

            tmpTargets.Clear();
            return bestTarget;
        }
        /// <summary>
        /// 检查是否应该忽略非战斗人员
        /// </summary>
        private static bool ShouldIgnoreNoncombatant(Thing searcherThing, IAttackTarget target, TargetScanFlags flags)
        {
            // 只对Pawn类型的目标进行判断
            if (!(target is Pawn pawn))
                return false;

            // 如果是战斗人员，不忽略
            if (pawn.IsCombatant())
                return false;

            // 如果设置了忽略非战斗人员标志，则忽略
            if ((flags & TargetScanFlags.IgnoreNonCombatants) > TargetScanFlags.None)
                return true;

            // 如果看不到非战斗人员，则忽略
            return !GenSight.LineOfSightToThing(searcherThing.Position, pawn, searcherThing.Map, false, null);
        }

        /// <summary>
        /// 检查是否可以从当前位置射击目标
        /// </summary>
        private static bool CanShootAtFromCurrentPosition(IAttackTarget target, IAttackTargetSearcher searcher, Verb verb)
        {
            return verb != null && verb.CanHitTargetFrom(searcher.Thing.Position, target.Thing);
        }

        /// <summary>
        /// 通过权重随机获取射击目标
        /// </summary>
        private static IAttackTarget GetRandomShootingTargetByScore(List<IAttackTarget> targets, IAttackTargetSearcher searcher, Verb verb, Vector3 angle)
        {
            var availableTargets = GetAvailableShootingTargetsByScore(targets, searcher, verb, angle);
            if (availableTargets.TryRandomElementByWeight(x => x.Second, out Pair<IAttackTarget, float> result))
            {
                return result.First;
            }
            return null;
        }

        /// <summary>
        /// 获取可用射击目标及其分数的列表
        /// </summary>
        private static List<Pair<IAttackTarget, float>> GetAvailableShootingTargetsByScore(
            List<IAttackTarget> rawTargets,
            IAttackTargetSearcher searcher,
            Verb verb,
            Vector3 angle)
        {
            availableShootingTargets.Clear();

            if (rawTargets.Count == 0)
                return availableShootingTargets;

            // 初始化临时列表
            tmpTargetScores.Clear();
            tmpCanShootAtTarget.Clear();

            float highestScore = float.MinValue;
            IAttackTarget bestTarget = null;

            // 第一轮遍历：计算基础分数并标记可射击目标
            for (int i = 0; i < rawTargets.Count; i++)
            {
                tmpTargetScores.Add(float.MinValue);
                tmpCanShootAtTarget.Add(false);

                // 跳过搜索者自身
                if (rawTargets[i] == searcher)
                    continue;

                // 检查是否可以射击
                bool canShoot = CanShootAtFromCurrentPosition(rawTargets[i], searcher, verb);
                tmpCanShootAtTarget[i] = canShoot;

                if (canShoot)
                {
                    // 计算射击目标分数
                    float score = GetShootingTargetScore(rawTargets[i], searcher, verb, angle);
                    tmpTargetScores[i] = score;

                    // 更新最佳目标
                    if (bestTarget == null || score > highestScore)
                    {
                        bestTarget = rawTargets[i];
                        highestScore = score;
                    }
                }
            }

            // 构建可用目标列表
            for (int j = 0; j < rawTargets.Count; j++)
            {
                if (rawTargets[j] != searcher && tmpCanShootAtTarget[j])
                {
                    availableShootingTargets.Add(new Pair<IAttackTarget, float>(rawTargets[j], tmpTargetScores[j]));
                }
            }

            return availableShootingTargets;
        }

        /// <summary>
        /// 计算射击目标分数（核心评分算法）
        /// </summary>
        private static float GetShootingTargetScore(IAttackTarget target, IAttackTargetSearcher searcher, Verb verb, Vector3 angle)
        {
            float score = 60f;  // 基础分数

            // 距离因素：越近分数越高（最多40分）
            float distance = (target.Thing.Position - searcher.Thing.Position).LengthHorizontal;
            score -= Mathf.Min(distance, 40f);

            // 目标正在瞄准自己：加分
            if (target.TargetCurrentlyAimingAt == searcher.Thing)
                score += 10f;

            // 最近攻击目标：加分（如果最近攻击过这个目标）
            if (searcher.LastAttackedTarget == target.Thing && Find.TickManager.TicksGame - searcher.LastAttackTargetTick <= 300)
                score += 40f;

            // 掩体因素：目标有掩体保护则减分
            float blockChance = CoverUtility.CalculateOverallBlockChance(target.Thing.Position, searcher.Thing.Position, searcher.Thing.Map);
            score -= blockChance * 10f;

            // Pawn特定因素
            if (target is Pawn pawnTarget)
            {
                // 非战斗人员减分
                score -= NonCombatantScore(pawnTarget);

                // 远程攻击目标特殊处理
                if (verb.verbProps.ai_TargetHasRangedAttackScoreOffset != 0f &&
                    pawnTarget.CurrentEffectiveVerb != null &&
                    pawnTarget.CurrentEffectiveVerb.verbProps.Ranged)
                {
                    score += verb.verbProps.ai_TargetHasRangedAttackScoreOffset;
                }

                // 倒地目标大幅减分
                if (pawnTarget.Downed)
                    score -= 50f;
            }

            // 友军误伤因素
            score += FriendlyFireBlastRadiusTargetScoreOffset(target, searcher, verb);
            score += FriendlyFireConeTargetScoreOffset(target, searcher, verb);

            // 角度因素：计算与理想角度的偏差
            Vector3 targetDirection = (target.Thing.DrawPos - searcher.Thing.DrawPos).Yto0();
            float angleDeviation = Vector3.Angle(angle, targetDirection);

            // 防止除零错误
            if (angleDeviation < 0.1f)
                angleDeviation = 0.1f;

            // 最终分数计算：考虑目标优先级因子和角度偏差
            float finalScore = score * target.TargetPriorityFactor / angleDeviation;

            // 确保返回正数
            return Mathf.Max(finalScore, 0.01f);
        }

        /// <summary>
        /// 计算非战斗人员分数
        /// </summary>
        private static float NonCombatantScore(Thing target)
        {
            if (!(target is Pawn pawn))
                return 0f;

            if (!pawn.IsCombatant())
                return 50f;  // 非战斗人员大幅减分

            if (pawn.DevelopmentalStage.Juvenile())
                return 25f;  // 未成年人中等减分

            return 0f;  // 战斗成年人不减分
        }

        /// <summary>
        /// 计算爆炸半径内的友军误伤分数偏移
        /// </summary>
        private static float FriendlyFireBlastRadiusTargetScoreOffset(IAttackTarget target, IAttackTargetSearcher searcher, Verb verb)
        {
            // 检查是否启用了避免友军误伤半径
            if (verb.verbProps.ai_AvoidFriendlyFireRadius <= 0f)
                return 0f;

            Map map = target.Thing.Map;
            IntVec3 targetPosition = target.Thing.Position;
            int cellCount = GenRadial.NumCellsInRadius(verb.verbProps.ai_AvoidFriendlyFireRadius);
            float friendlyFireScore = 0f;

            // 遍历爆炸半径内的所有单元格
            for (int i = 0; i < cellCount; i++)
            {
                IntVec3 checkCell = targetPosition + GenRadial.RadialPattern[i];

                if (!checkCell.InBounds(map))
                    continue;

                bool hasLineOfSight = true;
                List<Thing> thingsInCell = checkCell.GetThingList(map);

                // 检查单元格内的所有物体
                for (int j = 0; j < thingsInCell.Count; j++)
                {
                    Thing thing = thingsInCell[j];

                    // 只关心攻击目标且不是当前目标
                    if (!(thing is IAttackTarget) || thing == target)
                        continue;

                    // 检查视线（只检查一次）
                    if (hasLineOfSight)
                    {
                        if (!GenSight.LineOfSight(targetPosition, checkCell, map, true, null, 0, 0))
                            break;  // 没有视线，跳过这个单元格

                        hasLineOfSight = false;
                    }

                    // 计算误伤分数
                    float hitScore;
                    if (thing == searcher)
                        hitScore = FriendlyFireScoreOffsetSelf;  // 击中自己
                    else if (!(thing is Pawn))
                        hitScore = FriendlyFireScoreOffsetPerNonPawn;  // 非Pawn物体
                    else if (thing.def.race.Animal)
                        hitScore = FriendlyFireScoreOffsetPerAnimal;  // 动物
                    else
                        hitScore = FriendlyFireScoreOffsetPerHumanlikeOrMechanoid;  // 人类或机械族

                    // 根据敌对关系调整分数
                    if (!searcher.Thing.HostileTo(thing))
                        friendlyFireScore -= hitScore;  // 友军：减分
                    else
                        friendlyFireScore += hitScore * 0.6f;  // 敌军：小幅加分
                }
            }

            return friendlyFireScore;
        }

        /// <summary>
        /// 计算锥形范围内的友军误伤分数偏移
        /// </summary>
        private static float FriendlyFireConeTargetScoreOffset(IAttackTarget target, IAttackTargetSearcher searcher, Verb verb)
        {
            // 只对Pawn类型的搜索者进行计算
            if (!(searcher.Thing is Pawn searcherPawn))
                return 0f;

            // 检查智能等级
            if (searcherPawn.RaceProps.intelligence < Intelligence.ToolUser)
                return 0f;

            // 机械族不计算锥形误伤
            if (searcherPawn.RaceProps.IsMechanoid)
                return 0f;

            // 只处理射击类动词
            if (!(verb is Verb_Shoot shootVerb))
                return 0f;

            ThingDef projectileDef = shootVerb.verbProps.defaultProjectile;
            if (projectileDef == null)
                return 0f;

            // 高空飞行的抛射物不计算锥形误伤
            if (projectileDef.projectile.flyOverhead)
                return 0f;

            Map map = searcherPawn.Map;

            // 获取射击报告
            ShotReport report = ShotReport.HitReportFor(searcherPawn, verb, (Thing)target);

            // 计算强制失误半径
            float forcedMissRadius = Mathf.Max(
                VerbUtility.CalculateAdjustedForcedMiss(verb.verbProps.ForcedMissRadius, report.ShootLine.Dest - report.ShootLine.Source),
                1.5f);

            // 获取可能被误伤的所有单元格
            IEnumerable<IntVec3> potentialHitCells =
                from dest in GenRadial.RadialCellsAround(report.ShootLine.Dest, forcedMissRadius, true)
                where dest.InBounds(map)
                select new ShootLine(report.ShootLine.Source, dest)
                into line
                from pos in line.Points().Concat(line.Dest).TakeWhile(pos => pos.CanBeSeenOverFast(map))
                select pos;

            potentialHitCells = potentialHitCells.Distinct();

            float coneFriendlyFireScore = 0f;

            // 计算锥形范围内的误伤分数
            foreach (IntVec3 cell in potentialHitCells)
            {
                float interceptChance = VerbUtility.InterceptChanceFactorFromDistance(report.ShootLine.Source.ToVector3Shifted(), cell);

                if (interceptChance <= 0f)
                    continue;

                List<Thing> thingsInCell = cell.GetThingList(map);

                for (int i = 0; i < thingsInCell.Count; i++)
                {
                    Thing thing = thingsInCell[i];

                    if (!(thing is IAttackTarget) || thing == target)
                        continue;

                    // 计算误伤分数
                    float hitScore;
                    if (thing == searcher)
                        hitScore = FriendlyFireScoreOffsetSelf;
                    else if (!(thing is Pawn))
                        hitScore = FriendlyFireScoreOffsetPerNonPawn;
                    else if (thing.def.race.Animal)
                        hitScore = FriendlyFireScoreOffsetPerAnimal;
                    else
                        hitScore = FriendlyFireScoreOffsetPerHumanlikeOrMechanoid;

                    // 根据拦截概率和敌对关系调整分数
                    hitScore *= interceptChance;
                    if (!searcher.Thing.HostileTo(thing))
                        hitScore = -hitScore;  // 友军：减分
                    else
                        hitScore *= 0.6f;  // 敌军：小幅加分

                    coneFriendlyFireScore += hitScore;
                }
            }

            return coneFriendlyFireScore;
        }
    }
}
