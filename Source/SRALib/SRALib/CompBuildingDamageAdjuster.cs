using RimWorld;
using UnityEngine;
using Verse;

namespace SRA
{
    public class CompBuildingDamageAdjuster : ThingComp
    {
        private CompProperties_BuildingDamageAdjuster Props => (CompProperties_BuildingDamageAdjuster)props;

        public override void PostPreApplyDamage(ref DamageInfo dinfo, out bool absorbed)
        {
            absorbed = false;

            if (Props.onlyAffectHarmfulDamage && !dinfo.Def.harmsHealth)
            {
                return;
            }

            float vanillaBuildingFactor = GetVanillaBuildingDamageFactor(dinfo);
            float damage = ApplyDamageAdjustment(dinfo.Amount * vanillaBuildingFactor);
            if (vanillaBuildingFactor > 0f)
            {
                damage /= vanillaBuildingFactor;
            }

            if (damage <= 0f)
            {
                dinfo.SetAmount(0f);
                absorbed = true;
                return;
            }

            dinfo.SetAmount(damage);
        }

        private float ApplyDamageAdjustment(float damage)
        {
            if (Props.damageTakenMax > 0f)
            {
                damage = Mathf.Min(damage, Props.damageTakenMax);
            }

            if (Props.damageTakenReduce > 0f)
            {
                damage -= Props.damageTakenReduce;
            }

            damage *= Props.damageTakenMult;
            return damage;
        }

        private float GetVanillaBuildingDamageFactor(DamageInfo dinfo)
        {
            if (parent == null || parent.def == null || !parent.def.useHitPoints || !dinfo.Def.harmsHealth || parent.def.category != ThingCategory.Building)
            {
                return 1f;
            }

            float factor = dinfo.Def.buildingDamageFactor;
            factor *= parent.def.passability == Traversability.Impassable
                ? dinfo.Def.buildingDamageFactorImpassable
                : dinfo.Def.buildingDamageFactorPassable;

            if (dinfo.Def.scaleDamageToBuildingsBasedOnFlammability)
            {
                factor *= Mathf.Max(0.05f, parent.GetStatValue(StatDefOf.Flammability, true, -1));
            }

            if (dinfo.Instigator is Pawn pawn && pawn.IsShambler)
            {
                factor *= 1.5f;
            }

            if (ModsConfig.BiotechActive && dinfo.Instigator != null && (dinfo.WeaponBodyPartGroup != null || (dinfo.Weapon != null && dinfo.Weapon.IsMeleeWeapon)) && parent.def.IsDoor)
            {
                factor *= dinfo.Instigator.GetStatValue(StatDefOf.MeleeDoorDamageFactor, true, -1);
            }

            return factor;
        }
    }
}
