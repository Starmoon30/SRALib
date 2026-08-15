
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace SRA
{
    public class SRA_RailgunProjectileExtension : DefModExtension
    {
        // 非目标命中后允许继续飞行的次数。
        public int maxPenetrations = 1;
        // 两次穿透命中之间的最小间隔，避免同一格或同一批目标反复触发。
        public int penetrationDelayTicks = 15;

        // 每次有效命中时生成的爆炸半径。
        public float explosionRadius = 1.5f;
        // 爆炸伤害；<= 0 时使用 projectile 本体按武器解析后的伤害。
        public int explosionDamage = 0;
        // 爆炸穿甲；<= 0 时使用 projectile 本体按武器解析后的穿甲。
        public float explosionArmorPenetration = 0f;
        // 爆炸使用的伤害类型。
        public DamageDef damageDef = DamageDefOf.Bullet;
        // 爆炸音效。
        public SoundDef explosionSound = SRALib_DefOf.EnergyShield_Broken;
        // 爆炸前播放的额外 Effecter。
        public EffecterDef explosionEffect;
        // Effecter 维持时间；<= 0 时立即触发并清理。
        public int explosionEffectLifetimeTicks;

        // 爆炸半径内额外施加的 Hediff。
        public HediffDef explosionHediff;
        // Hediff 严重度；<= 0 时按 1 处理，避免生成无效 hediff。
        public float explosionHediffSeverity = 0f;
    }

    public class Projectile_SRA_Railgun : Projectile
    {
        private int penetrationsLeft;
        private int lastPenetrationTick;
        private Thing lastHitThing;
        private int resolvedExplosionDamage = -1;
        private float resolvedExplosionArmorPenetration = -1f;
        private SRA_RailgunProjectileExtension projectileExt;
        private readonly HashSet<Thing> tmpIgnoredThings = new HashSet<Thing>();
        private readonly HashSet<Thing> tmpExtraDamageTargetSet = new HashSet<Thing>();
        private readonly List<Thing> tmpExtraDamageTargets = new List<Thing>();

        private SRA_RailgunProjectileExtension ProjectileExt
        {
            get
            {
                if (projectileExt == null)
                {
                    projectileExt = def.GetModExtension<SRA_RailgunProjectileExtension>();
                }

                return projectileExt;
            }
        }

        protected override void Impact(Thing hitThing, bool blockedByShield = false)
        {
            SRA_RailgunProjectileExtension ext = ProjectileExt;
            if (ext == null)
            {
                base.Impact(hitThing, blockedByShield);
                return;
            }

            Map map = Map;
            if (map == null)
            {
                base.Impact(hitThing, blockedByShield);
                return;
            }

            if (ShouldIgnoreCollision(hitThing))
            {
                if (Position == DestinationCell)
                {
                    ProcessImpact(ext, null, map, Position);
                    base.Impact(null, blockedByShield);
                }

                return;
            }

            bool isIntendedTarget = IsIntendedTarget(hitThing);
            if (hitThing == null || hitThing != lastHitThing)
            {
                lastHitThing = hitThing;
                ProcessImpact(ext, hitThing, map, Position);
            }

            if (!isIntendedTarget && CanPenetrate(ext))
            {
                penetrationsLeft--;
                lastPenetrationTick = Find.TickManager.TicksGame;
                return;
            }

            base.Impact(hitThing, blockedByShield);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref penetrationsLeft, "penetrationsLeft", 0);
            Scribe_Values.Look(ref lastPenetrationTick, "lastPenetrationTick", 0);
            Scribe_References.Look(ref lastHitThing, "lastHitThing");
            Scribe_Values.Look(ref resolvedExplosionDamage, "resolvedExplosionDamage", -1);
            Scribe_Values.Look(ref resolvedExplosionArmorPenetration, "resolvedExplosionArmorPenetration", -1f);
        }

        public override void Launch(Thing launcher, Vector3 origin, LocalTargetInfo usedTarget, LocalTargetInfo intendedTarget, ProjectileHitFlags hitFlags, bool preventFriendlyFire = false, Thing equipment = null, ThingDef targetCoverDef = null)
        {
            base.Launch(
                launcher,
                origin,
                usedTarget,
                intendedTarget,
                hitFlags,
                preventFriendlyFire,
                equipment,
                targetCoverDef
            );

            projectileExt = def.GetModExtension<SRA_RailgunProjectileExtension>();
            if (projectileExt != null)
            {
                penetrationsLeft = projectileExt.maxPenetrations;
                lastPenetrationTick = -projectileExt.penetrationDelayTicks;
                resolvedExplosionDamage = ResolveExplosionDamage(projectileExt);
                resolvedExplosionArmorPenetration = ResolveExplosionArmorPenetration(projectileExt);
            }
        }

        private void ProcessImpact(SRA_RailgunProjectileExtension ext, Thing hitThing, Map map, IntVec3 position)
        {
            bool filterNonHostiles = !IsExplicitSameFactionBuildingTarget(hitThing);
            BattleLogEntry_RangedImpact battleLogEntry = new BattleLogEntry_RangedImpact(launcher, hitThing, intendedTarget.Thing, equipmentDef, def, targetCoverDef);
            Find.BattleLog.Add(battleLogEntry);
            bool instigatorGuilty = !(launcher is Pawn pawn) || !pawn.Drafted;

            SpawnExplosionEffect(ext, map, position);
            List<Thing> ignoredThings = CollectIgnoredThingsAndExtraDamageTargets(ext, hitThing, map, position, filterNonHostiles);
            ApplyExtraDamages(battleLogEntry, instigatorGuilty);

            if (ext.explosionHediff != null)
            {
                ApplyHediffToCollectedPawnTargets(ext, filterNonHostiles);
            }

            GenExplosion.DoExplosion(
                center: position,
                map: map,
                radius: ext.explosionRadius,
                damType: ext.damageDef,
                instigator: launcher,
                damAmount: ResolvedExplosionDamage(ext),
                armorPenetration: ResolvedExplosionArmorPenetration(ext),
                explosionSound: ext.explosionSound,
                weapon: equipmentDef,
                projectile: def,
                intendedTarget: intendedTarget.Thing,
                ignoredThings: ignoredThings);

            tmpIgnoredThings.Clear();
            tmpExtraDamageTargetSet.Clear();
            tmpExtraDamageTargets.Clear();
        }

        private void SpawnExplosionEffect(SRA_RailgunProjectileExtension ext, Map map, IntVec3 position)
        {
            if (ext.explosionEffect == null)
            {
                return;
            }

            TargetInfo target = new TargetInfo(position, map, false);
            Effecter effecter = ext.explosionEffect.Spawn().Trigger(target, target, -1);
            if (ext.explosionEffectLifetimeTicks > 0)
            {
                map.effecterMaintainer.AddEffecterToMaintain(effecter, position, ext.explosionEffectLifetimeTicks);
            }
            else
            {
                effecter.Trigger(target, target, -1);
                effecter.Cleanup();
            }
        }

        private List<Thing> CollectIgnoredThingsAndExtraDamageTargets(SRA_RailgunProjectileExtension ext, Thing hitThing, Map map, IntVec3 position, bool filterNonHostiles)
        {
            tmpIgnoredThings.Clear();
            tmpExtraDamageTargetSet.Clear();
            tmpExtraDamageTargets.Clear();

            foreach (IntVec3 cell in GenRadial.RadialCellsAround(position, ext.explosionRadius, true))
            {
                if (!cell.InBounds(map))
                {
                    continue;
                }

                List<Thing> thingsInCell = map.thingGrid.ThingsListAt(cell);
                for (int i = thingsInCell.Count - 1; i >= 0; i--)
                {
                    Thing thing = thingsInCell[i];
                    if (thing == null || thing.Destroyed)
                    {
                        continue;
                    }

                    if (thing == this)
                    {
                        tmpIgnoredThings.Add(thing);
                        continue;
                    }

                    if (filterNonHostiles && thing != hitThing && !GenHostility.HostileTo(thing, launcher))
                    {
                        tmpIgnoredThings.Add(thing);
                        continue;
                    }

                    AddExtraDamageTarget(thing);
                }
            }

            return tmpIgnoredThings.Count > 0 ? new List<Thing>(tmpIgnoredThings) : null;
        }

        private void AddExtraDamageTarget(Thing thing)
        {
            if (tmpExtraDamageTargetSet.Add(thing))
            {
                tmpExtraDamageTargets.Add(thing);
            }
        }

        private void ApplyExtraDamages(BattleLogEntry_RangedImpact battleLogEntry, bool instigatorGuilty)
        {
            if (tmpExtraDamageTargets.Count == 0)
            {
                return;
            }

            foreach (Thing thing in tmpExtraDamageTargets)
            {
                if (thing == null || thing.Destroyed)
                {
                    continue;
                }

                ApplyExtraDamageList(thing, battleLogEntry, instigatorGuilty, extraDamages);
                ApplyExtraDamageList(thing, battleLogEntry, instigatorGuilty, def.projectile.extraDamages);
            }
        }

        private void ApplyExtraDamageList(Thing thing, BattleLogEntry_RangedImpact battleLogEntry, bool instigatorGuilty, List<ExtraDamage> extraDamageList)
        {
            if (extraDamageList.NullOrEmpty())
            {
                return;
            }

            for (int i = 0; i < extraDamageList.Count; i++)
            {
                ExtraDamage extraDamage = extraDamageList[i];
                if (extraDamage?.def == null || extraDamage.amount <= 0f || !Rand.Chance(extraDamage.chance))
                {
                    continue;
                }

                DamageInfo dinfo = new DamageInfo(extraDamage.def, extraDamage.amount, extraDamage.AdjustedArmorPenetration(), ExactRotation.eulerAngles.y, launcher, null, equipmentDef, DamageInfo.SourceCategory.ThingOrUnknown, intendedTarget.Thing, instigatorGuilty);
                thing.TakeDamage(dinfo).AssociateWithLog(battleLogEntry);
            }
        }

        private bool ShouldIgnoreCollision(Thing hitThing)
        {
            return IsSameFactionBuilding(hitThing) && !IsExplicitTarget(hitThing);
        }

        private bool IsExplicitSameFactionBuildingTarget(Thing hitThing)
        {
            return IsSameFactionBuilding(hitThing) && IsExplicitTarget(hitThing);
        }

        private bool IsSameFactionBuilding(Thing thing)
        {
            if (!(thing is Building building))
            {
                return false;
            }

            Faction launcherFaction = launcher?.Faction ?? Faction;
            Faction buildingFaction = building.Faction;
            return launcherFaction != null && buildingFaction != null && launcherFaction == buildingFaction;
        }

        private bool IsExplicitTarget(Thing thing)
        {
            return thing != null && (usedTarget.Thing == thing || intendedTarget.Thing == thing);
        }

        private bool IsIntendedTarget(Thing hitThing)
        {
            return intendedTarget.Thing == null || hitThing == intendedTarget.Thing || hitThing == null;
        }

        private bool CanPenetrate(SRA_RailgunProjectileExtension ext)
        {
            return penetrationsLeft > 0 && Find.TickManager.TicksGame - lastPenetrationTick >= ext.penetrationDelayTicks;
        }

        private int ResolvedExplosionDamage(SRA_RailgunProjectileExtension ext)
        {
            if (resolvedExplosionDamage < 0)
            {
                resolvedExplosionDamage = ResolveExplosionDamage(ext);
            }

            return resolvedExplosionDamage;
        }

        private float ResolvedExplosionArmorPenetration(SRA_RailgunProjectileExtension ext)
        {
            if (resolvedExplosionArmorPenetration < 0f)
            {
                resolvedExplosionArmorPenetration = ResolveExplosionArmorPenetration(ext);
            }

            return resolvedExplosionArmorPenetration;
        }

        private int ResolveExplosionDamage(SRA_RailgunProjectileExtension ext)
        {
            return ext.explosionDamage > 0 ? ext.explosionDamage : DamageAmount;
        }

        private float ResolveExplosionArmorPenetration(SRA_RailgunProjectileExtension ext)
        {
            return ext.explosionArmorPenetration > 0f ? ext.explosionArmorPenetration : ArmorPenetration;
        }

        private void ApplyHediffToTarget(Pawn target, HediffDef hediffDef, float severity)
        {
            if (target == null || target.Dead || hediffDef == null)
            {
                return;
            }

            float resolvedSeverity = severity > 0f ? severity : 1f;
            Hediff existing = target.health?.hediffSet?.GetFirstHediffOfDef(hediffDef);
            if (existing != null)
            {
                existing.Severity = ClampHediffSeverity(hediffDef, existing.Severity + resolvedSeverity);
                return;
            }

            Hediff hediff = HediffMaker.MakeHediff(hediffDef, target);
            hediff.Severity = ClampHediffSeverity(hediffDef, resolvedSeverity);
            target.health.AddHediff(hediff);
        }

        private static float ClampHediffSeverity(HediffDef hediffDef, float severity)
        {
            return hediffDef.maxSeverity > 0f ? Mathf.Min(severity, hediffDef.maxSeverity) : severity;
        }

        private void ApplyHediffToCollectedPawnTargets(SRA_RailgunProjectileExtension ext, bool filterNonHostiles)
        {
            foreach (Thing thing in tmpExtraDamageTargets)
            {
                if (thing is Pawn pawn && (!filterNonHostiles || GenHostility.HostileTo(pawn, launcher)))
                {
                    ApplyHediffToTarget(pawn, ext.explosionHediff, ext.explosionHediffSeverity);
                }
            }
        }
    }
}
