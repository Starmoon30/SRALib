using RimWorld;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using Verse.Noise;

namespace SRA
{

    // 爆炸属性定义类
    public class MultiExplosive_BeamProperties
    {
        public float radius;
        public DamageDef damageDef;
        public int damageAmount = 1;
        public float armorPenetration = 1f;
        public SoundDef explosionSound;
        public bool explosionDamageFalloff = true;
        public EffecterDef explosionEffect;
        public int explosionEffectLifetimeTicks;
        public bool onlyAntiHostile = false;
    }

    public class MultiExplosive_BeamExtension : DefModExtension
    {
        public List<MultiExplosive_BeamProperties> MultiExplosive_Beams = new List<MultiExplosive_BeamProperties>();
    }
    public class Projectile_MultiExplosive_Beam : Beam
    {
        private IntVec3 center;

        public override void Launch(Thing launcher, Vector3 origin, LocalTargetInfo usedTarget, LocalTargetInfo intendedTarget, ProjectileHitFlags hitFlags, bool preventFriendlyFire = false, Thing equipment = null, ThingDef targetCoverDef = null)
        {
            center = usedTarget.ToTargetInfo(base.Map).Cell;
            base.Launch(launcher, origin, usedTarget, intendedTarget, hitFlags, preventFriendlyFire, equipment, targetCoverDef);
        }
        protected override void Impact(Thing hitThing, bool blockedByShield = false)
        {
            var extension = this.def.GetModExtension<MultiExplosive_BeamExtension>();
            if (extension != null && extension.MultiExplosive_Beams != null && extension.MultiExplosive_Beams.Count > 0)
            {
                foreach (var explosion in extension.MultiExplosive_Beams)
                {
                    ExecuteExplosion(explosion, center);
                }
            }
            base.Impact(hitThing, blockedByShield);
        }

        private void ExecuteExplosion(MultiExplosive_BeamProperties properties, IntVec3 center)
        {

            if (properties.explosionEffect != null)
            {
                Effecter effecter = properties.explosionEffect.Spawn().Trigger(new TargetInfo(Position, launcher.Map, false), this.launcher, -1);
                if (properties.explosionEffectLifetimeTicks != 0)
                {
                    Map.effecterMaintainer.AddEffecterToMaintain(effecter, center.ToVector3().ToIntVec3(), properties.explosionEffectLifetimeTicks);
                }
                else
                {
                    effecter.Trigger(new TargetInfo(center, Map, false), new TargetInfo(center, Map, false), -1);
                    effecter.Cleanup();
                }
            }
            List<Thing> thingsIgnoredByExplosion = new List<Thing>();
            if (properties.onlyAntiHostile)
            {
                foreach (IntVec3 cell in GenRadial.RadialCellsAround(center, properties.radius, true))
                {
                    if (!cell.InBounds(Map)) continue;
                    foreach (Thing thing in Map.thingGrid.ThingsListAt(cell))
                    {
                        // 敌我识别
                        if (!GenHostility.HostileTo(thing, launcher))
                        {
                            thingsIgnoredByExplosion.Add(thing);
                        }
                    }
                }
            }
            GenExplosion.DoExplosion(
                center: center,
                map: Map, 
                radius: properties.radius,
                damType: properties.damageDef,
                instigator: launcher,
                damAmount: properties.damageAmount,
                armorPenetration: properties.armorPenetration,
                explosionSound: properties.explosionSound,
                weapon: equipmentDef,
                damageFalloff: properties.explosionDamageFalloff,
                ignoredThings: thingsIgnoredByExplosion
            );
        }
    }
}