using System.Collections.Generic;
using RimWorld;
using Verse;

namespace SRA
{
    public class ExplosionParams
    {
        // 额外爆炸的伤害类型；为空或 radius <= 0 时该条目会被跳过。
        public DamageDef damageDef;

        // 额外爆炸半径。
        public float radius;

        // 额外爆炸伤害，-1 时沿用原版默认语义。
        public int damageAmount = -1;

        // 额外爆炸穿甲，-1 时沿用原版默认语义。
        public float armorPenetration = -1f;

        // 额外爆炸音效。
        public SoundDef soundExplode;
    }

    public class ProjectileProperties_CompoundExplosion : ProjectileProperties
    {
        // 主爆炸之外追加执行的爆炸列表。
        public List<ExplosionParams> additionalExplosions;
    }

    public class Projectile_CompoundExplosion : Projectile_Explosive
    {
        protected override void Explode()
        {
            Map map = Map;
            IntVec3 position = Position;
            ProjectileProperties_CompoundExplosion props = def.projectile as ProjectileProperties_CompoundExplosion;
            if (props == null)
            {
                Log.ErrorOnce($"SRALib: Projectile_CompoundExplosion ({def.defName}) must use ProjectileProperties_CompoundExplosion.", def.defName.GetHashCode());
                base.Explode();
                return;
            }

            Destroy(DestroyMode.Vanish);
            Thing launcherThing = launcher;
            ThingDef equipment = equipmentDef;
            Thing intendedThing = intendedTarget.Thing;
            float? direction = origin.AngleToFlat(destination);

            DoExplosionFromProjectileProperties(map, position, props, launcherThing, equipment, def, intendedThing, direction, props.doExplosionVFX, DamageAmount, ArmorPenetration);
            DoAdditionalExplosions(map, position, props, launcherThing, equipment, def, intendedThing, direction);
            TrySpawnSingleFilth(map, position, props);
        }

        private void DoAdditionalExplosions(Map map, IntVec3 position, ProjectileProperties_CompoundExplosion props, Thing launcherThing, ThingDef equipment, ThingDef projectileDef, Thing intendedThing, float? direction)
        {
            if (props.additionalExplosions.NullOrEmpty())
            {
                return;
            }

            for (int i = 0; i < props.additionalExplosions.Count; i++)
            {
                ExplosionParams explosion = props.additionalExplosions[i];
                if (explosion?.damageDef == null || explosion.radius <= 0f)
                {
                    Log.Warning($"SRALib: Projectile_CompoundExplosion ({def.defName}) has an invalid additionalExplosions entry.");
                    continue;
                }

                GenExplosion.DoExplosion(
                    center: position,
                    map: map,
                    radius: explosion.radius,
                    damType: explosion.damageDef,
                    instigator: launcherThing,
                    damAmount: explosion.damageAmount,
                    armorPenetration: explosion.armorPenetration,
                    explosionSound: explosion.soundExplode,
                    weapon: equipment,
                    projectile: projectileDef,
                    intendedTarget: intendedThing,
                    chanceToStartFire: explosion.damageDef.defName.ToLower() == "flame" ? 0.5f : 0f,
                    damageFalloff: props.explosionDamageFalloff,
                    direction: direction,
                    doVisualEffects: false,
                    propagationSpeed: explosion.damageDef.expolosionPropagationSpeed,
                    applyDamageToExplosionCellsNeighbors: props.applyDamageToExplosionCellsNeighbors,
                    screenShakeFactor: props.screenShakeFactor);
            }
        }

        public static void DoExplosionFromProjectileProperties(Map map, IntVec3 center, ProjectileProperties props, Thing instigator, ThingDef weapon, ThingDef projectile, Thing intendedTarget, float? direction = null, bool doVisualEffects = true, int? damageAmountOverride = null, float? armorPenetrationOverride = null)
        {
            GenExplosion.DoExplosion(
                center: center,
                map: map,
                radius: props.explosionRadius,
                damType: props.damageDef,
                instigator: instigator,
                damAmount: damageAmountOverride ?? props.GetDamageAmount(instigator, null),
                armorPenetration: armorPenetrationOverride ?? props.GetArmorPenetration(instigator, null),
                explosionSound: props.soundExplode,
                weapon: weapon,
                projectile: projectile,
                intendedTarget: intendedTarget,
                postExplosionSpawnThingDef: props.postExplosionSpawnThingDef ?? (props.explosionSpawnsSingleFilth ? null : props.filth),
                postExplosionSpawnChance: props.postExplosionSpawnChance,
                postExplosionSpawnThingCount: props.postExplosionSpawnThingCount,
                postExplosionGasType: props.postExplosionGasType,
                applyDamageToExplosionCellsNeighbors: props.applyDamageToExplosionCellsNeighbors,
                preExplosionSpawnThingDef: props.preExplosionSpawnThingDef,
                preExplosionSpawnChance: props.preExplosionSpawnChance,
                preExplosionSpawnThingCount: props.preExplosionSpawnThingCount,
                chanceToStartFire: props.explosionChanceToStartFire,
                damageFalloff: props.explosionDamageFalloff,
                direction: direction,
                doVisualEffects: doVisualEffects,
                propagationSpeed: props.damageDef.expolosionPropagationSpeed,
                postExplosionSpawnThingDefWater: props.postExplosionSpawnThingDefWater,
                screenShakeFactor: props.screenShakeFactor,
                postExplosionSpawnSingleThingDef: props.postExplosionSpawnSingleThingDef,
                preExplosionSpawnSingleThingDef: props.preExplosionSpawnSingleThingDef);
        }

        private void TrySpawnSingleFilth(Map map, IntVec3 position, ProjectileProperties props)
        {
            if (!props.explosionSpawnsSingleFilth || props.filth == null || props.filthCount.TrueMax <= 0 || !Rand.Chance(props.filthChance) || position.Filled(map))
            {
                return;
            }

            FilthMaker.TryMakeFilth(position, map, props.filth, props.filthCount.RandomInRange, FilthSourceFlags.None, true);
        }
    }
}
