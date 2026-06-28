using RimWorld;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using Verse.Noise;

namespace SRA
{

    // 爆炸属性定义类
    public class MultiExplosive_BeamProperties : MultiExplosionProperties
    {
    }

    public class MultiExplosive_BeamExtension : DefModExtension
    {
        public List<MultiExplosive_BeamProperties> MultiExplosive_Beams = new List<MultiExplosive_BeamProperties>();
    }
    public class Projectile_MultiExplosive_Beam : Beam
    {
        private const int MiningYieldApplications = 2;

        private IntVec3 center = IntVec3.Invalid;

        public override void Launch(Thing launcher, Vector3 origin, LocalTargetInfo usedTarget, LocalTargetInfo intendedTarget, ProjectileHitFlags hitFlags, bool preventFriendlyFire = false, Thing equipment = null, ThingDef targetCoverDef = null)
        {
            if (this.usedTarget != null)
            {
                center = usedTarget.ToTargetInfo(base.Map).Cell;
            }
            base.Launch(launcher, origin, usedTarget, intendedTarget, hitFlags, preventFriendlyFire, equipment, targetCoverDef);
        }
        protected override void Impact(Thing hitThing, bool blockedByShield = false)
        {
            var extension = this.def.GetModExtension<MultiExplosive_BeamExtension>();
            if (center != IntVec3.Invalid && extension != null && extension.MultiExplosive_Beams != null && extension.MultiExplosive_Beams.Count > 0)
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
                Effecter effecter = properties.explosionEffect.Spawn().Trigger(new TargetInfo(center, launcher.Map, false), this.launcher, -1);
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
                            AddIgnoredThing(thingsIgnoredByExplosion, thing);
                        }
                    }
                }
            }
            List<IntVec3> affectedCellsOverride = null;
            if (properties.penetrateObstacles)
            {
                affectedCellsOverride = GetPenetratingExplosionCells(center, Map, properties.radius);
            }
            if (properties.mining)
            {
                List<IntVec3> miningCells = affectedCellsOverride ?? GetStandardExplosionCells(properties, center);
                ApplyMiningExplosionToMineables(properties, miningCells, center, thingsIgnoredByExplosion);
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
                intendedTarget: intendedTarget.Thing,
                preExplosionSpawnThingDef: properties.preExplosionSpawnThingDef,
                preExplosionSpawnChance: properties.preExplosionSpawnChance,
                preExplosionSpawnThingCount: properties.preExplosionSpawnThingCount,
                postExplosionSpawnThingDef: properties.postExplosionSpawnThingDef,
                postExplosionSpawnChance: properties.postExplosionSpawnChance,
                postExplosionSpawnThingCount: properties.postExplosionSpawnThingCount,
                postExplosionGasType: properties.postExplosionGasType,
                postExplosionGasRadiusOverride: properties.postExplosionGasRadiusOverride,
                postExplosionGasAmount: properties.postExplosionGasAmount,
                ignoredThings: thingsIgnoredByExplosion,
                overrideCells: affectedCellsOverride,
                preExplosionSpawnSingleThingDef: properties.preExplosionSpawnSingleThingDef,
                postExplosionSpawnSingleThingDef: properties.postExplosionSpawnSingleThingDef
            );
        }

        private static List<IntVec3> GetPenetratingExplosionCells(IntVec3 center, Map map, float radius)
        {
            List<IntVec3> cells = new List<IntVec3>();
            foreach (IntVec3 cell in GenRadial.RadialCellsAround(center, radius, true))
            {
                if (cell.InBounds(map))
                {
                    cells.Add(cell);
                }
            }

            return cells;
        }

        private List<IntVec3> GetStandardExplosionCells(MultiExplosionProperties properties, IntVec3 center)
        {
            List<IntVec3> cells = new List<IntVec3>();
            if (properties.damageDef == null)
            {
                return cells;
            }

            foreach (IntVec3 cell in properties.damageDef.Worker.ExplosionCellsToHit(center, Map, properties.radius))
            {
                cells.Add(cell);
            }

            return cells;
        }

        private void ApplyMiningExplosionToMineables(MultiExplosionProperties properties, List<IntVec3> cells, IntVec3 explosionCenter, List<Thing> ignoredThings)
        {
            if (cells == null || cells.Count == 0 || Map == null)
            {
                return;
            }

            HashSet<Thing> processedMineables = new HashSet<Thing>();
            for (int i = 0; i < cells.Count; i++)
            {
                IntVec3 cell = cells[i];
                if (!cell.InBounds(Map))
                {
                    continue;
                }

                List<Thing> thingList = Map.thingGrid.ThingsListAt(cell);
                for (int j = thingList.Count - 1; j >= 0; j--)
                {
                    Mineable mineable = thingList[j] as Mineable;
                    if (mineable == null || mineable.Destroyed || !processedMineables.Add(mineable))
                    {
                        continue;
                    }

                    AddIgnoredThing(ignoredThings, mineable);
                    ApplyMiningExplosionToMineable(properties, mineable, cell, explosionCenter, ignoredThings);
                }
            }
        }

        private void ApplyMiningExplosionToMineable(MultiExplosionProperties properties, Mineable mineable, IntVec3 cell, IntVec3 explosionCenter, List<Thing> ignoredThings)
        {
            if (!mineable.def.useHitPoints || properties.damageDef != null && !properties.damageDef.harmsHealth)
            {
                return;
            }

            float adjustedDamage = GetAdjustedBuildingDamage(properties.damageDef, GetExplosionDamageAmountAt(properties, cell, explosionCenter), mineable);
            int damage = Mathf.Min(mineable.HitPoints, GenMath.RoundRandom(adjustedDamage));
            if (damage <= 0)
            {
                return;
            }

            if (damage >= mineable.HitPoints)
            {
                DestroyMineableFromMiningDamage(mineable, damage, ignoredThings);
                return;
            }

            ApplyMiningYield(mineable, damage);
            mineable.HitPoints -= damage;
        }

        private void ApplyMiningYield(Mineable mineable, int damage)
        {
            Pawn miner = launcher as Pawn;
            for (int i = 0; i < MiningYieldApplications; i++)
            {
                mineable.Notify_TookMiningDamage(damage, miner);
            }
        }

        private static int GetExplosionDamageAmountAt(MultiExplosionProperties properties, IntVec3 cell, IntVec3 center)
        {
            int damageAmount = ResolveDamageAmount(properties);
            if (!properties.explosionDamageFalloff || properties.radius <= 0f)
            {
                return damageAmount;
            }

            float t = cell.DistanceTo(center) / properties.radius;
            return Mathf.Max(GenMath.RoundRandom(Mathf.Lerp(damageAmount, damageAmount * 0.2f, t)), 1);
        }

        private static int ResolveDamageAmount(MultiExplosionProperties properties)
        {
            if (properties.damageAmount >= 0)
            {
                return properties.damageAmount;
            }

            if (properties.damageDef != null && properties.damageDef.defaultDamage >= 0)
            {
                return properties.damageDef.defaultDamage;
            }

            return 1;
        }

        private float GetAdjustedBuildingDamage(DamageDef damageDef, float damageAmount, Building building)
        {
            if (damageDef == null)
            {
                return damageAmount;
            }

            float adjustedDamage = damageAmount * damageDef.buildingDamageFactor;
            adjustedDamage *= building.def.passability == Traversability.Impassable ? damageDef.buildingDamageFactorImpassable : damageDef.buildingDamageFactorPassable;
            if (damageDef.scaleDamageToBuildingsBasedOnFlammability)
            {
                adjustedDamage *= Mathf.Max(0.05f, building.GetStatValue(StatDefOf.Flammability, true, -1));
            }

            Pawn pawn = launcher as Pawn;
            if (pawn != null && pawn.IsShambler)
            {
                adjustedDamage *= 1.5f;
            }

            return adjustedDamage;
        }

        private void DestroyMineableFromMiningDamage(Mineable mineable, int damage, List<Thing> ignoredThings)
        {
            if (mineable.Destroyed)
            {
                return;
            }

            Map map = mineable.Map;
            IntVec3 position = mineable.Position;
            ThingDef yieldThingDef = mineable.def.building?.mineableThing;
            Dictionary<Thing, int> yieldStacksBefore = GetNearbyThingStacks(map, position, yieldThingDef);
            ApplyMiningYield(mineable, damage);
            mineable.DestroyMined(launcher as Pawn);
            AddNewOrChangedYieldThingsToIgnored(map, position, yieldThingDef, yieldStacksBefore, ignoredThings);
        }

        private static void AddIgnoredThing(List<Thing> ignoredThings, Thing thing)
        {
            if (thing != null && !ignoredThings.Contains(thing))
            {
                ignoredThings.Add(thing);
            }
        }

        private static Dictionary<Thing, int> GetNearbyThingStacks(Map map, IntVec3 center, ThingDef thingDef)
        {
            Dictionary<Thing, int> stacks = new Dictionary<Thing, int>();
            if (map == null || thingDef == null)
            {
                return stacks;
            }

            foreach (IntVec3 cell in GenRadial.RadialCellsAround(center, 5f, true))
            {
                if (!cell.InBounds(map))
                {
                    continue;
                }

                List<Thing> thingList = map.thingGrid.ThingsListAt(cell);
                for (int i = 0; i < thingList.Count; i++)
                {
                    Thing thing = thingList[i];
                    if (thing.def == thingDef && !stacks.ContainsKey(thing))
                    {
                        stacks.Add(thing, thing.stackCount);
                    }
                }
            }

            return stacks;
        }

        private static void AddNewOrChangedYieldThingsToIgnored(Map map, IntVec3 center, ThingDef thingDef, Dictionary<Thing, int> stacksBefore, List<Thing> ignoredThings)
        {
            if (map == null || thingDef == null)
            {
                return;
            }

            foreach (IntVec3 cell in GenRadial.RadialCellsAround(center, 5f, true))
            {
                if (!cell.InBounds(map))
                {
                    continue;
                }

                List<Thing> thingList = map.thingGrid.ThingsListAt(cell);
                for (int i = 0; i < thingList.Count; i++)
                {
                    Thing thing = thingList[i];
                    if (thing.def != thingDef)
                    {
                        continue;
                    }

                    int stackCountBefore;
                    if (stacksBefore == null || !stacksBefore.TryGetValue(thing, out stackCountBefore) || thing.stackCount > stackCountBefore)
                    {
                        AddIgnoredThing(ignoredThings, thing);
                    }
                }
            }
        }
    }
}
