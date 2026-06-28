using System.Collections.Generic;
using RimWorld;
using Verse;

namespace SRA
{
    public class DamageWorker_CapacitySource_Extension : DamageWorker_NoDamageFactor_Extension
    {
        // 直接指定允许命中的器官标签，例如 SightSource、ConsciousnessSource。
        public List<BodyPartTagDef> capacitySourceTags = new List<BodyPartTagDef>();

        // 通过能力名映射到原版 capacity source tag，例如 Consciousness、BloodPumping。
        public List<PawnCapacityDef> capacities = new List<PawnCapacityDef>();

        // 默认不传播，避免伤害从目标能力器官扩散到无关部位。
        public bool preventDamagePropagation = true;

        // 当 pawn 身上不存在任何匹配器官时，默认直接跳过这次伤害。
        public bool skipPawnDamageWhenNoTarget = true;

        // 非 Pawn 目标默认仍可正常受伤；改为 false 可让这种伤害只作用于 Pawn。
        public bool allowNonPawnDamage = true;
    }

    public class DamageWorker_AddInjury_CapacitySource : DamageWorker_AddInjury_NoDamageFactor
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

        public override DamageResult Apply(DamageInfo dinfo, Thing victim)
        {
            DamageWorker_CapacitySource_Extension ext = dinfo.Def.GetModExtension<DamageWorker_CapacitySource_Extension>();
            List<BodyPartTagDef> targetTags = GetTargetTags(ext);
            bool hasRestrictedTargets = targetTags.Count > 0;

            Pawn pawn = victim as Pawn;
            if (pawn == null)
            {
                if (hasRestrictedTargets && ext != null && !ext.allowNonPawnDamage)
                {
                    return new DamageResult();
                }

                return base.Apply(dinfo, victim);
            }

            if (!hasRestrictedTargets)
            {
                return base.Apply(dinfo, victim);
            }

            if (GetAvailableTargetParts(pawn, targetTags).Count == 0)
            {
                if (ext == null || ext.skipPawnDamageWhenNoTarget)
                {
                    return new DamageResult();
                }

                return base.Apply(dinfo, victim);
            }

            if (ext == null || ext.preventDamagePropagation)
            {
                dinfo.SetAllowDamagePropagation(false);
            }

            return base.Apply(dinfo, victim);
        }

        protected override BodyPartRecord ChooseHitPart(DamageInfo dinfo, Pawn pawn)
        {
            DamageWorker_CapacitySource_Extension ext = dinfo.Def.GetModExtension<DamageWorker_CapacitySource_Extension>();
            List<BodyPartTagDef> targetTags = GetTargetTags(ext);
            if (targetTags.Count == 0)
            {
                return base.ChooseHitPart(dinfo, pawn);
            }

            List<BodyPartRecord> availableTargetParts = GetAvailableTargetParts(pawn, targetTags);
            if (availableTargetParts.Count == 0)
            {
                return base.ChooseHitPart(dinfo, pawn);
            }

            BodyPartRecord hitPart = dinfo.HitPart;
            if (hitPart != null && availableTargetParts.Contains(hitPart))
            {
                return hitPart;
            }

            return ChooseWeightedPart(availableTargetParts);
        }

        private static List<BodyPartTagDef> GetTargetTags(DamageWorker_CapacitySource_Extension ext)
        {
            List<BodyPartTagDef> targetTags = new List<BodyPartTagDef>();
            if (ext == null)
            {
                return targetTags;
            }

            AddTags(targetTags, ext.capacitySourceTags);
            AddCapacitySourceTags(targetTags, ext.capacities);
            return targetTags;
        }

        private static void AddTags(List<BodyPartTagDef> targetTags, List<BodyPartTagDef> tags)
        {
            if (tags == null)
            {
                return;
            }

            for (int i = 0; i < tags.Count; i++)
            {
                BodyPartTagDef tag = tags[i];
                if (tag != null && !targetTags.Contains(tag))
                {
                    targetTags.Add(tag);
                }
            }
        }

        private static void AddCapacitySourceTags(List<BodyPartTagDef> targetTags, List<PawnCapacityDef> capacities)
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

                string[] tagNames;
                if (!CapacitySourceTagNames.TryGetValue(capacity.defName, out tagNames))
                {
                    continue;
                }

                for (int j = 0; j < tagNames.Length; j++)
                {
                    BodyPartTagDef tag = DefDatabase<BodyPartTagDef>.GetNamedSilentFail(tagNames[j]);
                    if (tag != null && !targetTags.Contains(tag))
                    {
                        targetTags.Add(tag);
                    }
                }
            }
        }

        private static List<BodyPartRecord> GetAvailableTargetParts(Pawn pawn, List<BodyPartTagDef> targetTags)
        {
            List<BodyPartRecord> result = new List<BodyPartRecord>();
            if (pawn == null || pawn.RaceProps?.body == null || pawn.health?.hediffSet == null)
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

        private static BodyPartRecord ChooseWeightedPart(List<BodyPartRecord> parts)
        {
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

            if (part.coverage > 0f)
            {
                return part.coverage;
            }

            return 1f;
        }
    }
}
