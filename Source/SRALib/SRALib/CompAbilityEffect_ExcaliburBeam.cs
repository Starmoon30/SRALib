using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace SRA
{
    public class CompProperties_AbilityExcaliburBeam : CompProperties_AbilityEffect
    {
        // Invisible short-lived controller Def. It owns the rendering and delayed damage pulse.
        public string beamDefName;

        // Existing gameplay values retained from the original ability.
        public float damageAmount;
        public float armorPenetration;
        public float pathWidth;
        public DamageDef damageDef;
        public SRABeamTargetIgnore targetignore = SRABeamTargetIgnore.ignoreFriendly;

        // Beam presentation configuration.
        public int visualDurationTicks = 24;
        public float sweepStartDistance = 2.5f;
        public float beamStartOffset = 0.75f;
        public ThingDef beamMoteDef;
        public List<ThingDef> extraBeamMoteDefs;
        public FleckDef beamGroundFleckDef;
        public float beamFleckChancePerTick = 0.16f;
        public EffecterDef beamEndEffecterDef;
        public FleckDef beamLineFleckDef;
        public float beamLineFleckChancePerCell = 0.018f;
        public SoundDef beamSoundDef;

        // One-shot cast sound retained for compatibility with the original definition.
        public SoundDef soundDef;

        public CompProperties_AbilityExcaliburBeam()
        {
            compClass = typeof(CompAbilityEffect_ExcaliburBeam);
        }
    }

    public class CompAbilityEffect_ExcaliburBeam : CompAbilityEffect
    {
        public new CompProperties_AbilityExcaliburBeam Props => (CompProperties_AbilityExcaliburBeam)props;

        public override void Apply(LocalTargetInfo target, LocalTargetInfo dest)
        {
            base.Apply(target, dest);

            Pawn caster = parent?.pawn;
            if (caster == null || !caster.Spawned || caster.Map == null || !target.Cell.IsValid)
            {
                return;
            }

            if (!target.Cell.InBounds(caster.Map))
            {
                return;
            }

            if (string.IsNullOrEmpty(Props.beamDefName))
            {
                Log.Error("SRA Excalibur beam needs a beamDefName.");
                return;
            }

            ThingDef beamDef = DefDatabase<ThingDef>.GetNamed(Props.beamDefName, false);
            if (beamDef == null)
            {
                Log.Error("SRA Excalibur beam could not find ThingDef " + Props.beamDefName + ".");
                return;
            }

            Thing spawned = GenSpawn.Spawn(beamDef, caster.Position, caster.Map);
            Thing_ExcaliburBeam beam = spawned as Thing_ExcaliburBeam;
            if (beam == null)
            {
                Log.Error("SRA Excalibur beam ThingDef " + Props.beamDefName + " must use Thing_ExcaliburBeam.");
                spawned.Destroy(DestroyMode.Vanish);
                return;
            }

            beam.Configure(caster, target.Cell, Props);
            beam.StartStrike();

            if (Props.soundDef != null)
            {
                Props.soundDef.PlayOneShot(new TargetInfo(caster.Position, caster.Map, false));
            }
        }

        public override void DrawEffectPreview(LocalTargetInfo target)
        {
            base.DrawEffectPreview(target);

            Pawn caster = parent?.pawn;
            if (caster == null || caster.Map == null || !target.IsValid)
            {
                return;
            }

            List<IntVec3> affectedCells = CalculateAffectedCells(caster, target.Cell);
            GenDraw.DrawFieldEdges(affectedCells, Color.red);
            GenDraw.DrawLineBetween(caster.Position.ToVector3Shifted(), target.CenterVector3, SimpleColor.White);
        }

        private List<IntVec3> CalculateAffectedCells(Pawn caster, IntVec3 targetCell)
        {
            Map map = caster.Map;
            HashSet<IntVec3> cells = new HashSet<IntVec3>();
            ShootLine shootLine = new ShootLine(caster.Position, targetCell);
            int minOffset = -Mathf.FloorToInt(Props.pathWidth / 2f);
            int maxOffset = Mathf.CeilToInt(Props.pathWidth / 2f);

            foreach (IntVec3 cell in shootLine.Points())
            {
                for (int x = minOffset; x <= maxOffset; x++)
                {
                    for (int z = minOffset; z <= maxOffset; z++)
                    {
                        IntVec3 affectedCell = new IntVec3(cell.x + x, cell.y, cell.z + z);
                        if (affectedCell.InBounds(map))
                        {
                            cells.Add(affectedCell);
                        }
                    }
                }
            }

            return new List<IntVec3>(cells);
        }
    }
}
