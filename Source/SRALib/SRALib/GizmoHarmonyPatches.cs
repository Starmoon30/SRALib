using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace SRA
{
    /// <summary>
    /// 集中处理无法通过原版 Comp/Verb 虚接口追加的 Gizmo。
    /// 这些补丁只在选中对象时枚举按钮，不参与游戏 tick。
    /// </summary>
    public static class SRAGizmoHarmonyPatches
    {
        /// <summary>
        /// 为不满足原版展示条件的 Pawn 补充声明了 IAlwaysShowGizmo 的 Hediff 按钮。
        /// </summary>
        [HarmonyPatch(typeof(Pawn), nameof(Pawn.GetGizmos))]
        public static class PawnGetGizmos
        {
            public static void Postfix(Pawn __instance, ref IEnumerable<Gizmo> __result)
            {
                __result = AppendAlwaysShowGizmos(__result, __instance);
            }
        }

        /// <summary>
        /// 原版 CompEquippable 没有按 Verb 追加普通 Gizmo 的虚接口，
        /// 因此在装备栏枚举时补充 Verb_ShootWithOffset 的弹种切换按钮。
        /// </summary>
        [HarmonyPatch(typeof(CompEquippable), nameof(CompEquippable.CompGetEquippedGizmosExtra))]
        public static class CompEquippableGetEquippedGizmosExtra
        {
            public static void Postfix(CompEquippable __instance, ref IEnumerable<Gizmo> __result)
            {
                __result = AppendMultiProjectileGizmos(__result, __instance);
            }
        }

        private static IEnumerable<Gizmo> AppendAlwaysShowGizmos(IEnumerable<Gizmo> source, Pawn pawn)
        {
            if (source != null)
            {
                foreach (Gizmo gizmo in source)
                {
                    yield return gizmo;
                }
            }

            if (pawn == null || Find.Selector.SingleSelectedThing != pawn || pawn.IsColonistPlayerControlled || pawn.IsColonyMech || pawn.IsPrisonerOfColony || (pawn.Dead && pawn.HasShowGizmosOnCorpseHediff))
            {
                yield break;
            }

            List<Hediff> hediffs = pawn.health?.hediffSet?.hediffs;
            if (hediffs == null)
            {
                yield break;
            }

            for (int i = 0; i < hediffs.Count; i++)
            {
                Hediff hediff = hediffs[i];
                if (hediff is IAlwaysShowGizmo alwaysShowHediff && alwaysShowHediff.AlwaysShowGizmo)
                {
                    foreach (Gizmo gizmo in hediff.GetGizmos())
                    {
                        yield return gizmo;
                    }
                }

                if (!(hediff is HediffWithComps hediffWithComps) || hediffWithComps.comps == null)
                {
                    continue;
                }

                for (int j = 0; j < hediffWithComps.comps.Count; j++)
                {
                    HediffComp comp = hediffWithComps.comps[j];
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

        private static IEnumerable<Gizmo> AppendMultiProjectileGizmos(IEnumerable<Gizmo> source, CompEquippable compEquippable)
        {
            if (source != null)
            {
                foreach (Gizmo gizmo in source)
                {
                    yield return gizmo;
                }
            }

            List<Verb> verbs = compEquippable?.AllVerbs;
            if (verbs == null)
            {
                yield break;
            }

            for (int i = 0; i < verbs.Count; i++)
            {
                if (verbs[i] is Verb_ShootWithOffset shootVerb)
                {
                    foreach (Gizmo gizmo in shootVerb.GetMultiProjectileGizmos())
                    {
                        yield return gizmo;
                    }
                }
            }
        }
    }
}
