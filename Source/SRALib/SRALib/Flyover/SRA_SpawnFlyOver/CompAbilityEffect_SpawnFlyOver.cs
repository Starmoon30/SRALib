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

        public override void Apply(LocalTargetInfo target, LocalTargetInfo dest)
        {
            base.Apply(target, dest);
            
            if (parent.pawn == null || parent.pawn.Map == null)
                return;

            try
            {
                Log.Message($"FlyOver skill activated by {parent.pawn.Label} at position {parent.pawn.Position}");
                Log.Message($"Target cell: {target.Cell}, Dest: {dest.Cell}");
                
                // 计算起始和结束位置
                IntVec3 startPos, endPos;
                
                // 根据进场类型选择不同的计算方法
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
                
                Log.Message($"Final positions - Start: {startPos}, End: {endPos}");
                
                // 验证位置是否有效
                if (!startPos.InBounds(parent.pawn.Map))
                {
                    Log.Warning($"Start position {startPos} is out of bounds, adjusting to map center");
                    startPos = parent.pawn.Map.Center;
                }
                
                if (!endPos.InBounds(parent.pawn.Map))
                {
                    Log.Warning($"End position {endPos} is out of bounds, adjusting to map center");
                    endPos = parent.pawn.Map.Center;
                }
                
                // 确保起点和终点不同
                if (startPos == endPos)
                {
                    Log.Warning($"FlyOver start and end positions are the same: {startPos}. Adjusting end position.");
                    IntVec3 randomOffset = new IntVec3(Rand.Range(-10, 11), 0, Rand.Range(-10, 11));
                    endPos += randomOffset;
                    endPos = GetSafeMapPosition(endPos, parent.pawn.Map);
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

        // 修复的预览绘制方法
        public override void DrawEffectPreview(LocalTargetInfo target)
        {
            base.DrawEffectPreview(target);

            if (parent.pawn != null && parent.pawn.Map != null)
            {
                Map map = parent.pawn.Map;
                
                try
                {
                    // 计算飞行路径
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

                    // 确保位置在地图范围内
                    startPos = GetSafeMapPosition(startPos, map);
                    endPos = GetSafeMapPosition(endPos, map);

                    // 检查预览稳定性
                    if (!IsPreviewStable(startPos, endPos, map))
                    {
                        return;
                    }

                    // 根据不同类型显示不同的预览
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
                    // 忽略预览绘制中的错误，避免影响游戏体验
                }
            }
        }

        // 安全的位置计算方法
        private IntVec3 GetSafeMapPosition(IntVec3 pos, Map map)
        {
            if (map == null) return pos;
            
            // 确保位置在地图范围内
            pos.x = Mathf.Clamp(pos.x, 0, map.Size.x - 1);
            pos.z = Mathf.Clamp(pos.z, 0, map.Size.z - 1);
            
            return pos;
        }

        // 预览绘制稳定性检查
        private bool IsPreviewStable(IntVec3 startPos, IntVec3 endPos, Map map)
        {
            if (map == null) return false;
            
            // 检查位置是否有效
            if (!startPos.IsValid || !endPos.IsValid) return false;
            
            // 检查位置是否在地图范围内
            if (!startPos.InBounds(map) || !endPos.InBounds(map)) return false;
            
            // 检查距离是否合理（避免过短的路径）
            float distance = Vector3.Distance(startPos.ToVector3(), endPos.ToVector3());
            if (distance < 5f) return false;
            
            return true;
        }

        // 修复：绘制地面扫射预览，现在接受目标单元格参数
        private void DrawStrafingAreaPreview(IntVec3 startPos, IntVec3 endPos, IntVec3 targetCell)
        {
            Map map = parent.pawn.Map;

            // 计算飞行方向
            Vector3 flightDirection = (endPos.ToVector3() - startPos.ToVector3()).normalized;
            if (flightDirection == Vector3.zero)
            {
                flightDirection = Vector3.forward;
            }

            // 只计算扫射影响区域的单元格
            List<IntVec3> strafeImpactCells = CalculateStrafingImpactCells(targetCell, flightDirection);

            // 绘制扫射影响区域的预览单元格
            foreach (IntVec3 cell in strafeImpactCells)
            {
                if (cell.InBounds(map))
                {
                    GenDraw.DrawFieldEdges(new List<IntVec3> { cell }, Props.strafePreviewColor, 0.5f);
                }
            }

            // 绘制飞行路径线
            GenDraw.DrawLineBetween(startPos.ToVector3Shifted(), endPos.ToVector3Shifted(), SimpleColor.Red, 0.2f);

            // 绘制扫射范围边界
            DrawStrafingBoundaries(targetCell, flightDirection);
        }

        // 修复：计算扫射影响区域的单元格，现在以目标单元格为中心
        private List<IntVec3> CalculateStrafingImpactCells(IntVec3 targetCell, Vector3 flightDirection)
        {
            List<IntVec3> cells = new List<IntVec3>();
            Map map = parent.pawn.Map;
            
            // 计算垂直于飞行方向的方向
            Vector3 perpendicular = new Vector3(-flightDirection.z, 0f, flightDirection.x).normalized;
            
            // 修复：以目标单元格为中心计算扫射区域
            Vector3 targetCenter = targetCell.ToVector3();
            
            // 计算扫射区域的起始和结束位置（基于扫射长度，以目标为中心）
            float strafeHalfLength = Props.strafeLength * 0.5f;
            Vector3 strafeStart = targetCenter - flightDirection * strafeHalfLength;
            Vector3 strafeEnd = targetCenter + flightDirection * strafeHalfLength;
            
            // 使用整数步进避免浮点精度问题
            int steps = Mathf.Max(1, Mathf.CeilToInt(Props.strafeLength));
            for (int i = 0; i <= steps; i++)
            {
                float progress = (float)i / steps;
                Vector3 centerPoint = Vector3.Lerp(strafeStart, strafeEnd, progress);
                
                // 在垂直方向扩展扫射宽度
                for (int w = -Props.strafeWidth; w <= Props.strafeWidth; w++)
                {
                    Vector3 offset = perpendicular * w;
                    Vector3 cellPos = centerPoint + offset;
                    
                    // 使用更精确的单元格转换
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
            
            Log.Message($"Strafing Area: Calculated {cells.Count} impact cells centered at {targetCell}");
            return cells;
        }

        // 修复：绘制扫射范围边界，现在以目标单元格为中心
        private void DrawStrafingBoundaries(IntVec3 targetCell, Vector3 flightDirection)
        {
            Map map = parent.pawn.Map;
            Vector3 perpendicular = new Vector3(-flightDirection.z, 0f, flightDirection.x).normalized;
            
            // 修复：以目标单元格为中心
            Vector3 targetCenter = targetCell.ToVector3();
            
            // 计算扫射区域的起始和结束位置
            float strafeHalfLength = Props.strafeLength * 0.5f;
            Vector3 strafeStart = targetCenter - flightDirection * strafeHalfLength;
            Vector3 strafeEnd = targetCenter + flightDirection * strafeHalfLength;
            
            // 计算扫射区域的四个角
            Vector3 startLeft = strafeStart + perpendicular * Props.strafeWidth;
            Vector3 startRight = strafeStart - perpendicular * Props.strafeWidth;
            Vector3 endLeft = strafeEnd + perpendicular * Props.strafeWidth;
            Vector3 endRight = strafeEnd - perpendicular * Props.strafeWidth;
            
            // 转换为 IntVec3 并确保在地图范围内
            IntVec3 startLeftCell = GetSafeMapPosition(new IntVec3((int)startLeft.x, (int)startLeft.y, (int)startLeft.z), map);
            IntVec3 startRightCell = GetSafeMapPosition(new IntVec3((int)startRight.x, (int)startRight.y, (int)startRight.z), map);
            IntVec3 endLeftCell = GetSafeMapPosition(new IntVec3((int)endLeft.x, (int)endLeft.y, (int)endLeft.z), map);
            IntVec3 endRightCell = GetSafeMapPosition(new IntVec3((int)endRight.x, (int)endRight.y, (int)endRight.z), map);
            
            // 绘制边界线 - 只绘制在地图范围内的线段
            if (startLeftCell.InBounds(map) && endLeftCell.InBounds(map))
                GenDraw.DrawLineBetween(startLeftCell.ToVector3Shifted(), endLeftCell.ToVector3Shifted(), SimpleColor.Red, 0.2f);
            
            if (startRightCell.InBounds(map) && endRightCell.InBounds(map))
                GenDraw.DrawLineBetween(startRightCell.ToVector3Shifted(), endRightCell.ToVector3Shifted(), SimpleColor.Red, 0.2f);
            
            if (startLeftCell.InBounds(map) && startRightCell.InBounds(map))
                GenDraw.DrawLineBetween(startLeftCell.ToVector3Shifted(), startRightCell.ToVector3Shifted(), SimpleColor.Red, 0.2f);
            
            if (endLeftCell.InBounds(map) && endRightCell.InBounds(map))
                GenDraw.DrawLineBetween(endLeftCell.ToVector3Shifted(), endRightCell.ToVector3Shifted(), SimpleColor.Red, 0.2f);
        }

        // 绘制扇形区域预览
        private void DrawSectorAreaPreview(IntVec3 startPos, IntVec3 endPos)
        {
            Map map = parent.pawn.Map;
            
            // 计算飞行方向
            Vector3 flightDirection = (endPos.ToVector3() - startPos.ToVector3()).normalized;
            if (flightDirection == Vector3.zero)
            {
                flightDirection = Vector3.forward;
            }

            // 计算垂直于飞行方向的方向
            Vector3 perpendicular = new Vector3(-flightDirection.z, 0f, flightDirection.x).normalized;

            // 使用strafeWidth来近似扇形扫过的区域宽度
            List<IntVec3> previewCells = CalculateRectangularPreviewArea(startPos, endPos, flightDirection, perpendicular);
            
            // 绘制预览区域
            foreach (IntVec3 cell in previewCells)
            {
                if (cell.InBounds(map))
                {
                    GenDraw.DrawFieldEdges(new List<IntVec3> { cell }, Props.sectorPreviewColor, 0.3f);
                }
            }

            // 绘制飞行路径线
            GenDraw.DrawLineBetween(startPos.ToVector3Shifted(), endPos.ToVector3Shifted(), SimpleColor.Blue, 0.2f);
            
            // 绘制预览区域边界
            DrawRectangularPreviewBoundaries(startPos, endPos, flightDirection, perpendicular);
        }

        // 计算矩形预览区域（近似扇形扫过的区域）
        private List<IntVec3> CalculateRectangularPreviewArea(IntVec3 startPos, IntVec3 endPos, Vector3 flightDirection, Vector3 perpendicular)
        {
            List<IntVec3> cells = new List<IntVec3>();
            Map map = parent.pawn.Map;
            
            // 计算飞行路径的总长度
            float totalPathLength = Vector3.Distance(startPos.ToVector3(), endPos.ToVector3());
            
            // 沿着飞行路径计算预览单元格
            int steps = Mathf.Max(1, Mathf.CeilToInt(totalPathLength));
            for (int i = 0; i <= steps; i++)
            {
                float progress = (float)i / steps;
                Vector3 centerPoint = Vector3.Lerp(startPos.ToVector3(), endPos.ToVector3(), progress);
                
                // 在垂直方向扩展预览宽度（使用strafeWidth）
                for (int w = -Props.strafeWidth; w <= Props.strafeWidth; w++)
                {
                    Vector3 offset = perpendicular * w;
                    Vector3 cellPos = centerPoint + offset;
                    
                    // 使用精确的单元格转换
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

        // 绘制矩形预览边界
        private void DrawRectangularPreviewBoundaries(IntVec3 startPos, IntVec3 endPos, Vector3 flightDirection, Vector3 perpendicular)
        {
            Map map = parent.pawn.Map;
            
            // 计算预览区域的四个角
            Vector3 startLeft = startPos.ToVector3() + perpendicular * Props.strafeWidth;
            Vector3 startRight = startPos.ToVector3() - perpendicular * Props.strafeWidth;
            Vector3 endLeft = endPos.ToVector3() + perpendicular * Props.strafeWidth;
            Vector3 endRight = endPos.ToVector3() - perpendicular * Props.strafeWidth;
            
            // 转换为 IntVec3 并确保在地图范围内
            IntVec3 startLeftCell = GetSafeMapPosition(new IntVec3((int)startLeft.x, (int)startLeft.y, (int)startLeft.z), map);
            IntVec3 startRightCell = GetSafeMapPosition(new IntVec3((int)startRight.x, (int)startRight.y, (int)startRight.z), map);
            IntVec3 endLeftCell = GetSafeMapPosition(new IntVec3((int)endLeft.x, (int)endLeft.y, (int)endLeft.z), map);
            IntVec3 endRightCell = GetSafeMapPosition(new IntVec3((int)endRight.x, (int)endRight.y, (int)endRight.z), map);
            
            // 绘制边界线 - 只绘制在地图范围内的线段
            if (startLeftCell.InBounds(map) && endLeftCell.InBounds(map))
                GenDraw.DrawLineBetween(startLeftCell.ToVector3Shifted(), endLeftCell.ToVector3Shifted(), SimpleColor.Blue, 0.2f);
            
            if (startRightCell.InBounds(map) && endRightCell.InBounds(map))
                GenDraw.DrawLineBetween(startRightCell.ToVector3Shifted(), endRightCell.ToVector3Shifted(), SimpleColor.Blue, 0.2f);
            
            if (startLeftCell.InBounds(map) && startRightCell.InBounds(map))
                GenDraw.DrawLineBetween(startLeftCell.ToVector3Shifted(), startRightCell.ToVector3Shifted(), SimpleColor.Blue, 0.2f);
            
            if (endLeftCell.InBounds(map) && endRightCell.InBounds(map))
                GenDraw.DrawLineBetween(endLeftCell.ToVector3Shifted(), endRightCell.ToVector3Shifted(), SimpleColor.Blue, 0.2f);
        }

        // 预处理扫射目标单元格
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

            // 应用最小和最大射弹数限制
            if (Props.maxStrafeProjectiles > -1 && confirmedTargets.Count > Props.maxStrafeProjectiles)
            {
                // 如果超出最大值，随机丢弃一些目标
                confirmedTargets = confirmedTargets.InRandomOrder().Take(Props.maxStrafeProjectiles).ToList();
            }
            
            if (Props.minStrafeProjectiles > -1 && confirmedTargets.Count < Props.minStrafeProjectiles)
            {
                // 如果不足最小值，从之前未选中的格子里补充
                int needed = Props.minStrafeProjectiles - confirmedTargets.Count;
                if (needed > 0 && missedCells.Count > 0)
                {
                    confirmedTargets.AddRange(missedCells.InRandomOrder().Take(Mathf.Min(needed, missedCells.Count)));
                }
            }
            
            Log.Message($"Strafing Preprocess: {confirmedTargets.Count}/{potentialTargets.Count} cells confirmed after min/max adjustment.");
            return confirmedTargets;
        }

        // 修复：创建地面扫射飞越，现在接受目标单元格参数
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

            // 设置基本属性
            flyOver.spawnContentsOnImpact = Props.dropContentsOnImpact;
            flyOver.playFlyOverSound = Props.playFlyOverSound;
            
            // 获取扫射组件并设置预处理后的目标单元格
            CompGroundStrafing strafingComp = flyOver.GetComp<CompGroundStrafing>();
            if (strafingComp != null)
            {
                // 修复：计算扫射区域的所有单元格，以目标单元格为中心
                Vector3 flightDirection = (endPos.ToVector3() - startPos.ToVector3()).normalized;
                List<IntVec3> potentialTargetCells = CalculateStrafingImpactCells(targetCell, flightDirection);
                
                if (potentialTargetCells.Count > 0)
                {
                    // 预处理：根据概率筛选实际会被射击的单元格
                    List<IntVec3> confirmedTargetCells = PreprocessStrafingTargets(
                        potentialTargetCells,
                        Props.strafeFireChance
                    );
                    
                    if (confirmedTargetCells.Count > 0)
                    {
                        strafingComp.SetConfirmedTargets(confirmedTargetCells);
                    }
                    else
                    {
                        Log.Warning("No confirmed target cells after preprocessing!");
                    }
                }
                else
                {
                    Log.Error("No potential target cells calculated for ground strafing!");
                }
            }
            else
            {
                Log.Error("FlyOver def does not have CompGroundStrafing component!");
            }
        }

        // 创建扇形监视飞越
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

            // 设置基本属性
            flyOver.spawnContentsOnImpact = Props.dropContentsOnImpact;
            flyOver.playFlyOverSound = Props.playFlyOverSound;
            
            Log.Message($"SectorSurveillance FlyOver created: {flyOver} from {startPos} to {endPos}");
        }

        // 计算垂直线进场路径
        private void CalculatePerpendicularPath(LocalTargetInfo target, out IntVec3 startPos, out IntVec3 endPos)
        {
            Map map = parent.pawn.Map;
            IntVec3 casterPos = parent.pawn.Position;
            IntVec3 targetPos = target.Cell;

            Log.Message($"Calculating perpendicular path: Caster={casterPos}, Target={targetPos}");

            // 计算施法者到目标的方向向量
            Vector3 directionToTarget = (targetPos.ToVector3() - casterPos.ToVector3()).normalized;
            
            // 如果方向为零向量，使用随机方向
            if (directionToTarget == Vector3.zero)
            {
                directionToTarget = new Vector3(Rand.Range(-1f, 1f), 0, Rand.Range(-1f, 1f)).normalized;
                Log.Message($"Using random direction: {directionToTarget}");
            }

            // 计算垂直于施法者-目标连线的方向（旋转90度）
            Vector3 perpendicularDirection = new Vector3(-directionToTarget.z, 0, directionToTarget.x).normalized;
            
            Log.Message($"Perpendicular direction: {perpendicularDirection}");

            // 从目标点出发，向垂直方向的两侧延伸找到地图边缘
            IntVec3 edge1 = FindMapEdgeInDirection(map, targetPos, perpendicularDirection);
            IntVec3 edge2 = FindMapEdgeInDirection(map, targetPos, -perpendicularDirection);

            // 随机选择起点和终点（确保目标点在路径上）
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

            Log.Message($"Perpendicular path: {startPos} -> {targetPos} -> {endPos}");
        }

        // 在指定方向上找到地图边缘
        private IntVec3 FindMapEdgeInDirection(Map map, IntVec3 fromPos, Vector3 direction)
        {
            // 确保方向向量有效
            if (direction == Vector3.zero)
            {
                direction = new Vector3(Rand.Range(-1f, 1f), 0, Rand.Range(-1f, 1f)).normalized;
            }
            
            // 使用更精确的地图边界计算
            IntVec3 mapCenter = map.Center;
            IntVec3 mapSize = new IntVec3(map.Size.x, 0, map.Size.z);
            
            // 计算与地图边界的交点
            Vector3 fromVec = fromPos.ToVector3();
            Vector3 dirNormalized = direction.normalized;
            
            // 计算到各个边界的距离
            float tMin = float.MaxValue;
            IntVec3? bestEdgePos = null;
            
            // 检查四个边界
            for (int i = 0; i < 4; i++)
            {
                float t = 0f;
                IntVec3 edgePos = IntVec3.Invalid;
                
                switch (i)
                {
                    case 0: // 左边界 (x = 0)
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
                        
                    case 1: // 右边界 (x = map.Size.x - 1)
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
                        
                    case 2: // 下边界 (z = 0)
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
                        
                    case 3: // 上边界 (z = map.Size.z - 1)
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
                
                // 找到最近的有效边界点
                if (edgePos.IsValid && edgePos.InBounds(map) && t > 0 && t < tMin)
                {
                    tMin = t;
                    bestEdgePos = edgePos;
                }
            }
            
            if (bestEdgePos.HasValue)
            {
                return bestEdgePos.Value;
            }
            
            // 如果没找到合适的边界点，使用随机边缘位置
            Log.Warning($"Could not find map edge in direction {direction}, using random edge");
            return GetRandomMapEdgePosition(map);
        }

        // 原有的位置计算方法
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
                    Log.Message($"Random map edge selected as end position: {endPos}");
                    break;

                default:
                    endPos = target.Cell;
                    break;
            }

            return GetSafeMapPosition(endPos, map);
        }

        // 原有的辅助方法
        private IntVec3 GetOppositeMapEdgeThroughCenter(Map map, IntVec3 startPos)
        {
            IntVec3 center = map.Center;
            Vector3 toCenter = (center.ToVector3() - startPos.ToVector3()).normalized;
            
            if (toCenter == Vector3.zero)
            {
                toCenter = new Vector3(Rand.Range(-1f, 1f), 0, Rand.Range(-1f, 1f)).normalized;
                Log.Message($"Using random direction to center: {toCenter}");
            }
            
            Vector3 fromCenter = toCenter;
            IntVec3 oppositeEdge = GetMapEdgePositionFromCenter(map, fromCenter);
            
            Log.Message($"Found opposite edge through center: {oppositeEdge}");
            return oppositeEdge;
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
                    IntVec3 edgePos = FindClosestValidPosition(testPos, map);
                    Log.Message($"Found map edge from center: {edgePos} (direction: {direction}, distance: {i})");
                    return edgePos;
                }
            }
            
            Log.Warning("Could not find map edge from center, using random edge");
            return GetRandomMapEdgePosition(map);
        }

        private IntVec3 GetMapEdgePosition(Map map, Vector3 direction)
        {
            if (direction == Vector3.zero)
            {
                direction = new Vector3(Rand.Range(-1f, 1f), 0, Rand.Range(-1f, 1f)).normalized;
                Log.Message($"Using random direction: {direction}");
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
                    IntVec3 edgePos = FindClosestValidPosition(testPos, map);
                    Log.Message($"Found map edge position: {edgePos} (direction: {direction}, distance: {i})");
                    return edgePos;
                }
            }
            
            Log.Warning("Could not find map edge in direction, using random edge");
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

        private IntVec3 GetRandomMapEdgePosition(Map map)
        {
            int edge = Rand.Range(0, 4);
            int x, z;
            
            switch (edge)
            {
                case 0: // 上边
                    x = Rand.Range(0, map.Size.x);
                    z = 0;
                    break;
                case 1: // 右边
                    x = map.Size.x - 1;
                    z = Rand.Range(0, map.Size.z);
                    break;
                case 2: // 下边
                    x = Rand.Range(0, map.Size.x);
                    z = map.Size.z - 1;
                    break;
                case 3: // 左边
                default:
                    x = 0;
                    z = Rand.Range(0, map.Size.z);
                    break;
            }
            
            IntVec3 edgePos = new IntVec3(x, 0, z);
            Log.Message($"Random map edge position: {edgePos}");
            return edgePos;
        }

        private IntVec3 GetFixedDistancePosition(IntVec3 startPos, IntVec3 targetPos)
        {
            Vector3 direction = (targetPos.ToVector3() - startPos.ToVector3()).normalized;
            IntVec3 endPos = startPos + new IntVec3(
                (int)(direction.x * Props.flyOverDistance),
                0,
                (int)(direction.z * Props.flyOverDistance));
            
            Log.Message($"Fixed distance position: {endPos} (from {startPos}, distance: {Props.flyOverDistance})");
            return endPos;
        }

        private Vector3 GetDirectionFromCasterToTarget(LocalTargetInfo target)
        {
            Vector3 direction = (target.Cell.ToVector3() - parent.pawn.Position.ToVector3()).normalized;
            
            if (direction == Vector3.zero)
            {
                direction = new Vector3(Rand.Range(-1f, 1f), 0, Rand.Range(-1f, 1f)).normalized;
                Log.Message($"Using random direction: {direction}");
            }
            
            return direction;
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
            
            if (Props.customSound != null)
            {
                // 自定义音效逻辑
            }
            
            Log.Message($"Standard FlyOver created: {flyOver} from {startPos} to {endPos}");
        }

        // 更新技能提示信息
        public override string ExtraLabelMouseAttachment(LocalTargetInfo target)
        {
            if (Props.enableGroundStrafing)
            {
                return $"扫射区域: {Props.strafeWidth * 2 + 1}格宽度";
            }
            else if (Props.enableSectorSurveillance)
            {
                return $"扇形监视: 约{Props.strafeWidth * 2 + 1}格宽度\n(具体参数在飞行物定义中)";
            }
            
            return base.ExtraLabelMouseAttachment(target);
        }

        public override bool Valid(LocalTargetInfo target, bool throwMessages = false)
        {
            return base.Valid(target, throwMessages) && 
                   parent.pawn != null && 
                   parent.pawn.Map != null &&
                   target.Cell.IsValid &&
                   target.Cell.InBounds(parent.pawn.Map);
        }
    }
}
