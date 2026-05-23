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

            float damage = dinfo.Amount;
            if (Props.damageTakenMax > 0f)
            {
                damage = Mathf.Min(damage, Props.damageTakenMax);
            }

            if (Props.damageTakenReduce > 0f)
            {
                damage -= Props.damageTakenReduce;
            }

            damage *= Props.damageTakenMult;

            if (damage <= 0f)
            {
                dinfo.SetAmount(0f);
                absorbed = true;
                return;
            }

            dinfo.SetAmount(damage);
        }
    }
}
