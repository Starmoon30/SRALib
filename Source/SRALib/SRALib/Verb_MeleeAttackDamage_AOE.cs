using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.Sound;

namespace SRA
{

    public class Effecter_Extension : DefModExtension
    {
        public EffecterDef effcterDef;
    }
    public class MeleeAttackAOE_Extension : DefModExtension
    {
        public float angle = 100f;
        public float radius = 1.7f;
        public int maxHitTarget = 3;
        public float extraAccuracy = 0;
        public float extraTracking = 0;
    }

    public static class AreaFinder
    {
        public static bool ConeShapedArea(IntVec3 startPos, IntVec3 targetPos, float sectorAngle, float directionAngle, float radius)
        {
            Vector3 v = (targetPos - startPos).ToVector3();
            float distance = v.MagnitudeHorizontal();
            
            if (radius > 0f && distance > radius)
            {
                return false;
            }
            
            v.Normalize();
            float targetAngle = NormalizeAngle360(v.ToAngleFlat());
            float minAngle = NormalizeAngle360(directionAngle - sectorAngle / 2f);
            float maxAngle = NormalizeAngle360(directionAngle + sectorAngle / 2f);
            
            if (minAngle <= maxAngle)
            {
                return targetAngle >= minAngle && targetAngle <= maxAngle;
            }
            else
            {
                return targetAngle >= minAngle || targetAngle <= maxAngle;
            }
        }
        private static float NormalizeAngle360(float angle)
        {
            angle %= 360f;
            if (angle < 0f)
            {
                angle += 360f;
            }
            return angle;
        }
        public static IntVec3 GetRandomDropSpot(Map map, bool useTradeDropSpot, bool allowFogged)
        {
            if (useTradeDropSpot)
            {
                return DropCellFinder.TradeDropSpot(map);
            }
            
            IntVec3 result;
            if (CellFinderLoose.TryGetRandomCellWith((IntVec3 x) => x.Standable(map) && !x.Roofed(map) && (allowFogged || !x.Fogged(map)) && map.reachability.CanReachColony(x), map, 1000, out result))
            {
                return result;
            }
            
            return DropCellFinder.RandomDropSpot(map, true);
        }
        public static IntVec3 FindNearEdgeCell(Map map, Predicate<IntVec3> extraCellValidator)
        {
            TraverseParms traverseParams = TraverseParms.For(TraverseMode.PassDoors, Danger.Deadly, false, false, false, true, false);
            Predicate<IntVec3> baseValidator = (IntVec3 x) => x.Standable(map) && !x.Fogged(map) && map.reachability.CanReachMapEdge(x, traverseParams) && map.reachability.CanReach(x, MapGenerator.PlayerStartSpot, PathEndMode.OnCell, TraverseParms.For(TraverseMode.PassDoors, Danger.Deadly, false, false, false, true, false));
            
            IntVec3 root;
            if (CellFinder.TryFindRandomEdgeCellWith((IntVec3 x) => baseValidator(x) && (extraCellValidator == null || extraCellValidator(x)), map, CellFinder.EdgeRoadChance_Neutral, out root))
            {
                return CellFinder.RandomClosewalkCellNear(root, map, 5, null);
            }
            
            if (CellFinder.TryFindRandomEdgeCellWith(baseValidator, map, CellFinder.EdgeRoadChance_Neutral, out root))
            {
                return CellFinder.RandomClosewalkCellNear(root, map, 5, null);
            }
            
            Log.Warning("Could not find any valid edge cell connected to PlayerStartSpot. Using random cell instead.");
            return CellFinder.RandomCell(map);
        }
    }
    // Token: 0x02000172 RID: 370
    public class Verb_MeleeAttackDamage_AOE : Verb_MeleeAttackDamage
    {
        public MeleeAttackAOE_Extension Extension
        {
            get
            {
                ManeuverDef maneuver = this.maneuver;
                return (maneuver != null) ? maneuver.GetModExtension<MeleeAttackAOE_Extension>() : null;
            }
        }
        public virtual void EffecterTrigger(LocalTargetInfo target)
        {
            ManeuverDef maneuver = this.maneuver;
            Effecter_Extension effecter_Extension = (maneuver != null) ? maneuver.GetModExtension<Effecter_Extension>() : null;
            
            if (effecter_Extension?.effcterDef != null && this.CasterPawn != null && target.Thing != null && this.CasterPawn.Map != null)
            {
                Effecter effecter = effecter_Extension.effcterDef.Spawn(this.CasterPawn.Position, target.Thing.Position, this.CasterPawn.Map, 1f);
                effecter?.Cleanup();
            }
        }
        public virtual List<Pawn> TargetPawns()
        {
            List<Pawn> list = new List<Pawn>();
            Thing thing = this.currentTarget.Thing;
            Vector3 v = (thing.Position - this.CasterPawn.Position).ToVector3();
            v.Normalize();
            float directionAngle = v.ToAngleFlat();
            Pawn primaryTarget = thing as Pawn;
            
            this.EffecterTrigger(thing);
            
            foreach (Thing thing2 in GenRadial.RadialDistinctThingsAround(this.CasterPawn.Position, this.CasterPawn.Map, this.Extension.radius, true))
            {
                Pawn pawn = thing2 as Pawn;
                if (pawn == null || pawn == this.CasterPawn || pawn == primaryTarget)
                {
                    continue;
                }
                
                bool isInCone = AreaFinder.ConeShapedArea(this.CasterPawn.Position, pawn.Position, this.Extension.angle, directionAngle, this.Extension.radius);
                bool isHostile = pawn.Faction == null || this.CasterPawn.Faction == null || 
                                (pawn.Faction != this.CasterPawn.Faction && pawn.Faction.RelationKindWith(this.CasterPawn.Faction) == FactionRelationKind.Hostile);
                
                if (isInCone && isHostile)
                {
                    list.Add(pawn);
                    Log.Message(pawn.Position.ToString());
                }
            }
            
            return list;
        }

        // Token: 0x060006D8 RID: 1752 RVA: 0x00026188 File Offset: 0x00024388
        protected override bool TryCastShot()
        {
            Pawn casterPawn = this.CasterPawn;
            
            if (!casterPawn.Spawned)
            {
                return false;
            }
            
            if (casterPawn.stances.FullBodyBusy)
            {
                return false;
            }
            
            Thing primaryTarget = this.currentTarget.Thing;
            if (!this.CanHitTarget(primaryTarget))
            {
                Log.Warning($"{casterPawn} meleed {primaryTarget} from out of melee position.");
            }
            
            casterPawn.rotationTracker.Face(primaryTarget.DrawPos);
            
            List<Pawn> aoeTargets = this.TargetPawns();
            List<Thing> allTargets = new List<Thing>();
            
            //if (aoeTargets.NullOrEmpty())
            //{
            //    return base.TryCastShot();
            //}
            
            // Select AOE targets (up to maxHitTarget - 1)
            if (aoeTargets.Count > this.Extension.maxHitTarget - 1)
            {
                for (int i = 0; i < this.Extension.maxHitTarget - 1; i++)
                {
                    Pawn selectedTarget = aoeTargets.RandomElement();
                    allTargets.Add(selectedTarget);
                    aoeTargets.Remove(selectedTarget);
                }
            }
            else
            {
                allTargets.AddRange(aoeTargets);
            }
            
            // Add primary target
            allTargets.Add(primaryTarget);
            
            // Grant melee skill XP
            if (!this.IsTargetImmobile(this.currentTarget) && casterPawn.skills != null && 
                (this.currentTarget.Pawn == null || !this.currentTarget.Pawn.IsColonyMech))
            {
                casterPawn.skills.Learn(SkillDefOf.Melee, 200f * this.verbProps.AdjustedFullCycleTime(this, casterPawn), false, false);
            }
            
            bool primaryTargetHit = false;
            
            foreach (Thing target in allTargets)
            {
                Pawn targetPawn = target as Pawn;
                bool isPrimaryTarget = (target == primaryTarget);
                
                // Set melee threat
                if (targetPawn != null && !targetPawn.Dead && 
                    (casterPawn.MentalStateDef != MentalStateDefOf.SocialFighting || targetPawn.MentalStateDef != MentalStateDefOf.SocialFighting) && 
                    (casterPawn.story == null || !casterPawn.story.traits.DisableHostilityFrom(targetPawn)))
                {
                    targetPawn.mindState.meleeThreat = casterPawn;
                    targetPawn.mindState.lastMeleeThreatHarmTick = Find.TickManager.TicksGame;
                }
                
                Map map = target.Map;
                Vector3 drawPos = target.DrawPos;
                SoundDef soundDef = null;
                
                // Check hit chance with extraAccuracy bonus
                if (Rand.Chance(this.GetNonMissChance(target) + this.Extension.extraAccuracy))
                {
                    // Check dodge chance with extraTracking bonus
                    if (!Rand.Chance(this.GetDodgeChance(target) - this.Extension.extraTracking))
                    {
                        // Hit successful
                        soundDef = (target.def.category == ThingCategory.Building) ? this.SoundHitBuilding() : this.SoundHitPawn();
                        
                        if (this.verbProps.impactMote != null)
                        {
                            MoteMaker.MakeStaticMote(drawPos, map, this.verbProps.impactMote, 1f, false, 0f);
                        }
                        
                        if (this.verbProps.impactFleck != null)
                        {
                            FleckMaker.Static(drawPos, map, this.verbProps.impactFleck, 1f);
                        }
                        
                        BattleLogEntry_MeleeCombat combatLog = this.CreateCombatLog((ManeuverDef maneuver) => maneuver.combatLogRulesHit, true, target);
                        
                        if (isPrimaryTarget)
                        {
                            primaryTargetHit = true;
                        }
                        
                        DamageWorker.DamageResult damageResult = this.ApplyMeleeDamageToTarget(target);
                        
                        if (targetPawn != null && damageResult.totalDamageDealt > 0f)
                        {
                            this.ApplyMeleeSlaveSuppression(targetPawn, damageResult.totalDamageDealt);
                        }
                        
                        if (damageResult.stunned && damageResult.parts.NullOrEmpty())
                        {
                            Find.BattleLog.RemoveEntry(combatLog);
                        }
                        else
                        {
                            damageResult.AssociateWithLog(combatLog);
                            if (damageResult.deflected)
                            {
                                combatLog.RuleDef = this.maneuver.combatLogRulesDeflect;
                                combatLog.alwaysShowInCompact = false;
                            }
                        }
                    }
                    else
                    {
                        // Target dodged
                        if (isPrimaryTarget)
                        {
                            primaryTargetHit = false;
                            soundDef = this.SoundDodge(target);
                        }
                        
                        MoteMaker.ThrowText(drawPos, map, "TextMote_Dodge".Translate(), 1.9f);
                        base.CreateCombatLog((ManeuverDef maneuver) => maneuver.combatLogRulesDodge, false);
                    }
                }
                else
                {
                    // Attack missed
                    if (isPrimaryTarget)
                    {
                        primaryTargetHit = false;
                        soundDef = this.SoundMiss();
                    }
                    
                    base.CreateCombatLog((ManeuverDef maneuver) => maneuver.combatLogRulesMiss, false);
                }
                
                // Play sound for primary target
                if (soundDef != null && isPrimaryTarget)
                {
                    soundDef.PlayOneShot(new TargetInfo(target.Position, map, false));
                }
                
                // Notify melee attack animation for primary target
                if (casterPawn.Spawned && isPrimaryTarget)
                {
                    casterPawn.Drawer.Notify_MeleeAttackOn(target);
                }
                
                // Apply stagger to target pawn
                if (targetPawn != null && !targetPawn.Dead && targetPawn.Spawned)
                {
                    targetPawn.stances.stagger.StaggerFor(95, 0.17f);
                }
                
                // Face primary target
                if (casterPawn.Spawned && isPrimaryTarget)
                {
                    casterPawn.rotationTracker.FaceCell(primaryTarget.Position);
                }
                
                // Notify caller for primary target
                if (casterPawn.caller != null && isPrimaryTarget)
                {
                    casterPawn.caller.Notify_DidMeleeAttack();
                }
            }
            
            return primaryTargetHit;
        }

        // Token: 0x060006D9 RID: 1753 RVA: 0x00026734 File Offset: 0x00024934
        private bool CanApplyMeleeSlaveSuppression(Pawn targetPawn)
        {
            return this.CasterPawn != null && this.CasterPawn.IsColonist && !this.CasterPawn.IsSlave && targetPawn != null && targetPawn.IsSlaveOfColony && targetPawn.health.capacities.CanBeAwake && !SlaveRebellionUtility.IsRebelling(targetPawn);
        }

        // Token: 0x060006DA RID: 1754 RVA: 0x00026794 File Offset: 0x00024994
        private void ApplyMeleeSlaveSuppression(Pawn targetPawn, float damageDealt)
        {
            if (this.CanApplyMeleeSlaveSuppression(targetPawn))
            {
                SlaveRebellionUtility.IncrementMeleeSuppression(this.CasterPawn, targetPawn, damageDealt);
            }
        }

        // Token: 0x060006DB RID: 1755 RVA: 0x000267C0 File Offset: 0x000249C0
        private float GetNonMissChance(LocalTargetInfo target)
        {
            if (this.surpriseAttack)
            {
                return 1f;
            }
            
            if (this.IsTargetImmobile(target))
            {
                return 1f;
            }
            
            float hitChance = this.CasterPawn.GetStatValue(StatDefOf.MeleeHitChance, true, -1);
            
            if (ModsConfig.IdeologyActive && target.HasThing)
            {
                if (DarknessCombatUtility.IsOutdoorsAndLit(target.Thing))
                {
                    hitChance += this.caster.GetStatValue(StatDefOf.MeleeHitChanceOutdoorsLitOffset, true, -1);
                }
                else if (DarknessCombatUtility.IsOutdoorsAndDark(target.Thing))
                {
                    hitChance += this.caster.GetStatValue(StatDefOf.MeleeHitChanceOutdoorsDarkOffset, true, -1);
                }
                else if (DarknessCombatUtility.IsIndoorsAndDark(target.Thing))
                {
                    hitChance += this.caster.GetStatValue(StatDefOf.MeleeHitChanceIndoorsDarkOffset, true, -1);
                }
                else if (DarknessCombatUtility.IsIndoorsAndLit(target.Thing))
                {
                    hitChance += this.caster.GetStatValue(StatDefOf.MeleeHitChanceIndoorsLitOffset, true, -1);
                }
            }
            
            return hitChance;
        }

        // Token: 0x060006DC RID: 1756 RVA: 0x000268DC File Offset: 0x00024ADC
        private float GetDodgeChance(LocalTargetInfo target)
        {
            if (this.surpriseAttack)
            {
                return 0f;
            }
            
            if (this.IsTargetImmobile(target))
            {
                return 0f;
            }
            
            Pawn targetPawn = target.Thing as Pawn;
            if (targetPawn == null)
            {
                return 0f;
            }
            
            Stance_Busy busyStance = targetPawn.stances.curStance as Stance_Busy;
            if (busyStance != null && busyStance.verb != null && !busyStance.verb.verbProps.IsMeleeAttack)
            {
                return 0f;
            }
            
            float dodgeChance = targetPawn.GetStatValue(StatDefOf.MeleeDodgeChance, true, -1);
            
            if (ModsConfig.IdeologyActive)
            {
                if (DarknessCombatUtility.IsOutdoorsAndLit(target.Thing))
                {
                    dodgeChance += targetPawn.GetStatValue(StatDefOf.MeleeDodgeChanceOutdoorsLitOffset, true, -1);
                }
                else if (DarknessCombatUtility.IsOutdoorsAndDark(target.Thing))
                {
                    dodgeChance += targetPawn.GetStatValue(StatDefOf.MeleeDodgeChanceOutdoorsDarkOffset, true, -1);
                }
                else if (DarknessCombatUtility.IsIndoorsAndDark(target.Thing))
                {
                    dodgeChance += targetPawn.GetStatValue(StatDefOf.MeleeDodgeChanceIndoorsDarkOffset, true, -1);
                }
                else if (DarknessCombatUtility.IsIndoorsAndLit(target.Thing))
                {
                    dodgeChance += targetPawn.GetStatValue(StatDefOf.MeleeDodgeChanceIndoorsLitOffset, true, -1);
                }
            }
            
            return dodgeChance;
        }

        // Token: 0x060006DD RID: 1757 RVA: 0x00026A40 File Offset: 0x00024C40
        private bool IsTargetImmobile(LocalTargetInfo target)
        {
            Thing thing = target.Thing;
            Pawn pawn = thing as Pawn;
            return thing.def.category != ThingCategory.Pawn || pawn.Downed || pawn.GetPosture() > PawnPosture.Standing;
        }

        // Token: 0x060006DE RID: 1758 RVA: 0x00026A84 File Offset: 0x00024C84
        private SoundDef SoundHitPawn()
        {
            if (base.EquipmentSource != null && !base.EquipmentSource.def.meleeHitSound.NullOrUndefined())
            {
                return base.EquipmentSource.def.meleeHitSound;
            }
            
            if (this.tool != null && !this.tool.soundMeleeHit.NullOrUndefined())
            {
                return this.tool.soundMeleeHit;
            }
            
            if (base.EquipmentSource != null && base.EquipmentSource.Stuff != null)
            {
                if (this.verbProps.meleeDamageDef.armorCategory == DamageArmorCategoryDefOf.Sharp)
                {
                    if (!base.EquipmentSource.Stuff.stuffProps.soundMeleeHitSharp.NullOrUndefined())
                    {
                        return base.EquipmentSource.Stuff.stuffProps.soundMeleeHitSharp;
                    }
                }
                else
                {
                    if (!base.EquipmentSource.Stuff.stuffProps.soundMeleeHitBlunt.NullOrUndefined())
                    {
                        return base.EquipmentSource.Stuff.stuffProps.soundMeleeHitBlunt;
                    }
                }
            }
            
            if (this.CasterPawn != null && !this.CasterPawn.def.race.soundMeleeHitPawn.NullOrUndefined())
            {
                return this.CasterPawn.def.race.soundMeleeHitPawn;
            }
            
            return SoundDefOf.Pawn_Melee_Punch_HitPawn;
        }

        // Token: 0x060006DF RID: 1759 RVA: 0x00026C14 File Offset: 0x00024E14
        private SoundDef SoundHitBuilding()
        {
            Building building = this.currentTarget.Thing as Building;
            if (building != null && !building.def.building.soundMeleeHitOverride.NullOrUndefined())
            {
                return building.def.building.soundMeleeHitOverride;
            }
            
            if (base.EquipmentSource != null && !base.EquipmentSource.def.meleeHitSound.NullOrUndefined())
            {
                return base.EquipmentSource.def.meleeHitSound;
            }
            
            if (this.tool != null && !this.tool.soundMeleeHit.NullOrUndefined())
            {
                return this.tool.soundMeleeHit;
            }
            
            if (base.EquipmentSource != null && base.EquipmentSource.Stuff != null)
            {
                if (this.verbProps.meleeDamageDef.armorCategory == DamageArmorCategoryDefOf.Sharp)
                {
                    if (!base.EquipmentSource.Stuff.stuffProps.soundMeleeHitSharp.NullOrUndefined())
                    {
                        return base.EquipmentSource.Stuff.stuffProps.soundMeleeHitSharp;
                    }
                }
                else
                {
                    if (!base.EquipmentSource.Stuff.stuffProps.soundMeleeHitBlunt.NullOrUndefined())
                    {
                        return base.EquipmentSource.Stuff.stuffProps.soundMeleeHitBlunt;
                    }
                }
            }
            
            if (this.CasterPawn != null && !this.CasterPawn.def.race.soundMeleeHitBuilding.NullOrUndefined())
            {
                return this.CasterPawn.def.race.soundMeleeHitBuilding;
            }
            
            return SoundDefOf.MeleeHit_Unarmed;
        }

        // Token: 0x060006E0 RID: 1760 RVA: 0x00026DF0 File Offset: 0x00024FF0
        private SoundDef SoundMiss()
        {
            if (this.CasterPawn != null)
            {
                if (this.tool != null && !this.tool.soundMeleeMiss.NullOrUndefined())
                {
                    return this.tool.soundMeleeMiss;
                }
                
                if (!this.CasterPawn.def.race.soundMeleeMiss.NullOrUndefined())
                {
                    return this.CasterPawn.def.race.soundMeleeMiss;
                }
            }
            
            return SoundDefOf.Pawn_Melee_Punch_Miss;
        }

        private SoundDef SoundDodge(Thing target)
        {
            if (target.def.race != null && target.def.race.soundMeleeDodge != null)
            {
                return target.def.race.soundMeleeDodge;
            }
            
            return this.SoundMiss();
        }

        public BattleLogEntry_MeleeCombat CreateCombatLog(Func<ManeuverDef, RulePackDef> rulePackGetter, bool alwaysShow, Thing target)
        {
            if (this.maneuver == null || this.tool == null)
            {
                return null;
            }
            
            BattleLogEntry_MeleeCombat combatLog = new BattleLogEntry_MeleeCombat(
                rulePackGetter(this.maneuver), 
                alwaysShow, 
                this.CasterPawn, 
                target, 
                base.ImplementOwnerType, 
                this.tool.labelUsedInLogging ? this.tool.label : "", 
                (base.EquipmentSource == null) ? null : base.EquipmentSource.def, 
                (base.HediffCompSource == null) ? null : base.HediffCompSource.Def, 
                this.maneuver.logEntryDef
            );
            
            Find.BattleLog.Add(combatLog);
            return combatLog;
        }
    }
}
