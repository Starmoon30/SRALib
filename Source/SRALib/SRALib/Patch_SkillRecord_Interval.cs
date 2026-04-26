using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace SRA
{
    // 挂在 TraitDef 上的自定义标签扩展
    public class SRA_TraitTagExtension : DefModExtension
    {
        public List<string> tags;
    }

    [HarmonyPatch(typeof(SkillRecord), nameof(SkillRecord.Interval))]
    public static class Patch_SkillRecord_Interval
    {
        private const string NoSkillDecayTag = "SRA_NoSkillDecay";

        [HarmonyPrefix]
        public static bool Prefix(SkillRecord __instance)
        {
            Pawn pawn = __instance?.Pawn;
            if (pawn?.story?.traits == null)
                return true;

            foreach (Trait trait in pawn.story.traits.allTraits)
            {
                if (trait?.def == null)
                    continue;

                SRA_TraitTagExtension ext = trait.def.GetModExtension<SRA_TraitTagExtension>();
                if (ext?.tags != null && ext.tags.Contains(NoSkillDecayTag))
                {
                    // 返回 false，直接跳过原版 Interval，从而不执行技能衰减
                    return false;
                }
            }

            return true;
        }
    }
}
