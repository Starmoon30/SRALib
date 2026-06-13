using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using System.Threading;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

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

        public static bool BlockStunAndMentalState(Pawn p)
        {
            return HediffComp_SRABarrier.PawnHasActiveMentalBarrier(p);
        }

        public static void PreApplyDamage_Prefix(Pawn __instance, ref DamageInfo dinfo)
        {
            if (__instance == null || __instance.Dead || __instance.health == null) return;
            if (dinfo.Amount <= 0.001f) return;

            var barriers = SRABarrierCache.GetSorted(__instance);
            if (barriers == null || barriers.Count == 0) return;

            for (int i = 0; i < barriers.Count; i++)
            {
                var barrier = barriers[i];
                if (barrier == null || barrier.parent == null) continue;
                if (!barrier.CanAbsorb) continue;

                barrier.AbsorbDamage(ref dinfo);
                if (dinfo.Amount <= 0.001f)
                {
                    dinfo.SetAmount(0f);
                    return;
                }
            }
        }
    }

    [HarmonyPatch(typeof(StunHandler), nameof(StunHandler.StunFor))]
    public static class Patch_StunHandler_StunFor
    {
        static bool Prefix(StunHandler __instance)
        {
            Pawn pawn = __instance.parent as Pawn;
            if (pawn != null && SRABarrierHarmonyPatches.BlockStunAndMentalState(pawn))
            {
                return false;
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(MentalStateHandler), nameof(MentalStateHandler.TryStartMentalState))]
    public static class Patch_MentalStateHandler_TryStartMentalState
    {
        static readonly AccessTools.FieldRef<MentalStateHandler, Pawn> PawnRef =
            AccessTools.FieldRefAccess<MentalStateHandler, Pawn>("pawn");

        static bool Prefix(MentalStateHandler __instance, MentalStateDef stateDef, ref bool __result, bool causedByMood)
        {
            Pawn pawn = PawnRef(__instance);
            if (pawn != null && SRABarrierHarmonyPatches.BlockStunAndMentalState(pawn))
            {
                __result = false;
                return false;
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(Pawn_HealthTracker), nameof(Pawn_HealthTracker.AddHediff),
        new Type[] { typeof(HediffDef), typeof(BodyPartRecord), typeof(DamageInfo?), typeof(DamageWorker.DamageResult) })]
    public static class Patch_PawnHealthTracker_AddHediff_Def
    {
        static bool Prefix(Pawn ___pawn, HediffDef def, ref Hediff __result)
        {
            if (HediffComp_SRABarrier.IsBlockedMentalBarrierHediff(def) && SRABarrierHarmonyPatches.BlockStunAndMentalState(___pawn))
            {
                __result = null;
                return false;
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(Pawn_HealthTracker), nameof(Pawn_HealthTracker.AddHediff),
        new Type[] { typeof(Hediff), typeof(BodyPartRecord), typeof(DamageInfo?), typeof(DamageWorker.DamageResult) })]
    public static class Patch_PawnHealthTracker_AddHediff_Instance
    {
        static bool Prefix(Pawn ___pawn, Hediff hediff)
        {
            if (HediffComp_SRABarrier.IsBlockedMentalBarrierHediff(hediff?.def) && SRABarrierHarmonyPatches.BlockStunAndMentalState(___pawn))
            {
                return false;
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(Pawn), "GetGizmos")]
    public static class Pawn_GetGizmos_Patch
    {
        static void Postfix(Pawn __instance, ref IEnumerable<Gizmo> __result)
        {
            if (Find.Selector.SingleSelectedThing != __instance)
                return;

            var alwaysShowGizmos = GetAlwaysShowGizmos(__instance);
            if (alwaysShowGizmos != null)
            {
                __result = __result.Concat(alwaysShowGizmos);
            }
        }

        public static IEnumerable<Gizmo> GetAlwaysShowGizmos(Pawn pawn)
        {
            if (Find.Selector.SingleSelectedThing != pawn || pawn.IsColonistPlayerControlled || pawn.IsColonyMech || pawn.IsPrisonerOfColony || (pawn.Dead && pawn.HasShowGizmosOnCorpseHediff))
                yield break;
            if (pawn.health?.hediffSet?.hediffs != null)
            {
                foreach (Hediff hediff in pawn.health.hediffSet.hediffs)
                {
                    if (hediff is IAlwaysShowGizmo alwaysShowHediff && alwaysShowHediff.AlwaysShowGizmo)
                    {
                        foreach (Gizmo gizmo in hediff.GetGizmos())
                        {
                            yield return gizmo;
                        }
                    }
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
