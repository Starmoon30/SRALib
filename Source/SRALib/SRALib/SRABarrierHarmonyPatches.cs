using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using Verse;

namespace SRA
{
    [StaticConstructorOnStartup]
    public static class SRABarrierHarmonyPatches
    {
        static SRABarrierHarmonyPatches()
        {
            try
            {
                var harmony = new Harmony("rimworld.SRA.SRAbarriersystem");
                harmony.Patch(
                    original: AccessTools.Method(typeof(Pawn), nameof(Pawn.PreApplyDamage)),
                    prefix: new HarmonyMethod(typeof(SRABarrierHarmonyPatches), nameof(PreApplyDamage_Prefix))
                );
            }
            catch (Exception ex)
            {
                Log.Error($"[SRA Barrier] Failed to apply Harmony patches: {ex}");
            }
        }

        public static void PreApplyDamage_Prefix(Pawn __instance, ref DamageInfo dinfo)
        {
            try
            {
                if (__instance == null || __instance.Dead || __instance.health == null) return;

                var barriers = new List<HediffComp_SRABarrier>();
                foreach (Hediff hediff in __instance.health.hediffSet.hediffs)
                {
                    if (hediff.TryGetComp<HediffComp_SRABarrier>() is HediffComp_SRABarrier barrier &&
                        barrier.CanAbsorb)
                    {
                        barriers.Add(barrier);
                    }
                }
                barriers.Sort((a, b) => b.Props.priority.CompareTo(a.Props.priority));
                foreach (var barrier in barriers)
                {
                    barrier.AbsorbDamage(ref dinfo);
                    if (dinfo.Amount <= 0.001f) return;
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[SRA Barrier] Error in damage absorption: {ex}");
            }
        }
    }

    [HarmonyPatch(typeof(Pawn), "GetGizmos")]
    public static class Pawn_GetGizmos_Patch
    {
        static void Postfix(Pawn __instance, ref IEnumerable<Gizmo> __result)
        {
            // 如果当前选中的不是这个pawn，则不做任何事
            if (Find.Selector.SingleSelectedThing != __instance)
                return;

            // 获取需要总是显示的gizmo
            var alwaysShowGizmos = GetAlwaysShowGizmos(__instance);
            if (alwaysShowGizmos != null)
            {
                // 将原来的gizmo和新的gizmo合并
                __result = __result.Concat(alwaysShowGizmos);
            }
        }

        // 获取所有应该总是显示的 Gizmo
        public static IEnumerable<Gizmo> GetAlwaysShowGizmos(Pawn pawn)
        {
            if (Find.Selector.SingleSelectedThing != pawn || pawn.IsColonistPlayerControlled || pawn.IsColonyMech || pawn.IsPrisonerOfColony || (pawn.Dead && pawn.HasShowGizmosOnCorpseHediff))
                yield break;
            if (pawn.health?.hediffSet?.hediffs != null)
            {
                foreach (Hediff hediff in pawn.health.hediffSet.hediffs)
                {
                    // 检查 Hediff 本身
                    if (hediff is IAlwaysShowGizmo alwaysShowHediff && alwaysShowHediff.AlwaysShowGizmo)
                    {
                        foreach (Gizmo gizmo in hediff.GetGizmos())
                        {
                            yield return gizmo;
                        }
                    }
                    // 检查 HediffComps
                    if (hediff is HediffWithComps hediffWithComps)
                    {
                        foreach (HediffComp comp in hediffWithComps.comps)
                        {
                            if (comp is IAlwaysShowGizmo alwaysShowComp && alwaysShowComp.AlwaysShowGizmo)
                            {
                                foreach (Gizmo gizmo in comp.CompGetGizmos())
                                {
                                    yield return gizmo;
                                }
                            }
                        }
                    }
                }
            }
        }
    }
}