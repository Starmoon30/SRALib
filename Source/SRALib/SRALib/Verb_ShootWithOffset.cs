using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using Verse;
using Verse.Sound;

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
    /// <summary>
    /// 同轮多射弹时的资源消耗策略。
    /// </summary>
    public enum SRAProjectileAmmoConsumptionMode
    {
        /// <summary>
        /// 整轮齐射只消耗一次资源，适合一枚弹壳产生多枚散弹的武器。
        /// </summary>
        perVolley,

        /// <summary>
        /// 每个实际生成的 projectile 都消耗一次资源，适合多联导弹或多管齐射。
        /// </summary>
        perProjectile
    }

    /// <summary>
    /// 射弹在弹种按钮、菜单和 tooltip 中使用的显示配置。
    /// </summary>
    public class SRAProjectileDisplay
    {
        /// <summary>
        /// 可选的 Keyed 本地化名称。留空时使用 projectile 自身的本地化名称。
        /// </summary>
        public string labelKey;

        /// <summary>
        /// 可选的 Keyed 本地化描述。留空时使用 projectile 自身的本地化描述。
        /// </summary>
        public string descriptionKey;

        /// <summary>
        /// 可选的按钮贴图路径，不带文件扩展名。留空时使用 projectile.uiIcon。
        /// </summary>
        public string iconPath;

        /// <summary>
        /// 弹种选择按钮内图标的绘制缩放。默认 1，与原版 Command 的标准图标尺寸一致。
        /// 仅影响本弹种选择 Gizmo，不会影响射弹在地图或物品栏中的显示。
        /// </summary>
        public float iconDrawScale = 1f;
    }

    /// <summary>
    /// 可手动选择的替代射弹模式。
    /// 默认射弹不需要写入这里，直接沿用 VerbProperties.defaultProjectile。
    /// </summary>
    public class SRAProjectileMode : SRAProjectileDisplay
    {
        /// <summary>
        /// 选中该模式时实际发射的 projectile。
        /// </summary>
        public ThingDef projectile;
    }

    /// <summary>
    /// 为 Verb_ShootWithOffset 提供弹种切换和同轮多射弹能力。
    /// </summary>
    public class VerbProperties_SRAMultiProjectile : VerbProperties
    {
        /// <summary>
        /// 每次常规开火额外生成的 projectile 数。0 表示完全遵循原版单发行为。
        /// 实际射弹总数始终为 1 + additionalProjectilesPerShot。
        /// </summary>
        public int additionalProjectilesPerShot = 0;

        /// <summary>
        /// 同轮多射弹时的资源消耗方式。默认每轮只消耗一次，适合霰弹等单发多弹头武器。
        /// </summary>
        public SRAProjectileAmmoConsumptionMode ammoConsumptionMode = SRAProjectileAmmoConsumptionMode.perVolley;

        /// <summary>
        /// 默认射弹在弹种按钮、菜单和 tooltip 中使用的可选显示覆盖。
        /// 不包含 projectile 字段，实际默认射弹仍由继承的 defaultProjectile 决定。
        /// </summary>
        public SRAProjectileDisplay defaultProjectileDisplay;

        /// <summary>
        /// 除 defaultProjectile 外可供玩家切换的替代射弹列表。
        /// </summary>
        public List<SRAProjectileMode> alternativeProjectiles;
    }

    /// <summary>
    /// 为带 VerbProperties_SRAMultiProjectile 的武器或炮塔 gun 添加特殊细则。
    /// </summary>
    public static class SRAMultiProjectileStatsUtility
    {
        private const int ProjectileCountDisplayPriority = 5550;

        public static bool HasMultiProjectileVerb(ThingDef def)
        {
            List<VerbProperties> verbs = GetVerbs(def);
            if (verbs == null)
            {
                return false;
            }

            for (int i = 0; i < verbs.Count; i++)
            {
                if (verbs[i] is VerbProperties_SRAMultiProjectile)
                {
                    return true;
                }
            }

            return false;
        }

        public static IEnumerable<StatDrawEntry> AppendSpecialDisplayStats(IEnumerable<StatDrawEntry> source, ThingDef def)
        {
            if (source != null)
            {
                foreach (StatDrawEntry entry in source)
                {
                    yield return entry;
                }
            }

            List<VerbProperties> verbs = GetVerbs(def);
            if (verbs == null)
            {
                yield break;
            }

            StatCategoryDef category = def != null && def.category == ThingCategory.Pawn
                ? StatCategoryDefOf.PawnCombat
                : StatCategoryDefOf.Weapon_Ranged;
            int multiProjectileVerbIndex = 0;
            for (int i = 0; i < verbs.Count; i++)
            {
                VerbProperties_SRAMultiProjectile props = verbs[i] as VerbProperties_SRAMultiProjectile;
                if (props == null)
                {
                    continue;
                }

                int projectileCount = Mathf.Max(1, props.additionalProjectilesPerShot + 1);
                string value = "SRA_ProjectilesPerShotValue".Translate(projectileCount);
                yield return new StatDrawEntry(
                    category,
                    "SRA_ProjectilesPerShotLabel".Translate(),
                    value,
                    "SRA_ProjectilesPerShotDesc".Translate(),
                    ProjectileCountDisplayPriority - multiProjectileVerbIndex * 100);
                multiProjectileVerbIndex++;
            }
        }

        private static List<VerbProperties> GetVerbs(ThingDef def)
        {
            if (def?.Verbs != null && def.Verbs.Count > 0)
            {
                return def.Verbs;
            }

            return def?.building?.turretGunDef?.Verbs;
        }
    }

    public class Verb_ShootWithOffset : Verb_Shoot
    {
        public int offset = 0;

        // null 表示沿用原版 Projectile 属性，也就是 defaultProjectile 或已装填的可换弹药。
        // 只保存替代射弹 Def，而不保存列表下标，避免 XML 调整顺序后切换到错误的模式。
        private ThingDef selectedAlternativeProjectile;

        public CompSustainedShoot CompSustainedShoot
        {
            get
            {
                return base.EquipmentSource?.TryGetComp<CompSustainedShoot>();
            }
        }

        public VerbProperties_SRAMultiProjectile MultiProjectileProps => verbProps as VerbProperties_SRAMultiProjectile;

        /// <summary>
        /// 当前一轮开火实际生成的 projectile 数。additionalProjectilesPerShot 是额外数量，
        /// 因此即使没有配置多射弹，也始终至少为 1。
        /// </summary>
        public int ProjectilesPerShot => Mathf.Max(1, (MultiProjectileProps?.additionalProjectilesPerShot ?? 0) + 1);

        public bool HasProjectileSelection => HasAlternativeProjectileModes();

        public ThingDef SelectedProjectile => ResolveSelectedProjectile();

        /// <summary>
        /// 生成当前选择射弹的按钮。Pawn 装备通过 CompEquippable 补丁调用，
        /// Building_TurretGunHasSpeed 则直接转发该方法。
        /// </summary>
        public IEnumerable<Gizmo> GetMultiProjectileGizmos()
        {
            if (!HasAlternativeProjectileModes())
            {
                yield break;
            }

            ThingDef projectile = ResolveSelectedProjectile();
            SRAProjectileDisplay display = GetSelectedProjectileDisplay(projectile);
            Command_Action command = new Command_Action
            {
                defaultLabel = GetProjectileLabel(projectile, display),
                defaultDesc = BuildProjectileTooltip(projectile, display),
                icon = GetProjectileIcon(projectile, display),
                iconAngle = projectile?.uiIconAngle ?? 0f,
                iconOffset = projectile?.uiIconOffset ?? Vector2.zero,
                // 原版 Command 的图标默认按 1.0 绘制。不能把射弹 Def 的 uiIconScale
                // 套到 SRAProjectileDisplay 的自定义图标上，否则同一按钮会比原版 Gizmo 偏大或偏小。
                iconDrawScale = Mathf.Max(0.01f, display?.iconDrawScale ?? 1f),
                action = OpenProjectileSelectionMenu
            };

            // 弹种切换只影响下一轮射击，不能在同一 burst 中途改变已经开始的弹种。
            if (state == VerbState.Bursting)
            {
                command.Disable("SRA_ProjectileSelectionUnavailableDuringBurst".Translate());
            }

            yield return command;
        }

        private bool HasAlternativeProjectileModes()
        {
            List<SRAProjectileMode> modes = MultiProjectileProps?.alternativeProjectiles;
            if (modes == null)
            {
                return false;
            }

            for (int i = 0; i < modes.Count; i++)
            {
                if (modes[i]?.projectile != null)
                {
                    return true;
                }
            }

            return false;
        }

        private ThingDef ResolveSelectedProjectile()
        {
            NormalizeSelectedAlternativeProjectile();
            return selectedAlternativeProjectile ?? base.Projectile;
        }

        private void NormalizeSelectedAlternativeProjectile()
        {
            if (selectedAlternativeProjectile != null && !IsAlternativeProjectile(selectedAlternativeProjectile))
            {
                selectedAlternativeProjectile = null;
            }
        }

        private bool IsAlternativeProjectile(ThingDef projectile)
        {
            if (projectile == null)
            {
                return false;
            }

            List<SRAProjectileMode> modes = MultiProjectileProps?.alternativeProjectiles;
            if (modes == null)
            {
                return false;
            }

            for (int i = 0; i < modes.Count; i++)
            {
                if (modes[i]?.projectile == projectile)
                {
                    return true;
                }
            }

            return false;
        }

        private SRAProjectileMode GetAlternativeProjectileMode(ThingDef projectile)
        {
            if (projectile == null || selectedAlternativeProjectile != projectile)
            {
                return null;
            }

            List<SRAProjectileMode> modes = MultiProjectileProps?.alternativeProjectiles;
            if (modes == null)
            {
                return null;
            }

            for (int i = 0; i < modes.Count; i++)
            {
                SRAProjectileMode mode = modes[i];
                if (mode?.projectile == projectile)
                {
                    return mode;
                }
            }

            return null;
        }

        private SRAProjectileDisplay GetSelectedProjectileDisplay(ThingDef projectile)
        {
            SRAProjectileMode alternativeMode = GetAlternativeProjectileMode(projectile);
            return alternativeMode ?? MultiProjectileProps?.defaultProjectileDisplay;
        }

        private void OpenProjectileSelectionMenu()
        {
            List<FloatMenuOption> options = new List<FloatMenuOption>();
            ThingDef defaultProjectile = base.Projectile;
            options.Add(CreateProjectileSelectionOption(defaultProjectile, MultiProjectileProps?.defaultProjectileDisplay));

            List<SRAProjectileMode> modes = MultiProjectileProps?.alternativeProjectiles;
            if (modes != null)
            {
                for (int i = 0; i < modes.Count; i++)
                {
                    SRAProjectileMode mode = modes[i];
                    if (mode?.projectile != null)
                    {
                        options.Add(CreateProjectileSelectionOption(mode.projectile, mode));
                    }
                }
            }

            Find.WindowStack.Add(new FloatMenu(options));
        }

        private FloatMenuOption CreateProjectileSelectionOption(ThingDef projectile, SRAProjectileDisplay display)
        {
            SRAProjectileMode mode = display as SRAProjectileMode;
            bool isCurrent = mode == null ? selectedAlternativeProjectile == null : selectedAlternativeProjectile == projectile;
            string label = GetProjectileLabel(projectile, display);
            if (isCurrent)
            {
                label = "SRA_ProjectileSelectionCurrent".Translate(label);
            }

            return new FloatMenuOption(label, delegate
            {
                selectedAlternativeProjectile = mode?.projectile;
                SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            });
        }

        private string GetProjectileLabel(ThingDef projectile, SRAProjectileDisplay display)
        {
            if (display != null && !display.labelKey.NullOrEmpty())
            {
                return display.labelKey.Translate();
            }

            return projectile?.LabelCap.ToString() ?? "SRA_ProjectileSelectionNoProjectile".Translate();
        }

        private Texture2D GetProjectileIcon(ThingDef projectile, SRAProjectileDisplay display)
        {
            if (display != null && !display.iconPath.NullOrEmpty())
            {
                Texture2D customIcon = ContentFinder<Texture2D>.Get(display.iconPath, reportFailure: false);
                if (customIcon != null)
                {
                    return customIcon;
                }
            }

            return projectile?.uiIcon ?? BaseContent.BadTex;
        }

        private string BuildProjectileTooltip(ThingDef projectile, SRAProjectileDisplay display)
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine(GetProjectileLabel(projectile, display));

            string description = display != null && !display.descriptionKey.NullOrEmpty()
                ? display.descriptionKey.Translate()
                : projectile?.description;
            if (!description.NullOrEmpty())
            {
                builder.AppendLine();
                builder.AppendLine(description);
            }

            ProjectileProperties projectileProperties = projectile?.projectile;
            if (projectileProperties != null)
            {
                builder.AppendLine();
                DamageDef damageDef = projectileProperties.damageDef;
                if (damageDef != null)
                {
                    int damage = projectileProperties.GetDamageAmount(base.EquipmentSource);
                    builder.AppendLine("SRA_ProjectileSelectionDamage".Translate(damageDef.LabelCap, damage));

                    if (damageDef.armorCategory != null)
                    {
                        float armorPenetration = projectileProperties.GetArmorPenetration(base.EquipmentSource);
                        builder.AppendLine("SRA_ProjectileSelectionArmorPenetration".Translate(armorPenetration.ToStringPercent()));
                    }
                }

                builder.AppendLine("SRA_ProjectileSelectionSpeed".Translate(projectileProperties.speed.ToString("0.##")));
                if (projectileProperties.explosionRadius > 0f)
                {
                    builder.AppendLine("SRA_ProjectileSelectionExplosionRadius".Translate(projectileProperties.explosionRadius.ToString("0.##")));
                }
            }

            builder.AppendLine("SRA_ProjectilesPerShotTooltip".Translate(ProjectilesPerShot));
            return builder.ToString().TrimEndNewlines();
        }

        private int GetAmmoConsumptionCount()
        {
            return MultiProjectileProps?.ammoConsumptionMode == SRAProjectileAmmoConsumptionMode.perProjectile
                ? ProjectilesPerShot
                : 1;
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
            // TryCastNextBurstShot 会在发射后递减 burstShotsLeft，不能再用
            // “剩余发数等于初始发数”判断经验。持续射击每次重新进入 warmup
            // 都代表一次实际射击周期，因此在发射前沿用原版经验计算。
            LearnShootingExperience();
            base.TryCastNextBurstShot();
            compSustainedShoot.Notify_SustainedBurstProgress(this.burstShotsLeft);
        }

        private void LearnShootingExperience()
        {
            Pawn pawn = this.currentTarget.Thing as Pawn;
            if (pawn == null || pawn.Downed || pawn.IsColonyMech || !this.CasterIsPawn || this.CasterPawn.skills == null)
            {
                return;
            }

            float baseExperience = pawn.HostileTo(this.caster) ? 170f : 20f;
            float cycleTime = this.verbProps.AdjustedFullCycleTime(this, this.CasterPawn);
            this.CasterPawn.skills.Learn(SkillDefOf.Shooting, baseExperience * cycleTime, false, false);
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
            Scribe_Defs.Look(ref selectedAlternativeProjectile, "sraSelectedAlternativeProjectile");
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                NormalizeSelectedAlternativeProjectile();
            }
        }

        protected override bool TryCastShot()
        {
            int projectilesFired;
            bool num = BaseTryCastShot(out projectilesFired);
            if (num && CasterIsPawn)
            {
                for (int i = 0; i < projectilesFired; i++)
                {
                    CasterPawn.records.Increment(RecordDefOf.ShotsFired);
                }
            }

            return num;
        }

        protected bool BaseTryCastShot()
        {
            int ignoredProjectilesFired;
            return BaseTryCastShot(out ignoredProjectilesFired);
        }

        /// <summary>
        /// 一轮射击只处理一次 LOS、资源消耗和开火位置；随后立即生成本轮全部 projectile。
        /// 这样 multi projectile 不会被原版 burst 间隔拆开，也不会错误重复触发外层冷却。
        /// </summary>
        protected bool BaseTryCastShot(out int projectilesFired)
        {
            projectilesFired = 0;

            if (currentTarget.HasThing && currentTarget.Thing.Map != caster.Map)
            {
                return false;
            }

            ThingDef projectile = ResolveSelectedProjectile();
            if (projectile == null)
            {
                return false;
            }

            Comp_HNGT_GlobalBallisticAttack remoteArtilleryComp = (caster as ThingWithComps)?.GetComp<Comp_HNGT_GlobalBallisticAttack>();
            if (remoteArtilleryComp != null && remoteArtilleryComp.IsFiringInterMap)
            {
                bool remoteShotFired = TryCastRemoteArtilleryFakeShot(projectile);
                projectilesFired = remoteShotFired ? 1 : 0;
                return remoteShotFired;
            }

            ShootLine resultingLine;
            bool flag = TryFindShootLineFromTo(caster.Position, currentTarget, out resultingLine);
            if (verbProps.stopBurstWithoutLos && !flag)
            {
                return false;
            }

            int ammoConsumptionCount = GetAmmoConsumptionCount();
            if (!TryConsumeEquipmentShots(ammoConsumptionCount, requireLoadedAmmo: false))
            {
                return false;
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

            int projectileCount = ProjectilesPerShot;
            for (int i = 0; i < projectileCount; i++)
            {
                if (TryLaunchProjectile(projectile, resultingLine, manningPawn, equipmentSource, drawPos))
                {
                    projectilesFired++;
                }
            }

            return projectilesFired > 0;
        }

        /// <summary>
        /// 只负责生成一枚 projectile 并执行原版的偏离、掩体和命中结算。
        /// 参数中的 ShootLine 按值传递，确保每枚射弹独立进行随机偏离，
        /// 而不会污染同一轮后续射弹的目标线。
        /// </summary>
        private bool TryLaunchProjectile(ThingDef projectileDef, ShootLine resultingLine, Thing manningPawn, Thing equipmentSource, Vector3 drawPos)
        {
            Projectile projectile = (Projectile)GenSpawn.Spawn(projectileDef, resultingLine.Source, caster.Map);
            if (equipmentSource != null && equipmentSource.TryGetComp(out CompUniqueWeapon comp))
            {
                foreach (WeaponTraitDef item in comp.TraitsListForReading)
                {
                    if (item.damageDefOverride != null)
                    {
                        projectile.damageDefOverride = item.damageDefOverride;
                    }

                    if (!item.extraDamages.NullOrEmpty())
                    {
                        Projectile projectile3 = projectile;
                        if (projectile3.extraDamages == null)
                        {
                            projectile3.extraDamages = new List<ExtraDamage>();
                        }

                        projectile.extraDamages.AddRange(item.extraDamages);
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

                        projectile.Launch(manningPawn, drawPos, forcedMissTarget, currentTarget, projectileHitFlags, preventFriendlyFire, equipmentSource);
                        return true;
                    }
                }
            }

            ShotReport shotReport = ShotReport.HitReportFor(caster, this, currentTarget);
            Thing randomCoverToMissInto = shotReport.GetRandomCoverToMissInto();
            ThingDef targetCoverDef = randomCoverToMissInto?.def;
            if (verbProps.canGoWild && !Rand.Chance(shotReport.AimOnTargetChance_IgnoringPosture))
            {
                bool flyOverhead = projectile.def?.projectile != null && projectile.def.projectile.flyOverhead;
                resultingLine.ChangeDestToMissWild(shotReport.AimOnTargetChance_StandardTarget, flyOverhead, caster.Map);
                ProjectileHitFlags projectileHitFlags2 = ProjectileHitFlags.NonTargetWorld;
                if (Rand.Chance(0.5f) && canHitNonTargetPawnsNow)
                {
                    projectileHitFlags2 |= ProjectileHitFlags.NonTargetPawns;
                }

                projectile.Launch(manningPawn, drawPos, resultingLine.Dest, currentTarget, projectileHitFlags2, preventFriendlyFire, equipmentSource, targetCoverDef);
                return true;
            }

            if (currentTarget.Thing != null && currentTarget.Thing.def.CanBenefitFromCover && !Rand.Chance(shotReport.PassCoverChance))
            {
                ProjectileHitFlags projectileHitFlags3 = ProjectileHitFlags.NonTargetWorld;
                if (canHitNonTargetPawnsNow)
                {
                    projectileHitFlags3 |= ProjectileHitFlags.NonTargetPawns;
                }

                projectile.Launch(manningPawn, drawPos, randomCoverToMissInto, currentTarget, projectileHitFlags3, preventFriendlyFire, equipmentSource, targetCoverDef);
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
                projectile.Launch(manningPawn, drawPos, currentTarget, currentTarget, projectileHitFlags4, preventFriendlyFire, equipmentSource, targetCoverDef);
            }
            else
            {
                projectile.Launch(manningPawn, drawPos, resultingLine.Dest, currentTarget, projectileHitFlags4, preventFriendlyFire, equipmentSource, targetCoverDef);
            }
            return true;
        }

        private bool TryCastRemoteArtilleryFakeShot(ThingDef projectileDef)
        {
            if (caster?.Map == null || !currentTarget.IsValid)
            {
                return false;
            }

            if (!TryConsumeEquipmentShots(1, requireLoadedAmmo: true))
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

        /// <summary>
        /// 处理一轮射击的资源消耗。perProjectile 先检查 CompChangeableProjectile 的
        /// 已装填数量，确保不会出现只发出一部分齐射的情况。
        /// </summary>
        private bool TryConsumeEquipmentShots(int shotCount, bool requireLoadedAmmo)
        {
            shotCount = Mathf.Max(1, shotCount);
            CompChangeableProjectile compChangeableProjectile = base.EquipmentSource?.GetComp<CompChangeableProjectile>();
            CompRefuelable refuelableAmmo = requireLoadedAmmo ? GetRemoteArtilleryFuelComp() : null;
            // 替代射弹会绕过 base.Projectile 的空值检查，因此只要装备带有原版
            // CompChangeableProjectile，就始终按本轮实际消耗量确认已装填弹药。
            if (compChangeableProjectile != null && compChangeableProjectile.loadedCount < shotCount)
            {
                return false;
            }

            if (requireLoadedAmmo)
            {
                float requiredFuel = shotCount * Comp_HNGT_GlobalBallisticAttack.RemoteFuelPerFakeShot;
                if (refuelableAmmo != null && refuelableAmmo.Fuel < requiredFuel)
                {
                    return false;
                }
            }

            for (int i = 0; i < shotCount; i++)
            {
                compChangeableProjectile?.Notify_ProjectileLaunched();

                if (refuelableAmmo != null)
                {
                    refuelableAmmo.ConsumeFuel(Comp_HNGT_GlobalBallisticAttack.RemoteFuelPerFakeShot);
                }

                base.EquipmentSource?.GetComp<CompApparelVerbOwner_Charged>()?.UsedOnce();
            }

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
