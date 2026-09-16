using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace SRA
{
    /// <summary>
    /// 挂载在炮塔内置武器 Def（turretGunDef）上的同轴副武器定义。
    /// 副武器使用 Building_TurretGunHasSpeed 已经锁定的目标和当前转向，
    /// 不创建第二个 Verb 或独立索敌循环。
    /// </summary>
    public class ModExtension_CoaxialWeapon : DefModExtension
    {
        /// <summary>
        /// 副武器实际生成的射弹。留空时整个同轴系统不启用。
        /// </summary>
        public ThingDef projectile;

        /// <summary>
        /// 副武器最大射程。小于等于 0 时使用主炮 Verb 的最大射程。
        /// </summary>
        public float range = 0f;

        /// <summary>
        /// 副武器最小射程。小于等于 0 时不限制最小射程。
        /// </summary>
        public float minRange = 0f;

        /// <summary>
        /// 两次副武器射击之间的冷却时间，单位为 tick。0 表示每 tick 最多可射击一次。
        /// </summary>
        public int cooldownTicks = 10;

        /// <summary>
        /// 单轮 burst 的射击次数。副武器会锁定开始本轮时的主炮目标，直到本轮结束或目标失效。
        /// </summary>
        public int burstShotCount = 1;

        /// <summary>
        /// 同一轮 burst 中相邻两发之间的 tick 间隔。
        /// 0 仍会保持为每 tick 最多一发，与原版 Verb 的更新频率一致。
        /// </summary>
        public int ticksBetweenBurstShots = 15;

        /// <summary>
        /// 主炮朝向与目标方向允许的最大夹角，单位为度。副武器只有在炮塔基本对正目标后才会开火。
        /// </summary>
        public float aimTolerance = 1f;

        /// <summary>
        /// 是否要求副武器到目标之间存在无阻挡视线。
        /// </summary>
        public bool requireLineOfSight = true;

        /// <summary>
        /// 强制散布半径。大于 0 时，每发会在目标格周围该半径内随机选择落点。
        /// </summary>
        public float forcedMissRadius = 0f;

        /// <summary>
        /// 副武器开火时播放的声音。留空时不额外播放声音。
        /// </summary>
        public SoundDef shootSound;

        /// <summary>
        /// 独立弹药对应的物品 Def。留空时副武器不消耗弹药，也不会生成装填工作。
        /// </summary>
        public ThingDef ammoThingDef;

        /// <summary>
        /// 每个 ammoThingDef 物品可装填的副武器射击次数。
        /// </summary>
        public int shotsPerAmmoItem = 1;

        /// <summary>
        /// 副武器独立弹仓可储存的最大射击次数。仅配置 ammoThingDef 时生效。
        /// </summary>
        public int maxAmmo = 100;

        /// <summary>
        /// 新建炮塔时副武器弹仓的初始装填比例，范围为 0 到 1。
        /// 实际射击次数按最大容量乘以该比例后四舍五入；读档不会重新初始化。
        /// </summary>
        public float initialAmmoPercent = 0f;

        /// <summary>
        /// 自动补给阈值比例，范围为 0 到 1。当前弹药比例低于或等于该值时，
        /// 搬运 Pawn 才会获得装填工作；一次装填仍会尽量补满整个弹仓。
        /// 默认 0.3，与原版 CompProperties_Refuelable 的 autoRefuelPercent 一致。
        /// </summary>
        public float autoReloadPercent = 0.3f;

        /// <summary>
        /// 搬运 Pawn 完成一次副武器装填所需的工作时间，单位为 tick。
        /// 默认 240 tick，与原版 JobDriver_Refuel 的装填等待时间一致。
        /// </summary>
        public int reloadTicks = 240;

        /// <summary>
        /// 自动装填时允许搜索弹药的最大距离。默认 9999 格，与原版
        /// RefuelWorkGiverUtility.FindBestFuel 的近似全图搜索范围一致。
        /// 可按 Def 缩小以限制物流距离，但会偏离原版装填行为。
        /// </summary>
        public float reloadSearchRadius = 9999f;

        /// <summary>
        /// 副武器炮管、制退和炮口火焰的视觉定义。
        /// 使用 ModExtension_ShootWithOffset 的全部字段和坐标语义；其中 offsets 同时定义副武器射击出口、
        /// 炮管中心和火焰基准点。留空时副武器仍可射击，但不绘制额外炮管动画。
        /// </summary>
        public ModExtension_ShootWithOffset visuals;
    }

    /// <summary>
    /// 独立同轴弹仓的原版样式库存条。
    /// 使用 Gizmo_Slider 以获得与 CompRefuelable 相同的外观，但不提供目标库存设置，
    /// 因为同轴弹仓始终自动装填至上限。
    /// </summary>
    public class Gizmo_CoaxialAmmoLevel : Gizmo_Slider
    {
        private readonly Building_TurretGunHasSpeed turret;

        private static bool draggingBar;

        /// <summary>
        /// 创建指定炮塔的独立副炮弹仓库存条。
        /// </summary>
        public Gizmo_CoaxialAmmoLevel(Building_TurretGunHasSpeed turret)
        {
            this.turret = turret;
        }

        /// <summary>
        /// Gizmo_Slider 初始化需要目标值；库存条不可拖动，因此返回当前百分比即可。
        /// </summary>
        protected override float Target
        {
            get
            {
                return ValuePercent;
            }
            set
            {
                // 独立副炮弹仓没有目标库存配置，保持为只读显示。
            }
        }

        /// <summary>
        /// 当前库存占最大库存的比例。容量为零时安全地显示为空条。
        /// </summary>
        protected override float ValuePercent
        {
            get
            {
                int capacity = turret?.CoaxialAmmoCapacity ?? 0;
                return capacity > 0 ? (float)(turret?.CoaxialAmmoCount ?? 0) / capacity : 0f;
            }
        }

        /// <summary>
        /// 库存条标题使用实际弹药物品的本地化名称。
        /// </summary>
        protected override string Title => "SRA_CoaxialWeapon_AmmoGizmo".Translate(turret.CoaxialAmmoThingDef.LabelCap);

        /// <summary>
        /// 与原版燃料 Gizmo 一致，显示实际库存和容量而非百分比。
        /// </summary>
        protected override string BarLabel => (turret?.CoaxialAmmoCount ?? 0).ToString() + " / " + (turret?.CoaxialAmmoCapacity ?? 0).ToString();

        /// <summary>
        /// 副炮弹仓会自动装填至容量上限，不允许玩家拖动改变目标库存。
        /// </summary>
        protected override bool IsDraggable => false;

        /// <summary>
        /// Gizmo_Slider 需要保存拖动状态；当前库存条不可拖动，但保持与原版实现一致。
        /// </summary>
        protected override bool DraggingBar
        {
            get
            {
                return draggingBar;
            }
            set
            {
                draggingBar = value;
            }
        }

        /// <summary>
        /// 原版 Gizmo_SetFuelLevel 对库存条本体也不提供额外提示文本。
        /// </summary>
        protected override string GetTooltip()
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// 为独立副武器弹仓维护可装填炮塔缓存。
    /// WorkGiver 仅在 Pawn 正常寻找搬运工作时枚举此缓存；缓存只在炮塔生成和移除时更新，
    /// 不进行逐 tick 的全地图建筑扫描。
    /// </summary>
    public class MapComponent_CoaxialWeaponTurrets : MapComponent
    {
        private readonly List<Building_TurretGunHasSpeed> turrets = new List<Building_TurretGunHasSpeed>();

        public MapComponent_CoaxialWeaponTurrets(Map map)
            : base(map)
        {
        }

        public void Register(Building_TurretGunHasSpeed turret)
        {
            if (turret != null && turret.UsesIndependentCoaxialAmmo && !turrets.Contains(turret))
            {
                turrets.Add(turret);
            }
        }

        public void Deregister(Building_TurretGunHasSpeed turret)
        {
            if (turret != null)
            {
                turrets.Remove(turret);
            }
        }

        public bool HasReloadableTurret()
        {
            PruneInvalidTurrets();
            for (int i = 0; i < turrets.Count; i++)
            {
                if (turrets[i].NeedsCoaxialAmmoReload)
                {
                    return true;
                }
            }

            return false;
        }

        public IEnumerable<Thing> ReloadableTurrets()
        {
            PruneInvalidTurrets();
            for (int i = 0; i < turrets.Count; i++)
            {
                Building_TurretGunHasSpeed turret = turrets[i];
                if (turret.NeedsCoaxialAmmoReload)
                {
                    yield return turret;
                }
            }
        }

        private void PruneInvalidTurrets()
        {
            for (int i = turrets.Count - 1; i >= 0; i--)
            {
                Building_TurretGunHasSpeed turret = turrets[i];
                if (turret == null || turret.Destroyed || !turret.Spawned || turret.Map != map || !turret.UsesIndependentCoaxialAmmo)
                {
                    turrets.RemoveAt(i);
                }
            }
        }
    }

    /// <summary>
    /// 为配置了独立同轴弹药的炮塔创建装填工作。
    /// </summary>
    public class WorkGiver_ReloadCoaxialWeapon : WorkGiver_Scanner
    {
        public override ThingRequest PotentialWorkThingRequest => ThingRequest.ForGroup(ThingRequestGroup.BuildingArtificial);

        public override PathEndMode PathEndMode => PathEndMode.Touch;

        public override bool ShouldSkip(Pawn pawn, bool forced = false)
        {
            Map map = pawn?.Map;
            return map == null || !map.GetComponent<MapComponent_CoaxialWeaponTurrets>().HasReloadableTurret();
        }

        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)
        {
            Map map = pawn?.Map;
            if (map == null)
            {
                yield break;
            }

            foreach (Thing turret in map.GetComponent<MapComponent_CoaxialWeaponTurrets>().ReloadableTurrets())
            {
                yield return turret;
            }
        }

        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            return t is Building_TurretGunHasSpeed turret && turret.TryFindCoaxialReloadAmmo(pawn, forced, out _);
        }

        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            if (!(t is Building_TurretGunHasSpeed turret) || !turret.TryFindCoaxialReloadAmmo(pawn, forced, out Thing ammo))
            {
                return null;
            }

            // 与原版 RefuelWorkGiverUtility.RefuelJob 一致：不在选工阶段锁定搬运量。
            // Pawn 真正开始任务时再依据弹仓空位写入 job.count，避免多名搬运工或射击
            // 改变库存后使用过期的数量。
            return JobMaker.MakeJob(SRAJobDefOf.SRA_ReloadCoaxialWeapon, turret, ammo);
        }
    }

    /// <summary>
    /// 搬运独立副武器弹药并写入炮塔保存的射击次数库存。
    /// </summary>
    public class JobDriver_ReloadCoaxialWeapon : JobDriver
    {
        private Building_TurretGunHasSpeed Turret => job.GetTarget(TargetIndex.A).Thing as Building_TurretGunHasSpeed;

        private Thing Ammo => job.GetTarget(TargetIndex.B).Thing;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(Turret, job, 1, -1, null, errorOnFailed)
                && pawn.Reserve(Ammo, job, 1, -1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedNullOrForbidden(TargetIndex.A);
            AddFailCondition(delegate
            {
                return Turret == null;
            });
            // 与原版 JobDriver_Refuel 相同：其他搬运工已填满弹仓时正常完成，
            // 而不是把当前任务标记为失败并在同一 tick 内不断重试。
            AddEndCondition(delegate
            {
                return Turret != null && !Turret.NeedsCoaxialAmmoReload
                    ? JobCondition.Succeeded
                    : JobCondition.Ongoing;
            });

            // 以下顺序与原版 JobDriver_Refuel 对齐。StartCarryThing 会把 TargetIndex.B
            // 替换为 Pawn 手持的 Thing；因此仅在搬运前要求其已生成，后续只检查是否被销毁。
            yield return Toils_General.DoAtomic(delegate
            {
                job.count = Turret.CoaxialAmmoItemsNeeded;
            });

            Toil reserveAmmo = Toils_Reserve.Reserve(TargetIndex.B, 1, -1, null, false);
            yield return reserveAmmo;
            yield return Toils_Goto.GotoThing(TargetIndex.B, PathEndMode.ClosestTouch)
                .FailOnSomeonePhysicallyInteracting(TargetIndex.B)
                .FailOnDespawnedNullOrForbidden(TargetIndex.B);
            yield return Toils_Haul.StartCarryThing(TargetIndex.B, false, true, false, true, false)
                .FailOnDestroyedNullOrForbidden(TargetIndex.B);
            yield return Toils_Haul.CheckForGetOpportunityDuplicate(reserveAmmo, TargetIndex.B, TargetIndex.None, true, null);
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            // Toil 创建时读取一次 Def；等待过程中不产生额外的配置读取或地图搜索。
            yield return Toils_General.Wait(Turret == null ? 1 : Turret.CoaxialReloadTicks)
                .FailOnDestroyedNullOrForbidden(TargetIndex.A)
                .FailOnDestroyedNullOrForbidden(TargetIndex.B)
                .FailOnCannotTouch(TargetIndex.A, PathEndMode.Touch)
                .WithProgressBarToilDelay(TargetIndex.A, false, -0.5f);

            yield return Toils_General.DoAtomic(delegate
            {
                // 原版 Toils_Refuel.FinalizeRefueling 也从 job 的 TargetIndex.B 获取手持物。
                // 不读取 carryTracker，确保 stack 被拆分或合并后仍使用任务实际持有的弹药。
                Turret?.LoadCoaxialAmmo(job.GetTarget(TargetIndex.B).Thing);
            });
        }
    }

    [DefOf]
    public static class SRAJobDefOf
    {
        public static JobDef SRA_ReloadCoaxialWeapon;

        static SRAJobDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(SRAJobDefOf));
        }
    }
}
