using RimWorld;
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.Sound;

namespace SRA
{
    public class Damage_SRABarrier_factor_Extension : DefModExtension
    {
        public float damage_SRABarrier_factor = -1f;
    }

    public interface IAlwaysShowGizmo
    {
        bool AlwaysShowGizmo { get; }
    }

    public class HediffCompProperties_SRABarrier : HediffCompProperties
    {
        // Maximum stored barrier points.
        public float maxBarrier = 100f;

        // Barrier damage multiplier. Higher values make the same incoming hit consume more barrier.
        public float DamageTakenMult = 1f;

        // Maximum barrier-relevant damage per hit. Values at or below 0 disable the cap.
        public float DamageTakenMax = 0f;

        // Flat reduction before barrier damage is applied. Values at or below 0 disable the reduction.
        public float DamageTakenReduce = 0f;

        // Barrier points restored once per real-time second after regenDelay has passed.
        public float regenRate = 5f;

        // Seconds after taking damage before normal regeneration can resume.
        public float regenDelay = 3f;

        // Seconds after the barrier breaks before it can reactivate.
        public float rechargeCooldown = 10f;

        // Remove the parent hediff immediately when the barrier breaks.
        public bool RemoveWhenDestroy = false;

        // Psychic bulwark: blocks stun, mental states, catatonic breakdown, and porcupine quills.
        public bool BlockStunAndMentalState = false;

        // Hardened barrier: damage against the barrier is processed through the pawn's final armor.
        public bool HardenedBarrier = false;

        // Deflective barrier: uses the pawn's final MeleeDodgeChance as a universal dodge chance.
        public bool DeflectiveBarrier = false;

        // Higher priority barriers attempt to absorb damage before lower priority barriers.
        public int priority = 0;

        // Effects fired once when a real barrier hit reduces the barrier to zero.
        public List<SRABarrierEffect> whenDestroy;

        // Effects fired once when regeneration or recharge raises the barrier from not-full to full.
        public List<SRABarrierEffect> whenRegenFull;

        // Effects fired after a hit actually consumes barrier points. Direct blocks do not fire this.
        public List<SRABarrierEffect> whenAbsorbDamage;

        public HediffCompProperties_SRABarrier() => compClass = typeof(HediffComp_SRABarrier);

        public override IEnumerable<string> ConfigErrors(HediffDef parentDef)
        {
            foreach (string error in base.ConfigErrors(parentDef))
            {
                yield return error;
            }

            foreach (string error in ConfigErrorsForEffects(whenDestroy, nameof(whenDestroy)))
            {
                yield return error;
            }

            foreach (string error in ConfigErrorsForEffects(whenRegenFull, nameof(whenRegenFull)))
            {
                yield return error;
            }

            foreach (string error in ConfigErrorsForEffects(whenAbsorbDamage, nameof(whenAbsorbDamage)))
            {
                yield return error;
            }
        }

        private static IEnumerable<string> ConfigErrorsForEffects(List<SRABarrierEffect> effects, string listName)
        {
            if (effects.NullOrEmpty())
            {
                yield break;
            }

            for (int i = 0; i < effects.Count; i++)
            {
                SRABarrierEffect effect = effects[i];
                if (effect == null)
                {
                    yield return $"{listName} contains a null effect at index {i}.";
                    continue;
                }

                foreach (string error in effect.ConfigErrors())
                {
                    yield return $"{listName}[{i}]: {error}";
                }
            }
        }
    }

    public class HediffComp_SRABarrier : HediffComp, IAlwaysShowGizmo
    {
        private static HediffDef porcupineQuillDef;
        private float currentBarrier;
        private int lastDamageTick = -1;
        private int brokenTick = -1;
        private Dictionary<string, int> effectLastRunTicks;
        private bool initialRegenFullTriggered;
        public bool isActive = true;

        public HediffCompProperties_SRABarrier Props =>
            (HediffCompProperties_SRABarrier)props;

        public float CurrentBarrier
        {
            get => currentBarrier;
            set => currentBarrier = Mathf.Clamp(value, 0, Props.maxBarrier);
        }

        public bool InCooldown =>
            brokenTick > 0 && Find.TickManager.TicksGame < brokenTick +
            (Props.rechargeCooldown * GenTicks.TicksPerRealSecond);

        public bool CanAbsorb =>
            isActive && CurrentBarrier > 0 && !InCooldown;

        public static HediffDef PorcupineQuillDef
        {
            get
            {
                if (porcupineQuillDef == null)
                {
                    porcupineQuillDef = DefDatabase<HediffDef>.GetNamedSilentFail("PorcupineQuill");
                }
                return porcupineQuillDef;
            }
        }

        public override void CompPostMake() =>
            CurrentBarrier = Props.maxBarrier;

        public override void CompPostPostAdd(DamageInfo? dinfo)
        {
            base.CompPostPostAdd(dinfo);
            SRABarrierCache.MarkDirty(Pawn);
            TryTriggerInitialRegenFullEffects();
        }

        public override void CompPostPostRemoved()
        {
            SRABarrierCache.MarkDirty(Pawn);
            base.CompPostPostRemoved();
        }

        public override void CompPostMerged(Hediff other)
        {
            base.CompPostMerged(other);
            SRABarrierCache.MarkDirty(Pawn);
        }

        public override void CompExposeData()
        {
            base.CompExposeData();
            Scribe_Values.Look(ref currentBarrier, "currentBarrier");
            Scribe_Values.Look(ref lastDamageTick, "lastDamageTick", -1);
            Scribe_Values.Look(ref brokenTick, "brokenTick", -1);
            Scribe_Collections.Look(ref effectLastRunTicks, "effectLastRunTicks", LookMode.Value, LookMode.Value);
            Scribe_Values.Look(ref initialRegenFullTriggered, "initialRegenFullTriggered", true);
            Scribe_Values.Look(ref isActive, "isActive", true);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (effectLastRunTicks == null)
                {
                    effectLastRunTicks = new Dictionary<string, int>();
                }
                SRABarrierCache.MarkDirty(Pawn);
            }
        }

        public float GetCooldownSeconds()
        {
            return Props.rechargeCooldown -
                (Find.TickManager.TicksGame - brokenTick) / (float)GenTicks.TicksPerRealSecond;
        }

        public override void CompPostTick(ref float severityAdjustment)
        {
            if (!Pawn.Spawned || Pawn.Dead)
            {
                return;
            }

            if (Pawn.IsHashIntervalTick(GenTicks.TicksPerRealSecond))
            {
                if (!isActive)
                {
                    if (GetCooldownSeconds() <= 0)
                    {
                        if (Props.rechargeCooldown > 0)
                        {
                            float barrierBefore = CurrentBarrier;
                            CurrentBarrier = Props.maxBarrier;
                            TryTriggerRegenFullEffects(barrierBefore);
                            SRALib_DefOf.EnergyShield_Reset.PlayOneShot(new TargetInfo(Pawn.Position, Pawn.Map, false));
                        }
                        isActive = true;
                    }
                    return;
                }

                if (Props.BlockStunAndMentalState)
                {
                    RemoveStunAndMentalState();
                }

                if (CurrentBarrier >= Props.maxBarrier)
                {
                    return;
                }

                bool pastRegenDelay = lastDamageTick < 0 ||
                    Find.TickManager.TicksGame > lastDamageTick +
                    (Props.regenDelay * GenTicks.TicksPerRealSecond);

                if (pastRegenDelay)
                {
                    float barrierBefore = CurrentBarrier;
                    CurrentBarrier += Props.regenRate;
                    TryTriggerRegenFullEffects(barrierBefore);
                }
            }
        }

        public void AbsorbDamage(ref DamageInfo dinfo)
        {
            if (!CanAbsorb)
            {
                return;
            }

            float damageBefore = dinfo.Amount;
            float damageToAbsorb = dinfo.Amount;
            Damage_SRABarrier_factor_Extension ext = dinfo.Def.GetModExtension<Damage_SRABarrier_factor_Extension>();
            if (ext != null && ext.damage_SRABarrier_factor >= 0f)
            {
                damageToAbsorb *= ext.damage_SRABarrier_factor;
            }
            else if (!dinfo.Def.harmsHealth)
            {
                dinfo.SetAmount(0f);
                return;
            }

            if (Props.DeflectiveBarrier && TryDeflectDamage())
            {
                dinfo.SetAmount(0f);
                return;
            }

            if (Props.HardenedBarrier)
            {
                damageToAbsorb = ApplyHardenedBarrierArmor(damageToAbsorb, ref dinfo);
                if (damageToAbsorb <= 0f)
                {
                    dinfo.SetAmount(0f);
                    return;
                }
            }

            if (Props.DamageTakenMax > 0f)
            {
                damageToAbsorb = Mathf.Min(damageToAbsorb, Props.DamageTakenMax);
            }

            if (Props.DamageTakenReduce > 0f)
            {
                damageToAbsorb -= Props.DamageTakenReduce;
            }

            float incomingDamageFactor = Math.Min(Pawn.GetStatValue(StatDefOf.IncomingDamageFactor, true, -1), 1f);
            if (damageToAbsorb <= 0f || Props.DamageTakenMult <= 0f || incomingDamageFactor <= 0f)
            {
                dinfo.SetAmount(0f);
                return;
            }

            float barrierBefore = CurrentBarrier;
            float absorbed = Mathf.Min(CurrentBarrier / Props.DamageTakenMult / incomingDamageFactor, damageToAbsorb);
            float barrierDamageTaken = absorbed * Props.DamageTakenMult * incomingDamageFactor;
            CurrentBarrier -= barrierDamageTaken;
            dinfo.SetAmount(Mathf.Min(dinfo.Amount, damageToAbsorb - absorbed));
            lastDamageTick = Find.TickManager.TicksGame;
            TriggerEffects(
                Props.whenAbsorbDamage,
                SRABarrierEffectTrigger.WhenAbsorbDamage,
                dinfo,
                damageBefore,
                dinfo.Amount,
                absorbed,
                barrierDamageTaken,
                barrierBefore,
                CurrentBarrier);

            if (CurrentBarrier <= 0.001f)
            {
                CurrentBarrier = 0f;
                brokenTick = Find.TickManager.TicksGame;
                isActive = false;
                SRALib_DefOf.EnergyShield_Broken.PlayOneShot(new TargetInfo(Pawn.Position, Pawn.Map, false));
                TriggerEffects(
                    Props.whenDestroy,
                    SRABarrierEffectTrigger.WhenDestroy,
                    dinfo,
                    damageBefore,
                    dinfo.Amount,
                    absorbed,
                    barrierDamageTaken,
                    barrierBefore,
                    CurrentBarrier);
                if (Props.RemoveWhenDestroy)
                {
                    parent.Severity = 0f;
                }
            }
        }

        public bool CanRunEffect(string key, int cooldownTicks)
        {
            if (key.NullOrEmpty() || cooldownTicks <= 0)
            {
                return true;
            }

            if (effectLastRunTicks == null || !effectLastRunTicks.TryGetValue(key, out int lastTick))
            {
                return true;
            }

            return Find.TickManager.TicksGame >= lastTick + cooldownTicks;
        }

        public void NotifyEffectRan(string key)
        {
            if (key.NullOrEmpty())
            {
                return;
            }

            if (effectLastRunTicks == null)
            {
                effectLastRunTicks = new Dictionary<string, int>();
            }

            effectLastRunTicks[key] = Find.TickManager.TicksGame;
        }

        private void TryTriggerRegenFullEffects(float barrierBefore)
        {
            if (barrierBefore >= Props.maxBarrier || CurrentBarrier < Props.maxBarrier)
            {
                return;
            }

            TriggerEffects(
                Props.whenRegenFull,
                SRABarrierEffectTrigger.WhenRegenFull,
                null,
                0f,
                0f,
                0f,
                0f,
                barrierBefore,
                CurrentBarrier);
        }

        private void TryTriggerInitialRegenFullEffects()
        {
            if (initialRegenFullTriggered || Pawn == null)
            {
                return;
            }

            initialRegenFullTriggered = true;

            // A newly added barrier starts at full strength and should behave like it just crossed
            // the full threshold, so XML authors can use whenRegenFull for initial shield effects.
            TryTriggerRegenFullEffects(0f);
        }

        private void TriggerEffects(
            List<SRABarrierEffect> effects,
            SRABarrierEffectTrigger trigger,
            DamageInfo? dinfo,
            float damageBefore,
            float damageAfter,
            float absorbedDamage,
            float barrierDamageTaken,
            float barrierBefore,
            float barrierAfter)
        {
            if (effects.NullOrEmpty() || Pawn == null)
            {
                return;
            }

            SRABarrierEffectContext context = new SRABarrierEffectContext
            {
                barrier = this,
                pawn = Pawn,
                dinfo = dinfo,
                damageBefore = damageBefore,
                damageAfter = damageAfter,
                absorbedDamage = absorbedDamage,
                barrierDamageTaken = barrierDamageTaken,
                barrierBefore = barrierBefore,
                barrierAfter = barrierAfter
            };

            for (int i = 0; i < effects.Count; i++)
            {
                SRABarrierEffect effect = effects[i];
                if (effect == null)
                {
                    continue;
                }

                try
                {
                    effect.TryApply(context, trigger, i);
                }
                catch (Exception ex)
                {
                    Log.Error("[SRA Barrier] Effect failed on " + Pawn.ToStringSafe() + ": " + ex);
                }
            }
        }

        private float ApplyHardenedBarrierArmor(float damageAmount, ref DamageInfo dinfo)
        {
            if (Pawn == null || dinfo.IgnoreArmor || dinfo.InstantPermanentInjury)
            {
                return damageAmount;
            }

            DamageDef def = dinfo.Def;
            if (def.armorCategory == null)
            {
                return damageAmount;
            }

            if (dinfo.HitPart == null)
            {
                float amount = damageAmount;
                ApplyArmorWithoutHitPart(ref amount, dinfo.ArmorPenetrationInt, Pawn.GetStatValue(def.armorCategory.armorRatingStat, true, -1), ref def);
                dinfo.Def = def;
                return amount;
            }

            bool deflectedByMetalArmor = false;
            bool diminishedByMetalArmor;
            float result = ArmorUtility.GetPostArmorDamage(
                Pawn,
                damageAmount,
                dinfo.ArmorPenetrationInt,
                dinfo.HitPart,
                ref def,
                out deflectedByMetalArmor,
                out diminishedByMetalArmor);
            dinfo.Def = def;
            return result;
        }

        private static void ApplyArmorWithoutHitPart(ref float amount, float armorPenetration, float armorRating, ref DamageDef damageDef)
        {
            float armorDiff = Mathf.Max(armorRating - armorPenetration, 0f);
            float rand = Rand.Value;
            if (rand < armorDiff * 0.5f)
            {
                amount = 0f;
                return;
            }

            if (rand < armorDiff)
            {
                amount = GenMath.RoundRandom(amount / 2f);
                if (damageDef.armorCategory == DamageArmorCategoryDefOf.Sharp)
                {
                    damageDef = DamageDefOf.Blunt;
                }
            }
        }

        private bool TryDeflectDamage()
        {
            float dodgeChance = GetDeflectiveBarrierDodgeChance();
            return dodgeChance > 0f && Rand.Chance(Mathf.Clamp01(dodgeChance));
        }

        private float GetDeflectiveBarrierDodgeChance()
        {
            if (Pawn == null || !Pawn.Spawned || IsBarrierDodgeTargetImmobile())
            {
                return 0f;
            }

            return Mathf.Max(0f, Pawn.GetStatValue(StatDefOf.MeleeDodgeChance, true, -1));
        }

        private bool IsBarrierDodgeTargetImmobile()
        {
            return Pawn == null || Pawn.Downed || Pawn.GetPosture() > PawnPosture.Standing;
        }

        public void RemoveStunAndMentalState()
        {
            Pawn pawn = Pawn;
            if (pawn != null)
            {
                RemoveAllHediffsOfDef(pawn, HediffDefOf.CatatonicBreakdown);
                RemoveAllHediffsOfDef(pawn, PorcupineQuillDef);
                pawn.stances?.stunner?.StopStun();
                MentalState mentalState = pawn.MentalState;
                if (mentalState != null)
                {
                    mentalState.RecoverFromState();
                }
            }
        }

        private static void RemoveAllHediffsOfDef(Pawn pawn, HediffDef def)
        {
            if (pawn?.health?.hediffSet == null || def == null)
            {
                return;
            }

            Hediff hediff;
            while ((hediff = pawn.health.hediffSet.GetFirstHediffOfDef(def, false)) != null)
            {
                pawn.health.RemoveHediff(hediff);
            }
        }

        public static bool IsBlockedMentalBarrierHediff(HediffDef def)
        {
            if (def == null)
            {
                return false;
            }

            if (def == HediffDefOf.CatatonicBreakdown)
            {
                return true;
            }

            HediffDef porcupineQuill = PorcupineQuillDef;
            if (porcupineQuill != null && def == porcupineQuill)
            {
                return true;
            }

            return false;
        }

        public static bool PawnHasActiveMentalBarrier(Pawn pawn)
        {
            if (pawn == null)
            {
                return false;
            }

            List<HediffComp_SRABarrier> barriers = SRABarrierCache.GetSorted(pawn);
            if (barriers == null || barriers.Count == 0)
            {
                return false;
            }

            for (int i = 0; i < barriers.Count; i++)
            {
                HediffComp_SRABarrier barrier = barriers[i];
                if (barrier == null || barrier.parent == null)
                {
                    continue;
                }

                if (barrier.isActive && barrier.Props.BlockStunAndMentalState)
                {
                    return true;
                }
            }

            return false;
        }

        public override string CompTipStringExtra
        {
            get
            {
                StringBuilder stringBuilder = new StringBuilder();
                stringBuilder.Append("SRA_BarrierTipExtra".Translate(
                    Props.maxBarrier.ToString(),
                    Props.regenRate.ToString(),
                    Props.regenDelay.ToString(),
                    Props.rechargeCooldown.ToString(),
                    Props.DamageTakenMult.ToString(),
                    Props.DamageTakenMax.ToString(),
                    Props.DamageTakenReduce.ToString()));
                if (Props.BlockStunAndMentalState)
                {
                    stringBuilder.Append("SRA_BarrierPsychicBulwarkExtra".Translate());
                }
                if (Props.HardenedBarrier)
                {
                    stringBuilder.Append("SRA_BarrierHardenedExtra".Translate());
                }
                if (Props.DeflectiveBarrier)
                {
                    stringBuilder.Append("SRA_BarrierDeflectiveExtra".Translate());
                }
                return stringBuilder.ToString();
            }
        }

        public bool AlwaysShowGizmo => true;

        public override IEnumerable<Gizmo> CompGetGizmos()
        {
            yield return new SRABarrierGizmo(this);
        }
    }

    public static class SRABarrierCache
    {
        private sealed class Entry
        {
            public bool dirty = true;
            public readonly List<HediffComp_SRABarrier> sorted = new List<HediffComp_SRABarrier>(4);
        }

        private static readonly Dictionary<int, Entry> map = new Dictionary<int, Entry>(256);

        public static List<HediffComp_SRABarrier> GetSorted(Pawn pawn)
        {
            if (pawn == null)
            {
                return null;
            }

            int key = pawn.thingIDNumber;
            Entry e;
            if (!map.TryGetValue(key, out e))
            {
                e = new Entry();
                map[key] = e;
            }

            if (e.dirty)
            {
                Rebuild(pawn, e);
            }
            return e.sorted;
        }

        public static void MarkDirty(Pawn pawn)
        {
            if (pawn == null)
            {
                return;
            }

            int key = pawn.thingIDNumber;
            Entry e;
            if (!map.TryGetValue(key, out e))
            {
                e = new Entry();
                map[key] = e;
            }
            e.dirty = true;
        }

        public static void RemovePawn(Pawn pawn)
        {
            if (pawn == null)
            {
                return;
            }
            map.Remove(pawn.thingIDNumber);
        }

        private static void Rebuild(Pawn pawn, Entry e)
        {
            e.dirty = false;
            e.sorted.Clear();

            List<Hediff> hediffs = pawn?.health?.hediffSet?.hediffs;
            if (hediffs == null)
            {
                return;
            }

            for (int i = 0; i < hediffs.Count; i++)
            {
                HediffComp_SRABarrier b = hediffs[i].TryGetComp<HediffComp_SRABarrier>();
                if (b != null)
                {
                    e.sorted.Add(b);
                }
            }

            e.sorted.Sort((a, b) => b.Props.priority.CompareTo(a.Props.priority));
        }
    }
}
