using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace SRA
{
    public class DirectHediffApplicationRequest
    {
        public Pawn pawn;
        public HediffDef hediffDef;
        public DamageDef damageDef;
        public float severity;
        public int durationTicks = -1;
        public List<BodyPartTagDef> capacitySourceTags;
        public List<PawnCapacityDef> capacities;
        public BodyPartRecord fallbackPart;
        public bool applyToWholeBody;
        public bool applyToAllMatchingParts;
        public bool skipPawnWhenNoTargetPart = true;
        public bool applyToDeadPawns;
        public bool destroysBodyParts = true;
        public ThingDef sourceDef;
        public string sourceLabel;
        public BodyPartGroupDef sourceBodyPartGroup;
        public HediffDef sourceHediffDef;
        public string sourceToolLabel;
        public DamageInfo? damageInfo;
        public DamageWorker.DamageResult damageResult;
        public bool recordDamageResult;
    }

    public struct DirectHediffApplicationResult
    {
        public int appliedCount;
        public float totalDamageRecorded;

        public bool Applied => appliedCount > 0;
    }

    public static class DirectHediffApplicationUtility
    {
        private static readonly Dictionary<string, string[]> CapacitySourceTagNames = new Dictionary<string, string[]>
        {
            { "BloodFiltration", new[] { "BloodFiltrationSource" } },
            { "BloodPumping", new[] { "BloodPumpingSource" } },
            { "Breathing", new[] { "BreathingSource" } },
            { "Consciousness", new[] { "ConsciousnessSource" } },
            { "Eating", new[] { "EatingSource" } },
            { "Hearing", new[] { "HearingSource" } },
            { "Manipulation", new[] { "ManipulationLimbCore" } },
            { "Metabolism", new[] { "MetabolismSource" } },
            { "Moving", new[] { "MovingLimbCore" } },
            { "Sight", new[] { "SightSource" } },
            { "Talking", new[] { "TalkingSource" } }
        };

        public static DirectHediffApplicationResult Apply(DirectHediffApplicationRequest request)
        {
            DirectHediffApplicationResult result = new DirectHediffApplicationResult();
            if (request == null || request.pawn == null || request.pawn.health == null || request.pawn.RaceProps?.body == null)
            {
                return result;
            }

            Pawn pawn = request.pawn;
            if (pawn.Dead && !request.applyToDeadPawns)
            {
                return result;
            }

            if (request.severity <= 0f)
            {
                return result;
            }

            List<BodyPartTagDef> targetTags = GetTargetTags(request.capacitySourceTags, request.capacities);
            bool hasPartFilters = targetTags.Count > 0;
            List<BodyPartRecord> targetParts = GetAvailableTargetParts(pawn, targetTags);
            if (targetParts.Count > 0)
            {
                if (request.applyToAllMatchingParts)
                {
                    for (int i = 0; i < targetParts.Count; i++)
                    {
                        ApplyToPart(request, targetParts[i], ref result);
                    }
                    return result;
                }

                ApplyToPart(request, ChooseWeightedPart(targetParts), ref result);
                return result;
            }

            if (hasPartFilters && request.skipPawnWhenNoTargetPart)
            {
                return result;
            }

            if (request.fallbackPart != null && !pawn.health.hediffSet.PartIsMissing(request.fallbackPart))
            {
                ApplyToPart(request, request.fallbackPart, ref result);
                return result;
            }

            if (request.applyToWholeBody)
            {
                ApplyToPart(request, null, ref result);
            }

            return result;
        }

        public static bool HasTargetFilters(List<BodyPartTagDef> capacitySourceTags, List<PawnCapacityDef> capacities)
        {
            return !capacitySourceTags.NullOrEmpty() || !capacities.NullOrEmpty();
        }

        public static List<BodyPartTagDef> GetTargetTags(List<BodyPartTagDef> capacitySourceTags, List<PawnCapacityDef> capacities)
        {
            List<BodyPartTagDef> result = new List<BodyPartTagDef>();
            AddTags(result, capacitySourceTags);
            AddCapacitySourceTags(result, capacities);
            return result;
        }

        public static List<BodyPartRecord> GetAvailableTargetParts(Pawn pawn, List<BodyPartTagDef> targetTags)
        {
            List<BodyPartRecord> result = new List<BodyPartRecord>();
            if (pawn == null || pawn.RaceProps?.body == null || pawn.health?.hediffSet == null || targetTags.NullOrEmpty())
            {
                return result;
            }

            for (int i = 0; i < targetTags.Count; i++)
            {
                BodyPartTagDef tag = targetTags[i];
                if (tag == null)
                {
                    continue;
                }

                List<BodyPartRecord> partsWithTag = pawn.RaceProps.body.GetPartsWithTag(tag);
                if (partsWithTag == null)
                {
                    continue;
                }

                for (int j = 0; j < partsWithTag.Count; j++)
                {
                    BodyPartRecord part = partsWithTag[j];
                    if (part != null && !result.Contains(part) && !pawn.health.hediffSet.PartIsMissing(part))
                    {
                        result.Add(part);
                    }
                }
            }

            return result;
        }

        public static BodyPartRecord ChooseWeightedPart(List<BodyPartRecord> parts)
        {
            if (parts.NullOrEmpty())
            {
                return null;
            }

            float totalWeight = 0f;
            for (int i = 0; i < parts.Count; i++)
            {
                totalWeight += GetPartWeight(parts[i]);
            }

            if (totalWeight <= 0f)
            {
                return parts[parts.Count - 1];
            }

            float value = Rand.Value * totalWeight;
            for (int i = 0; i < parts.Count; i++)
            {
                value -= GetPartWeight(parts[i]);
                if (value <= 0f)
                {
                    return parts[i];
                }
            }

            return parts[parts.Count - 1];
        }

        private static void ApplyToPart(DirectHediffApplicationRequest request, BodyPartRecord part, ref DirectHediffApplicationResult result)
        {
            Pawn pawn = request.pawn;
            HediffDef resolvedHediffDef = ResolveHediffDef(request, part);
            if (resolvedHediffDef == null)
            {
                return;
            }

            if (part == null && typeof(Hediff_Injury).IsAssignableFrom(resolvedHediffDef.hediffClass))
            {
                part = pawn.RaceProps.body.corePart;
            }

            if (part != null && pawn.health.hediffSet.PartIsMissing(part))
            {
                return;
            }

            float partHealthBeforeAdd = part != null ? pawn.health.hediffSet.GetPartHealth(part) : 0f;
            Hediff hediff = HediffMaker.MakeHediff(resolvedHediffDef, pawn, part);
            hediff.Severity = request.severity;
            Hediff_Injury injury = hediff as Hediff_Injury;
            if (injury != null)
            {
                injury.Part = part;
                injury.sourceDef = request.sourceDef;
                injury.sourceLabel = request.sourceLabel ?? request.sourceDef?.label ?? "";
                injury.sourceBodyPartGroup = request.sourceBodyPartGroup;
                injury.sourceHediffDef = request.sourceHediffDef;
                injury.sourceToolLabel = request.sourceToolLabel;
                injury.destroysBodyParts = request.destroysBodyParts;
            }

            if (request.durationTicks > 0)
            {
                HediffComp_Disappears disappears = hediff.TryGetComp<HediffComp_Disappears>();
                if (disappears != null)
                {
                    disappears.ticksToDisappear = request.durationTicks;
                }
            }

            DamageWorker.DamageResult damageResult = request.recordDamageResult ? request.damageResult : null;
            pawn.health.AddHediff(hediff, part, request.damageInfo, damageResult);
            result.appliedCount++;

            if (request.recordDamageResult && request.damageResult != null)
            {
                float recordedDamage = injury != null && part != null ? Mathf.Min(injury.Severity, partHealthBeforeAdd) : 0f;
                request.damageResult.totalDamageDealt += recordedDamage;
                request.damageResult.wounded = true;
                if (part != null)
                {
                    request.damageResult.AddPart(pawn, part);
                }
                request.damageResult.AddHediff(hediff);
                result.totalDamageRecorded += recordedDamage;
            }
        }

        private static HediffDef ResolveHediffDef(DirectHediffApplicationRequest request, BodyPartRecord part)
        {
            if (request.hediffDef != null)
            {
                return request.hediffDef;
            }

            DamageDef resolvedDamageDef = request.damageDef;
            if (resolvedDamageDef == null)
            {
                return null;
            }

            BodyPartRecord resolvedPart = part ?? request.pawn.RaceProps.body.corePart;
            return resolvedPart != null ? HealthUtility.GetHediffDefFromDamage(resolvedDamageDef, request.pawn, resolvedPart) : resolvedDamageDef.hediff;
        }

        private static void AddTags(List<BodyPartTagDef> result, List<BodyPartTagDef> tags)
        {
            if (tags == null)
            {
                return;
            }

            for (int i = 0; i < tags.Count; i++)
            {
                BodyPartTagDef tag = tags[i];
                if (tag != null && !result.Contains(tag))
                {
                    result.Add(tag);
                }
            }
        }

        private static void AddCapacitySourceTags(List<BodyPartTagDef> result, List<PawnCapacityDef> capacities)
        {
            if (capacities == null)
            {
                return;
            }

            for (int i = 0; i < capacities.Count; i++)
            {
                PawnCapacityDef capacity = capacities[i];
                if (capacity == null)
                {
                    continue;
                }

                if (!CapacitySourceTagNames.TryGetValue(capacity.defName, out string[] tagNames))
                {
                    continue;
                }

                for (int j = 0; j < tagNames.Length; j++)
                {
                    BodyPartTagDef tag = DefDatabase<BodyPartTagDef>.GetNamedSilentFail(tagNames[j]);
                    if (tag != null && !result.Contains(tag))
                    {
                        result.Add(tag);
                    }
                }
            }
        }

        private static float GetPartWeight(BodyPartRecord part)
        {
            if (part == null)
            {
                return 0f;
            }

            if (part.coverageAbs > 0f)
            {
                return part.coverageAbs;
            }

            return part.coverage > 0f ? part.coverage : 1f;
        }
    }
}
