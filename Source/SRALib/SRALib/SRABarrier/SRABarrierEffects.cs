using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace SRA
{
    public enum SRABarrierEffectTrigger
    {
        WhenDestroy,
        WhenRegenFull,
        WhenAbsorbDamage
    }

    public class SRABarrierEffectContext
    {
        public HediffComp_SRABarrier barrier;
        public Pawn pawn;
        public DamageInfo? dinfo;

        // Incoming damage amount before this barrier handled it. Zero for non-damage triggers.
        public float damageBefore;

        // Incoming damage amount after this barrier handled it. Zero for non-damage triggers.
        public float damageAfter;

        // Amount of incoming damage intercepted by this barrier after barrier-specific damage rules.
        public float absorbedDamage;

        // Actual barrier value consumed by the intercepted damage.
        public float barrierDamageTaken;

        public float barrierBefore;
        public float barrierAfter;
    }

    public abstract class SRABarrierEffect
    {
        // Per-barrier cooldown in ticks. A value of 0 or lower means no cooldown.
        public int cooldownTicks = 0;

        // Per-barrier cooldown in seconds. Used only when cooldownTicks is not positive.
        public float cooldownSeconds = 0f;

        // Optional stable key. Entries with the same key share cooldown on the same barrier instance.
        public string cooldownKey;

        public int CooldownTicks
        {
            get
            {
                if (cooldownTicks > 0)
                {
                    return cooldownTicks;
                }

                if (cooldownSeconds > 0f)
                {
                    return Mathf.RoundToInt(cooldownSeconds * GenTicks.TicksPerRealSecond);
                }

                return 0;
            }
        }

        public bool TryApply(SRABarrierEffectContext context, SRABarrierEffectTrigger trigger, int index)
        {
            if (context?.barrier == null || context.pawn == null)
            {
                return false;
            }

            string key = GetCooldownKey(trigger, index);
            int cooldown = CooldownTicks;
            if (cooldown > 0 && !context.barrier.CanRunEffect(key, cooldown))
            {
                return false;
            }

            if (!Apply(context))
            {
                return false;
            }

            if (cooldown > 0)
            {
                context.barrier.NotifyEffectRan(key);
            }
            return true;
        }

        public virtual IEnumerable<string> ConfigErrors()
        {
            yield break;
        }

        protected abstract bool Apply(SRABarrierEffectContext context);

        private string GetCooldownKey(SRABarrierEffectTrigger trigger, int index)
        {
            if (!cooldownKey.NullOrEmpty())
            {
                return cooldownKey;
            }

            return GetType().FullName + "_" + trigger + "_" + index;
        }
    }

    public class SRABarrierEffect_AddHediff : SRABarrierEffect
    {
        // Hediff added to the pawn when this effect triggers.
        public HediffDef hediffDef;

        // Optional target body part. Leave null to add the hediff to the whole body.
        public BodyPartDef bodyPartDef;

        // Severity assigned to the added hediff. A value below 0 keeps the hediff's default severity.
        public float severity = -1f;

        // If true and an existing matching hediff is present, increase or overwrite it instead of adding another copy.
        public bool affectExisting = true;

        // When affectExisting is true, add severity to the existing hediff instead of setting it.
        public bool addSeverityToExisting = true;

        // Optional duration for hediffs with HediffComp_Disappears. A value below 0 leaves duration unchanged.
        public int durationTicks = -1;

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string error in base.ConfigErrors())
            {
                yield return error;
            }

            if (hediffDef == null)
            {
                yield return "SRABarrierEffect_AddHediff requires hediffDef.";
            }
        }

        protected override bool Apply(SRABarrierEffectContext context)
        {
            Pawn pawn = context.pawn;
            if (hediffDef == null || pawn?.health == null)
            {
                return false;
            }

            BodyPartRecord part = FindBodyPart(pawn);
            Hediff hediff = affectExisting ? FindExistingHediff(pawn, part) : null;
            if (hediff == null)
            {
                hediff = HediffMaker.MakeHediff(hediffDef, pawn, part);
                ApplySeverity(hediff, false);
                ApplyDuration(hediff);
                pawn.health.AddHediff(hediff, part);
                return true;
            }

            ApplySeverity(hediff, addSeverityToExisting);
            ApplyDuration(hediff);
            return true;
        }

        private BodyPartRecord FindBodyPart(Pawn pawn)
        {
            if (bodyPartDef == null || pawn?.RaceProps?.body == null)
            {
                return null;
            }

            List<BodyPartRecord> parts = pawn.RaceProps.body.AllParts;
            for (int i = 0; i < parts.Count; i++)
            {
                if (parts[i].def == bodyPartDef)
                {
                    return parts[i];
                }
            }

            return null;
        }

        private Hediff FindExistingHediff(Pawn pawn, BodyPartRecord part)
        {
            List<Hediff> hediffs = pawn.health.hediffSet?.hediffs;
            if (hediffs == null)
            {
                return null;
            }

            for (int i = 0; i < hediffs.Count; i++)
            {
                Hediff hediff = hediffs[i];
                if (hediff.def == hediffDef && (part == null || hediff.Part == part))
                {
                    return hediff;
                }
            }

            return null;
        }

        private void ApplySeverity(Hediff hediff, bool add)
        {
            if (hediff == null || severity < 0f)
            {
                return;
            }

            if (add)
            {
                hediff.Severity += severity;
                return;
            }

            hediff.Severity = severity;
        }

        private void ApplyDuration(Hediff hediff)
        {
            if (durationTicks < 0)
            {
                return;
            }

            HediffComp_Disappears disappears = hediff?.TryGetComp<HediffComp_Disappears>();
            if (disappears != null)
            {
                disappears.ticksToDisappear = durationTicks;
            }
        }
    }
}
