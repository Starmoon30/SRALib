﻿using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace SRA
{
    public class TrajectoryHeightPoint
    {
        public float progress = 0f;  // 弹道进度 (0.0 - 1.0)
        public float height = 0f;    // 相对高度倍数

        public TrajectoryHeightPoint() { }

        public TrajectoryHeightPoint(float progress, float height)
        {
            this.progress = Mathf.Clamp01(progress);
            this.height = height;
        }
    }

public class NorthArcModExtension : DefModExtension
        {
            // === 现有字段（保留向后兼容）===
            // 控制向北偏移的高度（格数），值越大弧度越高
            public float northOffsetDistance = 10f;

            // 控制曲线的形状，值越大曲线越陡峭
            public float curveSteepness = 1f;

            // 是否使用弧形轨迹（默认为true，如果为false则使用直线轨迹）
            public bool useArcTrajectory = true;

            // === 新增：自定义弹道高度曲线 ===
            // 弹道高度曲线控制点列表，如果为 null 或空则使用默认抛物线
            public List<TrajectoryHeightPoint> trajectoryHeightPoints = null;

            // === 新增：追踪配置 ===
            // 是否启用目标追踪功能
            public bool enableTracking = false;

            // 目标搜索半径（格数）
            public float targetSearchRadius = 11f;

            // 未找到目标时的随机位置搜索半径（格数）
            public float fallbackSearchRadius = 7f;

            // 目标移动超过此距离时视为丢失（格数）
            public float maxTargetLostDistance = 5f;

            // === 新增：锁定指示器配置 ===
            // 锁定指示器的 ThingDef 名称，默认使用 CMC_Mote_MissileLocked
            public string lockIndicatorMoteDef = null;

            // 锁定指示器的缩放比例，0 表示使用默认值（射弹尺寸 * 2）
            public float lockIndicatorScale = 0f;
        }


    public class Projectile_MultiExplosive_NorthArcTrail : Projectile_MultiExplosive
    {
        // --- 禁用一下继承来的尾迹渲染 ---
        protected override bool isNorthArcTrail => true;
        // --- 弹道部分变量 ---
        public float northOffsetDistance = 0f;

        private Vector3 exactPositionInt;
        private float curveSteepness = 1f;

        private Vector3 originPos;
        private Vector3 destinationPos;
        private Vector3 bezierControlPoint;
        private int ticksFlying;
        private int totalTicks;
        private bool initialized = false;

        private int Fleck_MakeFleckTick;
        private Vector3 lastTickPosition;

        // 新增：绘制相关变量
        private float currentArcHeight;

        // 新增：用于保存真实计算的位置（仅XZ平面）
        private Vector3 horizontalPosition;

        // === 追踪系统字段 ===
        private bool enableTracking = false;
        private LocalTargetInfo trackingIntendedTarget;
        private Vector3 lastTargetPos = Vector3.zero;
        private bool targetInit = false;
        private static System.Reflection.FieldInfo usedTargetField;

        // === 锁定指示器字段 ===
        private Mote_ScaleAndRotate lockIndicatorMote = null;

        // === 自定义弹道高度曲线字段 ===
        private List<TrajectoryHeightPoint> sortedHeightPoints = null;
        private bool useCustomHeightCurve = false;

        // === 配置缓存 ===
        private NorthArcModExtension cachedExtension = null;
        // 修改：简化ExactPosition计算
        public override Vector3 ExactPosition
        {
            get
            {
                if (!initialized)
                    return base.ExactPosition;

                // 返回水平位置，保持Y轴为定义的高度
                // RimWorld使用Y轴作为高度层，不应该随意改变
                return new Vector3(horizontalPosition.x, def.Altitude, horizontalPosition.z);
            }
        }

        // 修改：重写ExactRotation以考虑高度变化
        public override Quaternion ExactRotation => Quaternion.LookRotation(GetCurrentDirection());

        // 新增：获取带高度的位置（用于特效绘制）
        public Vector3 PositionWithHeight
        {
            get
            {
                if (!initialized)
                    return ExactPosition;

                return new Vector3(
                    horizontalPosition.x,
                    def.Altitude,
                    horizontalPosition.z + currentArcHeight // 适当缩放高度
                );
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref originPos, "originPos");
            Scribe_Values.Look(ref destinationPos, "destinationPos");
            Scribe_Values.Look(ref bezierControlPoint, "bezierControlPoint");
            Scribe_Values.Look(ref ticksFlying, "ticksFlying", 0);
            Scribe_Values.Look(ref totalTicks, "totalTicks", 0);
            Scribe_Values.Look(ref initialized, "initialized", false);
            Scribe_Values.Look(ref northOffsetDistance, "northOffsetDistance", 0f);
            Scribe_Values.Look(ref exactPositionInt, "exactPositionInt", Vector3.zero);
            Scribe_Values.Look(ref curveSteepness, "curveSteepness", 1f);
            Scribe_Values.Look(ref currentArcHeight, "currentArcHeight", 0f);
            Scribe_Values.Look(ref horizontalPosition, "horizontalPosition", Vector3.zero);

            Scribe_Values.Look(ref Fleck_MakeFleckTick, "Fleck_MakeFleckTick", 0);
            Scribe_Values.Look(ref lastTickPosition, "lastTickPosition", Vector3.zero);

            // 追踪系统字段
            Scribe_Values.Look(ref enableTracking, "enableTracking", false);
            Scribe_TargetInfo.Look(ref trackingIntendedTarget, "intendedTarget");
            Scribe_Values.Look(ref lastTargetPos, "lastTargetPos", Vector3.zero);
            Scribe_Values.Look(ref targetInit, "targetInit", false);

            // 自定义弹道高度曲线字段
            Scribe_Values.Look(ref useCustomHeightCurve, "useCustomHeightCurve", false);
            Scribe_Collections.Look(ref sortedHeightPoints, "sortedHeightPoints", LookMode.Deep);
        }

        public override void Launch(Thing launcher, Vector3 origin, LocalTargetInfo usedTarget, LocalTargetInfo intendedTarget, ProjectileHitFlags hitFlags, bool preventFriendlyFire = false, Thing equipment = null, ThingDef targetCoverDef = null)
        {
            base.Launch(launcher, origin, usedTarget, intendedTarget, hitFlags, preventFriendlyFire, equipment, targetCoverDef);

            // 缓存配置扩展
            cachedExtension = def.GetModExtension<NorthArcModExtension>();
            
            // 初始化追踪系统
            if (cachedExtension != null && cachedExtension.enableTracking)
            {
                enableTracking = true;
                this.trackingIntendedTarget = intendedTarget;
                targetInit = false;
            }

            // 初始化自定义弹道高度曲线
            if (cachedExtension != null && cachedExtension.trajectoryHeightPoints != null && 
                cachedExtension.trajectoryHeightPoints.Count >= 2)
            {
                useCustomHeightCurve = true;
                sortedHeightPoints = new List<TrajectoryHeightPoint>(cachedExtension.trajectoryHeightPoints);
                
                // 按 progress 排序
                sortedHeightPoints.Sort((a, b) => a.progress.CompareTo(b.progress));
                
                // 验证控制点数量
                if (sortedHeightPoints.Count > 10)
                {
                    Log.Warning($"[Projectile Tracking] Too many trajectory height points ({sortedHeightPoints.Count}). Limiting to 10.");
                    sortedHeightPoints = sortedHeightPoints.GetRange(0, 10);
                }

                // 错误处理：自定义高度曲线配置错误 - 验证并修正 progress 值
                foreach (var point in sortedHeightPoints)
                {
                    if (point.progress < 0f || point.progress > 1f)
                    {
                        Log.Warning($"[Projectile Tracking] Invalid progress value {point.progress} in trajectory height point. Clamping to [0, 1].");
                        point.progress = Mathf.Clamp01(point.progress);
                    }
                }

                // 调试日志：输出自定义高度曲线控制点
                Log.Message($"[Projectile Tracking] Custom height curve enabled with {sortedHeightPoints.Count} points:");
                foreach (var point in sortedHeightPoints)
                {
                    Log.Message($"  - Progress: {point.progress}, Height: {point.height}");
                }
            }
            else if (cachedExtension != null && cachedExtension.trajectoryHeightPoints != null && 
                     cachedExtension.trajectoryHeightPoints.Count < 2)
            {
                Log.Warning($"[Projectile Tracking] Insufficient trajectory height points ({cachedExtension.trajectoryHeightPoints.Count}). Falling back to default parabola.");
                useCustomHeightCurve = false;
            }
            else
            {
                if (cachedExtension == null)
                {
                    Log.Message("[Projectile Tracking] No NorthArcModExtension found. Using default parabola.");
                }
                else if (cachedExtension.trajectoryHeightPoints == null)
                {
                    Log.Message("[Projectile Tracking] trajectoryHeightPoints is null. Using default parabola.");
                }
                useCustomHeightCurve = false;
            }

            // 获取北向偏移配置
            if (cachedExtension != null)
            {
                northOffsetDistance = cachedExtension.northOffsetDistance;
                curveSteepness = cachedExtension.curveSteepness;

                // 错误处理：验证配置值的合理性
                if (northOffsetDistance < 0f)
                {
                    Log.Warning($"[Projectile Tracking] Invalid northOffsetDistance {northOffsetDistance}. Using absolute value.");
                    northOffsetDistance = Mathf.Abs(northOffsetDistance);
                }

                if (cachedExtension.targetSearchRadius <= 0f)
                {
                    Log.Warning($"[Projectile Tracking] Invalid targetSearchRadius {cachedExtension.targetSearchRadius}. Using default value 11.");
                    cachedExtension.targetSearchRadius = 11f;
                }

                if (cachedExtension.fallbackSearchRadius <= 0f)
                {
                    Log.Warning($"[Projectile Tracking] Invalid fallbackSearchRadius {cachedExtension.fallbackSearchRadius}. Using default value 7.");
                    cachedExtension.fallbackSearchRadius = 7f;
                }

                if (cachedExtension.maxTargetLostDistance <= 0f)
                {
                    Log.Warning($"[Projectile Tracking] Invalid maxTargetLostDistance {cachedExtension.maxTargetLostDistance}. Using default value 5.");
                    cachedExtension.maxTargetLostDistance = 5f;
                }
            }
            else
            {
                northOffsetDistance = def.projectile.arcHeightFactor * 3;
            }

            // --- 初始化弹道 ---
            originPos = origin;
            destinationPos = usedTarget.CenterVector3;

            // 错误处理：数值计算错误 - 验证速度值
            float speed = def.projectile.speed;
            if (speed <= 0)
            {
                Log.Warning($"[Projectile Tracking] Invalid projectile speed {speed}. Using default value 1.");
                speed = 1f;
            }

            float distance = (originPos - destinationPos).MagnitudeHorizontal();
            totalTicks = Mathf.CeilToInt(distance / speed * 100f);
            
            // 错误处理：数值计算错误 - 确保 totalTicks 至少为 1
            if (totalTicks < 1)
            {
                Log.Warning($"[Projectile Tracking] Calculated totalTicks is {totalTicks}. Setting to minimum value 1.");
                totalTicks = 1;
            }

            ticksFlying = 0;

            // 贝塞尔曲线计算
            Vector3 midPoint = (originPos + destinationPos) / 2f;
            Vector3 apexPoint = midPoint + new Vector3(0, 0, northOffsetDistance);
            bezierControlPoint = 2f * apexPoint - midPoint;

            initialized = true;
            horizontalPosition = origin;
            exactPositionInt = origin;
            lastTickPosition = origin;
            currentArcHeight = 0f;
        }

        protected override void Tick()
        {

            if (this.Destroyed)
            {
                return;
            }

            if (!initialized)
            {
                return;
            }

            // === 追踪系统更新 ===
            if (enableTracking)
            {
                if (trackingIntendedTarget != null && trackingIntendedTarget.Thing != null)
                {
                    // 更新现有目标
                    UpdateTargetTracking();
                }
                else if (DistanceCoveredFraction < 0.67f)
                {
                    // 搜索新目标
                    FindNextTarget(destinationPos);
                }
            }

            ticksFlying++;

            // 1. 计算当前帧的新位置
            float t = (float)ticksFlying / (float)totalTicks;
            if (t > 1f) t = 1f;

            float u = 1 - t;
            // 水平位移 (贝塞尔)
            Vector3 nextPos = (u * u * originPos) + (2 * u * t * bezierControlPoint) + (t * t * destinationPos);

            // 错误处理：地图边界错误 - 射弹位置超出地图范围
            if (!nextPos.ToIntVec3().InBounds(base.Map))
            {
                Log.Warning($"[Projectile Tracking] Projectile position {nextPos} is out of map bounds. Destroying projectile.");
                DestroyLockIndicator();
                this.Destroy();
                return;
            }

            // 垂直高度 (使用自定义曲线或默认抛物线)
            currentArcHeight = CalculateHeightAtProgress(t);
            horizontalPosition = nextPos; // 保存水平位置

            exactPositionInt = nextPos;

            // 2. 处理拖尾特效
            if (TailDef != null && TailDef.tailFleckDef != null)
            {
                Fleck_MakeFleckTick++;
                if (Fleck_MakeFleckTick >= TailDef.fleckDelayTicks)
                {
                    if (Fleck_MakeFleckTick >= (TailDef.fleckDelayTicks + TailDef.fleckMakeFleckTickMax))
                    {
                        Fleck_MakeFleckTick = TailDef.fleckDelayTicks;
                    }

                    Map map = base.Map;
                    if (map != null)
                    {
                        int count = TailDef.fleckMakeFleckNum.RandomInRange;
                        // 使用带高度的位置生成尾迹
                        Vector3 currentPosition = PositionWithHeight;
                        Vector3 previousPosition = lastTickPosition;

                        if ((currentPosition - previousPosition).MagnitudeHorizontalSquared() > 0.0001f)
                        {
                            float moveAngle = (currentPosition - previousPosition).AngleFlat();

                            for (int i = 0; i < count; i++)
                            {
                                float velocityAngle = TailDef.fleckAngle.RandomInRange + moveAngle;

                                FleckCreationData dataStatic = FleckMaker.GetDataStatic(currentPosition, map, TailDef.tailFleckDef, TailDef.fleckScale.RandomInRange);
                                dataStatic.rotation = moveAngle;
                                dataStatic.rotationRate = TailDef.fleckRotation.RandomInRange;
                                dataStatic.velocityAngle = velocityAngle;
                                dataStatic.velocitySpeed = TailDef.fleckSpeed.RandomInRange;
                                map.flecks.CreateFleck(dataStatic);
                            }
                        }
                    }
                }
            }
            lastTickPosition = PositionWithHeight;
            if (ticksFlying > totalTicks)
            {
                Position = PositionWithHeight.ToIntVec3();
                Impact(null);
                return;
            }
        }

        // 修改：重写绘制方法
        protected override void DrawAt(Vector3 drawLoc, bool flip = false)
        {
            if (!initialized)
            {
                base.DrawAt(drawLoc, flip);
                return;
            }

            // 使用固定的绘制位置，但考虑高度偏移
            Vector3 finalDrawPos = ExactPosition;

            // 调整绘制位置以考虑抛物线高度
            // 但保持Y轴在合理范围内，避免被裁剪
            finalDrawPos.z += Mathf.Clamp(currentArcHeight, -0.5f, 200f);

            // 绘制阴影
            if (def.projectile.shadowSize > 0f)
            {
                DrawShadow(finalDrawPos);
            }

            Quaternion rotation = ExactRotation;
            if (def.projectile.spinRate != 0f)
            {
                float spinAngle = 60f / def.projectile.spinRate;
                rotation = Quaternion.AngleAxis((float)Find.TickManager.TicksGame % spinAngle / spinAngle * 360f, Vector3.up);
            }

            // 使用正确的绘制方法
            if (def.projectile.useGraphicClass)
            {
                // 确保图形缩放合适
                float scaleFactor = 1f + currentArcHeight * 0.1f; // 轻微缩放模拟远近
                Matrix4x4 matrix = Matrix4x4.TRS(
                    finalDrawPos,
                    rotation,
                    new Vector3(scaleFactor, 1f, scaleFactor)
                );
                Graphics.DrawMesh(MeshPool.GridPlane(def.graphicData.drawSize), matrix, DrawMat, 0);
            }
            else
            {
                Graphics.DrawMesh(MeshPool.GridPlane(def.graphicData.drawSize), finalDrawPos, rotation, DrawMat, 0);
            }

            Comps_PostDraw();
        }

        // 修改：简化阴影绘制
        private void DrawShadow(Vector3 drawLoc)
        {
            if (def.projectile.shadowSize <= 0f)
                return;

            Material shadowMat = MaterialPool.MatFrom("Things/Skyfaller/SkyfallerShadowCircle", ShaderDatabase.Transparent);
            if (shadowMat == null) return;

            // 根据当前高度调整阴影大小
            float normalizedHeight = Mathf.Clamp01(currentArcHeight / (def.projectile.arcHeightFactor + 0.01f));
            float shadowSize = def.projectile.shadowSize * Mathf.Lerp(1f, 0.4f, normalizedHeight);

            Vector3 scale = new Vector3(shadowSize, 1f, shadowSize);
            Vector3 shadowOffset = new Vector3(0f, -0.05f, 0f); // 稍微降低阴影位置

            Matrix4x4 matrix = Matrix4x4.TRS(drawLoc + shadowOffset, Quaternion.identity, scale);
            Graphics.DrawMesh(MeshPool.plane10, matrix, shadowMat, 0);
        }
        // 计算高度对进度 t 的导数（用于方向计算）
        private float CalculateHeightDerivativeAtProgress(float t)
        {
            t = Mathf.Clamp01(t);
            if (!useCustomHeightCurve || sortedHeightPoints == null || sortedHeightPoints.Count < 2)
                return 0f;

            // 查找分段（同之前）
            int segmentIndex = -1;
            for (int i = 0; i < sortedHeightPoints.Count - 1; i++)
            {
                if (t >= sortedHeightPoints[i].progress && t <= sortedHeightPoints[i + 1].progress)
                {
                    segmentIndex = i;
                    break;
                }
            }
            if (segmentIndex == -1) return 0f;

            TrajectoryHeightPoint p1 = sortedHeightPoints[segmentIndex];
            TrajectoryHeightPoint p2 = sortedHeightPoints[segmentIndex + 1];
            float delta = p2.progress - p1.progress;
            if (delta <= 0f) return 0f;

            float s = (t - p1.progress) / delta;

            // 获取四个控制点（处理边界）
            TrajectoryHeightPoint p0, p3;
            if (segmentIndex == 0)
                p0 = new TrajectoryHeightPoint(p1.progress - delta, p1.height);
            else
                p0 = sortedHeightPoints[segmentIndex - 1];

            if (segmentIndex == sortedHeightPoints.Count - 2)
                p3 = new TrajectoryHeightPoint(p2.progress + delta, p2.height);
            else
                p3 = sortedHeightPoints[segmentIndex + 2];

            // Catmull-Rom 样条导数公式
            float dh_ds = 0.5f * (
                (-p0.height + p2.height) +
                2f * (2f * p0.height - 5f * p1.height + 4f * p2.height - p3.height) * s +
                3f * (-p0.height + 3f * p1.height - 3f * p2.height + p3.height) * s * s
            );

            float dh_dt = dh_ds / delta;
            return Mathf.Clamp(dh_dt, -100f, 100f); // 可选限制
        }
        // 计算当前位置的切线方向（考虑高度变化）
        private Vector3 GetCurrentDirection()
        {
            if (!initialized || totalTicks <= 0)
                return (destinationPos - originPos).normalized;

            float t = (float)ticksFlying / (float)totalTicks;
            if (t > 1f) t = 1f;

            float u = 1 - t;

            // 水平切线（贝塞尔导数，Y=0）
            Vector3 horizontalTangent = 2 * u * (bezierControlPoint - originPos) + 2 * t * (destinationPos - bezierControlPoint);
            horizontalTangent.y = 0f;

            // 计算高度导数（仅当使用自定义曲线时，否则为0）
            float heightDerivative = 0f;
            if (useCustomHeightCurve && sortedHeightPoints != null && sortedHeightPoints.Count >= 2)
            {
                heightDerivative = CalculateHeightDerivativeAtProgress(t);
                // 可选：缩放因子，使角度变化更明显
                heightDerivative *= 1f;
            }

            // 合成速度向量：水平切线 + Z 方向的高度导数
            Vector3 velocity = new Vector3(horizontalTangent.x, 0f, horizontalTangent.z + heightDerivative);

            // 防止零向量
            if (velocity.magnitude < 0.0001f)
                return (destinationPos - originPos).normalized;

            return velocity.normalized;
        }

        protected override void Impact(Thing hitThing, bool blockedByShield = false)
        {
            if (hitThing == null && ticksFlying <= totalTicks) {
                return;
            }
            // 错误处理：确保锁定指示器被清理，即使发生错误
            try
            {
                DestroyLockIndicator();
            }
            catch (System.Exception ex)
            {
                Log.Error($"[Projectile Tracking] Error during lock indicator cleanup in Impact: {ex.Message}");
            }
            // 调用基类碰撞逻辑
            base.Impact(hitThing, blockedByShield);
        }

        // === 目标搜索和验证方法 ===

        /// <summary>
        /// 在目标区域搜索下一个有效目标
        /// </summary>
        /// <param name="targetPos">搜索中心位置</param>
        private void FindNextTarget(Vector3 targetPos)
        {
            if (cachedExtension == null || !enableTracking)
                return;

            // 错误处理：地图边界错误 - 验证搜索中心在地图范围内
            IntVec3 center = IntVec3.FromVector3(targetPos);
            if (!center.InBounds(Map))
            {
                Log.Warning($"[Projectile Tracking] Search center {center} is out of map bounds. Using projectile current position.");
                center = horizontalPosition.ToIntVec3();
                if (!center.InBounds(Map))
                {
                    Log.Warning("[Projectile Tracking] Cannot find valid search center. Disabling tracking.");
                    enableTracking = false;
                    return;
                }
            }

            float searchRadius = cachedExtension.targetSearchRadius;

            // 搜索敌对单位
            foreach (IntVec3 cell in GenRadial.RadialCellsAround(center, searchRadius, true))
            {
                if (!cell.InBounds(Map))
                    continue;

                Pawn pawn = cell.GetFirstPawn(Map);
                if (pawn != null && IsValidTarget(pawn))
                {
                    trackingIntendedTarget = new LocalTargetInfo(pawn);
                    targetInit = false;
                    return;
                }
            }

            // 未找到目标，使用随机地面位置
            CellRect fallbackRect = CellRect.CenteredOn(center, (int)cachedExtension.fallbackSearchRadius);
            IntVec3 fallbackCell = fallbackRect.RandomCell;

            // 确保备用位置在地图范围内
            if (fallbackCell.InBounds(Map))
            {
                trackingIntendedTarget = new LocalTargetInfo(fallbackCell);
            }
            else
            {
                // 如果随机位置超出边界，使用中心点
                if (center.InBounds(Map))
                {
                    trackingIntendedTarget = new LocalTargetInfo(center);
                }
                else
                {
                    Log.Warning("[Projectile Tracking] Cannot find valid fallback position. Disabling tracking.");
                    enableTracking = false;
                    return;
                }
            }

            usedTarget = trackingIntendedTarget;// 同步
            targetInit = false;
        }

        /// <summary>
        /// 验证目标是否有效
        /// </summary>
        /// <param name="pawn">要验证的 Pawn</param>
        /// <returns>目标是否有效</returns>
        private bool IsValidTarget(Pawn pawn)
        {
            if (pawn == null || pawn.Dead || pawn.Downed)
                return false;

            if (launcher == null)
                return true;

            return pawn.Faction != null &&
                   pawn.Faction.HostileTo(launcher.Faction);
        }

        /// <summary>
        /// 更新目标追踪状态
        /// </summary>
        private void UpdateTargetTracking()
        {
            // 错误处理：目标验证错误 - trackingIntendedTarget.Thing 在 Tick 之间被销毁或变为 null
            if (!enableTracking || trackingIntendedTarget == null)
            {
                DestroyLockIndicator();
                return;
            }

            if (trackingIntendedTarget.Thing == null)
            {
                Log.Warning("[Projectile Tracking] Target Thing became null during tracking. Clearing target and destroying indicator.");
                trackingIntendedTarget = null;
                targetInit = false;
                DestroyLockIndicator();
                return;
            }

            // 初始化上次目标位置（首次调用时）
            if (!targetInit)
            {
                lastTargetPos = trackingIntendedTarget.Thing.DrawPos;
                targetInit = true;
            }

            // 检测目标是否丢失
            Vector3 currentTargetPos = trackingIntendedTarget.Thing.DrawPos;
            float distanceMoved = (currentTargetPos - lastTargetPos).magnitude;
            if (distanceMoved > cachedExtension.maxTargetLostDistance)
            {
                // 目标丢失
                trackingIntendedTarget = null;
                targetInit = false;
                DestroyLockIndicator();
                usedTarget = new LocalTargetInfo(exactPositionInt.ToIntVec3());
                return;
            }

            // 错误处理：地图边界错误 - 验证目标位置在地图范围内
            if (!currentTargetPos.ToIntVec3().InBounds(Map))
            {
                trackingIntendedTarget = null;
                targetInit = false;
                DestroyLockIndicator();
                usedTarget = new LocalTargetInfo(exactPositionInt.ToIntVec3());
                return;
            }
            // 更新目标位置
            destinationPos = currentTargetPos;
            lastTargetPos = currentTargetPos;

            // 同步基类的 usedTarget
            this.usedTarget = trackingIntendedTarget;

            // 重新计算贝塞尔控制点
            RecalculateBezierControlPoint();

            // 重新计算总飞行时间
            RecalculateTotalTicks();

            // 管理锁定指示器
            ManageLockIndicator();
        }
        /// <summary>
        /// 重新计算贝塞尔曲线控制点
        /// </summary>
        private void RecalculateBezierControlPoint()
        {
            // 保持控制点的相对位置关系
            Vector3 midPoint = (originPos + destinationPos) / 2f;
            Vector3 apexPoint = midPoint + new Vector3(0, 0, northOffsetDistance);
            bezierControlPoint = 2f * apexPoint - midPoint;
        }

        /// <summary>
        /// 重新计算总飞行时间
        /// </summary>
        private void RecalculateTotalTicks()
        {
            float speed = def.projectile.speed;
            if (speed <= 0) speed = 1f;

            float distance = (originPos - destinationPos).MagnitudeHorizontal();
            int newTotalTicks = Mathf.CeilToInt(distance / speed * 100f);
            if (newTotalTicks < 1) newTotalTicks = 1;

            // 平滑过渡：保持当前进度比例
            float currentProgress = (float)ticksFlying / (float)totalTicks;
            totalTicks = newTotalTicks;
            ticksFlying = Mathf.RoundToInt(currentProgress * newTotalTicks);
        }

        /// <summary>
        /// 计算指定进度的弹道高度
        /// </summary>
        /// <param name="progress">弹道进度 (0.0 - 1.0)</param>
        /// <returns>当前进度的高度值</returns>
        private float CalculateHeightAtProgress(float progress)
        {
            progress = Mathf.Clamp01(progress);

            // 如果使用自定义高度曲线
            if (useCustomHeightCurve && sortedHeightPoints != null)
            {
                float height = CatmullRomSpline(progress, sortedHeightPoints);
                return height;
            }
            // 否则使用默认抛物线
            else
            {
                float defaultHeight = def.projectile.arcHeightFactor * GenMath.InverseParabola(progress);
                return defaultHeight;
            }
        }

        private float CatmullRomSpline(float t, List<TrajectoryHeightPoint> points)
        {
            // 找到 t 所在的控制点区间
            int segmentIndex = -1;
            for (int i = 0; i < points.Count - 1; i++)
            {
                if (t >= points[i].progress && t <= points[i + 1].progress)
                {
                    segmentIndex = i;
                    break;
                }
            }

            // 边界情况处理
            if (segmentIndex == -1)
            {
                if (t <= points[0].progress) return points[0].height;
                if (t >= points[points.Count - 1].progress) return points[points.Count - 1].height;
            }

            // 获取四个控制点用于 Catmull-Rom 样条
            TrajectoryHeightPoint p0, p1, p2, p3;

            p1 = points[segmentIndex];
            p2 = points[segmentIndex + 1];

            // 处理边界：使用镜像点
            if (segmentIndex == 0)
            {
                // 第一段：镜像 p1
                p0 = new TrajectoryHeightPoint(
                    p1.progress - (p2.progress - p1.progress),
                    p1.height
                );
            }
            else
            {
                p0 = points[segmentIndex - 1];
            }

            if (segmentIndex == points.Count - 2)
            {
                // 最后一段：镜像 p2
                p3 = new TrajectoryHeightPoint(
                    p2.progress + (p2.progress - p1.progress),
                    p2.height
                );
            }
            else
            {
                p3 = points[segmentIndex + 2];
            }

            // 归一化 t 到当前段 [0, 1]
            float segmentT = (t - p1.progress) / (p2.progress - p1.progress);

            // Catmull-Rom 样条公式
            float t2 = segmentT * segmentT;
            float t3 = t2 * segmentT;

            float height = 0.5f * (
                (2f * p1.height) +
                (-p0.height + p2.height) * segmentT +
                (2f * p0.height - 5f * p1.height + 4f * p2.height - p3.height) * t2 +
                (-p0.height + 3f * p1.height - 3f * p2.height + p3.height) * t3
            );

            // 数值稳定性检查：限制异常值
            // 防止样条插值产生过大的超调
            float minHeight = Mathf.Min(p0.height, p1.height, p2.height, p3.height);
            float maxHeight = Mathf.Max(p0.height, p1.height, p2.height, p3.height);
            float allowedOvershoot = (maxHeight - minHeight) * 0.5f; // 允许 50% 的超调

            height = Mathf.Clamp(height, minHeight - allowedOvershoot, maxHeight + allowedOvershoot);

            return height;
        }



        /// <summary>
        /// 管理锁定指示器的生命周期
        /// </summary>
        private void ManageLockIndicator()
        {
            // 检查目标有效性
            if (trackingIntendedTarget == null || trackingIntendedTarget.Thing == null)
            {
                DestroyLockIndicator();
                return;
            }

            // 创建或维护锁定指示器
            if (lockIndicatorMote == null || lockIndicatorMote.Destroyed)
            {
                CreateLockIndicator();
            }
            else
            {
                // 错误处理：Mote 维护失败
                try
                {
                    // 维护现有指示器
                    lockIndicatorMote.MaintainMote();
                }
                catch (System.Exception ex)
                {
                    Log.Error($"[Projectile Tracking] Failed to maintain lock indicator mote: {ex.Message}");
                    DestroyLockIndicator();
                }
            }
        }


        /// <summary>
        /// 创建锁定指示器
        /// </summary>
        private void CreateLockIndicator()
        {
            try
            {
                // 获取 Mote 定义
                string moteDefName = cachedExtension?.lockIndicatorMoteDef;
                ThingDef moteDef;

                if (string.IsNullOrEmpty(moteDefName))
                {
                    return;
                }
                else
                {
                    moteDef = DefDatabase<ThingDef>.GetNamed(moteDefName, false);
                    if (moteDef == null)
                    {
                        Log.Warning($"[Projectile Tracking] Lock indicator mote '{moteDefName}' not found. Skipping indicator creation.");
                        return;
                    }
                }


                // 创建 Mote_ScaleAndRotate 实例
                lockIndicatorMote = (Mote_ScaleAndRotate)ThingMaker.MakeThing(moteDef, null);

                // 设置缩放
                float scale = cachedExtension?.lockIndicatorScale ?? 0f;
                if (scale <= 0f)
                {
                    scale = def.graphicData.drawSize.x * 2f;
                }
                lockIndicatorMote.Scale = scale;
                lockIndicatorMote.iniscale = scale;

                // 设置位置偏移（渲染在 PawnRope 层级）
                Vector3 offset = new Vector3(0f, 0f, 0f);
                offset.y = AltitudeLayer.PawnRope.AltitudeFor();

                // 附着到目标
                lockIndicatorMote.Attach(trackingIntendedTarget.Thing, offset, false);

                // 设置位置
                lockIndicatorMote.exactPosition = trackingIntendedTarget.Thing.DrawPos + offset;

                // 设置生命周期参数
                lockIndicatorMote.solidTimeOverride = 9999f;
                lockIndicatorMote.tickimpact = ticksToImpact + TickSpawned;
                lockIndicatorMote.tickspawned = TickSpawned;

                // 生成 Mote 到地图
                GenSpawn.Spawn(lockIndicatorMote, trackingIntendedTarget.Thing.Position, Map, WipeMode.Vanish);
            }
            catch (System.Exception ex)
            {
                Log.Error($"[Projectile Tracking] Failed to create lock indicator: {ex.Message}\n{ex.StackTrace}");
                lockIndicatorMote = null;
            }
        }

        /// <summary>
        /// 销毁锁定指示器
        /// </summary>
        private void DestroyLockIndicator()
        {
            if (lockIndicatorMote != null && !lockIndicatorMote.Destroyed)
            {
                try
                {
                    lockIndicatorMote.Destroy();
                }
                catch (System.Exception ex)
                {
                    Log.Error($"[Projectile Tracking] Failed to destroy lock indicator mote: {ex.Message}");
                }
            }
            lockIndicatorMote = null;
        }


        // 新增：确保在保存时位置正确
        public override void PostMapInit()
        {
            base.PostMapInit();

            // 错误处理：序列化/反序列化错误 - 验证状态一致性
            if (initialized && horizontalPosition == Vector3.zero)
            {
                Log.Warning("[Projectile Tracking] Horizontal position is zero after loading. Resetting to origin position.");
                horizontalPosition = originPos;
            }

            // 验证追踪状态一致性
            if (enableTracking && trackingIntendedTarget != null && trackingIntendedTarget.Thing == null)
            {
                Log.Warning("[Projectile Tracking] Target reference is invalid after loading. Clearing tracking state.");
                trackingIntendedTarget = null;
                targetInit = false;
                lockIndicatorMote = null;
            }

            // 验证自定义高度曲线状态
            if (useCustomHeightCurve && (sortedHeightPoints == null || sortedHeightPoints.Count < 2))
            {
                Log.Warning("[Projectile Tracking] Custom height curve state is invalid after loading. Falling back to default parabola.");
                useCustomHeightCurve = false;
                sortedHeightPoints = null;
            }

            // 验证数值有效性
            if (totalTicks < 1)
            {
                Log.Warning($"[Projectile Tracking] Invalid totalTicks ({totalTicks}) after loading. Resetting to 1.");
                totalTicks = 1;
            }

            if (ticksFlying < 0)
            {
                Log.Warning($"[Projectile Tracking] Invalid ticksFlying ({ticksFlying}) after loading. Resetting to 0.");
                ticksFlying = 0;
            }

            // 验证地图引用
            if (Map == null)
            {
                Log.Error("[Projectile Tracking] Map is null after loading. Projectile will be destroyed.");
                this.Destroy();
                return;
            }

            // 验证位置在地图范围内
            if (initialized && !horizontalPosition.ToIntVec3().InBounds(Map))
            {
                Log.Warning($"[Projectile Tracking] Projectile position {horizontalPosition} is out of bounds after loading. Destroying projectile.");
                this.Destroy();
                return;
            }
        }
    }
}
