using System.Collections.Generic;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;

namespace SRA
{
    public class CompProperties_BuildingDamageAdjuster : CompProperties
    {
        public float damageTakenMult = 1f;
        public float damageTakenMax = 0f;
        public float damageTakenReduce = 0f;
        public bool onlyAffectHarmfulDamage = true;

        public CompProperties_BuildingDamageAdjuster()
        {
            compClass = typeof(CompBuildingDamageAdjuster);
        }

        public override IEnumerable<StatDrawEntry> SpecialDisplayStats(StatRequest req)
        {
            foreach (StatDrawEntry statDrawEntry in base.SpecialDisplayStats(req))
            {
                yield return statDrawEntry;
            }

            string valueString = BuildValueString();
            if (valueString.NullOrEmpty())
            {
                yield break;
            }

            string reportText = "SRA_BuildingDamageAdjusterDesc".Translate() + "\n\n" + valueString;
            yield return new StatDrawEntry(
                StatCategoryDefOf.Building,
                "SRA_BuildingDamageAdjusterLabel".Translate(),
                valueString,
                reportText,
                2450);
        }

        private string BuildValueString()
        {
            StringBuilder stringBuilder = new StringBuilder();

            if (!Mathf.Approximately(damageTakenMult, 1f))
            {
                stringBuilder.AppendLine("SRA_BuildingDamageAdjusterValueMult".Translate(damageTakenMult.ToStringPercent()));
            }

            if (damageTakenMax > 0f)
            {
                stringBuilder.AppendLine("SRA_BuildingDamageAdjusterValueMax".Translate(damageTakenMax.ToString("0.##")));
            }

            if (damageTakenReduce > 0f)
            {
                stringBuilder.AppendLine("SRA_BuildingDamageAdjusterValueReduce".Translate(damageTakenReduce.ToString("0.##")));
            }

            return stringBuilder.ToString().TrimEndNewlines();
        }
    }
}
