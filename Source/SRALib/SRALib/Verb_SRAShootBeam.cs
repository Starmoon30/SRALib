using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace SRA
{
    public enum SRABeamTargetIgnore
    {
        // 只伤害敌对目标。
        ignoreNonHostile,

        // 伤害所有敌对目标，以及阻挡视线的中立目标；友方始终忽略。
        ignoreNonLOSBlockingNonHostile,

        // 只忽略同阵营和盟友目标。
        ignoreFriendly,

        // 不进行阵营/敌我过滤。
        ignoreNothing
    }

    public class SRABeamTrajectoryPoint
    {
        // 相对目标点的偏移：x 为右侧，y 为射击前方，参考方向为从正下方向正上方目标射击。
        public Vector2 offset = Vector2.zero;

        // burst 开始后到达该偏移点的 tick，相邻节点之间线性插值。
        public int arrivalTick;
    }

    public class VerbProperties_SRAShootBeam : VerbProperties
    {
        // 单次命中判定的伤害覆盖；小于 0 时沿用 beamTotalDamage 或 beamDamageDef.defaultDamage。
        public float beamDamageAmount = -1f;

        // 光束穿甲覆盖；小于 0 时沿用 beamDamageDef.defaultArmorPenetration。
        public float beamArmorPenetration = -1f;

        // 主伤害之外追加的额外伤害；会随 pathDamageFactor 等当前伤害倍率一起缩放。
        public List<ExtraDamage> extraDamages;

        // 落点判定半径；大于 0 时范围内单位视为被当前落点击中。
        public float hitRadius = 0f;

        // 目标过滤策略；从 ignoreNonHostile 到 ignoreNothing，忽略范围依次减小。
        public SRABeamTargetIgnore targetignore = SRABeamTargetIgnore.ignoreNothing;

        // 是否让发射源到当前落点之间的路径单位也受到伤害。
        public bool damageBeamPath = false;

        // 路径伤害粗细；0 表示只处理中心线格。
        public float pathHitRadius = 0f;

        // 路径伤害倍率；终点和 hitRadius 范围内伤害不受此倍率影响。
        public float pathDamageFactor = 1f;

        // 忽略 LOS 和墙体截断，光束可穿透障碍继续命中路径与落点。
        public bool penetrateObstacles = false;

        // 对 Mineable 使用采矿伤害逻辑，正常触发采矿产出。
        public bool mining = false;

        // 自定义光束落点轨迹；为空时沿用原版 beamWidth/beamCurvature/beamMaxDeviation 轨迹。
        public List<SRABeamTrajectoryPoint> customTrajectory;

        // If true, the current burst keeps firing at its cached target position after killing/destroying the original target.
        public bool forceCompleteBurst = false;

        // Additional maintained beam motes drawn between caster and current visual target.
        public List<ThingDef> extraBeamMoteDefs;
    }

    public class Verb_SRAShootBeam : Verb
    {
        private const int NumSubdivisionsPerUnitLength = 1;

        private VerbProperties_SRAShootBeam Props => verbProps as VerbProperties_SRAShootBeam;

        protected override int ShotsPerBurst => BurstShotCount;

        public float ShotProgress => TicksBetweenBurstShots <= 0 ? 1f : 1f - (float)ticksToNextPathStep / TicksBetweenBurstShots;

        public Vector3 InterpolatedPosition => GetBeamPositionAtTick(beamBurstTick);

        public override float? AimAngleOverride
        {
            get
            {
                if (state != VerbState.Bursting)
                {
                    return null;
                }

                return (InterpolatedPosition - caster.DrawPos).AngleFlat();
            }
        }

        private List<Vector3> path = new List<Vector3>();
        private readonly List<Vector3> tmpPath = new List<Vector3>();
        private readonly HashSet<IntVec3> pathCells = new HashSet<IntVec3>();
        private readonly HashSet<IntVec3> tmpPathCells = new HashSet<IntVec3>();
        private readonly HashSet<IntVec3> tmpHighlightCells = new HashSet<IntVec3>();
        private readonly HashSet<IntVec3> tmpSecondaryHighlightCells = new HashSet<IntVec3>();
        private readonly HashSet<Thing> damagedThingsThisShot = new HashSet<Thing>();
        private readonly List<SRABeamTrajectoryPoint> sortedCustomTrajectory = new List<SRABeamTrajectoryPoint>();

        private int ticksToNextPathStep;
        private int beamBurstTick;
        private Vector3 initialTargetPosition;
        private Vector3 lockedBurstTargetPosition;
        private IntVec3 lockedBurstTargetCell = IntVec3.Invalid;
        private bool hasLockedBurstTarget;
        private MoteDualAttached mote;
        private readonly List<MoteDualAttached> extraMotes = new List<MoteDualAttached>();
        private Effecter endEffecter;
        private Sustainer sustainer;

        private bool HasCustomTrajectory => !Props?.customTrajectory.NullOrEmpty() ?? false;

        public override bool TryStartCastOn(LocalTargetInfo castTarg, LocalTargetInfo destTarg, bool surpriseAttack = false, bool canHitNonTargetPawns = true, bool preventFriendlyFire = false, bool nonInterruptingSelfCast = false)
        {
            LocalTargetInfo effectiveTarget = verbProps.beamTargetsGround ? castTarg.Cell : castTarg;
            TryCacheBurstTarget(effectiveTarget);
            if (Props == null || !Props.penetrateObstacles)
            {
                bool started = base.TryStartCastOn(effectiveTarget, destTarg, surpriseAttack, canHitNonTargetPawns, preventFriendlyFire, nonInterruptingSelfCast);
                if (!started)
                {
                    ClearLockedBurstTarget();
                }

                return started;
            }

            if (caster == null)
            {
                Log.Error("Verb " + GetUniqueLoadID() + " needs caster to work (possibly lost during saving/loading).");
                ClearLockedBurstTarget();
                return false;
            }

            if (!caster.Spawned || state == VerbState.Bursting || !CanHitTarget(effectiveTarget))
            {
                ClearLockedBurstTarget();
                return false;
            }

            this.surpriseAttack = surpriseAttack;
            canHitNonTargetPawnsNow = canHitNonTargetPawns;
            this.preventFriendlyFire = preventFriendlyFire;
            this.nonInterruptingSelfCast = nonInterruptingSelfCast;
            currentTarget = effectiveTarget;
            currentDestination = destTarg;

            if (CasterIsPawn && WarmupTime > 0f)
            {
                ShootLine shootLine;
                if (!TryFindSRAShootLineFromTo(caster.Position, effectiveTarget, out shootLine))
                {
                    ClearLockedBurstTarget();
                    return false;
                }

                CasterPawn.Drawer.Notify_WarmingCastAlongLine(shootLine, caster.Position);
                float aimingDelayFactor = CasterPawn.GetStatValue(StatDefOf.AimingDelayFactor, true, -1);
                int ticks = (WarmupTime * aimingDelayFactor).SecondsToTicks();
                CasterPawn.stances.SetStance(new Stance_Warmup(ticks, effectiveTarget, this));
                if (verbProps.stunTargetOnCastStart && effectiveTarget.Pawn != null)
                {
                    effectiveTarget.Pawn.stances.stunner.StunFor(ticks, null, false, true, false);
                }
            }
            else
            {
                Ability ability = verbTracker.directOwner as Ability;
                if (ability != null)
                {
                    ability.lastCastTick = Find.TickManager.TicksGame;
                }

                WarmupComplete();
            }

            return true;
        }

        public override bool CanHitTargetFrom(IntVec3 root, LocalTargetInfo targ)
        {
            if (Props == null || !Props.penetrateObstacles)
            {
                return base.CanHitTargetFrom(root, targ);
            }

            if (targ.Thing != null && targ.Thing == caster)
            {
                return targetParams.canTargetSelf;
            }

            ShootLine shootLine;
            return (targ.Pawn == null || !targ.Pawn.IsPsychologicallyInvisible() || !caster.HostileTo(targ.Pawn)) &&
                   !ApparelPreventsShooting() &&
                   TryFindSRAShootLineFromTo(root, targ, out shootLine);
        }

        public override void DrawHighlight(LocalTargetInfo target)
        {
            base.DrawHighlight(target);
            if (caster?.Map == null)
            {
                return;
            }

            LocalTargetInfo effectiveTarget = verbProps.beamTargetsGround ? target.Cell : target;
            CalculatePath(effectiveTarget.CenterVector3, tmpPath, tmpPathCells, false);
            ShootLine shootLine;
            if (!TryFindSRAShootLineFromTo(caster.Position, effectiveTarget, out shootLine))
            {
                return;
            }

            for (int i = 0; i < tmpPath.Count; i++)
            {
                IntVec3 targetCell = tmpPath[i].Yto0().ToIntVec3();
                IntVec3 hitCell;
                if (TryGetHitCell(shootLine.Source, targetCell, out hitCell))
                {
                    AddCellsInRadius(hitCell, Props?.hitRadius ?? 0f, tmpHighlightCells);
                }

                if (Props != null && Props.damageBeamPath)
                {
                    foreach (IntVec3 cell in GetBeamPathCells(shootLine.Source, targetCell, Props.pathHitRadius))
                    {
                        tmpSecondaryHighlightCells.Add(cell);
                    }
                }
            }

            tmpSecondaryHighlightCells.RemoveWhere(c => tmpHighlightCells.Contains(c));
            if (tmpHighlightCells.Count > 0)
            {
                GenDraw.DrawFieldEdges(tmpHighlightCells.ToList(), verbProps.highlightColor ?? Color.white, null, null, 2900);
            }

            if (tmpSecondaryHighlightCells.Count > 0)
            {
                GenDraw.DrawFieldEdges(tmpSecondaryHighlightCells.ToList(), verbProps.secondaryHighlightColor ?? Color.white, null, null, 2900);
            }

            tmpHighlightCells.Clear();
            tmpSecondaryHighlightCells.Clear();
        }

        protected override bool TryCastShot()
        {
            if (!TryGetShotTarget(out LocalTargetInfo shotTarget))
            {
                return false;
            }

            ShootLine shootLine;
            bool hasShootLine = TryFindSRAShootLineFromTo(caster.Position, shotTarget, out shootLine);
            if (verbProps.stopBurstWithoutLos && !hasShootLine)
            {
                return false;
            }

            if (!hasShootLine)
            {
                return true;
            }

            NotifyEquipmentShotConsumed();
            lastShotTick = Find.TickManager.TicksGame;
            ticksToNextPathStep = TicksBetweenBurstShots;

            damagedThingsThisShot.Clear();
            Vector3 beamPosition = GetBeamPositionForCurrentShot();
            IntVec3 targetCell = beamPosition.Yto0().ToIntVec3();

            IntVec3 hitCell;
            if (TryGetHitCell(shootLine.Source, targetCell, out hitCell))
            {
                HitCell(hitCell, shootLine.Source, 1f);
                if (Props != null && Props.hitRadius > 0f)
                {
                    foreach (IntVec3 cell in CellsInRadius(hitCell, Props.hitRadius))
                    {
                        if (cell != hitCell)
                        {
                            HitCell(cell, shootLine.Source, 1f);
                        }
                    }
                }
                else if (verbProps.beamHitsNeighborCells)
                {
                    foreach (IntVec3 neighbour in GetBeamHitNeighbourCells(shootLine.Source, hitCell))
                    {
                        HitCell(neighbour, shootLine.Source, pathCells.Contains(neighbour) ? 1f : 0.5f);
                    }
                }
            }

            if (Props != null && Props.damageBeamPath)
            {
                foreach (IntVec3 cell in GetBeamPathCells(shootLine.Source, targetCell, Props.pathHitRadius))
                {
                    HitCell(cell, shootLine.Source, Props.pathDamageFactor);
                }
            }

            damagedThingsThisShot.Clear();
            return true;
        }

        public override void BurstingTick()
        {
            if (!CasterCanMaintainBeam())
            {
                Reset();
                return;
            }

            ticksToNextPathStep--;
            beamBurstTick++;
            Vector3 beamPosition = GetVisualBeamPosition();
            IntVec3 beamCell = beamPosition.Yto0().ToIntVec3();
            Vector3 beamVector = beamPosition - caster.Position.ToVector3Shifted();
            float beamLength = beamVector.MagnitudeHorizontal();
            Vector3 direction = beamVector.Yto0().normalized;

            if (direction == Vector3.zero)
            {
                direction = Vector3.forward;
            }

            Vector3 targetOffset = beamPosition - beamCell.ToVector3Shifted();
            IntVec3 visualCell = GetVisualTargetCell(beamPosition, out targetOffset);
            Vector3 startOffset = direction * verbProps.beamStartOffset;

            if (mote != null)
            {
                mote.UpdateTargets(new TargetInfo(caster.Position, caster.Map, false), new TargetInfo(visualCell, caster.Map, false), startOffset, targetOffset);
                mote.Maintain();
            }
            MaintainExtraBeamMotes(visualCell, startOffset, targetOffset);

            if (beamCell.InBounds(caster.Map) && verbProps.beamGroundFleckDef != null && Rand.Chance(verbProps.beamFleckChancePerTick))
            {
                FleckMaker.Static(beamPosition, caster.Map, verbProps.beamGroundFleckDef, 1f);
            }

            MaintainEndEffecter(visualCell, targetOffset);
            ThrowLineFlecks(direction, beamLength);
            sustainer?.Maintain();
        }

        public override void WarmupComplete()
        {
            if (!TryGetBurstTargetPosition(out Vector3 targetPosition))
            {
                Reset();
                return;
            }

            EndSustainer();
            burstShotsLeft = ShotsPerBurst;
            state = VerbState.Bursting;
            initialTargetPosition = targetPosition;
            beamBurstTick = 0;
            ticksToNextPathStep = TicksBetweenBurstShots;
            CalculatePath(targetPosition, path, pathCells, true);
            damagedThingsThisShot.Clear();

            Vector3 startPosition = GetBeamPositionAtTick(0);
            IntVec3 visualCell = GetVisualTargetCell(startPosition, out Vector3 visualOffset);

            if (verbProps.beamMoteDef != null)
            {
                mote = MoteMaker.MakeInteractionOverlay(verbProps.beamMoteDef, caster, new TargetInfo(visualCell, caster.Map, false));
                mote.UpdateTargets(new TargetInfo(caster.Position, caster.Map, false), new TargetInfo(visualCell, caster.Map, false), Vector3.zero, visualOffset);
            }

            CreateExtraBeamMotes(visualCell, Vector3.zero, visualOffset);

            TryCastNextBurstShot();
            ticksToNextPathStep = TicksBetweenBurstShots;
            endEffecter?.Cleanup();
            endEffecter = null;

            if (verbProps.soundCastBeam != null)
            {
                sustainer = verbProps.soundCastBeam.TrySpawnSustainer(SoundInfo.InMap(new TargetInfo(caster.Position, caster.Map, false), MaintenanceType.PerTick));
            }
        }

        private void CalculatePath(Vector3 target, List<Vector3> pathList, HashSet<IntVec3> pathCellsList, bool addRandomOffset)
        {
            pathList.Clear();
            pathCellsList.Clear();

            if (HasCustomTrajectory)
            {
                sortedCustomTrajectory.Clear();
                sortedCustomTrajectory.AddRange(Props.customTrajectory.Where(p => p != null));
                sortedCustomTrajectory.SortBy(p => p.arrivalTick);

                for (int i = 0; i < sortedCustomTrajectory.Count; i++)
                {
                    Vector3 position = TransformTrajectoryOffset(target, sortedCustomTrajectory[i].offset);
                    pathList.Add(position);
                    IntVec3 cell = position.Yto0().ToIntVec3();
                    if (cell.InBounds(caster.Map))
                    {
                        pathCellsList.Add(cell);
                    }
                }

                if (pathList.Count == 0)
                {
                    pathList.Add(target.Yto0());
                }

                return;
            }

            Vector3 toTarget = (target - caster.Position.ToVector3Shifted()).Yto0();
            float distance = toTarget.magnitude;
            Vector3 forward = distance > 0.001f ? toTarget.normalized : Vector3.forward;
            Vector3 lateral = forward.RotatedBy(-90f);
            float widthFactor = verbProps.beamFullWidthRange > 0f ? Mathf.Min(distance / verbProps.beamFullWidthRange, 1f) : 1f;
            float step = (verbProps.beamWidth + 1f) * widthFactor / ShotsPerBurst;
            Vector3 current = target.Yto0() - lateral * verbProps.beamWidth / 2f * widthFactor;
            pathList.Add(current);

            for (int i = 0; i < ShotsPerBurst; i++)
            {
                Vector3 randomDeviation = forward * (Rand.Value * verbProps.beamMaxDeviation) - forward / 2f;
                Vector3 curve = Mathf.Sin(((float)i / ShotsPerBurst + 0.5f) * Mathf.PI) * verbProps.beamCurvature * -forward - forward * verbProps.beamMaxDeviation / 2f;
                pathList.Add(current + (addRandomOffset ? (randomDeviation + curve) * widthFactor : curve * widthFactor));
                current += lateral * step;
            }

            for (int i = 0; i < pathList.Count; i++)
            {
                IntVec3 cell = pathList[i].ToIntVec3();
                if (cell.InBounds(caster.Map))
                {
                    pathCellsList.Add(cell);
                }
            }
        }

        private Vector3 GetBeamPositionForCurrentShot()
        {
            int shotIndex = Mathf.Clamp(ShotsPerBurst - burstShotsLeft, 0, Mathf.Max(ShotsPerBurst - 1, 0));
            return GetBeamPositionAtTick(shotIndex * TicksBetweenBurstShots);
        }

        private Vector3 GetBeamPositionAtTick(int tick)
        {
            if (HasCustomTrajectory)
            {
                return GetCustomTrajectoryPosition(tick);
            }

            if (path.Count == 0)
            {
                return CurrentBurstTargetPosition();
            }

            Vector3 targetDrift = CurrentBurstTargetPosition() - initialTargetPosition;
            if (path.Count == 1 || ShotsPerBurst <= 1)
            {
                return path[0] + targetDrift;
            }

            int duration = Mathf.Max(1, (ShotsPerBurst - 1) * TicksBetweenBurstShots);
            float pathIndex = Mathf.Clamp01((float)tick / duration) * (path.Count - 1);
            int lower = Mathf.FloorToInt(pathIndex);
            int upper = Mathf.Min(lower + 1, path.Count - 1);
            return Vector3.Lerp(path[lower], path[upper], pathIndex - lower) + targetDrift;
        }

        private Vector3 GetCustomTrajectoryPosition(int tick)
        {
            if (sortedCustomTrajectory.Count == 0)
            {
                sortedCustomTrajectory.AddRange(Props.customTrajectory.Where(p => p != null));
                sortedCustomTrajectory.SortBy(p => p.arrivalTick);
            }

            Vector3 target = CurrentBurstTargetPosition();
            if (sortedCustomTrajectory.Count == 0)
            {
                return target.Yto0();
            }

            if (tick <= sortedCustomTrajectory[0].arrivalTick)
            {
                Vector3 first = TransformTrajectoryOffset(target, sortedCustomTrajectory[0].offset);
                if (sortedCustomTrajectory[0].arrivalTick <= 0)
                {
                    return first;
                }

                return Vector3.Lerp(target.Yto0(), first, (float)tick / sortedCustomTrajectory[0].arrivalTick);
            }

            for (int i = 1; i < sortedCustomTrajectory.Count; i++)
            {
                SRABeamTrajectoryPoint previous = sortedCustomTrajectory[i - 1];
                SRABeamTrajectoryPoint next = sortedCustomTrajectory[i];
                if (tick <= next.arrivalTick)
                {
                    int duration = Mathf.Max(1, next.arrivalTick - previous.arrivalTick);
                    float t = Mathf.Clamp01((float)(tick - previous.arrivalTick) / duration);
                    return Vector3.Lerp(TransformTrajectoryOffset(target, previous.offset), TransformTrajectoryOffset(target, next.offset), t);
                }
            }

            return TransformTrajectoryOffset(target, sortedCustomTrajectory[sortedCustomTrajectory.Count - 1].offset);
        }

        private Vector3 TransformTrajectoryOffset(Vector3 target, Vector2 offset)
        {
            Vector3 toTarget = (target - caster.Position.ToVector3Shifted()).Yto0();
            Vector3 forward = toTarget.MagnitudeHorizontalSquared() > 0.0001f ? toTarget.normalized : Vector3.forward;
            Vector3 right = forward.RotatedBy(90f);
            return target.Yto0() + right * offset.x + forward * offset.y;
        }

        private bool TryGetShotTarget(out LocalTargetInfo shotTarget)
        {
            if (TryCacheBurstTarget(currentTarget))
            {
                shotTarget = currentTarget;
                return true;
            }

            if (Props != null && Props.forceCompleteBurst && TryGetLockedBurstTarget(out shotTarget))
            {
                return true;
            }

            shotTarget = LocalTargetInfo.Invalid;
            return false;
        }

        private bool TryGetBurstTargetPosition(out Vector3 targetPosition)
        {
            if (TryCacheBurstTarget(currentTarget))
            {
                targetPosition = lockedBurstTargetPosition;
                return true;
            }

            if (Props != null && Props.forceCompleteBurst && hasLockedBurstTarget)
            {
                targetPosition = lockedBurstTargetPosition;
                return true;
            }

            targetPosition = Vector3.zero;
            return false;
        }

        private Vector3 CurrentBurstTargetPosition()
        {
            if (TryGetBurstTargetPosition(out Vector3 targetPosition))
            {
                return targetPosition;
            }

            return initialTargetPosition;
        }

        private bool TryGetLockedBurstTarget(out LocalTargetInfo target)
        {
            if (!hasLockedBurstTarget)
            {
                target = LocalTargetInfo.Invalid;
                return false;
            }

            IntVec3 cell = lockedBurstTargetCell.IsValid ? lockedBurstTargetCell : lockedBurstTargetPosition.Yto0().ToIntVec3();
            if (!cell.IsValid)
            {
                target = LocalTargetInfo.Invalid;
                return false;
            }

            target = new LocalTargetInfo(cell);
            return true;
        }

        private bool TryCacheBurstTarget(LocalTargetInfo target)
        {
            if (!TryGetUsableTargetCenter(target, out Vector3 targetPosition))
            {
                return false;
            }

            lockedBurstTargetPosition = targetPosition;
            lockedBurstTargetCell = target.Cell.IsValid ? target.Cell : targetPosition.Yto0().ToIntVec3();
            hasLockedBurstTarget = true;
            return true;
        }

        private bool TryGetUsableTargetCenter(LocalTargetInfo target, out Vector3 targetPosition)
        {
            targetPosition = Vector3.zero;
            if (!target.IsValid)
            {
                return false;
            }

            if (!target.HasThing)
            {
                targetPosition = target.CenterVector3;
                return true;
            }

            Thing targetThing = target.Thing;
            if (targetThing == null || targetThing.Destroyed || !targetThing.Spawned || targetThing.Map != caster?.Map)
            {
                return false;
            }

            if (targetThing is Pawn pawn && pawn.Dead)
            {
                return false;
            }

            targetPosition = target.CenterVector3;
            return true;
        }

        private void ClearLockedBurstTarget()
        {
            hasLockedBurstTarget = false;
            lockedBurstTargetCell = IntVec3.Invalid;
            lockedBurstTargetPosition = Vector3.zero;
        }

        private Thing CurrentIntendedTargetThing()
        {
            Thing thing = currentTarget.Thing;
            if (thing == null || thing.Destroyed || !thing.Spawned || thing.Map != caster?.Map)
            {
                return null;
            }

            if (thing is Pawn pawn && pawn.Dead)
            {
                return null;
            }

            return thing;
        }

        private bool TryFindSRAShootLineFromTo(IntVec3 root, LocalTargetInfo target, out ShootLine shootLine)
        {
            if (Props == null || !Props.penetrateObstacles)
            {
                return TryFindShootLineFromTo(root, target, out shootLine, false);
            }

            if (target.HasThing && target.Thing.Map != caster.Map)
            {
                shootLine = default(ShootLine);
                return false;
            }

            CellRect occupiedRect = target.HasThing ? target.Thing.OccupiedRect() : CellRect.SingleCell(target.Cell);
            shootLine = new ShootLine(root, target.Cell);
            if (OutOfRange(root, target, occupiedRect))
            {
                return false;
            }

            if (verbProps.mustCastOnOpenGround && (!target.Cell.Standable(caster.Map) || caster.Map.thingGrid.CellContains(target.Cell, ThingCategory.Pawn)))
            {
                return false;
            }

            return true;
        }

        private bool TryGetHitCell(IntVec3 source, IntVec3 targetCell, out IntVec3 hitCell)
        {
            if (Props != null && Props.penetrateObstacles)
            {
                hitCell = targetCell.InBounds(caster.Map) ? targetCell : LastInBoundsCellOnLine(source, targetCell);
                return hitCell.IsValid && (!verbProps.beamCantHitWithinMinRange || hitCell.DistanceTo(source) >= verbProps.minRange);
            }

            IntVec3 lastVisible = IntVec3.Invalid;
            foreach (IntVec3 cell in CellsOnLine(source, targetCell))
            {
                if (!cell.InBounds(caster.Map))
                {
                    break;
                }

                if (!cell.CanBeSeenOverFast(caster.Map))
                {
                    break;
                }

                lastVisible = cell;
                if (cell == targetCell)
                {
                    break;
                }
            }

            if (verbProps.beamCantHitWithinMinRange && lastVisible.IsValid && lastVisible.DistanceTo(source) < verbProps.minRange)
            {
                hitCell = default(IntVec3);
                return false;
            }

            hitCell = lastVisible.IsValid ? lastVisible : targetCell;
            return lastVisible.IsValid;
        }

        private IEnumerable<IntVec3> GetBeamHitNeighbourCells(IntVec3 source, IntVec3 pos)
        {
            if (!verbProps.beamHitsNeighborCells)
            {
                yield break;
            }

            for (int i = 0; i < 4; i++)
            {
                IntVec3 cell = pos + GenAdj.CardinalDirections[i];
                if (cell.InBounds(caster.Map) && (!verbProps.beamHitsNeighborCellsRequiresLOS || Props != null && Props.penetrateObstacles || GenSight.LineOfSight(source, cell, caster.Map)))
                {
                    yield return cell;
                }
            }
        }

        private IEnumerable<IntVec3> GetBeamPathCells(IntVec3 source, IntVec3 target, float radius)
        {
            bool hasEnteredMap = false;
            HashSet<IntVec3> yieldedCells = new HashSet<IntVec3>();
            foreach (IntVec3 lineCell in CellsOnLine(source, target))
            {
                if (lineCell == source)
                {
                    continue;
                }

                if (!lineCell.InBounds(caster.Map))
                {
                    if (hasEnteredMap)
                    {
                        yield break;
                    }

                    continue;
                }

                hasEnteredMap = true;
                if (Props == null || !Props.penetrateObstacles)
                {
                    if (!lineCell.CanBeSeenOverFast(caster.Map))
                    {
                        yield break;
                    }
                }

                if (radius > 0f)
                {
                    foreach (IntVec3 cell in CellsInRadius(lineCell, radius))
                    {
                        if (yieldedCells.Add(cell))
                        {
                            yield return cell;
                        }
                    }
                }
                else
                {
                    if (yieldedCells.Add(lineCell))
                    {
                        yield return lineCell;
                    }
                }
            }
        }

        private IEnumerable<IntVec3> CellsOnLine(IntVec3 source, IntVec3 target)
        {
            ShootLine line = new ShootLine(source, target);
            foreach (IntVec3 cell in line.Points())
            {
                yield return cell;
            }

            yield return target;
        }

        private IEnumerable<IntVec3> CellsInRadius(IntVec3 center, float radius)
        {
            if (radius <= 0f)
            {
                if (center.InBounds(caster.Map))
                {
                    yield return center;
                }

                yield break;
            }

            foreach (IntVec3 cell in GenRadial.RadialCellsAround(center, radius, true))
            {
                if (cell.InBounds(caster.Map))
                {
                    yield return cell;
                }
            }
        }

        private void AddCellsInRadius(IntVec3 center, float radius, HashSet<IntVec3> cells)
        {
            foreach (IntVec3 cell in CellsInRadius(center, radius))
            {
                cells.Add(cell);
            }
        }

        private IntVec3 LastInBoundsCellOnLine(IntVec3 source, IntVec3 target)
        {
            IntVec3 result = IntVec3.Invalid;
            foreach (IntVec3 cell in CellsOnLine(source, target))
            {
                if (cell.InBounds(caster.Map))
                {
                    result = cell;
                    continue;
                }

                if (result.IsValid)
                {
                    break;
                }
            }

            return result;
        }

        private void HitCell(IntVec3 cell, IntVec3 sourceCell, float damageFactor)
        {
            if (!cell.InBounds(caster.Map))
            {
                return;
            }

            List<Thing> things = cell.GetThingList(caster.Map);
            for (int i = things.Count - 1; i >= 0; i--)
            {
                Thing thing = things[i];
                if (CanDamageThing(thing) && damagedThingsThisShot.Add(thing))
                {
                    ApplyDamage(thing, sourceCell, damageFactor);
                }
            }

            if (verbProps.beamSetsGroundOnFire && Rand.Chance(verbProps.beamChanceToStartFire))
            {
                FireUtility.TryStartFireIn(cell, caster.Map, 1f, caster, null);
            }
        }

        private bool CanDamageThing(Thing thing)
        {
            if (thing == null || !thing.Spawned || thing == caster)
            {
                return false;
            }

            if (!thing.def.useHitPoints && !(thing is Pawn))
            {
                return false;
            }

            Thing intendedTargetThing = CurrentIntendedTargetThing();
            if (thing is Pawn && !canHitNonTargetPawnsNow && thing != intendedTargetThing)
            {
                return false;
            }

            if (IsIgnoredByTargetIgnore(thing))
            {
                return false;
            }

            return Props != null && Props.penetrateObstacles || !CoverUtility.ThingCovered(thing, caster.Map);
        }

        private bool IsIgnoredByTargetIgnore(Thing thing)
        {
            SRABeamTargetIgnore targetIgnore = Props?.targetignore ?? SRABeamTargetIgnore.ignoreNothing;
            switch (targetIgnore)
            {
                case SRABeamTargetIgnore.ignoreNonHostile:
                    return !GenHostility.HostileTo(thing, caster);
                case SRABeamTargetIgnore.ignoreNonLOSBlockingNonHostile:
                    return IsFriendlyToCaster(thing) || (!GenHostility.HostileTo(thing, caster) && !BlocksLineOfSight(thing));
                case SRABeamTargetIgnore.ignoreFriendly:
                    return IsFriendlyToCaster(thing);
                case SRABeamTargetIgnore.ignoreNothing:
                default:
                    return false;
            }
        }

        private static bool BlocksLineOfSight(Thing thing)
        {
            if (thing is Building building)
            {
                return !building.CanBeSeenOver();
            }

            return thing?.def.Fillage == FillCategory.Full;
        }

        private bool IsFriendlyToCaster(Thing thing)
        {
            Faction casterFaction = caster?.Faction;
            Faction thingFaction = thing?.Faction;
            if (casterFaction == null || thingFaction == null)
            {
                return false;
            }

            return thingFaction == casterFaction || thingFaction.RelationKindWith(casterFaction) == FactionRelationKind.Ally;
        }

        private void ApplyDamage(Thing thing, IntVec3 sourceCell, float damageFactor)
        {
            DamageDef damageDef = verbProps.beamDamageDef;
            if (thing == null)
            {
                return;
            }

            float damageAmount = damageDef != null ? GetBeamDamageAmount(damageFactor) : 0f;
            if (damageAmount <= 0f && (Props?.extraDamages).NullOrEmpty())
            {
                return;
            }

            float angle = (thing.Position - sourceCell).AngleFlat;
            ThingDef equipmentDef = EquipmentSource?.def;
            Thing intendedTargetThing = CurrentIntendedTargetThing();
            BattleLogEntry_RangedImpact log = equipmentDef != null ? new BattleLogEntry_RangedImpact(caster, thing, intendedTargetThing, equipmentDef, null, null) : null;

            if (damageAmount > 0f && Props != null && Props.mining && thing is Mineable mineable)
            {
                ApplyMiningDamageToMineable(mineable, damageDef, damageAmount);
                ApplyBeamExtraDamages(thing, log, damageFactor, angle, equipmentDef, intendedTargetThing);
                return;
            }

            if (damageAmount > 0f && damageDef != null)
            {
                DamageInfo dinfo = new DamageInfo(damageDef, damageAmount, GetBeamArmorPenetration(damageDef), angle, caster, null, equipmentDef, DamageInfo.SourceCategory.ThingOrUnknown, intendedTargetThing, true, true, QualityCategory.Normal, true, false);
                DamageWorker.DamageResult result = thing.TakeDamage(dinfo);
                result.AssociateWithLog(log);
            }

            ApplyBeamExtraDamages(thing, log, damageFactor, angle, equipmentDef, intendedTargetThing);
            TryApplyBeamFire(thing);
        }

        private void ApplyBeamExtraDamages(Thing thing, BattleLogEntry_RangedImpact log, float damageFactor, float angle, ThingDef equipmentDef, Thing intendedTargetThing)
        {
            if ((Props?.extraDamages).NullOrEmpty())
            {
                return;
            }

            float factor = Mathf.Max(0f, damageFactor);
            for (int i = 0; i < Props.extraDamages.Count; i++)
            {
                ExtraDamage extraDamage = Props.extraDamages[i];
                if (extraDamage == null || extraDamage.def == null || !Rand.Chance(extraDamage.chance))
                {
                    continue;
                }

                float amount = extraDamage.amount * factor;
                if (amount <= 0f)
                {
                    continue;
                }

                DamageInfo dinfo = new DamageInfo(extraDamage.def, amount, extraDamage.AdjustedArmorPenetration(), angle, caster, null, equipmentDef, DamageInfo.SourceCategory.ThingOrUnknown, intendedTargetThing, true, true, QualityCategory.Normal, true, false);
                thing.TakeDamage(dinfo).AssociateWithLog(log);
            }
        }

        private float GetBeamDamageAmount(float damageFactor)
        {
            float amount;
            if (Props != null && Props.beamDamageAmount >= 0f)
            {
                amount = Props.beamDamageAmount;
            }
            else if (verbProps.beamTotalDamage > 0f)
            {
                amount = verbProps.beamTotalDamage / Mathf.Max(1, pathCells.Count);
            }
            else
            {
                amount = verbProps.beamDamageDef.defaultDamage;
            }

            return amount * Mathf.Max(0f, damageFactor);
        }

        private float GetBeamArmorPenetration(DamageDef damageDef)
        {
            if (Props != null && Props.beamArmorPenetration >= 0f)
            {
                return Props.beamArmorPenetration;
            }

            return damageDef?.defaultArmorPenetration ?? 0f;
        }

        private void TryApplyBeamFire(Thing thing)
        {
            if (thing.CanEverAttachFire())
            {
                float chance = verbProps.flammabilityAttachFireChanceCurve != null
                    ? verbProps.flammabilityAttachFireChanceCurve.Evaluate(thing.GetStatValue(StatDefOf.Flammability, true, -1))
                    : verbProps.beamChanceToAttachFire;
                if (Rand.Chance(chance))
                {
                    thing.TryAttachFire(verbProps.beamFireSizeRange.RandomInRange, caster);
                }
            }
            else if (thing.Position.InBounds(caster.Map) && Rand.Chance(verbProps.beamChanceToStartFire))
            {
                FireUtility.TryStartFireIn(thing.Position, caster.Map, verbProps.beamFireSizeRange.RandomInRange, caster, verbProps.flammabilityAttachFireChanceCurve);
            }
        }

        private void ApplyMiningDamageToMineable(Mineable mineable, DamageDef damageDef, float damageAmount)
        {
            if (mineable.Destroyed || !mineable.def.useHitPoints || damageDef != null && !damageDef.harmsHealth)
            {
                return;
            }

            int damage = Mathf.Min(mineable.HitPoints, GenMath.RoundRandom(GetAdjustedBuildingDamage(damageDef, damageAmount, mineable)));
            if (damage <= 0)
            {
                return;
            }

            Pawn miner = caster as Pawn;
            mineable.Notify_TookMiningDamage(damage, miner);
            if (damage >= mineable.HitPoints)
            {
                mineable.DestroyMined(miner);
            }
            else
            {
                mineable.HitPoints -= damage;
            }
        }

        private float GetAdjustedBuildingDamage(DamageDef damageDef, float damageAmount, Building building)
        {
            if (damageDef == null)
            {
                return damageAmount;
            }

            float adjustedDamage = damageAmount * damageDef.buildingDamageFactor;
            adjustedDamage *= building.def.passability == Traversability.Impassable ? damageDef.buildingDamageFactorImpassable : damageDef.buildingDamageFactorPassable;
            if (damageDef.scaleDamageToBuildingsBasedOnFlammability)
            {
                adjustedDamage *= Mathf.Max(0.05f, building.GetStatValue(StatDefOf.Flammability, true, -1));
            }

            if (caster is Pawn pawn && pawn.IsShambler)
            {
                adjustedDamage *= 1.5f;
            }

            return adjustedDamage;
        }

        private void NotifyEquipmentShotConsumed()
        {
            if (EquipmentSource == null)
            {
                return;
            }

            EquipmentSource.GetComp<CompChangeableProjectile>()?.Notify_ProjectileLaunched();
            EquipmentSource.GetComp<CompApparelReloadable>()?.UsedOnce();
        }

        private Vector3 GetVisualBeamPosition()
        {
            Vector3 beamPosition = InterpolatedPosition;
            if (Props != null && Props.penetrateObstacles)
            {
                return beamPosition;
            }

            IntVec3 beamCell = beamPosition.ToIntVec3();
            IntVec3 blockedCell = IntVec3.Invalid;
            foreach (IntVec3 cell in CellsOnLine(caster.Position, beamCell))
            {
                if (!cell.InBounds(caster.Map))
                {
                    break;
                }

                if (!cell.CanBeSeenOverFast(caster.Map))
                {
                    break;
                }

                blockedCell = cell;
                if (cell == beamCell)
                {
                    break;
                }
            }

            return blockedCell.IsValid ? blockedCell.ToVector3Shifted() : beamPosition;
        }

        private IntVec3 GetVisualTargetCell(Vector3 beamPosition, out Vector3 targetOffset)
        {
            IntVec3 beamCell = beamPosition.Yto0().ToIntVec3();
            if (beamCell.InBounds(caster.Map))
            {
                targetOffset = beamPosition - beamCell.ToVector3Shifted();
                return beamCell;
            }

            IntVec3 boundedCell = LastInBoundsCellOnLine(caster.Position, beamCell);
            if (!boundedCell.IsValid)
            {
                boundedCell = caster.Position;
            }

            targetOffset = beamPosition - boundedCell.ToVector3Shifted();
            return boundedCell;
        }

        private void CreateExtraBeamMotes(IntVec3 visualCell, Vector3 startOffset, Vector3 targetOffset)
        {
            DestroyExtraBeamMotes();
            if (Props?.extraBeamMoteDefs.NullOrEmpty() ?? true)
            {
                return;
            }

            foreach (ThingDef moteDef in Props.extraBeamMoteDefs)
            {
                if (moteDef == null)
                {
                    continue;
                }

                MoteDualAttached extraMote = MoteMaker.MakeInteractionOverlay(moteDef, caster, new TargetInfo(visualCell, caster.Map, false));
                extraMote.UpdateTargets(new TargetInfo(caster.Position, caster.Map, false), new TargetInfo(visualCell, caster.Map, false), startOffset, targetOffset);
                extraMotes.Add(extraMote);
            }
        }

        private void MaintainExtraBeamMotes(IntVec3 visualCell, Vector3 startOffset, Vector3 targetOffset)
        {
            if (extraMotes.Count == 0)
            {
                return;
            }

            TargetInfo source = new TargetInfo(caster.Position, caster.Map, false);
            TargetInfo target = new TargetInfo(visualCell, caster.Map, false);
            for (int i = extraMotes.Count - 1; i >= 0; i--)
            {
                MoteDualAttached extraMote = extraMotes[i];
                if (extraMote == null || extraMote.Destroyed)
                {
                    extraMotes.RemoveAt(i);
                    continue;
                }

                extraMote.UpdateTargets(source, target, startOffset, targetOffset);
                extraMote.Maintain();
            }
        }

        private void DestroyExtraBeamMotes()
        {
            for (int i = 0; i < extraMotes.Count; i++)
            {
                MoteDualAttached extraMote = extraMotes[i];
                if (extraMote != null && !extraMote.Destroyed)
                {
                    extraMote.Destroy();
                }
            }

            extraMotes.Clear();
        }

        private void MaintainEndEffecter(IntVec3 visualCell, Vector3 targetOffset)
        {
            if (!visualCell.InBounds(caster.Map))
            {
                endEffecter?.Cleanup();
                endEffecter = null;
                return;
            }

            if (endEffecter == null && verbProps.beamEndEffecterDef != null)
            {
                endEffecter = verbProps.beamEndEffecterDef.Spawn(visualCell, caster.Map, targetOffset, 1f);
            }

            if (endEffecter != null)
            {
                endEffecter.offset = targetOffset;
                endEffecter.EffectTick(new TargetInfo(visualCell, caster.Map, false), TargetInfo.Invalid);
                endEffecter.ticksLeft--;
            }
        }

        private void ThrowLineFlecks(Vector3 direction, float beamLength)
        {
            if (verbProps.beamLineFleckDef == null || beamLength <= 0f)
            {
                return;
            }

            float fleckCount = NumSubdivisionsPerUnitLength * beamLength;
            for (int i = 0; i < fleckCount; i++)
            {
                float chance = verbProps.beamLineFleckChanceCurve != null ? verbProps.beamLineFleckChanceCurve.Evaluate(i / fleckCount) : 1f;
                if (Rand.Chance(chance))
                {
                    Vector3 offset = i * direction - direction * Rand.Value + direction / 2f;
                    Vector3 position = caster.Position.ToVector3Shifted() + offset;
                    if (position.ToIntVec3().InBounds(caster.Map))
                    {
                        FleckMaker.Static(position, caster.Map, verbProps.beamLineFleckDef, 1f);
                    }
                }
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref path, "sraBeamPath", LookMode.Value);
            Scribe_Values.Look(ref ticksToNextPathStep, "sraTicksToNextPathStep", 0);
            Scribe_Values.Look(ref beamBurstTick, "sraBeamBurstTick", 0);
            Scribe_Values.Look(ref initialTargetPosition, "sraInitialTargetPosition");
            Scribe_Values.Look(ref lockedBurstTargetPosition, "sraLockedBurstTargetPosition");
            Scribe_Values.Look(ref lockedBurstTargetCell, "sraLockedBurstTargetCell", IntVec3.Invalid);
            Scribe_Values.Look(ref hasLockedBurstTarget, "sraHasLockedBurstTarget", false);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && path == null)
            {
                path = new List<Vector3>();
            }
        }

        public override void Reset()
        {
            base.Reset();
            if (mote != null && !mote.Destroyed)
            {
                mote.Destroy();
            }

            mote = null;
            DestroyExtraBeamMotes();
            endEffecter?.Cleanup();
            endEffecter = null;
            EndSustainer();
            ClearLockedBurstTarget();
            sortedCustomTrajectory.Clear();
            damagedThingsThisShot.Clear();
        }

        private bool CasterCanMaintainBeam()
        {
            return caster != null && !caster.Destroyed && caster.Spawned && caster.Map != null;
        }

        private void EndSustainer()
        {
            if (sustainer != null && !sustainer.Ended)
            {
                sustainer.End();
            }

            sustainer = null;
        }
    }
}
