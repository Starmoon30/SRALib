using RimWorld;
using RimWorld.BaseGen;
using System;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;
using System.Threading;
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
        // 添加公共访问器
        public float maxBarrier = 100f;
        public float DamageTakenMult = 1f;
        public float DamageTakenMax = 0f;
        public float DamageTakenReduce = 0f;
        public float regenRate = 5f;
        public float regenDelay = 3f;
        public float rechargeCooldown = 10f;
        public bool RemoveWhenDestroy = false;
        public bool BlockStunAndMentalState = false;
        public int priority = 0; // 优先级，越大的越先承伤

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
                SRABarrierCache.MarkDirty(Pawn);
        }

        public float GetCooldownSeconds()
        {
            return Props.rechargeCooldown - 
                (Find.TickManager.TicksGame - brokenTick) / (float)GenTicks.TicksPerRealSecond;
        }

        public override void CompPostTick(ref float severityAdjustment)
        {
            if (!Pawn.Spawned || Pawn.Dead) return;

            if (Pawn.IsHashIntervalTick(GenTicks.TicksPerRealSecond))
            {

                if (isActive == false)
                {
                    // 冷却结束后重新激活屏障
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
            if (!CanAbsorb) return;
            float damageToAbsorb = dinfo.Amount;
            var ext = dinfo.Def.GetModExtension<Damage_SRABarrier_factor_Extension>();
            if (ext != null && ext.damage_SRABarrier_factor >= 0)
            {
                damageToAbsorb *= ext.damage_SRABarrier_factor;
            }
            else if (!dinfo.Def.harmsHealth)
            {
                dinfo.SetAmount(0);
                return;
            }
            if (Props.DamageTakenMax > 0)
            {
                damageToAbsorb = (Mathf.Min(damageToAbsorb, Props.DamageTakenMax));
            }
            if (Props.DamageTakenReduce > 0)
            {
                damageToAbsorb -= Props.DamageTakenReduce;
            }
            float absorbed;
            float IncomingDamageFactor = Math.Min(Pawn.GetStatValue(StatDefOf.IncomingDamageFactor, true, -1), 1f);
            if (damageToAbsorb <= 0 || Props.DamageTakenMult <= 0 || IncomingDamageFactor <= 0)
            {
                dinfo.SetAmount(0);
                return;
            }
            else
            {
                absorbed = Mathf.Min(CurrentBarrier / Props.DamageTakenMult / IncomingDamageFactor, damageToAbsorb);

                CurrentBarrier -= absorbed * Props.DamageTakenMult * IncomingDamageFactor;
            }
            
            dinfo.SetAmount(Mathf.Min(dinfo.Amount, damageToAbsorb - absorbed));
            lastDamageTick = Find.TickManager.TicksGame;
            
            if (CurrentBarrier <= 0.001f)
            {
                CurrentBarrier = 0;
                brokenTick = Find.TickManager.TicksGame;
                isActive = false;
                SRALib_DefOf.EnergyShield_Broken.PlayOneShot(new TargetInfo(Pawn.Position, Pawn.Map, false));
                if (Props.RemoveWhenDestroy)
                {
                    parent.Severity = 0;
                }
            }
        }
        public void RemoveStunAndMentalState()
        {
            Pawn pawn = base.Pawn;
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
                return "SRA_BarrierTipExtra".Translate(
                Props.maxBarrier.ToString(),
                Props.regenRate.ToString(),
                Props.regenDelay.ToString(),
                Props.rechargeCooldown.ToString(),
                Props.DamageTakenMult.ToString(),
                Props.DamageTakenMax.ToString(),
                Props.DamageTakenReduce.ToString()
                );
            }
        }
        public bool AlwaysShowGizmo => true;
        public override IEnumerable<Gizmo> CompGetGizmos()
        {
            yield return new SRABarrierGizmo(this);
        }
    }
    /// <summary>
    /// 运行时缓存：Pawn -> 该 Pawn 上所有 HediffComp_SRABarrier
    /// 不存档，随时可重建（读档安全）
    /// </summary>
    public static class SRABarrierCache
    {
        private sealed class Entry
        {
            public bool dirty = true;
            public readonly List<HediffComp_SRABarrier> sorted = new(4);
        }

        private static readonly Dictionary<int, Entry> map = new(256);

        public static List<HediffComp_SRABarrier> GetSorted(Pawn pawn)
        {
            if (pawn == null) return null;
            int key = pawn.thingIDNumber;

            if (!map.TryGetValue(key, out var e))
            {
                e = new Entry();
                map[key] = e;
            }

            if (e.dirty) Rebuild(pawn, e);
            return e.sorted;
        }

        public static void MarkDirty(Pawn pawn)
        {
            if (pawn == null) return;
            int key = pawn.thingIDNumber;

            if (!map.TryGetValue(key, out var e))
            {
                e = new Entry();
                map[key] = e;
            }
            e.dirty = true;
        }

        public static void RemovePawn(Pawn pawn)
        {
            if (pawn == null) return;
            map.Remove(pawn.thingIDNumber);
        }

        private static void Rebuild(Pawn pawn, Entry e)
        {
            e.dirty = false;
            e.sorted.Clear();

            var hediffs = pawn?.health?.hediffSet?.hediffs;
            if (hediffs == null) return;

            for (int i = 0; i < hediffs.Count; i++)
            {
                var b = hediffs[i].TryGetComp<HediffComp_SRABarrier>();
                if (b != null) e.sorted.Add(b);
            }

            // priority 越大越先
            e.sorted.Sort((a, b) => b.Props.priority.CompareTo(a.Props.priority));
        }
    }
}