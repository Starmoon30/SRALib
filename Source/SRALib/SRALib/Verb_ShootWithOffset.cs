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
    public class PlaceWorker_ShowTurretWithOffsetRadius : PlaceWorker
    {
        public override AcceptanceReport AllowsPlacing(BuildableDef checkingDef, IntVec3 loc, Rot4 rot, Map map, Thing thingToIgnore = null, Thing thing = null)
        {
            VerbProperties verbProperties = ((ThingDef)checkingDef).building.turretGunDef.Verbs.Find((VerbProperties v) => v.verbClass == typeof(Verb_ShootWithOffset));
            if (verbProperties.range > 0f)
            {
                GenDraw.DrawRadiusRing(loc, verbProperties.range);
            }
            if (verbProperties.minRange > 0f)
            {
                GenDraw.DrawRadiusRing(loc, verbProperties.minRange);
            }
            return true;
        }
    }
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
        public List<Vector2> offsets = new List<Vector2>();
    }
    public class Verb_ShootWithOffset : Verb_Shoot
    {  
        public int offset = 0;
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

            ShootLine resultingLine;
            bool flag = TryFindShootLineFromTo(caster.Position, currentTarget, out resultingLine);
            if (verbProps.stopBurstWithoutLos && !flag)
            {
                return false;
            }

            if (base.EquipmentSource != null)
            {
                base.EquipmentSource.GetComp<CompChangeableProjectile>()?.Notify_ProjectileLaunched();
                base.EquipmentSource.GetComp<CompApparelVerbOwner_Charged>()?.UsedOnce();
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
            if (equipmentSource.TryGetComp(out CompUniqueWeapon comp))
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

        private Vector3 ApplyProjectileOffset(Vector3 originalDrawPos, Thing equipmentSource)
        {
            if (equipmentSource != null)
            {
                // 获取投射物偏移的模组扩展
                ModExtension_ShootWithOffset offsetExtension =
                    equipmentSource.def.GetModExtension<ModExtension_ShootWithOffset>();

                if (offsetExtension != null && offsetExtension.offsets != null && offsetExtension.offsets.Count > 0)
                {
                    // 获取当前连发射击的剩余次数
                    int burstShotsLeft = GetBurstShotsLeft();

                    // 计算从发射者到目标的角度
                    Vector3 targetPos = currentTarget.CenterVector3;
                    Vector3 casterPos = caster.DrawPos;
                    float rimworldAngle = targetPos.AngleToFlat(casterPos);

                    // 将RimWorld角度转换为适合偏移计算的角度
                    float correctedAngle = ConvertRimWorldAngleToOffsetAngle(rimworldAngle);

                    // 应用偏移并旋转到正确方向
                    Vector2 offset = offsetExtension.GetOffsetFor(burstShotsLeft);
                    Vector2 rotatedOffset = offset.RotatedBy(correctedAngle);

                    // 将2D偏移转换为3D并应用到绘制位置
                    originalDrawPos += new Vector3(rotatedOffset.x, 0f, rotatedOffset.y);
                }
            }

            return originalDrawPos;
        }

        /// <summary>
        /// 获取当前连发射击剩余次数
        /// </summary>
        /// <returns>连发射击剩余次数</returns>
        private int GetBurstShotsLeft()
        {
            if (burstShotsLeft >= 0)
            {
                return (int)burstShotsLeft;
            }
            return 0;
        }

        /// <summary>
        /// 将RimWorld角度转换为偏移计算用的角度
        /// RimWorld使用顺时针角度系统，需要转换为标准的数学角度系统
        /// </summary>
        /// <param name="rimworldAngle">RimWorld角度</param>
        /// <returns>转换后的角度</returns>
        private float ConvertRimWorldAngleToOffsetAngle(float rimworldAngle)
        {
            // RimWorld角度：0°=东，90°=北，180°=西，270°=南
            // 转换为：0°=东，90°=南，180°=西，270°=北
            return -rimworldAngle - 90f;
        }

    }
}
