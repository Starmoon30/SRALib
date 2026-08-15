using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using Verse;

namespace SRA
{
    public static class SRA_TurretRangeOverlayUtility
    {
        private const float MinRangeDisplayThreshold = 0.1f;

        public static VerbProperties FindSupportedTurretRangeVerb(ThingDef thingDef)
        {
            List<VerbProperties> verbs = thingDef?.building?.turretGunDef?.Verbs;
            if (verbs.NullOrEmpty())
            {
                return null;
            }

            for (int i = 0; i < verbs.Count; i++)
            {
                VerbProperties verbProperties = verbs[i];
                if (IsSupportedTurretRangeVerb(verbProperties))
                {
                    return verbProperties;
                }
            }

            return null;
        }

        public static bool IsSupportedTurretRangeVerb(VerbProperties verbProperties)
        {
            Type verbClass = verbProperties?.verbClass;
            return verbClass != null
                && (typeof(Verb_ShootWithOffset).IsAssignableFrom(verbClass)
                    || typeof(Verb_SRAShootBeam).IsAssignableFrom(verbClass));
        }

        public static void DrawTurretRangeRings(IntVec3 center, float maxRange, float minRange)
        {
            if (minRange >= maxRange)
            {
                return;
            }

            if (CanDrawRangeRing(maxRange))
            {
                GenDraw.DrawRadiusRing(center, maxRange);
            }

            if (minRange > MinRangeDisplayThreshold && CanDrawRangeRing(minRange))
            {
                GenDraw.DrawRadiusRing(center, minRange);
            }
        }

        private static bool CanDrawRangeRing(float radius)
        {
            return radius > 0f
                // GenDraw.DrawRadiusRing uses GenRadial.NumCellsInRadius internally;
                // radius >= MaxRadialPatternRadius would log an error.
                && radius < GenRadial.MaxRadialPatternRadius;
        }
    }

    public class PlaceWorker_ShowTurretWithOffsetRadius : PlaceWorker
    {
        public override AcceptanceReport AllowsPlacing(BuildableDef checkingDef, IntVec3 loc, Rot4 rot, Map map, Thing thingToIgnore = null, Thing thing = null)
        {
            VerbProperties verbProperties = SRA_TurretRangeOverlayUtility.FindSupportedTurretRangeVerb(checkingDef as ThingDef);
            if (verbProperties != null)
            {
                SRA_TurretRangeOverlayUtility.DrawTurretRangeRings(loc, verbProperties.range, verbProperties.minRange);
            }

            return true;
        }
    }
    /// <summary>
    /// 炮塔多炮管开火扩展。
    /// offsets 是核心数据：同一个 barrelIndex 会同时驱动投射物出生点、炮管制退和炮口火焰。
    /// </summary>
    public class ModExtension_ShootWithOffset : DefModExtension
    {
        public Vector2 GetOffsetFor(int index)
        {
            Vector2 result;
            if (this.offsets.NullOrEmpty<Vector2>())
            {
                result = Vector2.zero;
            }
            else
            {
                int index2 = index % this.offsets.Count;
                result = this.offsets[index2];
            }
            return result;
        }

        /// <summary>
        /// 可参与轮换的炮管数量。没有配置 offsets 时仍按单炮管处理。
        /// </summary>
        public int SlotCount
        {
            get
            {
                if (offsets.NullOrEmpty())
                {
                    return 1;
                }

                return offsets.Count;
            }
        }

        /// <summary>
        /// 将连发中的第几发映射到具体炮管，确保长 burst 会循环使用炮管。
        /// </summary>
        public int GetSlotIndexForShot(int shotIndex)
        {
            int slotCount = SlotCount;
            if (slotCount <= 0)
            {
                return 0;
            }

            return GenMath.PositiveMod(shotIndex, slotCount);
        }

        public bool HasBarrelRecoil => !barrelTexturePath.NullOrEmpty();

        public bool HasMuzzleFlash => !muzzleFlashTexturePath.NullOrEmpty() && muzzleFlashFrameCount > 0;

        public int MuzzleFlashDurationTicks => Mathf.Max(1, muzzleFlashFrameCount) * Mathf.Max(1, muzzleFlashTicksPerFrame);

        public Vector2 MuzzleFlashLocalOffset => new Vector2(muzzleFlashOffset.x, muzzleFlashOffset.y + muzzleFlashForwardOffset);

        /// <summary>
        /// 旧命名保留给外部代码兼容；现在只表示火焰视觉偏移，不参与 projectile 出口计算。
        /// </summary>
        public Vector2 MuzzleLocalOffset => MuzzleFlashLocalOffset;

        /// <summary>
        /// 统一基准点列表，也是炮管轮换顺序。
        /// x 表示相对发射基准点的左右偏移，y 表示沿射击方向的前后偏移。
        /// 对 Building_TurretGunHasSpeed，发射基准点是原版炮塔顶中心，也就是建筑 DrawPos + turretTopOffset。
        /// 这个位置会同时作为 projectile 出口、炮管贴图中心和炮口火焰微调基准。
        /// </summary>
        public List<Vector2> offsets = new List<Vector2>();

        /// <summary>
        /// 独立炮管贴图路径，不带文件扩展名。
        /// 留空时不会额外绘制炮管，也不会显示制退动画，仅保留 projectile offset。
        /// </summary>
        public string barrelTexturePath;

        /// <summary>
        /// 单根炮管贴图在地图上的绘制尺寸。
        /// x 是贴图横向长度，y 是贴图纵向厚度。
        /// 炮管贴图中心会贴在 offsets 定义的位置；制退时贴图中心会沿炮管后方移动 recoilAmount。
        /// </summary>
        public Vector2 barrelTextureSize = Vector2.one;

        /// <summary>
        /// 是否使用 MoteGlow 着色器绘制炮管。
        /// false 使用 DefaultShader，适合普通实体贴图；true 适合自发光炮管。
        /// </summary>
        public bool barrelUseGlowShader = false;

        /// <summary>
        /// 炮管贴图颜色乘算。
        /// 保持 white 时使用原贴图颜色。
        /// </summary>
        public Color barrelColor = Color.white;

        /// <summary>
        /// 炮管相对原版炮塔顶图的高度偏移。
        /// 会被限制在炮塔自身 altitudeLayer 到下一大层级之间；负数可低于炮塔顶图，但不会跨到树、地板等更低大层级。
        /// </summary>
        public float barrelAltitudeOffset = 0.05f;

        /// <summary>
        /// 最大制退距离。
        /// 开火时炮管会沿自身后方缩回这个距离，再逐渐回弹。
        /// </summary>
        public float recoilAmount = 0.5f;

        /// <summary>
        /// 制退动画总时长，单位 tick。
        /// 包含后坐阶段和回弹阶段。
        /// </summary>
        public int recoilDurationTicks = 20;

        /// <summary>
        /// 后坐阶段时长，单位 tick。
        /// 炮管会在这段时间内达到最大 recoilAmount，剩余时间用于回弹。
        /// </summary>
        public int recoilKickTicks = 5;

        /// <summary>
        /// 炮口火焰序列帧贴图路径，不带文件扩展名。
        /// 留空时不会绘制炮口火焰；支持横向或网格排列的 sprite sheet。
        /// </summary>
        public string muzzleFlashTexturePath;

        /// <summary>
        /// 炮口火焰单帧在地图上的绘制尺寸。
        /// 这是每一帧的实际显示大小，不是整张序列帧贴图的尺寸；x 是沿射击方向的长度，y 是横向宽度。
        /// 火焰贴图中心默认贴在 offsets 定义的基准点；需要前移时使用 muzzleFlashOffset 或 muzzleFlashForwardOffset。
        /// </summary>
        public Vector2 muzzleFlashDrawSize = Vector2.one;

        /// <summary>
        /// 炮口火焰相对 offsets 定义的基准点的局部偏移。
        /// x 表示左右微调，y 表示沿炮管方向前后微调，会与 muzzleFlashForwardOffset 叠加。
        /// 只影响火焰视觉，不影响 projectile 出口和炮管中心。
        /// </summary>
        public Vector2 muzzleFlashOffset = Vector2.zero;

        /// <summary>
        /// 炮口火焰从 offsets 定义的基准点向前移动的距离。
        /// 只影响火焰视觉，不影响 projectile 出口和炮管中心。
        /// </summary>
        public float muzzleFlashForwardOffset = 0f;

        /// <summary>
        /// 炮口火焰相对原版炮塔顶图的高度偏移。
        /// 通常应略高于 barrelAltitudeOffset，且同样会被限制在炮塔自身 altitudeLayer 内。
        /// </summary>
        public float muzzleFlashAltitudeOffset = 0.07f;

        /// <summary>
        /// 是否使用 MoteGlow 着色器绘制炮口火焰。
        /// 默认 true，适合发光火焰或能量武器闪光。
        /// </summary>
        public bool muzzleFlashUseGlowShader = true;

        /// <summary>
        /// 炮口火焰颜色乘算。
        /// 保持 white 时使用原贴图颜色，可用于把同一套火焰染成不同武器色。
        /// </summary>
        public Color muzzleFlashColor = Color.white;

        /// <summary>
        /// 序列帧总帧数。
        /// 例如 10000x1000 的横向 10 帧贴图应设置为 10。
        /// </summary>
        public int muzzleFlashFrameCount = 10;

        /// <summary>
        /// 序列帧每行的列数。
        /// 为 0 时视为 muzzleFlashFrameCount，也就是所有帧横向排成一行。
        /// </summary>
        public int muzzleFlashFrameColumns = 0;

        /// <summary>
        /// 每一帧持续的 tick 数。
        /// 总火焰时长为 muzzleFlashFrameCount * muzzleFlashTicksPerFrame。
        /// </summary>
        public int muzzleFlashTicksPerFrame = 1;
    }

    /// <summary>
    /// ShootWithOffset 的坐标换算集中在这里，避免 projectile、炮管和炮口火焰分别解释 offsets。
    /// 本系统约定 offset.x 是朝向右侧，offset.y 是沿炮口正前方。
    /// </summary>
    public static class SRA_ShootWithOffsetUtility
    {
        public static Vector3 LocalOffsetVector(Vector2 localOffset)
        {
            return new Vector3(localOffset.x, 0f, localOffset.y);
        }

        public static Quaternion OffsetRotation(float aimAngle)
        {
            return aimAngle.ToQuat();
        }

        public static Quaternion TurretGraphicRotation(float aimAngle)
        {
            return (TurretTop.ArtworkRotation + aimAngle).ToQuat();
        }

        public static Vector3 LocalOffsetToWorld(Vector3 origin, float aimAngle, Vector2 localOffset)
        {
            return origin + OffsetRotation(aimAngle) * LocalOffsetVector(localOffset);
        }

        public static Vector3 TurretTopCenter(Building_Turret turret, Vector3 drawLoc, Vector3 recoilDrawOffset, float recoilAngleOffset)
        {
            Vector2 turretTopOffset = Vector2.zero;
            if (turret?.def?.building != null)
            {
                turretTopOffset = turret.def.building.turretTopOffset;
            }

            Vector3 localTopOffset = new Vector3(turretTopOffset.x, 0f, turretTopOffset.y);
            localTopOffset = localTopOffset.RotatedBy(recoilAngleOffset);
            return drawLoc + localTopOffset + recoilDrawOffset;
        }

        public static Vector3 TurretTopCenter(Building_Turret turret, Vector3 drawLoc)
        {
            return TurretTopCenter(turret, drawLoc, Vector3.zero, 0f);
        }
    }

    /// <summary>
    /// 为序列帧贴图缓存不同 UV 的平面 mesh。
    /// 不修改共享 Material 的 mainTextureOffset，避免多个炮塔同帧绘制时互相污染。
    /// 也支持把视觉平面偏移到局部位置，但让 mesh.bounds.center 保持在原点，用父炮塔中心参与透明排序。
    /// </summary>
    public static class SRA_FrameMeshPool
    {
        // 1 / 512 格的量化误差远小于贴图抖动可见范围，同时能避免 recoil 浮点误差制造无限缓存键。
        private const float MeshQuantization = 512f;

        private static readonly Dictionary<int, Mesh> frameMeshes = new Dictionary<int, Mesh>();

        private static readonly Dictionary<AnchoredMeshKey, Mesh> anchoredMeshes = new Dictionary<AnchoredMeshKey, Mesh>();

        public static Mesh GetFrameMesh(int frameIndex, int frameCount, int frameColumns)
        {
            frameCount = Mathf.Max(1, frameCount);
            frameColumns = frameColumns > 0 ? frameColumns : frameCount;
            frameColumns = Mathf.Max(1, frameColumns);
            int rows = Mathf.Max(1, Mathf.CeilToInt((float)frameCount / frameColumns));
            frameIndex = Mathf.Clamp(frameIndex, 0, frameCount - 1);
            int key = GetKey(frameIndex, frameCount, frameColumns);
            if (!frameMeshes.TryGetValue(key, out Mesh mesh))
            {
                int column = frameIndex % frameColumns;
                int row = frameIndex / frameColumns;
                float width = 1f / frameColumns;
                float height = 1f / rows;

                // Unity UV 原点在左下；row 从上往下数，所以这里要翻转 y。
                Rect uvRect = new Rect(column * width, 1f - (row + 1) * height, width, height);
                Printer_Plane.GetUVs(uvRect, out Vector2 uv1, out Vector2 uv2, out Vector2 uv3, out Vector2 uv4, false);
                mesh = new Mesh
                {
                    name = "SRA_FrameMesh_" + frameIndex + "_" + frameCount + "_" + frameColumns,
                    vertices = new[]
                    {
                        new Vector3(-0.5f, 0f, -0.5f),
                        new Vector3(-0.5f, 0f, 0.5f),
                        new Vector3(0.5f, 0f, 0.5f),
                        new Vector3(0.5f, 0f, -0.5f)
                    },
                    uv = new[]
                    {
                        uv1,
                        uv2,
                        uv3,
                        uv4
                    },
                    triangles = new[]
                    {
                        0,
                        1,
                        2,
                        0,
                        2,
                        3
                    }
                };
                mesh.RecalculateBounds();
                frameMeshes.Add(key, mesh);
            }

            return mesh;
        }

        public static Mesh GetAnchoredMesh(Vector3 localCenter, Vector2 drawSize)
        {
            return GetAnchoredFrameMesh(localCenter, drawSize, 0, 1, 1);
        }

        public static Mesh GetAnchoredFrameMesh(Vector3 localCenter, Vector2 drawSize, int frameIndex, int frameCount, int frameColumns)
        {
            frameCount = Mathf.Max(1, frameCount);
            frameColumns = frameColumns > 0 ? frameColumns : frameCount;
            frameColumns = Mathf.Max(1, frameColumns);
            frameIndex = Mathf.Clamp(frameIndex, 0, frameCount - 1);

            float width = Mathf.Max(0.001f, drawSize.x);
            float height = Mathf.Max(0.001f, drawSize.y);
            AnchoredMeshKey key = new AnchoredMeshKey(localCenter, width, height, frameIndex, frameCount, frameColumns);
            if (!anchoredMeshes.TryGetValue(key, out Mesh mesh))
            {
                mesh = MakeAnchoredMesh(localCenter, width, height, frameIndex, frameCount, frameColumns);
                anchoredMeshes.Add(key, mesh);
            }

            return mesh;
        }

        private static Mesh MakeAnchoredMesh(Vector3 localCenter, float drawWidth, float drawHeight, int frameIndex, int frameCount, int frameColumns)
        {
            int rows = Mathf.Max(1, Mathf.CeilToInt((float)frameCount / frameColumns));
            int column = frameIndex % frameColumns;
            int row = frameIndex / frameColumns;
            float uvWidth = 1f / frameColumns;
            float uvHeight = 1f / rows;

            Rect uvRect = new Rect(column * uvWidth, 1f - (row + 1) * uvHeight, uvWidth, uvHeight);
            Printer_Plane.GetUVs(uvRect, out Vector2 uv1, out Vector2 uv2, out Vector2 uv3, out Vector2 uv4, false);

            float halfWidth = drawWidth * 0.5f;
            float halfHeight = drawHeight * 0.5f;
            Mesh mesh = new Mesh
            {
                name = "SRA_AnchoredFrameMesh_" + frameIndex + "_" + frameCount + "_" + frameColumns,
                vertices = new[]
                {
                    new Vector3(localCenter.x - halfWidth, localCenter.y, localCenter.z - halfHeight),
                    new Vector3(localCenter.x - halfWidth, localCenter.y, localCenter.z + halfHeight),
                    new Vector3(localCenter.x + halfWidth, localCenter.y, localCenter.z + halfHeight),
                    new Vector3(localCenter.x + halfWidth, localCenter.y, localCenter.z - halfHeight)
                },
                uv = new[]
                {
                    uv1,
                    uv2,
                    uv3,
                    uv4
                },
                triangles = new[]
                {
                    0,
                    1,
                    2,
                    0,
                    2,
                    3
                }
            };

            // 关键点：bounds.center 留在原点，透明排序锚在父炮塔 transform；
            // extents 扩大到覆盖偏移后的顶点，避免炮管/火焰伸出锚点后被视锥裁剪。
            float extentX = Mathf.Abs(localCenter.x) + halfWidth;
            float extentY = Mathf.Abs(localCenter.y) + 0.005f;
            float extentZ = Mathf.Abs(localCenter.z) + halfHeight;
            mesh.bounds = new Bounds(Vector3.zero, new Vector3(extentX * 2f, extentY * 2f, extentZ * 2f));
            return mesh;
        }

        private static int GetKey(int frameIndex, int frameCount, int frameColumns)
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + frameIndex;
                hash = hash * 31 + frameCount;
                hash = hash * 31 + frameColumns;
                return hash;
            }
        }

        private static int Quantize(float value)
        {
            return Mathf.RoundToInt(value * MeshQuantization);
        }

        private struct AnchoredMeshKey : IEquatable<AnchoredMeshKey>
        {
            private readonly int localCenterX;

            private readonly int localCenterY;

            private readonly int localCenterZ;

            private readonly int drawWidth;

            private readonly int drawHeight;

            private readonly int frameIndex;

            private readonly int frameCount;

            private readonly int frameColumns;

            public AnchoredMeshKey(Vector3 localCenter, float drawWidth, float drawHeight, int frameIndex, int frameCount, int frameColumns)
            {
                this.localCenterX = Quantize(localCenter.x);
                this.localCenterY = Quantize(localCenter.y);
                this.localCenterZ = Quantize(localCenter.z);
                this.drawWidth = Quantize(drawWidth);
                this.drawHeight = Quantize(drawHeight);
                this.frameIndex = frameIndex;
                this.frameCount = frameCount;
                this.frameColumns = frameColumns;
            }

            public bool Equals(AnchoredMeshKey other)
            {
                return localCenterX == other.localCenterX
                    && localCenterY == other.localCenterY
                    && localCenterZ == other.localCenterZ
                    && drawWidth == other.drawWidth
                    && drawHeight == other.drawHeight
                    && frameIndex == other.frameIndex
                    && frameCount == other.frameCount
                    && frameColumns == other.frameColumns;
            }

            public override bool Equals(object obj)
            {
                return obj is AnchoredMeshKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = 17;
                    hash = hash * 31 + localCenterX;
                    hash = hash * 31 + localCenterY;
                    hash = hash * 31 + localCenterZ;
                    hash = hash * 31 + drawWidth;
                    hash = hash * 31 + drawHeight;
                    hash = hash * 31 + frameIndex;
                    hash = hash * 31 + frameCount;
                    hash = hash * 31 + frameColumns;
                    return hash;
                }
            }
        }
    }
    public class Verb_ShootWithOffset : Verb_Shoot
    {
        public int offset = 0;

        public CompSustainedShoot CompSustainedShoot
        {
            get
            {
                return base.EquipmentSource?.TryGetComp<CompSustainedShoot>();
            }
        }

        protected override int ShotsPerBurst
        {
            get
            {
                Comp_HNGT_GlobalBallisticAttack remoteArtilleryComp = GetRemoteArtilleryComp();
                if (remoteArtilleryComp != null && remoteArtilleryComp.IsFiringInterMap)
                {
                    return Mathf.Max(1, remoteArtilleryComp.RemoteBurstShotCount);
                }

                CompSustainedShoot compSustainedShoot = this.CompSustainedShoot;
                bool hasCachedShots = this.state == VerbState.Idle && compSustainedShoot != null && compSustainedShoot.cachedBurstShotsLeft >= 1;
                if (hasCachedShots)
                {
                    return compSustainedShoot.cachedBurstShotsLeft;
                }

                return base.BurstShotCount;
            }
        }

        public int BurstShotsLeft
        {
            get
            {
                return this.burstShotsLeft;
            }
        }

        public override void OrderForceTarget(LocalTargetInfo target)
        {
            if (this.CompSustainedShoot == null)
            {
                base.OrderForceTarget(target);
                return;
            }

            this.forceTargetedDownedPawn = null;
            base.OrderForceTarget(target);
            if (target.Pawn != null && target.Pawn.Downed && target.Pawn.Spawned)
            {
                this.forceTargetedDownedPawn = target.Pawn;
            }

            this.currentTarget = target;
        }

        public override void WarmupComplete()
        {
            if (IsRemoteArtilleryActive)
            {
                base.WarmupComplete();
                return;
            }

            CompSustainedShoot compSustainedShoot = this.CompSustainedShoot;
            if (compSustainedShoot == null)
            {
                base.WarmupComplete();
                return;
            }

            compSustainedShoot.Notify_SustainedVerbStarted();
            this.burstShotsLeft = this.ShotsPerBurst;
            this.state = VerbState.Bursting;
            base.TryCastNextBurstShot();
            compSustainedShoot.Notify_SustainedBurstProgress(this.burstShotsLeft);
            Pawn pawn = this.currentTarget.Thing as Pawn;
            if (pawn != null && !pawn.Downed && !pawn.IsColonyMech && this.CasterIsPawn && this.CasterPawn.skills != null && this.burstShotsLeft == base.BurstShotCount)
            {
                float baseExperience = pawn.HostileTo(this.caster) ? 170f : 20f;
                float cycleTime = this.verbProps.AdjustedFullCycleTime(this, this.CasterPawn);
                this.CasterPawn.skills.Learn(SkillDefOf.Shooting, baseExperience * cycleTime, false, false);
            }
        }

        public override void BurstingTick()
        {
            base.BurstingTick();
            if (!IsRemoteArtilleryActive)
            {
                this.CompSustainedShoot?.Notify_SustainedBurstProgress(this.burstShotsLeft);
            }
        }

        public void ResetPreservingCastCompleteCallback()
        {
            // Vanilla Verb.Reset clears castCompleteCallback, but turrets need it to restore burst cooldown.
            Action callback = this.castCompleteCallback;
            base.Reset();
            this.castCompleteCallback = callback;
            this.forceTargetedDownedPawn = null;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look<Pawn>(ref this.forceTargetedDownedPawn, "forceTargetedDownedPawn", false);
        }

        protected override bool TryCastShot()
        {
            bool num = BaseTryCastShot();
            if (num && CasterIsPawn)
            {
                CasterPawn.records.Increment(RecordDefOf.ShotsFired);
            }

            return num;
        }
        protected bool BaseTryCastShot()
        {

            if (currentTarget.HasThing && currentTarget.Thing.Map != caster.Map)
            {
                return false;
            }

            ThingDef projectile = Projectile;
            if (projectile == null)
            {
                return false;
            }

            Comp_HNGT_GlobalBallisticAttack remoteArtilleryComp = (caster as ThingWithComps)?.GetComp<Comp_HNGT_GlobalBallisticAttack>();
            if (remoteArtilleryComp != null && remoteArtilleryComp.IsFiringInterMap)
            {
                return TryCastRemoteArtilleryFakeShot(projectile);
            }

            ShootLine resultingLine;
            bool flag = TryFindShootLineFromTo(caster.Position, currentTarget, out resultingLine);
            if (verbProps.stopBurstWithoutLos && !flag)
            {
                return false;
            }

            if (base.EquipmentSource != null)
            {
                TryConsumeEquipmentShot(requireLoadedAmmo: false);
            }

            lastShotTick = Find.TickManager.TicksGame;
            Thing manningPawn = caster;
            Thing equipmentSource = base.EquipmentSource;
            CompMannable compMannable = caster.TryGetComp<CompMannable>();
            if (compMannable?.ManningPawn != null)
            {
                manningPawn = compMannable.ManningPawn;
                equipmentSource = caster;
            }

            Vector3 drawPos = caster.DrawPos;
            drawPos = ApplyProjectileOffset(drawPos, equipmentSource);
            Projectile projectile2 = (Projectile)GenSpawn.Spawn(projectile, resultingLine.Source, caster.Map);
            if (equipmentSource != null && equipmentSource.TryGetComp(out CompUniqueWeapon comp))
            {
                foreach (WeaponTraitDef item in comp.TraitsListForReading)
                {
                    if (item.damageDefOverride != null)
                    {
                        projectile2.damageDefOverride = item.damageDefOverride;
                    }

                    if (!item.extraDamages.NullOrEmpty())
                    {
                        Projectile projectile3 = projectile2;
                        if (projectile3.extraDamages == null)
                        {
                            projectile3.extraDamages = new List<ExtraDamage>();
                        }

                        projectile2.extraDamages.AddRange(item.extraDamages);
                    }
                }
            }

            if (verbProps.ForcedMissRadius > 0.5f)
            {
                float num = verbProps.ForcedMissRadius;
                if (manningPawn is Pawn pawn)
                {
                    num *= verbProps.GetForceMissFactorFor(equipmentSource, pawn);
                }

                float num2 = VerbUtility.CalculateAdjustedForcedMiss(num, currentTarget.Cell - caster.Position);
                if (num2 > 0.5f)
                {
                    IntVec3 forcedMissTarget = GetForcedMissTarget(num2);
                    if (forcedMissTarget != currentTarget.Cell)
                    {
                        ProjectileHitFlags projectileHitFlags = ProjectileHitFlags.NonTargetWorld;
                        if (Rand.Chance(0.5f))
                        {
                            projectileHitFlags = ProjectileHitFlags.All;
                        }

                        if (!canHitNonTargetPawnsNow)
                        {
                            projectileHitFlags &= ~ProjectileHitFlags.NonTargetPawns;
                        }

                        projectile2.Launch(manningPawn, drawPos, forcedMissTarget, currentTarget, projectileHitFlags, preventFriendlyFire, equipmentSource);
                        return true;
                    }
                }
            }

            ShotReport shotReport = ShotReport.HitReportFor(caster, this, currentTarget);
            Thing randomCoverToMissInto = shotReport.GetRandomCoverToMissInto();
            ThingDef targetCoverDef = randomCoverToMissInto?.def;
            if (verbProps.canGoWild && !Rand.Chance(shotReport.AimOnTargetChance_IgnoringPosture))
            {
                bool flyOverhead = projectile2?.def?.projectile != null && projectile2.def.projectile.flyOverhead;
                resultingLine.ChangeDestToMissWild(shotReport.AimOnTargetChance_StandardTarget, flyOverhead, caster.Map);
                ProjectileHitFlags projectileHitFlags2 = ProjectileHitFlags.NonTargetWorld;
                if (Rand.Chance(0.5f) && canHitNonTargetPawnsNow)
                {
                    projectileHitFlags2 |= ProjectileHitFlags.NonTargetPawns;
                }

                projectile2.Launch(manningPawn, drawPos, resultingLine.Dest, currentTarget, projectileHitFlags2, preventFriendlyFire, equipmentSource, targetCoverDef);
                return true;
            }

            if (currentTarget.Thing != null && currentTarget.Thing.def.CanBenefitFromCover && !Rand.Chance(shotReport.PassCoverChance))
            {
                ProjectileHitFlags projectileHitFlags3 = ProjectileHitFlags.NonTargetWorld;
                if (canHitNonTargetPawnsNow)
                {
                    projectileHitFlags3 |= ProjectileHitFlags.NonTargetPawns;
                }

                projectile2.Launch(manningPawn, drawPos, randomCoverToMissInto, currentTarget, projectileHitFlags3, preventFriendlyFire, equipmentSource, targetCoverDef);
                return true;
            }

            ProjectileHitFlags projectileHitFlags4 = ProjectileHitFlags.IntendedTarget;
            if (canHitNonTargetPawnsNow)
            {
                projectileHitFlags4 |= ProjectileHitFlags.NonTargetPawns;
            }

            if (!currentTarget.HasThing || currentTarget.Thing.def.Fillage == FillCategory.Full)
            {
                projectileHitFlags4 |= ProjectileHitFlags.NonTargetWorld;
            }
            if (currentTarget.Thing != null)
            {
                projectile2.Launch(manningPawn, drawPos, currentTarget, currentTarget, projectileHitFlags4, preventFriendlyFire, equipmentSource, targetCoverDef);
            }
            else
            {
                projectile2.Launch(manningPawn, drawPos, resultingLine.Dest, currentTarget, projectileHitFlags4, preventFriendlyFire, equipmentSource, targetCoverDef);
            }
            return true;
        }

        private bool TryCastRemoteArtilleryFakeShot(ThingDef projectileDef)
        {
            if (caster?.Map == null || !currentTarget.IsValid)
            {
                return false;
            }

            if (!TryConsumeEquipmentShot(requireLoadedAmmo: true))
            {
                return false;
            }

            Thing manningPawn = caster;
            Thing equipmentSource = base.EquipmentSource;
            CompMannable compMannable = caster.TryGetComp<CompMannable>();
            if (compMannable?.ManningPawn != null)
            {
                manningPawn = compMannable.ManningPawn;
                equipmentSource = caster;
            }

            Vector3 launchPos = ApplyProjectileOffset(caster.DrawPos, equipmentSource);
            IntVec3 spawnCell = launchPos.ToIntVec3();
            if (!spawnCell.InBounds(caster.Map))
            {
                spawnCell = caster.Position;
            }

            Thing spawnedThing = GenSpawn.Spawn(projectileDef, spawnCell, caster.Map, WipeMode.Vanish);
            if (!(spawnedThing is Projectile fakeProjectile))
            {
                spawnedThing.Destroy(DestroyMode.Vanish);
                return false;
            }

            fakeProjectile.Launch(
                manningPawn,
                launchPos,
                currentTarget.Cell,
                currentTarget,
                ProjectileHitFlags.None,
                false,
                equipmentSource,
                null);
            lastShotTick = Find.TickManager.TicksGame;
            return true;
        }

        private bool TryConsumeEquipmentShot(bool requireLoadedAmmo)
        {
            CompChangeableProjectile compChangeableProjectile = base.EquipmentSource?.GetComp<CompChangeableProjectile>();
            CompRefuelable refuelableAmmo = requireLoadedAmmo ? GetRemoteArtilleryFuelComp() : null;
            if (requireLoadedAmmo)
            {
                if (compChangeableProjectile != null && !compChangeableProjectile.Loaded)
                {
                    return false;
                }

                if (refuelableAmmo != null && refuelableAmmo.Fuel < Comp_HNGT_GlobalBallisticAttack.RemoteFuelPerFakeShot)
                {
                    return false;
                }
            }

            if (compChangeableProjectile != null)
            {
                compChangeableProjectile.Notify_ProjectileLaunched();
            }

            if (refuelableAmmo != null)
            {
                refuelableAmmo.ConsumeFuel(Comp_HNGT_GlobalBallisticAttack.RemoteFuelPerFakeShot);
            }

            base.EquipmentSource?.GetComp<CompApparelVerbOwner_Charged>()?.UsedOnce();
            return true;
        }

        private CompRefuelable GetRemoteArtilleryFuelComp()
        {
            Building_TurretGunHasSpeed turret = caster as Building_TurretGunHasSpeed;
            if (turret == null)
            {
                return null;
            }

            Comp_HNGT_GlobalBallisticAttack remoteArtilleryComp = turret.GetComp<Comp_HNGT_GlobalBallisticAttack>();
            if (remoteArtilleryComp == null || !remoteArtilleryComp.IsFiringInterMap)
            {
                return null;
            }

            CompRefuelable refuelable = turret.refuelableComp ?? turret.TryGetComp<CompRefuelable>();
            if (refuelable == null || refuelable.Props == null || !refuelable.Props.consumeFuelOnlyWhenUsed)
            {
                return null;
            }

            return refuelable;
        }

        protected Vector3 ApplyProjectileOffset(Vector3 originalDrawPos, Thing equipmentSource)
        {
            ModExtension_ShootWithOffset offsetExtension = GetShootWithOffsetExtension(equipmentSource);
            if (offsetExtension == null)
            {
                return originalDrawPos;
            }

            if (caster is Building_TurretGunHasSpeed turret)
            {
                // 转速炮塔的视觉炮管使用 curAngle，因此 projectile 也使用同一个旋转基准。
                int barrelIndex = offsetExtension.GetSlotIndexForShot(GetCurrentShotIndex());
                Vector2 visualOffset = offsetExtension.GetOffsetFor(barrelIndex);
                turret.Notify_BarrelFired(barrelIndex);
                float aimAngle = AimAngleOverride ?? turret.curAngle;
                Vector3 origin = SRA_ShootWithOffsetUtility.TurretTopCenter(turret, originalDrawPos);
                return SRA_ShootWithOffsetUtility.LocalOffsetToWorld(origin, aimAngle, visualOffset);
            }

            // 使用目标方向计算偏移，保留旧版 Verb_ShootWithOffset 的开火定位语义。
            Vector3 targetPos = currentTarget.CenterVector3;
            Vector3 casterPos = caster.DrawPos;
            float rimworldAngle = targetPos.AngleToFlat(casterPos);
            float correctedAngle = ConvertRimWorldAngleToOffsetAngle(rimworldAngle);
            Vector2 legacyOffset = offsetExtension.GetOffsetFor(GetLegacyShotIndex());
            Vector2 rotatedOffset = legacyOffset.RotatedBy(correctedAngle);
            return originalDrawPos + new Vector3(rotatedOffset.x, 0f, rotatedOffset.y);
        }

        protected ModExtension_ShootWithOffset GetShootWithOffsetExtension(Thing equipmentSource)
        {
            // 炮塔常见情况：扩展写在 turretGunDef 上，开火时 EquipmentSource 就是这把 gun。
            ModExtension_ShootWithOffset extension = base.EquipmentSource?.def.GetModExtension<ModExtension_ShootWithOffset>();
            if (extension != null)
            {
                return extension;
            }

            // 兼容载具/炮塔让 equipmentSource 指向 caster 的情况。
            extension = equipmentSource?.def.GetModExtension<ModExtension_ShootWithOffset>();
            if (extension != null)
            {
                return extension;
            }

            // 最后允许直接写在 caster def 上，方便特殊建筑只定义一处。
            return caster?.def.GetModExtension<ModExtension_ShootWithOffset>();
        }

        protected int GetCurrentShotIndex()
        {
            // 原版 TryCastNextBurstShot 在每发成功后才递减 burstShotsLeft。
            // 因此开火前第 N 发的索引是 BurstShotCount - burstShotsLeft。
            int shotIndex = GetCurrentBurstShotCount() - burstShotsLeft;
            if (shotIndex >= 0)
            {
                return shotIndex;
            }

            return 0;
        }

        private int GetCurrentBurstShotCount()
        {
            Comp_HNGT_GlobalBallisticAttack remoteArtilleryComp = GetRemoteArtilleryComp();
            if (remoteArtilleryComp != null && remoteArtilleryComp.IsFiringInterMap)
            {
                return Mathf.Max(1, remoteArtilleryComp.RemoteBurstShotCount);
            }

            return BurstShotCount;
        }

        private Comp_HNGT_GlobalBallisticAttack GetRemoteArtilleryComp()
        {
            return (caster as ThingWithComps)?.GetComp<Comp_HNGT_GlobalBallisticAttack>();
        }

        private bool IsRemoteArtilleryActive
        {
            get
            {
                return GetRemoteArtilleryComp()?.IsFiringInterMap ?? false;
            }
        }

        protected int GetLegacyShotIndex()
        {
            // 非 Building_TurretGunHasSpeed 继续沿用旧版索引语义。
            if (burstShotsLeft >= 0)
            {
                return burstShotsLeft;
            }

            return 0;
        }

        /// <summary>
        /// 将旧版非转速炮塔路径使用的 AngleToFlat 结果转换为 Vector2.RotatedBy 需要的角度。
        /// Building_TurretGunHasSpeed 使用上面的统一 turretTopOffset + curAngle 路径。
        /// </summary>
        /// <param name="rimworldAngle">RimWorld角度</param>
        /// <returns>转换后的角度</returns>
        private float ConvertRimWorldAngleToOffsetAngle(float rimworldAngle)
        {
            // RimWorld角度：0°=东，90°=北，180°=西，270°=南
            // 转换为：0°=东，90°=南，180°=西，270°=北
            return -rimworldAngle - 90f;
        }

        public Pawn forceTargetedDownedPawn;

    }
}
