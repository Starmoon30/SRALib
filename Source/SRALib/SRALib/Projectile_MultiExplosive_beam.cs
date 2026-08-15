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
        // 落点额外判定半径；0 时不额外扫描落点格，保留原版 Beam 只命中一个目标的行为。
        public float hitRadius = 0f;

        // 目标过滤策略；语义与 Verb_SRAShootBeam.targetignore 相同。
        public SRABeamTargetIgnore targetignore = SRABeamTargetIgnore.ignoreNothing;

        // 是否让发射源到落点之间的路径单位也受到光束本体伤害。
        public bool damageBeamPath = false;

        // 路径伤害粗细；0 表示只处理中心线格。
        public float pathHitRadius = 0f;

        // 路径伤害倍率；落点额外判定半径内的伤害不受此倍率影响。
        public float pathDamageFactor = 1f;

        // 光束本体的落点/路径伤害是否忽略 LOS 和墙体截断。
        public bool penetrateObstacles = false;

        // 是否在 hitRadius 和 pathDamage 产生的额外判定格上也执行 MultiExplosive_Beams；默认只在主落点爆炸。
        public bool applyExplosionToExtraHitCells = false;

        public List<MultiExplosive_BeamProperties> MultiExplosive_Beams = new List<MultiExplosive_BeamProperties>();
    }
    public class Projectile_MultiExplosive_Beam : Beam
    {
        private const int MiningYieldApplications = 2;

        private IntVec3 center = IntVec3.Invalid;
        private readonly HashSet<Thing> damagedThingsThisImpact = new HashSet<Thing>();
        private readonly HashSet<IntVec3> extraHitCellsThisImpact = new HashSet<IntVec3>();
        private readonly HashSet<IntVec3> hitRadiusCellsThisImpact = new HashSet<IntVec3>();
        private readonly HashSet<IntVec3> pathHitCellsThisImpact = new HashSet<IntVec3>();

        public override void Launch(Thing launcher, Vector3 origin, LocalTargetInfo usedTarget, LocalTargetInfo intendedTarget, ProjectileHitFlags hitFlags, bool preventFriendlyFire = false, Thing equipment = null, ThingDef targetCoverDef = null)
        {
            if (usedTarget.IsValid)
            {
                center = usedTarget.Cell;
            }
            base.Launch(launcher, origin, usedTarget, intendedTarget, hitFlags, preventFriendlyFire, equipment, targetCoverDef);
        }
        protected override void Impact(Thing hitThing, bool blockedByShield = false)
        {
            MultiExplosive_BeamExtension extension = def.GetModExtension<MultiExplosive_BeamExtension>();
            Thing baseHitThing = hitThing;
            if (extension != null && baseHitThing != null && IsIgnoredByTargetIgnore(baseHitThing, extension.targetignore))
            {
                baseHitThing = null;
            }

            if (center.IsValid && extension != null && Map != null)
            {
                damagedThingsThisImpact.Clear();
                extraHitCellsThisImpact.Clear();
                hitRadiusCellsThisImpact.Clear();
                pathHitCellsThisImpact.Clear();
                if (baseHitThing != null)
                {
                    damagedThingsThisImpact.Add(baseHitThing);
                }

                AddExtraHitCells(extension);
                if (!extension.MultiExplosive_Beams.NullOrEmpty())
                {
                    for (int i = 0; i < extension.MultiExplosive_Beams.Count; i++)
                    {
                        ExecuteExplosion(extension.MultiExplosive_Beams[i], center);
                    }

                    if (extension.applyExplosionToExtraHitCells)
                    {
                        ApplyExplosionsToExtraHitCells(extension);
                    }
                }

                ApplyBeamDamageToExtraHitCells(extension);
                damagedThingsThisImpact.Clear();
                extraHitCellsThisImpact.Clear();
                hitRadiusCellsThisImpact.Clear();
                pathHitCellsThisImpact.Clear();
            }

            base.Impact(baseHitThing, blockedByShield);
        }

        private void AddExtraHitCells(MultiExplosive_BeamExtension extension)
        {
            if (extension.hitRadius > 0f)
            {
                foreach (IntVec3 cell in CellsInRadius(center, extension.hitRadius))
                {
                    hitRadiusCellsThisImpact.Add(cell);
                    extraHitCellsThisImpact.Add(cell);
                }
            }

            if (!extension.damageBeamPath)
            {
                return;
            }

            IntVec3 sourceCell = origin.Yto0().ToIntVec3();
            foreach (IntVec3 cell in GetBeamPathCells(sourceCell, center, extension.pathHitRadius, extension.penetrateObstacles))
            {
                pathHitCellsThisImpact.Add(cell);
                extraHitCellsThisImpact.Add(cell);
            }
        }

        private void ApplyExplosionsToExtraHitCells(MultiExplosive_BeamExtension extension)
        {
            if (extension.MultiExplosive_Beams.NullOrEmpty())
            {
                return;
            }

            foreach (IntVec3 cell in extraHitCellsThisImpact)
            {
                if (!cell.InBounds(Map) || cell == center)
                {
                    continue;
                }

                for (int i = 0; i < extension.MultiExplosive_Beams.Count; i++)
                {
                    ExecuteExplosion(extension.MultiExplosive_Beams[i], cell);
                }
            }
        }

        private void ApplyBeamDamageToExtraHitCells(MultiExplosive_BeamExtension extension)
        {
            if (DamageDef == null || extraHitCellsThisImpact.Count == 0)
            {
                return;
            }

            IntVec3 sourceCell = origin.Yto0().ToIntVec3();
            foreach (IntVec3 cell in extraHitCellsThisImpact)
            {
                float damageFactor = !hitRadiusCellsThisImpact.Contains(cell) && pathHitCellsThisImpact.Contains(cell) ? extension.pathDamageFactor : 1f;
                HitCellWithBeamDamage(cell, sourceCell, damageFactor, extension);
            }
        }

        private void HitCellWithBeamDamage(IntVec3 cell, IntVec3 sourceCell, float damageFactor, MultiExplosive_BeamExtension extension)
        {
            if (!cell.InBounds(Map))
            {
                return;
            }

            List<Thing> things = cell.GetThingList(Map);
            for (int i = things.Count - 1; i >= 0; i--)
            {
                Thing thing = things[i];
                if (CanDamageThingWithBeam(thing, extension) && damagedThingsThisImpact.Add(thing))
                {
                    ApplyBeamDamage(thing, sourceCell, damageFactor);
                }
            }
        }

        private bool CanDamageThingWithBeam(Thing thing, MultiExplosive_BeamExtension extension)
        {
            if (thing == null || !thing.Spawned || thing == this || thing == launcher)
            {
                return false;
            }

            if (thing.Map != Map)
            {
                return false;
            }

            if (!thing.def.useHitPoints && !(thing is Pawn))
            {
                return false;
            }

            if (thing is Pawn && thing != intendedTarget.Thing && thing != usedTarget.Thing && (HitFlags & ProjectileHitFlags.NonTargetPawns) == 0)
            {
                return false;
            }

            if (IsIgnoredByTargetIgnore(thing, extension.targetignore))
            {
                return false;
            }

            return extension.penetrateObstacles || !CoverUtility.ThingCovered(thing, Map);
        }

        private bool IsIgnoredByTargetIgnore(Thing thing, SRABeamTargetIgnore targetIgnore)
        {
            switch (targetIgnore)
            {
                case SRABeamTargetIgnore.ignoreNonHostile:
                    return !GenHostility.HostileTo(thing, launcher);
                case SRABeamTargetIgnore.ignoreNonLOSBlockingNonHostile:
                    return IsFriendlyToLauncher(thing) || (!GenHostility.HostileTo(thing, launcher) && !BlocksLineOfSight(thing));
                case SRABeamTargetIgnore.ignoreFriendly:
                    return IsFriendlyToLauncher(thing);
                case SRABeamTargetIgnore.ignoreNothing:
                default:
                    return false;
            }
        }

        private bool IsFriendlyToLauncher(Thing thing)
        {
            Faction launcherFaction = launcher?.Faction;
            Faction thingFaction = thing?.Faction;
            if (launcherFaction == null || thingFaction == null)
            {
                return false;
            }

            return thingFaction == launcherFaction || thingFaction.RelationKindWith(launcherFaction) == FactionRelationKind.Ally;
        }

        private static bool BlocksLineOfSight(Thing thing)
        {
            if (thing is Building building)
            {
                return !building.CanBeSeenOver();
            }

            return thing?.def.Fillage == FillCategory.Full;
        }

        private IEnumerable<IntVec3> GetBeamPathCells(IntVec3 source, IntVec3 target, float radius, bool penetrateObstacles)
        {
            bool hasEnteredMap = false;
            HashSet<IntVec3> yieldedCells = new HashSet<IntVec3>();
            foreach (IntVec3 lineCell in CellsOnLine(source, target))
            {
                if (lineCell == source)
                {
                    continue;
                }

                if (!lineCell.InBounds(Map))
                {
                    if (hasEnteredMap)
                    {
                        yield break;
                    }

                    continue;
                }

                hasEnteredMap = true;
                if (!penetrateObstacles && !lineCell.CanBeSeenOverFast(Map))
                {
                    yield break;
                }

                if (radius > 0f)
                {
                    foreach (IntVec3 cell in CellsInRadius(lineCell, radius))
                    {
                        if (yieldedCells.Add(cell))
                        {
                            yield return cell;
                        }
                    }
                }
                else if (yieldedCells.Add(lineCell))
                {
                    yield return lineCell;
                }
            }
        }

        private IEnumerable<IntVec3> CellsOnLine(IntVec3 source, IntVec3 target)
        {
            ShootLine line = new ShootLine(source, target);
            foreach (IntVec3 cell in line.Points())
            {
                yield return cell;
            }

            yield return target;
        }

        private IEnumerable<IntVec3> CellsInRadius(IntVec3 cell, float radius)
        {
            if (radius <= 0f)
            {
                if (cell.InBounds(Map))
                {
                    yield return cell;
                }

                yield break;
            }

            foreach (IntVec3 radialCell in GenRadial.RadialCellsAround(cell, radius, true))
            {
                if (radialCell.InBounds(Map))
                {
                    yield return radialCell;
                }
            }
        }

        private void ApplyBeamDamage(Thing thing, IntVec3 sourceCell, float damageFactor)
        {
            float amount = DamageAmount * Mathf.Max(0f, damageFactor);
            if (thing == null || DamageDef == null || amount <= 0f)
            {
                return;
            }

            BattleLogEntry_RangedImpact log = new BattleLogEntry_RangedImpact(launcher, thing, intendedTarget.Thing, equipmentDef, def, targetCoverDef);
            bool instigatorGuilty = !(launcher is Pawn pawn) || !pawn.Drafted;
            float angle = (thing.Position - sourceCell).AngleFlat;
            DamageInfo dinfo = new DamageInfo(DamageDef, amount, ArmorPenetration, angle, launcher, null, equipmentDef, DamageInfo.SourceCategory.ThingOrUnknown, intendedTarget.Thing, instigatorGuilty, true, QualityCategory.Normal, true, false);
            dinfo.SetWeaponQuality(equipmentQuality);
            thing.TakeDamage(dinfo).AssociateWithLog(log);

            if (thing is Pawn hitPawn)
            {
                hitPawn.stances?.stagger.Notify_BulletImpact(this);
            }

            ApplyBeamExtraDamages(thing, log, damageFactor, instigatorGuilty, angle);
        }

        private void ApplyBeamExtraDamages(Thing thing, BattleLogEntry_RangedImpact log, float damageFactor, bool instigatorGuilty, float angle)
        {
            ApplyBeamExtraDamageList(thing, log, extraDamages, damageFactor, instigatorGuilty, angle);
            ApplyBeamExtraDamageList(thing, log, def.projectile.extraDamages, damageFactor, instigatorGuilty, angle);
        }

        private void ApplyBeamExtraDamageList(Thing thing, BattleLogEntry_RangedImpact log, List<ExtraDamage> extraDamageList, float damageFactor, bool instigatorGuilty, float angle)
        {
            if (extraDamageList.NullOrEmpty())
            {
                return;
            }

            for (int i = 0; i < extraDamageList.Count; i++)
            {
                ExtraDamage extraDamage = extraDamageList[i];
                float amount = extraDamage.amount * Mathf.Max(0f, damageFactor);
                if (amount <= 0f || !Rand.Chance(extraDamage.chance))
                {
                    continue;
                }

                DamageInfo dinfo = new DamageInfo(extraDamage.def, amount, extraDamage.AdjustedArmorPenetration(), angle, launcher, null, equipmentDef, DamageInfo.SourceCategory.ThingOrUnknown, intendedTarget.Thing, instigatorGuilty, true, QualityCategory.Normal, true, false);
                dinfo.SetWeaponQuality(equipmentQuality);
                thing.TakeDamage(dinfo).AssociateWithLog(log);
            }
        }

        private void ExecuteExplosion(MultiExplosive_BeamProperties properties, IntVec3 center)
        {
            if (properties == null || Map == null)
            {
                return;
            }

            if (properties.explosionEffect != null)
            {
                Effecter effecter = properties.explosionEffect.Spawn().Trigger(new TargetInfo(center, Map, false), this.launcher, -1);
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
            DoExplosion(properties, center, thingsIgnoredByExplosion, affectedCellsOverride);
        }

        private void DoExplosion(MultiExplosive_BeamProperties properties, IntVec3 center, List<Thing> ignoredThings, List<IntVec3> affectedCellsOverride)
        {
            if (properties.HasExplosionProcessing)
            {
                ExplosionWithProcessingUtility.DoExplosion(
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
                    ignoredThings: ignoredThings,
                    overrideCells: affectedCellsOverride,
                    preExplosionSpawnSingleThingDef: properties.preExplosionSpawnSingleThingDef,
                    postExplosionSpawnSingleThingDef: properties.postExplosionSpawnSingleThingDef,
                    preNotifyEffects: properties.preNotifyEffects,
                    postNotifyEffects: properties.postNotifyEffects);
                return;
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
                ignoredThings: ignoredThings,
                overrideCells: affectedCellsOverride,
                preExplosionSpawnSingleThingDef: properties.preExplosionSpawnSingleThingDef,
                postExplosionSpawnSingleThingDef: properties.postExplosionSpawnSingleThingDef);
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
