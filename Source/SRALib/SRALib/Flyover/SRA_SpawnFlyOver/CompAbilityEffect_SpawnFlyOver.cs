using RimWorld;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace SRA
{
    public class CompAbilityEffect_SpawnFlyOver : CompAbilityEffect
    {
        public new CompProperties_AbilitySpawnFlyOver Props => (CompProperties_AbilitySpawnFlyOver)props;
        
        public override bool CanApplyOn(LocalTargetInfo target, LocalTargetInfo dest)
        {
            if (!base.CanApplyOn(target, dest))
                return false;
            return true;
        }

        public override void Apply(LocalTargetInfo target, LocalTargetInfo dest)
        {
            base.Apply(target, dest);
            if (parent.pawn == null || parent.pawn.Map == null)
                return;
            
            try
            {
                // 计算起始和结束位置
                IntVec3 startPos, endPos;
                if (Props.approachType == ApproachType.Perpendicular)
                {
                    CalculatePerpendicularPath(target, out startPos, out endPos);
                }
                else
                {
                    startPos = CalculateStartPosition(target);
                    endPos = CalculateEndPosition(target, startPos);
                }

                // 确保位置安全
                startPos = GetSafeMapPosition(startPos, parent.pawn.Map);
                endPos = GetSafeMapPosition(endPos, parent.pawn.Map);

                // 验证并优化飞行路径
                if (!ValidateAndOptimizePath(ref startPos, ref endPos, target.Cell))
                {
                    CalculateFallbackPath(target, out startPos, out endPos);
                }

                // 根据类型创建不同的飞越物体
                switch (Props.flyOverType)
                {
                    case FlyOverType.Standard:
                    default:
                        CreateStandardFlyOver(startPos, endPos);
                        break;
                    case FlyOverType.GroundStrafing:
                        CreateGroundStrafingFlyOver(startPos, endPos, target.Cell);
                        break;
                    case FlyOverType.SectorSurveillance:
                        CreateSectorSurveillanceFlyOver(startPos, endPos);
                        break;
                }
            }
            catch (System.Exception ex)
            {
                Log.Error($"Error spawning fly over: {ex}");
            }
        }

        public override string ExtraLabelMouseAttachment(LocalTargetInfo target)
        {
            string baseInfo = "";
            if (Props.enableGroundStrafing)
            {
                baseInfo = $"扫射区域: {Props.strafeWidth * 2 + 1}格宽度";
            }
            else if (Props.enableSectorSurveillance)
            {
                baseInfo = $"扇形监视: 约{Props.strafeWidth * 2 + 1}格宽度\n(具体参数在飞行物定义中)";
            }
            return baseInfo;
        }

        public override bool Valid(LocalTargetInfo target, bool throwMessages = false)
        {
            if (!base.Valid(target, throwMessages))
                return false;
            if (parent.pawn == null || parent.pawn.Map == null)
                return false;
            if (!target.Cell.IsValid || !target.Cell.InBounds(parent.pawn.Map))
                return false;
            return true;
        }

        private void CreateStandardFlyOver(IntVec3 startPos, IntVec3 endPos)
        {
            ThingDef flyOverDef = Props.flyOverDef ?? DefDatabase<ThingDef>.GetNamedSilentFail("ARA_HiveShip");
            if (flyOverDef == null)
            {
                Log.Warning("No fly over def specified for standard fly over");
                return;
            }

            FlyOver flyOver = FlyOver.MakeFlyOver(
                flyOverDef,
                startPos,
                endPos,
                parent.pawn.Map,
                Props.flightSpeed,
                Props.altitude
            );
            flyOver.spawnContentsOnImpact = Props.dropContentsOnImpact;
            flyOver.playFlyOverSound = Props.playFlyOverSound;
        }

        private void CreateGroundStrafingFlyOver(IntVec3 startPos, IntVec3 endPos, IntVec3 targetCell)
        {
            ThingDef flyOverDef = Props.flyOverDef ?? DefDatabase<ThingDef>.GetNamedSilentFail("ARA_HiveCorvette");
            if (flyOverDef == null)
            {
                Log.Warning("No fly over def specified for ground strafing fly over");
                return;
            }

            FlyOver flyOver = FlyOver.MakeFlyOver(
                flyOverDef,
                startPos,
                endPos,
                parent.pawn.Map,
                Props.flightSpeed,
                Props.altitude,
                casterPawn: parent.pawn
            );

            flyOver.spawnContentsOnImpact = Props.dropContentsOnImpact;
            flyOver.playFlyOverSound = Props.playFlyOverSound;

            CompGroundStrafing strafingComp = flyOver.GetComp<CompGroundStrafing>();
            if (strafingComp != null)
            {
                Vector3 flightDirection = (endPos.ToVector3() - startPos.ToVector3()).normalized;
                List<IntVec3> potentialTargetCells = CalculateStrafingImpactCells(targetCell, flightDirection);
                if (potentialTargetCells.Count > 0)
                {
                    List<IntVec3> confirmedTargetCells = PreprocessStrafingTargets(
                        potentialTargetCells,
                        Props.strafeFireChance
                    );
                    if (confirmedTargetCells.Count > 0)
                    {
                        strafingComp.SetConfirmedTargets(confirmedTargetCells);
                    }
                }
            }
        }

        private void CreateSectorSurveillanceFlyOver(IntVec3 startPos, IntVec3 endPos)
        {
            ThingDef flyOverDef = Props.flyOverDef ?? DefDatabase<ThingDef>.GetNamedSilentFail("ARA_HiveCorvette");
            if (flyOverDef == null)
            {
                Log.Warning("No fly over def specified for sector surveillance fly over");
                return;
            }

            FlyOver flyOver = FlyOver.MakeFlyOver(
                flyOverDef,
                startPos,
                endPos,
                parent.pawn.Map,
                Props.flightSpeed,
                Props.altitude,
                casterPawn: parent.pawn
            );

            flyOver.spawnContentsOnImpact = Props.dropContentsOnImpact;
            flyOver.playFlyOverSound = Props.playFlyOverSound;
        }

        private bool ValidateAndOptimizePath(ref IntVec3 startPos, ref IntVec3 endPos, IntVec3 targetCell)
        {
            Map map = parent.pawn.Map;

            if (!startPos.InBounds(map) || !endPos.InBounds(map))
                return false;

            if (startPos == endPos)
                return false;

            float distance = Vector3.Distance(startPos.ToVector3(), endPos.ToVector3());
            if (distance < 10f)
                return false;

            if (!IsPathNearTarget(startPos, endPos, targetCell))
            {
                OptimizePathForTarget(ref startPos, ref endPos, targetCell);
            }

            return true;
        }

        private bool IsPathNearTarget(IntVec3 startPos, IntVec3 endPos, IntVec3 targetCell)
        {
            Vector3 start = startPos.ToVector3();
            Vector3 end = endPos.ToVector3();
            Vector3 target = targetCell.ToVector3();

            Vector3 lineDirection = (end - start).normalized;
            Vector3 pointToLine = target - start;
            Vector3 projection = Vector3.Project(pointToLine, lineDirection);
            Vector3 perpendicular = pointToLine - projection;

            float distanceToLine = perpendicular.magnitude;
            float projectionLength = projection.magnitude;
            float lineLength = (end - start).magnitude;
            bool isWithinSegment = projectionLength >= 0 && projectionLength <= lineLength;

            return distanceToLine <= 15f && isWithinSegment;
        }

        private void OptimizePathForTarget(ref IntVec3 startPos, ref IntVec3 endPos, IntVec3 targetCell)
        {
            Map map = parent.pawn.Map;
            Vector3 target = targetCell.ToVector3();

            Vector3[] directions = {
                new Vector3(1, 0, 0),
                new Vector3(-1, 0, 0),
                new Vector3(0, 0, 1),
                new Vector3(0, 0, -1)
            };

            Vector3 bestStartDir = Vector3.zero;
            Vector3 bestEndDir = Vector3.zero;
            float bestScore = float.MinValue;

            for (int i = 0; i < directions.Length; i++)
            {
                for (int j = i + 1; j < directions.Length; j++)
                {
                    Vector3 dir1 = directions[i];
                    Vector3 dir2 = directions[j];

                    float dot = Vector3.Dot(dir1, dir2);
                    if (dot < -0.5f)
                    {
                        IntVec3 testStart = FindMapEdgeInDirection(map, targetCell, dir1);
                        IntVec3 testEnd = FindMapEdgeInDirection(map, targetCell, dir2);

                        if (testStart.InBounds(map) && testEnd.InBounds(map))
                        {
                            float pathLength = Vector3.Distance(testStart.ToVector3(), testEnd.ToVector3());
                            bool passesNearTarget = IsPathNearTarget(testStart, testEnd, targetCell);
                            float score = pathLength + (passesNearTarget ? 50f : 0f);

                            if (score > bestScore)
                            {
                                bestScore = score;
                                bestStartDir = dir1;
                                bestEndDir = dir2;
                            }
                        }
                    }
                }
            }

            if (bestStartDir != Vector3.zero && bestEndDir != Vector3.zero)
            {
                startPos = FindMapEdgeInDirection(map, targetCell, bestStartDir);
                endPos = FindMapEdgeInDirection(map, targetCell, bestEndDir);
            }
        }

        private void CalculateFallbackPath(LocalTargetInfo target, out IntVec3 startPos, out IntVec3 endPos)
        {
            Map map = parent.pawn.Map;
            IntVec3 targetPos = target.Cell;

            if (Rand.Value < 0.5f)
            {
                startPos = new IntVec3(0, 0, 0);
                endPos = new IntVec3(map.Size.x - 1, 0, map.Size.z - 1);
            }
            else
            {
                startPos = new IntVec3(map.Size.x - 1, 0, 0);
                endPos = new IntVec3(0, 0, map.Size.z - 1);
            }

            startPos = GetSafeMapPosition(startPos, map);
            endPos = GetSafeMapPosition(endPos, map);
        }

        private void CalculatePerpendicularPath(LocalTargetInfo target, out IntVec3 startPos, out IntVec3 endPos)
        {
            Map map = parent.pawn.Map;
            IntVec3 casterPos = parent.pawn.Position;
            IntVec3 targetPos = target.Cell;

            Vector3 directionToTarget = (targetPos.ToVector3() - casterPos.ToVector3()).normalized;
            if (directionToTarget == Vector3.zero)
            {
                directionToTarget = new Vector3(Rand.Range(-1f, 1f), 0, Rand.Range(-1f, 1f)).normalized;
            }

            Vector3 perpendicularDirection = new Vector3(-directionToTarget.z, 0, directionToTarget.x).normalized;

            IntVec3 edge1 = FindMapEdgeInDirection(map, targetPos, perpendicularDirection);
            IntVec3 edge2 = FindMapEdgeInDirection(map, targetPos, -perpendicularDirection);

            Vector3 edge1Vec = edge1.ToVector3();
            Vector3 edge2Vec = edge2.ToVector3();
            Vector3 targetVec = targetPos.ToVector3();

            Vector3 toEdge1 = (edge1Vec - targetVec).normalized;
            Vector3 toEdge2 = (edge2Vec - targetVec).normalized;
            float dot = Vector3.Dot(toEdge1, toEdge2);

            if (dot > -0.3f)
            {
                if (perpendicularDirection.x != 0)
                {
                    perpendicularDirection = new Vector3(0, 0, 1f);
                }
                else
                {
                    perpendicularDirection = new Vector3(1f, 0, 0);
                }

                edge1 = FindMapEdgeInDirection(map, targetPos, perpendicularDirection);
                edge2 = FindMapEdgeInDirection(map, targetPos, -perpendicularDirection);
            }

            if (Rand.Value < 0.5f)
            {
                startPos = edge1;
                endPos = edge2;
            }
            else
            {
                startPos = edge2;
                endPos = edge1;
            }
        }

        private IntVec3 FindMapEdgeInDirection(Map map, IntVec3 fromPos, Vector3 direction)
        {
            if (direction == Vector3.zero)
            {
                direction = new Vector3(Rand.Range(-1f, 1f), 0, Rand.Range(-1f, 1f)).normalized;
            }

            Vector3 fromVec = fromPos.ToVector3();
            Vector3 dirNormalized = direction.normalized;

            float minT = float.MaxValue;
            IntVec3? bestEdgePos = null;

            for (int i = 0; i < 4; i++)
            {
                float t = 0f;
                IntVec3 edgePos = IntVec3.Invalid;

                switch (i)
                {
                    case 0:
                        if (Mathf.Abs(dirNormalized.x) > 0.001f)
                        {
                            t = (0 - fromVec.x) / dirNormalized.x;
                            if (t > 0)
                            {
                                float z = fromVec.z + dirNormalized.z * t;
                                if (z >= 0 && z < map.Size.z)
                                {
                                    edgePos = new IntVec3(0, 0, Mathf.RoundToInt(z));
                                }
                            }
                        }
                        break;

                    case 1:
                        if (Mathf.Abs(dirNormalized.x) > 0.001f)
                        {
                            t = (map.Size.x - 1 - fromVec.x) / dirNormalized.x;
                            if (t > 0)
                            {
                                float z = fromVec.z + dirNormalized.z * t;
                                if (z >= 0 && z < map.Size.z)
                                {
                                    edgePos = new IntVec3(map.Size.x - 1, 0, Mathf.RoundToInt(z));
                                }
                            }
                        }
                        break;

                    case 2:
                        if (Mathf.Abs(dirNormalized.z) > 0.001f)
                        {
                            t = (0 - fromVec.z) / dirNormalized.z;
                            if (t > 0)
                            {
                                float x = fromVec.x + dirNormalized.x * t;
                                if (x >= 0 && x < map.Size.x)
                                {
                                    edgePos = new IntVec3(Mathf.RoundToInt(x), 0, 0);
                                }
                            }
                        }
                        break;

                    case 3:
                        if (Mathf.Abs(dirNormalized.z) > 0.001f)
                        {
                            t = (map.Size.z - 1 - fromVec.z) / dirNormalized.z;
                            if (t > 0)
                            {
                                float x = fromVec.x + dirNormalized.x * t;
                                if (x >= 0 && x < map.Size.x)
                                {
                                    edgePos = new IntVec3(Mathf.RoundToInt(x), 0, map.Size.z - 1);
                                }
                            }
                        }
                        break;
                }

                if (edgePos.IsValid && edgePos.InBounds(map) && t > 0 && t < minT)
                {
                    minT = t;
                    bestEdgePos = edgePos;
                }
            }

            if (bestEdgePos.HasValue)
            {
                return bestEdgePos.Value;
            }

            return GetRandomMapEdgePosition(map);
        }

        private IntVec3 GetRandomMapEdgePosition(Map map)
        {
            int edge = Rand.Range(0, 4);
            int x, z;

            switch (edge)
            {
                case 0:
                    x = Rand.Range(5, map.Size.x - 5);
                    z = 0;
                    break;
                case 1:
                    x = map.Size.x - 1;
                    z = Rand.Range(5, map.Size.z - 5);
                    break;
                case 2:
                    x = Rand.Range(5, map.Size.x - 5);
                    z = map.Size.z - 1;
                    break;
                case 3:
                default:
                    x = 0;
                    z = Rand.Range(5, map.Size.z - 5);
                    break;
            }

            x = Mathf.Clamp(x, 0, map.Size.x - 1);
            z = Mathf.Clamp(z, 0, map.Size.z - 1);

            return new IntVec3(x, 0, z);
        }

        public override void DrawEffectPreview(LocalTargetInfo target)
        {
            base.DrawEffectPreview(target);

            if (parent.pawn != null && parent.pawn.Map != null)
            {
                Map map = parent.pawn.Map;

                try
                {
                    IntVec3 startPos, endPos;
                    if (Props.approachType == ApproachType.Perpendicular)
                    {
                        CalculatePerpendicularPath(target, out startPos, out endPos);
                    }
                    else
                    {
                        startPos = CalculateStartPosition(target);
                        endPos = CalculateEndPosition(target, startPos);
                    }

                    startPos = GetSafeMapPosition(startPos, map);
                    endPos = GetSafeMapPosition(endPos, map);

                    if (!IsPreviewStable(startPos, endPos, map))
                    {
                        return;
                    }

                    if (Props.enableGroundStrafing && Props.showStrafePreview)
                    {
                        DrawStrafingAreaPreview(startPos, endPos, target.Cell);
                    }
                    else if (Props.enableSectorSurveillance && Props.showSectorPreview)
                    {
                        DrawSectorAreaPreview(startPos, endPos);
                    }
                }
                catch (System.Exception)
                {
                    // 忽略预览绘制中的错误
                }
            }
        }

        private IntVec3 GetSafeMapPosition(IntVec3 pos, Map map)
        {
            if (map == null) return pos;

            pos.x = Mathf.Clamp(pos.x, 0, map.Size.x - 1);
            pos.z = Mathf.Clamp(pos.z, 0, map.Size.z - 1);

            return pos;
        }

        private bool IsPreviewStable(IntVec3 startPos, IntVec3 endPos, Map map)
        {
            if (map == null) return false;
            if (!startPos.IsValid || !endPos.IsValid) return false;
            if (!startPos.InBounds(map) || !endPos.InBounds(map)) return false;

            float distance = Vector3.Distance(startPos.ToVector3(), endPos.ToVector3());
            if (distance < 5f) return false;

            return true;
        }

        private void DrawStrafingAreaPreview(IntVec3 startPos, IntVec3 endPos, IntVec3 targetCell)
        {
            Map map = parent.pawn.Map;

            Vector3 flightDirection = (endPos.ToVector3() - startPos.ToVector3()).normalized;
            if (flightDirection == Vector3.zero)
            {
                flightDirection = Vector3.forward;
            }

            List<IntVec3> strafeImpactCells = CalculateStrafingImpactCells(targetCell, flightDirection);

            foreach (IntVec3 cell in strafeImpactCells)
            {
                if (cell.InBounds(map))
                {
                    GenDraw.DrawFieldEdges(new List<IntVec3> { cell }, Props.strafePreviewColor, 0.5f);
                }
            }

            GenDraw.DrawLineBetween(startPos.ToVector3Shifted(), endPos.ToVector3Shifted(), SimpleColor.Red, 0.2f);
            DrawStrafingBoundaries(targetCell, flightDirection);
        }

        private List<IntVec3> CalculateStrafingImpactCells(IntVec3 targetCell, Vector3 flightDirection)
        {
            List<IntVec3> cells = new List<IntVec3>();
            Map map = parent.pawn.Map;

            Vector3 perpendicular = new Vector3(-flightDirection.z, 0f, flightDirection.x).normalized;
            Vector3 targetCenter = targetCell.ToVector3();

            float strafeHalfLength = Props.strafeLength * 0.5f;
            Vector3 strafeStart = targetCenter - flightDirection * strafeHalfLength;
            Vector3 strafeEnd = targetCenter + flightDirection * strafeHalfLength;

            int steps = Mathf.Max(1, Mathf.CeilToInt(Props.strafeLength));
            for (int i = 0; i <= steps; i++)
            {
                float progress = (float)i / steps;
                Vector3 centerPoint = Vector3.Lerp(strafeStart, strafeEnd, progress);

                for (int w = -Props.strafeWidth; w <= Props.strafeWidth; w++)
                {
                    Vector3 offset = perpendicular * w;
                    Vector3 cellPos = centerPoint + offset;

                    IntVec3 cell = new IntVec3(
                        Mathf.RoundToInt(cellPos.x),
                        Mathf.RoundToInt(cellPos.y),
                        Mathf.RoundToInt(cellPos.z)
                    );

                    if (cell.InBounds(map) && !cells.Contains(cell))
                    {
                        cells.Add(cell);
                    }
                }
            }

            return cells;
        }

        private void DrawStrafingBoundaries(IntVec3 targetCell, Vector3 flightDirection)
        {
            Map map = parent.pawn.Map;
            Vector3 perpendicular = new Vector3(-flightDirection.z, 0f, flightDirection.x).normalized;

            Vector3 targetCenter = targetCell.ToVector3();
            float strafeHalfLength = Props.strafeLength * 0.5f;
            Vector3 strafeStart = targetCenter - flightDirection * strafeHalfLength;
            Vector3 strafeEnd = targetCenter + flightDirection * strafeHalfLength;

            Vector3 startLeft = strafeStart + perpendicular * Props.strafeWidth;
            Vector3 startRight = strafeStart - perpendicular * Props.strafeWidth;
            Vector3 endLeft = strafeEnd + perpendicular * Props.strafeWidth;
            Vector3 endRight = strafeEnd - perpendicular * Props.strafeWidth;

            IntVec3 startLeftCell = GetSafeMapPosition(new IntVec3((int)startLeft.x, (int)startLeft.y, (int)startLeft.z), map);
            IntVec3 startRightCell = GetSafeMapPosition(new IntVec3((int)startRight.x, (int)startRight.y, (int)startRight.z), map);
            IntVec3 endLeftCell = GetSafeMapPosition(new IntVec3((int)endLeft.x, (int)endLeft.y, (int)endLeft.z), map);
            IntVec3 endRightCell = GetSafeMapPosition(new IntVec3((int)endRight.x, (int)endRight.y, (int)endRight.z), map);

            if (startLeftCell.InBounds(map) && endLeftCell.InBounds(map))
                GenDraw.DrawLineBetween(startLeftCell.ToVector3Shifted(), endLeftCell.ToVector3Shifted(), SimpleColor.Red, 0.2f);

            if (startRightCell.InBounds(map) && endRightCell.InBounds(map))
                GenDraw.DrawLineBetween(startRightCell.ToVector3Shifted(), endRightCell.ToVector3Shifted(), SimpleColor.Red, 0.2f);

            if (startLeftCell.InBounds(map) && startRightCell.InBounds(map))
                GenDraw.DrawLineBetween(startLeftCell.ToVector3Shifted(), startRightCell.ToVector3Shifted(), SimpleColor.Red, 0.2f);

            if (endLeftCell.InBounds(map) && endRightCell.InBounds(map))
                GenDraw.DrawLineBetween(endLeftCell.ToVector3Shifted(), endRightCell.ToVector3Shifted(), SimpleColor.Red, 0.2f);
        }

        private void DrawSectorAreaPreview(IntVec3 startPos, IntVec3 endPos)
        {
            Map map = parent.pawn.Map;

            Vector3 flightDirection = (endPos.ToVector3() - startPos.ToVector3()).normalized;
            if (flightDirection == Vector3.zero)
            {
                flightDirection = Vector3.forward;
            }

            Vector3 perpendicular = new Vector3(-flightDirection.z, 0f, flightDirection.x).normalized;
            List<IntVec3> previewCells = CalculateRectangularPreviewArea(startPos, endPos, flightDirection, perpendicular);

            foreach (IntVec3 cell in previewCells)
            {
                if (cell.InBounds(map))
                {
                    GenDraw.DrawFieldEdges(new List<IntVec3> { cell }, Props.sectorPreviewColor, 0.3f);
                }
            }

            GenDraw.DrawLineBetween(startPos.ToVector3Shifted(), endPos.ToVector3Shifted(), SimpleColor.Blue, 0.2f);
            DrawRectangularPreviewBoundaries(startPos, endPos, flightDirection, perpendicular);
        }

        private List<IntVec3> CalculateRectangularPreviewArea(IntVec3 startPos, IntVec3 endPos, Vector3 flightDirection, Vector3 perpendicular)
        {
            List<IntVec3> cells = new List<IntVec3>();
            Map map = parent.pawn.Map;

            float totalPathLength = Vector3.Distance(startPos.ToVector3(), endPos.ToVector3());
            int steps = Mathf.Max(1, Mathf.CeilToInt(totalPathLength));
            
            for (int i = 0; i <= steps; i++)
            {
                float progress = (float)i / steps;
                Vector3 centerPoint = Vector3.Lerp(startPos.ToVector3(), endPos.ToVector3(), progress);

                for (int w = -Props.strafeWidth; w <= Props.strafeWidth; w++)
                {
                    Vector3 offset = perpendicular * w;
                    Vector3 cellPos = centerPoint + offset;

                    IntVec3 cell = new IntVec3(
                        Mathf.RoundToInt(cellPos.x),
                        Mathf.RoundToInt(cellPos.y),
                        Mathf.RoundToInt(cellPos.z)
                    );

                    if (cell.InBounds(map) && !cells.Contains(cell))
                    {
                        cells.Add(cell);
                    }
                }
            }

            return cells;
        }

        private void DrawRectangularPreviewBoundaries(IntVec3 startPos, IntVec3 endPos, Vector3 flightDirection, Vector3 perpendicular)
        {
            Map map = parent.pawn.Map;

            Vector3 startLeft = startPos.ToVector3() + perpendicular * Props.strafeWidth;
            Vector3 startRight = startPos.ToVector3() - perpendicular * Props.strafeWidth;
            Vector3 endLeft = endPos.ToVector3() + perpendicular * Props.strafeWidth;
            Vector3 endRight = endPos.ToVector3() - perpendicular * Props.strafeWidth;

            IntVec3 startLeftCell = GetSafeMapPosition(new IntVec3((int)startLeft.x, (int)startLeft.y, (int)startLeft.z), map);
            IntVec3 startRightCell = GetSafeMapPosition(new IntVec3((int)startRight.x, (int)startRight.y, (int)startRight.z), map);
            IntVec3 endLeftCell = GetSafeMapPosition(new IntVec3((int)endLeft.x, (int)endLeft.y, (int)endLeft.z), map);
            IntVec3 endRightCell = GetSafeMapPosition(new IntVec3((int)endRight.x, (int)endRight.y, (int)endRight.z), map);

            if (startLeftCell.InBounds(map) && endLeftCell.InBounds(map))
                GenDraw.DrawLineBetween(startLeftCell.ToVector3Shifted(), endLeftCell.ToVector3Shifted(), SimpleColor.Blue, 0.2f);

            if (startRightCell.InBounds(map) && endRightCell.InBounds(map))
                GenDraw.DrawLineBetween(startRightCell.ToVector3Shifted(), endRightCell.ToVector3Shifted(), SimpleColor.Blue, 0.2f);

            if (startLeftCell.InBounds(map) && startRightCell.InBounds(map))
                GenDraw.DrawLineBetween(startLeftCell.ToVector3Shifted(), startRightCell.ToVector3Shifted(), SimpleColor.Blue, 0.2f);

            if (endLeftCell.InBounds(map) && endRightCell.InBounds(map))
                GenDraw.DrawLineBetween(endLeftCell.ToVector3Shifted(), endRightCell.ToVector3Shifted(), SimpleColor.Blue, 0.2f);
        }

        private List<IntVec3> PreprocessStrafingTargets(List<IntVec3> potentialTargets, float fireChance)
        {
            List<IntVec3> confirmedTargets = new List<IntVec3>();
            List<IntVec3> missedCells = new List<IntVec3>();
            
            foreach (IntVec3 cell in potentialTargets)
            {
                if (Rand.Value <= fireChance)
                {
                    confirmedTargets.Add(cell);
                }
                else
                {
                    missedCells.Add(cell);
                }
            }

            if (Props.maxStrafeProjectiles > -1 && confirmedTargets.Count > Props.maxStrafeProjectiles)
            {
                confirmedTargets = confirmedTargets.InRandomOrder().Take(Props.maxStrafeProjectiles).ToList();
            }

            if (Props.minStrafeProjectiles > -1 && confirmedTargets.Count < Props.minStrafeProjectiles)
            {
                int needed = Props.minStrafeProjectiles - confirmedTargets.Count;
                if (needed > 0 && missedCells.Count > 0)
                {
                    confirmedTargets.AddRange(missedCells.InRandomOrder().Take(Mathf.Min(needed, missedCells.Count)));
                }
            }

            return confirmedTargets;
        }

        private IntVec3 CalculateStartPosition(LocalTargetInfo target)
        {
            Map map = parent.pawn.Map;

            switch (Props.startPosition)
            {
                case StartPosition.Caster:
                    return parent.pawn.Position;
                case StartPosition.MapEdge:
                    return GetMapEdgePosition(map, GetDirectionFromCasterToTarget(target));
                case StartPosition.CustomOffset:
                    return GetSafeMapPosition(parent.pawn.Position + Props.customStartOffset, map);
                case StartPosition.RandomMapEdge:
                    return GetRandomMapEdgePosition(map);
                default:
                    return parent.pawn.Position;
            }
        }

        private IntVec3 CalculateEndPosition(LocalTargetInfo target, IntVec3 startPos)
        {
            Map map = parent.pawn.Map;
            IntVec3 endPos;

            switch (Props.endPosition)
            {
                case EndPosition.TargetCell:
                    endPos = target.Cell;
                    break;
                case EndPosition.OppositeMapEdge:
                    endPos = GetOppositeMapEdgeThroughCenter(map, startPos);
                    break;
                case EndPosition.CustomOffset:
                    endPos = GetSafeMapPosition(target.Cell + Props.customEndOffset, map);
                    break;
                case EndPosition.FixedDistance:
                    endPos = GetFixedDistancePosition(startPos, target.Cell);
                    break;
                case EndPosition.RandomMapEdge:
                    endPos = GetRandomMapEdgePosition(map);
                    break;
                default:
                    endPos = target.Cell;
                    break;
            }

            return GetSafeMapPosition(endPos, map);
        }

        private IntVec3 GetOppositeMapEdgeThroughCenter(Map map, IntVec3 startPos)
        {
            IntVec3 center = map.Center;
            Vector3 toCenter = (center.ToVector3() - startPos.ToVector3()).normalized;
            
            if (toCenter == Vector3.zero)
            {
                toCenter = new Vector3(Rand.Range(-1f, 1f), 0, Rand.Range(-1f, 1f)).normalized;
            }

            Vector3 fromCenter = toCenter;
            return GetMapEdgePositionFromCenter(map, fromCenter);
        }

        private IntVec3 GetMapEdgePositionFromCenter(Map map, Vector3 direction)
        {
            IntVec3 center = map.Center;
            float maxDist = Mathf.Max(map.Size.x, map.Size.z) * 0.6f;

            for (int i = 1; i <= maxDist; i++)
            {
                IntVec3 testPos = center + new IntVec3(
                    Mathf.RoundToInt(direction.x * i),
                    0,
                    Mathf.RoundToInt(direction.z * i));

                if (!testPos.InBounds(map))
                {
                    return FindClosestValidPosition(testPos, map);
                }
            }

            return GetRandomMapEdgePosition(map);
        }

        private IntVec3 GetMapEdgePosition(Map map, Vector3 direction)
        {
            if (direction == Vector3.zero)
            {
                direction = new Vector3(Rand.Range(-1f, 1f), 0, Rand.Range(-1f, 1f)).normalized;
            }

            IntVec3 center = map.Center;
            float maxDist = Mathf.Max(map.Size.x, map.Size.z) * 0.6f;

            for (int i = 1; i <= maxDist; i++)
            {
                IntVec3 testPos = center + new IntVec3(
                    Mathf.RoundToInt(direction.x * i),
                    0,
                    Mathf.RoundToInt(direction.z * i));

                if (!testPos.InBounds(map))
                {
                    return FindClosestValidPosition(testPos, map);
                }
            }

            return GetRandomMapEdgePosition(map);
        }

        private IntVec3 FindClosestValidPosition(IntVec3 invalidPos, Map map)
        {
            for (int radius = 1; radius <= 5; radius++)
            {
                foreach (IntVec3 pos in GenRadial.RadialPatternInRadius(radius))
                {
                    IntVec3 testPos = invalidPos + pos;
                    if (testPos.InBounds(map))
                    {
                        return testPos;
                    }
                }
            }

            return map.Center;
        }

        private IntVec3 GetFixedDistancePosition(IntVec3 startPos, IntVec3 targetPos)
        {
            Vector3 direction = (targetPos.ToVector3() - startPos.ToVector3()).normalized;
            return startPos + new IntVec3(
                (int)(direction.x * Props.flyOverDistance),
                0,
                (int)(direction.z * Props.flyOverDistance));
        }

        private Vector3 GetDirectionFromCasterToTarget(LocalTargetInfo target)
        {
            Vector3 direction = (target.Cell.ToVector3() - parent.pawn.Position.ToVector3()).normalized;

            if (direction == Vector3.zero)
            {
                direction = new Vector3(Rand.Range(-1f, 1f), 0, Rand.Range(-1f, 1f)).normalized;
            }

            return direction;
        }
    }
}
