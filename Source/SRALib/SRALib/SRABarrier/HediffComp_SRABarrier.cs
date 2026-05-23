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
        public float maxBarrier = 100f;
        public float DamageTakenMult = 1f;
        public float DamageTakenMax = 0f;
        public float DamageTakenReduce = 0f;
        public float regenRate = 5f;
        public float regenDelay = 3f;
        public float rechargeCooldown = 10f;
        public bool RemoveWhenDestroy = false;
        public bool BlockStunAndMentalState = false;
        public bool HardenedBarrier = false;
        public bool DeflectiveBarrier = false;
        public int priority = 0;

        public HediffCompProperties_SRABarrier() => compClass = typeof(HediffComp_SRABarrier);
    }

    public class HediffComp_SRABarrier : HediffComp, IAlwaysShowGizmo
    {
        private float currentBarrier;
        private int lastDamageTick = -1;
        private int brokenTick = -1;
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

        public override void CompPostMake() =>
            CurrentBarrier = Props.maxBarrier;

        public override void CompPostPostAdd(DamageInfo? dinfo)
        {
            base.CompPostPostAdd(dinfo);
            SRABarrierCache.MarkDirty(Pawn);
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
            Scribe_Values.Look(ref isActive, "isActive", true);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
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
                            CurrentBarrier = Props.maxBarrier;
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
                    CurrentBarrier += Props.regenRate;
                }
            }
        }

        public void AbsorbDamage(ref DamageInfo dinfo)
        {
            if (!CanAbsorb)
            {
                return;
            }

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

            float absorbed = Mathf.Min(CurrentBarrier / Props.DamageTakenMult / incomingDamageFactor, damageToAbsorb);
            CurrentBarrier -= absorbed * Props.DamageTakenMult * incomingDamageFactor;
            dinfo.SetAmount(Mathf.Min(dinfo.Amount, damageToAbsorb - absorbed));
            lastDamageTick = Find.TickManager.TicksGame;

            if (CurrentBarrier <= 0.001f)
            {
                CurrentBarrier = 0f;
                brokenTick = Find.TickManager.TicksGame;
                isActive = false;
                SRALib_DefOf.EnergyShield_Broken.PlayOneShot(new TargetInfo(Pawn.Position, Pawn.Map, false));
                if (Props.RemoveWhenDestroy)
                {
                    parent.Severity = 0f;
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
                Hediff catatonicBreakdown = pawn.health.hediffSet.GetFirstHediffOfDef(HediffDefOf.CatatonicBreakdown, false);
                if (catatonicBreakdown != null)
                {
                    pawn.health.RemoveHediff(catatonicBreakdown);
                }
                pawn.stances?.stunner?.StopStun();
                MentalState mentalState = pawn.MentalState;
                if (mentalState != null)
                {
                    mentalState.RecoverFromState();
                }
            }
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
