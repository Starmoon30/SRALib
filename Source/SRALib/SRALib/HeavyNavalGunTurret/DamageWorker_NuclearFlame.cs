using System.Collections.Generic;
using RimWorld;
using Verse;

namespace SRA
{
    public class DamageWorker_NuclearFlame : DamageWorker_AddInjury
    {
        public override DamageResult Apply(DamageInfo dinfo, Thing victim)
        {
            Pawn pawn = victim as Pawn;
            if (pawn != null && pawn.Faction == Faction.OfPlayer)
            {
                Find.TickManager.slower.SignalForceNormalSpeedShort();
            }

            Map map = victim.Map;
            DamageResult damageResult = base.Apply(dinfo, victim);
            if (map == null)
            {
                return damageResult;
            }

            TryMakeFreshInjuriesPermanent(pawn, damageResult);
            TryAttachFire(dinfo, victim, damageResult);
            TryTurnDestroyedThingToAsh(victim, pawn, map);
            return damageResult;
        }

        public override void ExplosionAffectCell(Explosion explosion, IntVec3 c, List<Thing> damagedThings, List<Thing> ignoredThings, bool canThrowMotes)
        {
            base.ExplosionAffectCell(explosion, c, damagedThings, ignoredThings, canThrowMotes);
            if (Rand.Chance(FireUtility.ChanceToStartFireIn(c, explosion.Map, null)))
            {
                FireUtility.TryStartFireIn(c, explosion.Map, Rand.Range(0.1f, 0.1f), explosion.instigator, null);
            }
        }

        public override void ExplosionStart(Explosion explosion, List<IntVec3> cellsToAffect)
        {
            base.ExplosionStart(explosion, cellsToAffect);
            if (explosion.Map == null)
            {
                return;
            }

            EffecterDef effecterDef = DefDatabase<EffecterDef>.GetNamed("NuclearFlameWave", false);
            if (effecterDef == null)
            {
                return;
            }

            Effecter effecter = effecterDef.Spawn();
            effecter.Trigger(new TargetInfo(explosion.Position, explosion.Map), TargetInfo.Invalid);
            effecter.Cleanup();
        }

        private static void TryMakeFreshInjuriesPermanent(Pawn pawn, DamageResult damageResult)
        {
            if (pawn == null || damageResult.hediffs == null || !pawn.RaceProps.IsFlesh)
            {
                return;
            }

            bool madePermanent = false;
            for (int i = 0; i < damageResult.hediffs.Count; i++)
            {
                HediffComp_GetsPermanent permComp = damageResult.hediffs[i].TryGetComp<HediffComp_GetsPermanent>();
                if (permComp != null && !permComp.IsPermanent)
                {
                    permComp.IsPermanent = true;
                    madePermanent = true;
                }
            }

            if (madePermanent)
            {
                pawn.health.hediffSet.DirtyCache();
            }
        }

        private static void TryAttachFire(DamageInfo dinfo, Thing victim, DamageResult damageResult)
        {
            if (!damageResult.deflected && !dinfo.InstantPermanentInjury && Rand.Chance(FireUtility.ChanceToAttachFireFromEvent(victim)))
            {
                victim.TryAttachFire(Rand.Range(0.1f, 0.1f), dinfo.Instigator);
            }
        }

        private static void TryTurnDestroyedThingToAsh(Thing victim, Pawn pawn, Map map)
        {
            if (!victim.Destroyed || pawn != null)
            {
                return;
            }

            foreach (IntVec3 cell in victim.OccupiedRect())
            {
                FilthMaker.TryMakeFilth(cell, map, ThingDefOf.Filth_Ash, 1, FilthSourceFlags.None, true);
            }
        }
    }
}

namespace Verse
{
    public class DamageWorker_NuclearFlame : SRA.DamageWorker_NuclearFlame
    {
    }
}
