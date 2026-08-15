using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Verse;

namespace SRA
{
    [HarmonyPatch(typeof(ThingDef), nameof(ThingDef.SpecialDisplayStats))]
    public static class Patch_ThingDef_SpecialDisplayStats_SRAShootBeam
    {
        public static void Postfix(ThingDef __instance, StatRequest req, ref IEnumerable<StatDrawEntry> __result)
        {
            if (!SRAShootBeamStatsUtility.HasSRAShootBeamVerb(__instance))
            {
                return;
            }

            __result = SRAShootBeamStatsUtility.AppendSpecialDisplayStats(__result, __instance, req);
        }
    }

    public static class SRAShootBeamStatsUtility
    {
        private const int DamageDisplayPriority = 5500;
        private const int DamageFrequencyDisplayPriority = 5490;
        private const int ArmorPenetrationDisplayPriority = 5400;
        private const int AreaDisplayPriority = 5380;

        public static bool HasSRAShootBeamVerb(ThingDef def)
        {
            if (def?.Verbs == null)
            {
                return false;
            }

            for (int i = 0; i < def.Verbs.Count; i++)
            {
                if (def.Verbs[i] is VerbProperties_SRAShootBeam)
                {
                    return true;
                }
            }

            return false;
        }

        public static IEnumerable<StatDrawEntry> AppendSpecialDisplayStats(IEnumerable<StatDrawEntry> source, ThingDef def, StatRequest req)
        {
            StatCategoryDef statCategory = GetStatCategory(def);
            bool suppressVanillaBeamArmorPenetration = ShouldSuppressVanillaBeamArmorPenetration(def);

            if (source != null)
            {
                foreach (StatDrawEntry entry in source)
                {
                    if (!suppressVanillaBeamArmorPenetration || !IsVanillaBeamArmorPenetrationEntry(entry, statCategory))
                    {
                        yield return entry;
                    }
                }
            }

            int beamVerbIndex = 0;
            for (int i = 0; i < def.Verbs.Count; i++)
            {
                VerbProperties_SRAShootBeam props = def.Verbs[i] as VerbProperties_SRAShootBeam;
                if (props == null)
                {
                    continue;
                }

                foreach (StatDrawEntry entry in BuildBeamEntries(props, statCategory, beamVerbIndex))
                {
                    yield return entry;
                }

                beamVerbIndex++;
            }
        }

        private static IEnumerable<StatDrawEntry> BuildBeamEntries(VerbProperties_SRAShootBeam props, StatCategoryDef statCategory, int beamVerbIndex)
        {
            int priorityOffset = beamVerbIndex * -100;

            StatDrawEntry damageEntry = BuildDamageEntry(props, statCategory, priorityOffset);
            if (damageEntry != null)
            {
                yield return damageEntry;
            }

            StatDrawEntry damageFrequencyEntry = BuildDamageFrequencyEntry(props, statCategory, priorityOffset);
            if (damageFrequencyEntry != null)
            {
                yield return damageFrequencyEntry;
            }

            StatDrawEntry armorPenetrationEntry = BuildArmorPenetrationEntry(props, statCategory, priorityOffset);
            if (armorPenetrationEntry != null)
            {
                yield return armorPenetrationEntry;
            }

            StatDrawEntry areaEntry = BuildAreaEntry(props, statCategory, priorityOffset);
            if (areaEntry != null)
            {
                yield return areaEntry;
            }
        }

        private static StatDrawEntry BuildDamageEntry(VerbProperties_SRAShootBeam props, StatCategoryDef statCategory, int priorityOffset)
        {
            DamageDef damageDef = props.beamDamageDef;
            if (damageDef == null)
            {
                return null;
            }

            string valueString;
            StringBuilder reportText = new StringBuilder();
            reportText.AppendLine("SRA_BeamDamageDesc".Translate());
            reportText.AppendLine();

            if (props.beamDamageAmount >= 0f)
            {
                valueString = "SRA_BeamDamageValue".Translate(damageDef.LabelCap, FormatNumber(props.beamDamageAmount));
            }
            else if (props.beamTotalDamage > 0f)
            {
                valueString = "SRA_BeamTotalDamageValue".Translate(damageDef.LabelCap, FormatNumber(props.beamTotalDamage));
            }
            else
            {
                valueString = "SRA_BeamDamageValue".Translate(damageDef.LabelCap, FormatNumber(damageDef.defaultDamage));
            }

            return NewEntry(statCategory, "SRA_BeamDamageLabel", valueString, reportText.ToString(), DamageDisplayPriority + priorityOffset);
        }

        private static StatDrawEntry BuildDamageFrequencyEntry(VerbProperties_SRAShootBeam props, StatCategoryDef statCategory, int priorityOffset)
        {
            int ticksBetweenHits = Mathf.Max(1, props.ticksBetweenBurstShots);
            float hitsPerSecond = 60f / ticksBetweenHits;
            string valueString = "SRA_BeamDamageFrequencyValue".Translate(FormatNumber(hitsPerSecond), ticksBetweenHits);
            return NewEntry(statCategory, "SRA_BeamDamageFrequencyLabel", valueString, "SRA_BeamDamageFrequencyDesc".Translate(), DamageFrequencyDisplayPriority + priorityOffset);
        }

        private static StatDrawEntry BuildArmorPenetrationEntry(VerbProperties_SRAShootBeam props, StatCategoryDef statCategory, int priorityOffset)
        {
            DamageDef damageDef = props.beamDamageDef;
            if (damageDef == null)
            {
                return null;
            }

            float armorPenetration = props.beamArmorPenetration >= 0f ? props.beamArmorPenetration : damageDef.defaultArmorPenetration;
            StringBuilder reportText = new StringBuilder();
            reportText.AppendLine("SRA_BeamArmorPenetrationDesc".Translate());
            reportText.AppendLine();
            if (props.beamArmorPenetration >= 0f)
            {
                reportText.AppendLine("SRA_BeamArmorPenetrationSource_Override".Translate("beamArmorPenetration", armorPenetration.ToStringPercent()));
            }

            return NewEntry(statCategory, "SRA_BeamArmorPenetrationLabel", armorPenetration.ToStringPercent(), reportText.ToString(), ArmorPenetrationDisplayPriority + priorityOffset);
        }

        private static StatDrawEntry BuildAreaEntry(VerbProperties_SRAShootBeam props, StatCategoryDef statCategory, int priorityOffset)
        {
            StringBuilder valueString = new StringBuilder();

            if (props.hitRadius > 0f)
            {
                valueString.AppendLine("SRA_BeamHitRadiusValue".Translate(FormatNumber(props.hitRadius)));
            }

            if (props.damageBeamPath)
            {
                valueString.AppendLine("SRA_BeamPathDamageValue".Translate(FormatNumber(props.pathHitRadius), props.pathDamageFactor.ToStringPercent()));
            }

            string trimmedValue = valueString.ToString().TrimEndNewlines();
            if (trimmedValue.NullOrEmpty())
            {
                return null;
            }

            string reportText = "SRA_BeamAreaDesc".Translate() + "\n\n" + trimmedValue;
            return NewEntry(statCategory, "SRA_BeamAreaLabel", trimmedValue, reportText, AreaDisplayPriority + priorityOffset);
        }

        private static StatDrawEntry NewEntry(StatCategoryDef statCategory, string labelKey, string valueString, string reportText, int displayPriority)
        {
            return new StatDrawEntry(statCategory, labelKey.Translate(), valueString, reportText, displayPriority);
        }

        private static bool ShouldSuppressVanillaBeamArmorPenetration(ThingDef def)
        {
            if (def?.Verbs == null)
            {
                return false;
            }

            for (int i = 0; i < def.Verbs.Count; i++)
            {
                VerbProperties verb = def.Verbs[i];
                if (verb.isPrimary)
                {
                    return verb is VerbProperties_SRAShootBeam && verb.defaultProjectile == null && verb.beamDamageDef != null;
                }
            }

            return false;
        }

        private static bool IsVanillaBeamArmorPenetrationEntry(StatDrawEntry entry, StatCategoryDef statCategory)
        {
            if (entry == null || entry.stat != null || entry.category != statCategory)
            {
                return false;
            }

            string armorPenetrationLabel = "ArmorPenetration".Translate().ToString().CapitalizeFirst();
            return string.Equals(entry.LabelCap, armorPenetrationLabel, StringComparison.Ordinal);
        }

        private static StatCategoryDef GetStatCategory(ThingDef def)
        {
            return def != null && def.category == ThingCategory.Pawn ? StatCategoryDefOf.PawnCombat : StatCategoryDefOf.Weapon_Ranged;
        }

        private static string FormatNumber(float value)
        {
            return value.ToString("0.##");
        }
    }
}
