using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace SRA
{

    public class VerbProperties_KT_Tachyon_Lances : VerbProperties
    {
        public float pathWidth = 1f; // Default path width
        public DamageDef damageDef; // Custom damage type
        public float damageAmount = -1f; // Custom damage amount
        public float armorPenetration = -1f; // Custom armor penetration
        public float maxRange = 1000f; // Default max range for beams
        public string beamDefName = "KT_Tachyon_LancesBeam"; // Default beam def name
    }
    public class Verb_KT_Tachyon_Lances : Verb
    {

        private ThingWithComps weapon
        {
            get
            {
                // 如果是Pawn，从装备获取武器
                if (this.caster is Pawn pawn)
                {
                    return pawn.equipment.Primary;
                }
                // 如果是炮塔或其他建筑，直接返回自身
                else if (this.caster is Building_TurretGun turret)
                {
                    return turret;
                }
                return null;
            }
        }
        private QualityCategory quality
        {
            get
            {
                return weapon.TryGetComp<CompQuality>().Quality;
            }
        }

        private float damageAmountBase
        {
            get
            {
                return this.weapon.def.tools.First<Tool>().power;
            }
        }

        private float armorPenetrationBase
        {
            get
            {
                return this.weapon.def.tools.First<Tool>().armorPenetration;
            }
        }

        private float damageAmount
        {
            get
            {
                if (this.KT_Tachyon_LancesProps.damageAmount > 0)
                {
                    return this.KT_Tachyon_LancesProps.damageAmount;
                }
                return 1.0f * this.damageAmountBase;
            }
        }

        private float armorPenetration
        {
            get
            {
                if (this.KT_Tachyon_LancesProps.armorPenetration >= 0)
                {
                    return this.KT_Tachyon_LancesProps.armorPenetration;
                }
                return 1.0f * this.armorPenetrationBase;
            }
        }

        private VerbProperties_KT_Tachyon_Lances KT_Tachyon_LancesProps
        {
            get
            {
                return (VerbProperties_KT_Tachyon_Lances)this.verbProps;
            }
        }

        protected override int ShotsPerBurst
        {
            get
            {
                return this.verbProps.burstShotCount;
            }
        }

        protected override bool TryCastShot()
        {
            // Calculate all affected cells once
            List<IntVec3> allAffectedCells = this.AffectedCells(this.currentTarget);

            // 获取炮塔或Pawn的位置
            IntVec3 casterPos = (this.caster is Building_TurretGun turret) ? turret.Position : this.caster.Position;

            // 创建光束
            KT_Tachyon_LancesBeam beam = (KT_Tachyon_LancesBeam)GenSpawn.Spawn(
                DefDatabase<ThingDef>.GetNamed(this.KT_Tachyon_LancesProps.beamDefName, true),
                casterPos,
                this.caster.Map
            );

            beam.caster = this.caster;
            beam.targetCell = this.currentTarget.Cell;
            beam.damageAmount = this.damageAmount;
            beam.armorPenetration = this.armorPenetration;
            beam.pathWidth = this.KT_Tachyon_LancesProps.pathWidth;
            beam.weaponDef = this.weapon.def;
            beam.damageDef = this.KT_Tachyon_LancesProps.damageDef;
            beam.StartStrike(allAffectedCells, this.ShotsPerBurst, this.ShotsPerBurst);

            return true;
        }

        public override void DrawHighlight(LocalTargetInfo target)
        {
            GenDraw.DrawFieldEdges(this.AffectedCells(target));
        }

        private List<IntVec3> AffectedCells(LocalTargetInfo target)
        {
            this.tmpCells.Clear();
            Vector3 vector = this.caster.Position.ToVector3Shifted().Yto0();
            IntVec3 endCell = this.TargetPosition(this.caster, target);
            this.tmpCells.Clear();

            ShootLine shootLine = new ShootLine(caster.Position, endCell);
            foreach (IntVec3 cell in shootLine.Points())
            {
                if (!cell.InBounds(this.caster.Map))
                {
                    break;
                }
                // Add cells around the current cell based on pathWidth
                // Convert pathWidth to proper radius for GenRadial
                float radius = Math.Max(0.5f, this.KT_Tachyon_LancesProps.pathWidth - 0.5f);
                foreach (IntVec3 radialCell in GenRadial.RadialCellsAround(cell, radius, true))
                {
                    if (radialCell.InBounds(this.caster.Map) && !this.tmpCells.Contains(radialCell))
                    {
                        this.tmpCells.Add(radialCell);
                    }
                }
            }
            return this.tmpCells;
        }

        public IntVec3 TargetPosition(Thing thething, LocalTargetInfo currentTarget)
        {
            IntVec3 position = thething.Position;
            IntVec3 cell = currentTarget.Cell;
            Vector3 direction = (cell - position).ToVector3().normalized;

            // Define a maximum range to prevent infinite loops or excessively long beams
            float maxRange = this.KT_Tachyon_LancesProps.maxRange; // Use configurable max range

            for (float i = 0; i < maxRange; i += 1f)
            {
                IntVec3 currentCell = (position.ToVector3() + direction * i).ToIntVec3();
                if (!currentCell.InBounds(thething.Map))
                {
                    return currentCell; // Reached map boundary
                }
            }
            return (position.ToVector3() + direction * maxRange).ToIntVec3(); // Reached max range
        }

        private bool CanUseCell(IntVec3 c)
        {
            return c.InBounds(this.caster.Map) && c != this.caster.Position;
        }

        private List<IntVec3> tmpCells = new List<IntVec3>();
    }

    public class KT_Tachyon_LancesBeam : Mote
    {
        public IntVec3 targetCell;
        public Thing caster;
        public ThingDef weaponDef;
        public float damageAmount;
        public float armorPenetration;
        public float pathWidth;
        public DamageDef damageDef;

        // Burst shot support
        public int burstShotsTotal = 1;
        public int currentBurstShot = 0;

        // Path cells for this burst
        private List<IntVec3> currentBurstCells;

        private int ticksToDetonate = 0;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref targetCell, "targetCell");
            Scribe_References.Look(ref caster, "caster");
            Scribe_Defs.Look(ref weaponDef, "weaponDef");
            Scribe_Values.Look(ref damageAmount, "damageAmount");
            Scribe_Values.Look(ref armorPenetration, "armorPenetration");
            Scribe_Values.Look(ref pathWidth, "pathWidth");
            Scribe_Defs.Look(ref damageDef, "damageDef");
            Scribe_Values.Look(ref burstShotsTotal, "burstShotsTotal", 1);
            Scribe_Values.Look(ref currentBurstShot, "currentBurstShot", 0);
        }

        public void StartStrike(List<IntVec3> allCells, int burstIndex, int totalBursts)
        {
            if (allCells == null || !allCells.Any())
            {
                Destroy();
                return;
            }

            currentBurstCells = new List<IntVec3>(allCells);
            currentBurstShot = burstIndex;
            burstShotsTotal = totalBursts;

            // Add a small delay before detonation for visual effect
            ticksToDetonate = 10; // 10 ticks delay before detonation
        }

        protected override void TimeInterval(float deltaTime)
        {
            base.TimeInterval(deltaTime);
            if (ticksToDetonate > 0)
            {
                ticksToDetonate--;
                if (ticksToDetonate == 0)
                {
                    Detonate();
                }
            }
        }

        private void Detonate()
        {
            if (currentBurstCells == null || !currentBurstCells.Any() || Map == null)
            {
                Destroy();
                return;
            }

            // Create a copy of the list to avoid modification during iteration
            List<IntVec3> cellsToDetonate = new List<IntVec3>(currentBurstCells);

            // Clear the current burst cells to prevent reuse
            currentBurstCells.Clear();

            foreach (IntVec3 cell in cellsToDetonate)
            {
                if (cell.InBounds(Map))
                {
                    // Apply explosion effect, but ignore the caster
                    List<Thing> ignoredThings = new List<Thing>();
                    if (caster != null)
                    {
                        ignoredThings.Add(caster);
                    }

                    DamageDef explosionDamageType = damageDef ?? DamageDefOf.Bomb;

                    // Create explosion parameters with more precise settings
                    GenExplosion.DoExplosion(
                        center: cell,
                        map: Map,
                        radius: 1.2f, // Slightly larger radius for better visual effect
                        damType: explosionDamageType,
                        instigator: caster,
                        damAmount: (int)damageAmount,
                        armorPenetration: armorPenetration,
                        explosionSound: null,
                        weapon: weaponDef,
                        projectile: null,
                        intendedTarget: null,
                        postExplosionSpawnThingDef: null,
                        postExplosionSpawnChance: 0f,
                        postExplosionSpawnThingCount: 1,
                        postExplosionGasType: null,
                        applyDamageToExplosionCellsNeighbors: true, // Apply damage to neighbor cells
                        preExplosionSpawnThingDef: null,
                        preExplosionSpawnChance: 0f,
                        preExplosionSpawnThingCount: 1,
                        chanceToStartFire: 0.1f, // Small chance to start fire
                        damageFalloff: true, // Add damage falloff
                        direction: null,
                        ignoredThings: ignoredThings,
                        affectedAngle: null,
                        doVisualEffects: true,
                        propagationSpeed: 0.5f, // Add some propagation speed for visual effect
                        screenShakeFactor: 0.3f, // Add screen shake
                        doSoundEffects: true,
                        postExplosionSpawnThingDefWater: null,
                        flammabilityChanceCurve: null,
                        overrideCells: null,
                        postExplosionSpawnSingleThingDef: null,
                        preExplosionSpawnSingleThingDef: null);
                }
            }

            // Check if there are more bursts to come
            if (currentBurstShot < burstShotsTotal - 1)
            {
                // Prepare for next burst
                ticksToDetonate = 15; // Wait 15 ticks before next burst
                currentBurstShot++;
            }
            else
            {
                // All bursts completed, destroy the mote
                Destroy();
            }
        }
    }
}