using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace SRA
{
    public class NorthArcModExtension : DefModExtension
    {
        // 控制向北偏移的高度（格数），值越大弧度越高
        public float northOffsetDistance = 10f;

        // 控制曲线的形状，值越大曲线越陡峭
        public float curveSteepness = 1f;

        // 是否使用弧形轨迹（默认为true，如果为false则使用直线轨迹）
        public bool useArcTrajectory = true;
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
                    def.Altitude + currentArcHeight * 0.3f, // 适当缩放高度
                    horizontalPosition.z
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
        }

        public override void Launch(Thing launcher, Vector3 origin, LocalTargetInfo usedTarget, LocalTargetInfo intendedTarget, ProjectileHitFlags hitFlags, bool preventFriendlyFire = false, Thing equipment = null, ThingDef targetCoverDef = null)
        {
            base.Launch(launcher, origin, usedTarget, intendedTarget, hitFlags, preventFriendlyFire, equipment, targetCoverDef);

            // 获取北向偏移配置
            NorthArcModExtension arcExtension = def.GetModExtension<NorthArcModExtension>();
            if (arcExtension != null)
            {
                northOffsetDistance = arcExtension.northOffsetDistance;
                curveSteepness = arcExtension.curveSteepness;
            }
            else
            {
                northOffsetDistance = def.projectile.arcHeightFactor * 3;
            }

            // --- 初始化弹道 ---
            originPos = origin;
            destinationPos = usedTarget.CenterVector3;

            float speed = def.projectile.speed;
            if (speed <= 0) speed = 1f;

            float distance = (originPos - destinationPos).MagnitudeHorizontal();
            totalTicks = Mathf.CeilToInt(distance / speed * 100f);
            if (totalTicks < 1) totalTicks = 1;

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
            base.Tick();

            if (this.Destroyed)
            {
                return;
            }

            if (!initialized)
            {
                return;
            }

            ticksFlying++;

            // 1. 计算当前帧的新位置
            float t = (float)ticksFlying / (float)totalTicks;
            if (t > 1f) t = 1f;

            float u = 1 - t;
            // 水平位移 (贝塞尔)
            Vector3 nextPos = (u * u * originPos) + (2 * u * t * bezierControlPoint) + (t * t * destinationPos);

            // 垂直高度 (抛物线)
            currentArcHeight = def.projectile.arcHeightFactor * GenMath.InverseParabola(t);
            horizontalPosition = nextPos; // 保存水平位置

            if (!nextPos.ToIntVec3().InBounds(base.Map))
            {
                this.Destroy();
                return;
            }

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

            if (ticksFlying >= totalTicks)
            {
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
            float heightAdjustment = currentArcHeight * 0.2f; // 缩放高度影响
            finalDrawPos.y += Mathf.Clamp(heightAdjustment, -0.5f, 2f);

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

        // 计算当前位置的切线方向（考虑高度变化）
        private Vector3 GetCurrentDirection()
        {
            if (!initialized || totalTicks <= 0)
            {
                return (destinationPos - originPos).normalized;
            }

            float t = (float)ticksFlying / (float)totalTicks;
            if (t > 1f) t = 1f;

            float u = 1 - t;
            Vector3 tangent = 2 * u * (bezierControlPoint - originPos) + 2 * t * (destinationPos - bezierControlPoint);

            if (tangent.MagnitudeHorizontalSquared() < 0.0001f)
            {
                return (destinationPos - originPos).normalized;
            }

            // 添加轻微的上/下方向以模拟抛物线
            float verticalComponent = GenMath.InverseParabola(t) * def.projectile.arcHeightFactor * 0.3f;
            return (tangent.normalized + new Vector3(0, verticalComponent, 0)).normalized;
        }

        protected override void Impact(Thing hitThing, bool blockedByShield = false)
        {
            base.Impact(hitThing, blockedByShield);
        }

        // 新增：确保在保存时位置正确
        public override void PostMapInit()
        {
            base.PostMapInit();

            // 确保位置数据有效
            if (initialized && horizontalPosition == Vector3.zero)
            {
                horizontalPosition = originPos;
            }
        }
    }
}
