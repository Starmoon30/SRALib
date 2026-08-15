using RimWorld;
using UnityEngine;
using Verse;

namespace SRA
{
    public class Projectile_BulletWithEffect_Extension : DefModExtension
    {
        public EffecterDef impactEffecter;

        // If true, non-target non-hostile pawns do not intercept or receive this bullet.
        public bool skipNonTargetFriendlyPawns = false;

        // If true, non-target non-hostile buildings, including walls, do not block this bullet.
        public bool skipFriendlyBuildings = false;
    }

    public class Projectile_BulletWithEffect : Bullet
    {
        public Projectile_BulletWithEffect_Extension Props => def.GetModExtension<Projectile_BulletWithEffect_Extension>();

        public override void Launch(Thing launcher, Vector3 origin, LocalTargetInfo usedTarget, LocalTargetInfo intendedTarget, ProjectileHitFlags hitFlags, bool preventFriendlyFire = false, Thing equipment = null, ThingDef targetCoverDef = null)
        {
            // Vanilla preventFriendlyFire only affects non-hostile pawns during free interception.
            bool shouldPreventFriendlyFire = preventFriendlyFire || (Props?.skipNonTargetFriendlyPawns ?? false);
            base.Launch(launcher, origin, usedTarget, intendedTarget, hitFlags, shouldPreventFriendlyFire, equipment, targetCoverDef);
        }

        protected override void Impact(Thing hitThing, bool blockedByShield = false)
        {
            if (ShouldSkipHitThing(hitThing))
            {
                if (!HasReachedDestination)
                {
                    return;
                }

                ImpactWithEffect(null, blockedByShield);
                return;
            }

            ImpactWithEffect(hitThing, blockedByShield);
        }

        private bool HasReachedDestination => ticksToImpact <= 0 || Position == DestinationCell;

        private bool ShouldSkipHitThing(Thing hitThing)
        {
            if (hitThing == null || launcher == null || hitThing == intendedTarget.Thing || hitThing == usedTarget.Thing)
            {
                return false;
            }

            if ((Props?.skipNonTargetFriendlyPawns ?? false) && hitThing is Pawn pawn)
            {
                return IsNonHostileToLauncher(pawn);
            }

            if ((Props?.skipFriendlyBuildings ?? false) && hitThing is Building)
            {
                return IsNonHostileToLauncher(hitThing);
            }

            return false;
        }

        private bool IsNonHostileToLauncher(Thing thing)
        {
            if (thing == null || launcher == null)
            {
                return false;
            }

            if (!launcher.Destroyed)
            {
                return !GenHostility.HostileTo(thing, launcher);
            }

            return launcher.Faction != null && !GenHostility.HostileTo(thing, launcher.Faction);
        }

        private void ImpactWithEffect(Thing hitThing, bool blockedByShield)
        {
            Map map = Map;
            IntVec3 impactCell = ExactPosition.ToIntVec3();
            Thing effectTarget = launcher;

            base.Impact(hitThing, blockedByShield);

            if (Props?.impactEffecter != null && effectTarget != null && map != null)
            {
                Props.impactEffecter.Spawn().Trigger(new TargetInfo(impactCell, map, false), effectTarget, -1);
            }
        }
    }
}
