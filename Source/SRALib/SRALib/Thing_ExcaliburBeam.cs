using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace SRA
{
    // A short-lived controller mote. It sweeps a direct-damage beam from the caster toward the selected cell.
    public class Thing_ExcaliburBeam : Mote
    {
        public IntVec3 targetCell;
        public IntVec3 originCell;
        public Pawn caster;
        public ThingDef weaponDef;
        public float damageAmount;
        public float armorPenetration;
        public float pathWidth;
        public DamageDef damageDef;
        public SRABeamTargetIgnore targetIgnore = SRABeamTargetIgnore.ignoreFriendly;

        public int visualDurationTicks = 24;
        public float sweepStartDistance = 2.5f;
        public float beamStartOffset = 0.75f;
        public ThingDef beamMoteDef;
        public List<ThingDef> extraBeamMoteDefs = new List<ThingDef>();
        public FleckDef beamGroundFleckDef;
        public float beamFleckChancePerTick;
        public EffecterDef beamEndEffecterDef;
        public FleckDef beamLineFleckDef;
        public float beamLineFleckChancePerCell;
        public SoundDef beamSoundDef;

        private int elapsedTicks;
        private bool strikeStarted;
        private bool visualsInitialized;
        private MoteDualAttached beamMote;
        private readonly List<MoteDualAttached> extraMotes = new List<MoteDualAttached>();
        private readonly HashSet<Thing> damagedThingsThisTick = new HashSet<Thing>();
        private Effecter endEffecter;
        private Sustainer sustainer;

        public void Configure(Pawn newCaster, IntVec3 newTargetCell, CompProperties_AbilityExcaliburBeam props)
        {
            caster = newCaster;
            originCell = newCaster != null ? newCaster.Position : Position;
            targetCell = newTargetCell;
            damageAmount = props.damageAmount;
            armorPenetration = props.armorPenetration;
            pathWidth = props.pathWidth;
            damageDef = props.damageDef;
            targetIgnore = props.targetignore;
            visualDurationTicks = Mathf.Max(1, props.visualDurationTicks);
            sweepStartDistance = Mathf.Max(0f, props.sweepStartDistance);
            beamStartOffset = props.beamStartOffset;
            beamMoteDef = props.beamMoteDef;
            extraBeamMoteDefs = props.extraBeamMoteDefs != null ? new List<ThingDef>(props.extraBeamMoteDefs) : new List<ThingDef>();
            beamGroundFleckDef = props.beamGroundFleckDef;
            beamFleckChancePerTick = props.beamFleckChancePerTick;
            beamEndEffecterDef = props.beamEndEffecterDef;
            beamLineFleckDef = props.beamLineFleckDef;
            beamLineFleckChancePerCell = props.beamLineFleckChancePerCell;
            beamSoundDef = props.beamSoundDef;
        }

        public void StartStrike()
        {
            if (Map == null || !originCell.InBounds(Map) || !targetCell.InBounds(Map))
            {
                Destroy(DestroyMode.Vanish);
                return;
            }

            elapsedTicks = 0;
            strikeStarted = true;
            visualsInitialized = false;
            EnsureVisuals(GetCurrentBeamPosition());
        }

        protected override void Tick()
        {
            base.Tick();
            if (Destroyed || !strikeStarted)
            {
                return;
            }

            elapsedTicks++;
            Vector3 beamPosition = GetCurrentBeamPosition();
            MaintainVisuals(beamPosition);
            ApplyBeamDamage(beamPosition);

            if (elapsedTicks >= visualDurationTicks)
            {
                FinishStrike();
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref targetCell, "sraExcaliburTargetCell");
            Scribe_Values.Look(ref originCell, "sraExcaliburOriginCell");
            Scribe_References.Look(ref caster, "sraExcaliburCaster");
            Scribe_Defs.Look(ref weaponDef, "sraExcaliburWeaponDef");
            Scribe_Values.Look(ref damageAmount, "sraExcaliburDamageAmount");
            Scribe_Values.Look(ref armorPenetration, "sraExcaliburArmorPenetration");
            Scribe_Values.Look(ref pathWidth, "sraExcaliburPathWidth");
            Scribe_Defs.Look(ref damageDef, "sraExcaliburDamageDef");
            Scribe_Values.Look(ref targetIgnore, "sraExcaliburTargetIgnore", SRABeamTargetIgnore.ignoreFriendly);
            Scribe_Values.Look(ref visualDurationTicks, "sraExcaliburVisualDuration", 24);
            Scribe_Values.Look(ref sweepStartDistance, "sraExcaliburSweepStartDistance", 2.5f);
            Scribe_Values.Look(ref beamStartOffset, "sraExcaliburBeamStartOffset", 0.75f);
            Scribe_Defs.Look(ref beamMoteDef, "sraExcaliburBeamMoteDef");
            Scribe_Collections.Look(ref extraBeamMoteDefs, "sraExcaliburExtraBeamMoteDefs", LookMode.Def);
            Scribe_Defs.Look(ref beamGroundFleckDef, "sraExcaliburGroundFleckDef");
            Scribe_Values.Look(ref beamFleckChancePerTick, "sraExcaliburGroundFleckChance");
            Scribe_Defs.Look(ref beamEndEffecterDef, "sraExcaliburEndEffecterDef");
            Scribe_Defs.Look(ref beamLineFleckDef, "sraExcaliburLineFleckDef");
            Scribe_Values.Look(ref beamLineFleckChancePerCell, "sraExcaliburLineFleckChance");
            Scribe_Defs.Look(ref beamSoundDef, "sraExcaliburBeamSoundDef");
            Scribe_Values.Look(ref elapsedTicks, "sraExcaliburElapsedTicks");
            Scribe_Values.Look(ref strikeStarted, "sraExcaliburStrikeStarted");

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (extraBeamMoteDefs == null)
                {
                    extraBeamMoteDefs = new List<ThingDef>();
                }

                // Child motes, effecters, and sustainers are runtime-only. Recreate the remaining sweep after loading.
                visualsInitialized = false;
                beamMote = null;
                extraMotes.Clear();
                damagedThingsThisTick.Clear();
                endEffecter = null;
                sustainer = null;
            }
        }

        private Vector3 GetCurrentBeamPosition()
        {
            Vector3 origin = originCell.ToVector3Shifted();
            Vector3 target = targetCell.ToVector3Shifted();
            Vector3 toTarget = (target - origin).Yto0();
            float distance = toTarget.magnitude;
            if (distance <= 0.001f)
            {
                return target;
            }

            Vector3 start = origin + toTarget.normalized * Mathf.Min(sweepStartDistance, distance);
            float progress = visualDurationTicks <= 1
                ? 1f
                : Mathf.Clamp01((float)elapsedTicks / (visualDurationTicks - 1));
            return Vector3.Lerp(start, target, progress);
        }

        private void EnsureVisuals(Vector3 beamPosition)
        {
            if (visualsInitialized || Map == null || caster == null || !caster.Spawned)
            {
                return;
            }

            visualsInitialized = true;
            TargetInfo source = new TargetInfo(originCell, Map, false);
            TargetInfo target = GetBeamTarget(beamPosition, out Vector3 targetOffset);
            Vector3 startOffset = GetBeamStartOffset();

            if (beamMoteDef != null)
            {
                beamMote = MoteMaker.MakeInteractionOverlay(beamMoteDef, caster, target);
                MaintainMote(beamMote, source, target, startOffset, targetOffset);
            }

            if (extraBeamMoteDefs != null)
            {
                for (int i = 0; i < extraBeamMoteDefs.Count; i++)
                {
                    ThingDef extraMoteDef = extraBeamMoteDefs[i];
                    if (extraMoteDef == null)
                    {
                        continue;
                    }

                    MoteDualAttached extraMote = MoteMaker.MakeInteractionOverlay(extraMoteDef, caster, target);
                    extraMotes.Add(extraMote);
                    MaintainMote(extraMote, source, target, startOffset, targetOffset);
                }
            }

            if (beamEndEffecterDef != null)
            {
                endEffecter = beamEndEffecterDef.Spawn(target.Cell, Map, targetOffset, 1f);
            }

            if (beamSoundDef != null)
            {
                sustainer = beamSoundDef.TrySpawnSustainer(SoundInfo.InMap(source, MaintenanceType.PerTick));
            }
        }

        private void MaintainVisuals(Vector3 beamPosition)
        {
            EnsureVisuals(beamPosition);
            if (Map == null)
            {
                return;
            }

            TargetInfo source = new TargetInfo(originCell, Map, false);
            TargetInfo target = GetBeamTarget(beamPosition, out Vector3 targetOffset);
            Vector3 startOffset = GetBeamStartOffset();
            MaintainMote(beamMote, source, target, startOffset, targetOffset);

            for (int i = extraMotes.Count - 1; i >= 0; i--)
            {
                MoteDualAttached extraMote = extraMotes[i];
                if (extraMote == null || extraMote.Destroyed)
                {
                    extraMotes.RemoveAt(i);
                    continue;
                }

                MaintainMote(extraMote, source, target, startOffset, targetOffset);
            }

            if (beamGroundFleckDef != null && Rand.Chance(beamFleckChancePerTick))
            {
                FleckMaker.Static(beamPosition, Map, beamGroundFleckDef, 1f);
            }

            if (endEffecter == null && beamEndEffecterDef != null)
            {
                endEffecter = beamEndEffecterDef.Spawn(target.Cell, Map, targetOffset, 1f);
            }

            if (endEffecter != null)
            {
                endEffecter.offset = targetOffset;
                endEffecter.EffectTick(target, TargetInfo.Invalid);
                endEffecter.ticksLeft--;
            }

            ThrowLineFlecks(beamPosition, startOffset);
            sustainer?.Maintain();
        }

        private TargetInfo GetBeamTarget(Vector3 beamPosition, out Vector3 targetOffset)
        {
            IntVec3 visualCell = beamPosition.Yto0().ToIntVec3();
            if (!visualCell.InBounds(Map))
            {
                visualCell = targetCell;
                targetOffset = Vector3.zero;
            }
            else
            {
                targetOffset = beamPosition - visualCell.ToVector3Shifted();
            }

            return new TargetInfo(visualCell, Map, false);
        }

        private Vector3 GetBeamStartOffset()
        {
            Vector3 beam = (targetCell.ToVector3Shifted() - originCell.ToVector3Shifted()).Yto0();
            if (beam.sqrMagnitude <= 0.0001f)
            {
                return Vector3.zero;
            }

            return beam.normalized * beamStartOffset;
        }

        private static void MaintainMote(MoteDualAttached mote, TargetInfo source, TargetInfo target, Vector3 startOffset, Vector3 targetOffset)
        {
            if (mote == null || mote.Destroyed)
            {
                return;
            }

            mote.UpdateTargets(source, target, startOffset, targetOffset);
            mote.Maintain();
        }

        private void ThrowLineFlecks(Vector3 beamPosition, Vector3 startOffset)
        {
            if (beamLineFleckDef == null || beamLineFleckChancePerCell <= 0f || Map == null)
            {
                return;
            }

            Vector3 start = originCell.ToVector3Shifted() + startOffset;
            float length = (beamPosition - start).MagnitudeHorizontal();
            int samples = Mathf.CeilToInt(length);
            if (samples <= 0)
            {
                return;
            }

            for (int i = 0; i < samples; i++)
            {
                if (!Rand.Chance(beamLineFleckChancePerCell))
                {
                    continue;
                }

                float progress = (i + Rand.Value) / samples;
                Vector3 position = Vector3.Lerp(start, beamPosition, progress);
                if (position.ToIntVec3().InBounds(Map))
                {
                    FleckMaker.Static(position, Map, beamLineFleckDef, 1f);
                }
            }
        }

        private void ApplyBeamDamage(Vector3 beamPosition)
        {
            if (Map == null || damageAmount <= 0f)
            {
                return;
            }

            damagedThingsThisTick.Clear();
            IntVec3 endCell = beamPosition.Yto0().ToIntVec3();
            if (!endCell.InBounds(Map))
            {
                return;
            }

            ShootLine line = new ShootLine(originCell, endCell);
            foreach (IntVec3 center in line.Points())
            {
                if (center != originCell)
                {
                    DamageArea(center);
                }
            }

            // ShootLine.Points is version-dependent about the final point, so explicitly cover the moving impact cell.
            if (endCell != originCell)
            {
                DamageArea(endCell);
            }
        }

        private void DamageArea(IntVec3 center)
        {
            int minOffset = -Mathf.FloorToInt(pathWidth / 2f);
            int maxOffset = Mathf.CeilToInt(pathWidth / 2f);
            for (int x = minOffset; x <= maxOffset; x++)
            {
                for (int z = minOffset; z <= maxOffset; z++)
                {
                    IntVec3 cell = new IntVec3(center.x + x, center.y, center.z + z);
                    if (!cell.InBounds(Map))
                    {
                        continue;
                    }

                    List<Thing> things = cell.GetThingList(Map);
                    for (int i = things.Count - 1; i >= 0; i--)
                    {
                        Thing thing = things[i];
                        if (CanDamageThing(thing) && damagedThingsThisTick.Add(thing))
                        {
                            DamageThing(thing);
                        }
                    }
                }
            }
        }

        private bool CanDamageThing(Thing thing)
        {
            return thing != null && !thing.Destroyed && thing.Spawned && thing != caster &&
                   (thing.def.useHitPoints || thing is Pawn) && !IsIgnoredByTargetIgnore(thing);
        }

        private bool IsIgnoredByTargetIgnore(Thing thing)
        {
            return targetIgnore == SRABeamTargetIgnore.ignoreFriendly && IsFriendlyToCaster(thing);
        }

        private bool IsFriendlyToCaster(Thing thing)
        {
            Faction casterFaction = caster?.Faction;
            Faction thingFaction = thing?.Faction;
            if (casterFaction == null || thingFaction == null)
            {
                return false;
            }

            return thingFaction == casterFaction || thingFaction.RelationKindWith(casterFaction) == FactionRelationKind.Ally;
        }

        private void DamageThing(Thing thing)
        {
            DamageDef appliedDamageDef = damageDef ?? DamageDefOf.Bomb;
            float angle = (thing.Position - originCell).AngleFlat;
            DamageInfo damageInfo = new DamageInfo(
                appliedDamageDef,
                damageAmount,
                armorPenetration,
                angle,
                caster,
                null,
                weaponDef,
                DamageInfo.SourceCategory.ThingOrUnknown,
                null,
                true,
                true,
                QualityCategory.Normal,
                true,
                false);
            thing.TakeDamage(damageInfo);
        }

        private void FinishStrike()
        {
            strikeStarted = false;
            if (endEffecter != null)
            {
                endEffecter.Cleanup();
                endEffecter = null;
            }

            EndSustainer();
            Destroy(DestroyMode.Vanish);
        }

        private void EndSustainer()
        {
            if (sustainer != null && !sustainer.Ended)
            {
                sustainer.End();
            }

            sustainer = null;
        }
    }
}
