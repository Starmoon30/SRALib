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

    }

    public static class AreaFinder
    {
        public static bool ConeShapedArea(IntVec3 startPos, IntVec3 targetPos, float sectorAngle, float directionAngle, float radius)
        {
            Vector3 v = (targetPos - startPos).ToVector3();
            float num = v.MagnitudeHorizontal();
            bool flag = radius > 0f;
            bool flag2 = flag && num > radius;
            bool result;
            if (flag2)
            {
                result = false;
            }
            else
            {
                v.Normalize();
                float num2 = v.ToAngleFlat();
                float num3 = directionAngle - sectorAngle / 2f;
                float num4 = directionAngle + sectorAngle / 2f;
                num2 = AreaFinder.NormalizeAngle360(num2);
                num3 = AreaFinder.NormalizeAngle360(num3);
                num4 = AreaFinder.NormalizeAngle360(num4);
                bool flag3 = num3 <= num4;
                if (flag3)
                {
                    result = (num2 >= num3 && num2 <= num4);
                }
                else
                {
                    result = (num2 >= num3 || num2 <= num4);
                }
            }
            return result;
        }
        private static float NormalizeAngle360(float angle)
        {
            angle %= 360f;
            bool flag = angle < 0f;
            if (flag)
            {
                angle += 360f;
            }
            return angle;
        }
        public static IntVec3 GetRandomDropSpot(Map map, bool useTradeDropSpot, bool allowFogged)
        {
            IntVec3 result;
            if (useTradeDropSpot)
            {
                result = DropCellFinder.TradeDropSpot(map);
            }
            else
            {
                IntVec3 intVec;
                bool flag = CellFinderLoose.TryGetRandomCellWith((IntVec3 x) => x.Standable(map) && !x.Roofed(map) && (allowFogged || !x.Fogged(map)) && map.reachability.CanReachColony(x), map, 1000, out intVec);
                if (flag)
                {
                    result = intVec;
                }
                else
                {
                    result = DropCellFinder.RandomDropSpot(map, true);
                }
            }
            return result;
        }
        public static IntVec3 FindNearEdgeCell(Map map, Predicate<IntVec3> extraCellValidator)
        {
            TraverseParms traverseParams = TraverseParms.For(TraverseMode.PassDoors, Danger.Deadly, false, false, false, true, false);
            Predicate<IntVec3> baseValidator = (IntVec3 x) => x.Standable(map) && !x.Fogged(map) && map.reachability.CanReachMapEdge(x, traverseParams) && map.reachability.CanReach(x, MapGenerator.PlayerStartSpot, PathEndMode.OnCell, TraverseParms.For(TraverseMode.PassDoors, Danger.Deadly, false, false, false, true, false));
            IntVec3 root;
            bool flag = CellFinder.TryFindRandomEdgeCellWith((IntVec3 x) => baseValidator(x) && (extraCellValidator == null || extraCellValidator(x)), map, CellFinder.EdgeRoadChance_Neutral, out root);
            IntVec3 result;
            if (flag)
            {
                result = CellFinder.RandomClosewalkCellNear(root, map, 5, null);
            }
            else
            {
                bool flag2 = CellFinder.TryFindRandomEdgeCellWith(baseValidator, map, CellFinder.EdgeRoadChance_Neutral, out root);
                if (flag2)
                {
                    result = CellFinder.RandomClosewalkCellNear(root, map, 5, null);
                }
                else
                {
                    Log.Warning("Could not find any valid edge cell connected to PlayerStartSpot. Using random cell instead.");
                    result = CellFinder.RandomCell(map);
                }
            }
            return result;
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
            bool flag = ((effecter_Extension != null) ? effecter_Extension.effcterDef : null) != null && this.CasterPawn != null && target.Thing != null && this.CasterPawn.Map != null;
            if (flag)
            {
                Effecter effecter = effecter_Extension.effcterDef.Spawn(this.CasterPawn.Position, target.Thing.Position, this.CasterPawn.Map, 1f);
                if (effecter != null)
                {
                    effecter.Cleanup();
                }
            }
        }
        public virtual List<Pawn> TargetPawns()
        {
            List<Pawn> list = new List<Pawn>();
            Thing thing = this.currentTarget.Thing;
            Vector3 v = (thing.Position - this.CasterPawn.Position).ToVector3();
            v.Normalize();
            float directionAngle = v.ToAngleFlat();
            Pawn pawn = thing as Pawn;
            this.EffecterTrigger(thing);
            foreach (Thing thing2 in GenRadial.RadialDistinctThingsAround(this.CasterPawn.Position, this.CasterPawn.Map, this.Extension.radius, true))
            {
                Pawn pawn2 = thing2 as Pawn;
                bool flag3 = pawn2 == null;
                if (!flag3)
                {
                    bool flag4 = pawn2 == this.CasterPawn || pawn2 == pawn;
                    if (!flag4)
                    {
                        bool flag5 = AreaFinder.ConeShapedArea(this.CasterPawn.Position, pawn2.Position, this.Extension.angle, directionAngle, this.Extension.radius) && (pawn2.Faction == null || this.CasterPawn.Faction == null || (pawn2.Faction != this.CasterPawn.Faction && pawn2.Faction.RelationKindWith(this.CasterPawn.Faction) == FactionRelationKind.Hostile));
                        if (flag5)
                        {
                            list.Add(pawn2);
                            Log.Message(pawn2.Position.ToString());
                        }
                    }
                }
            }
            return list;
        }

        // Token: 0x060006D8 RID: 1752 RVA: 0x00026188 File Offset: 0x00024388
        protected override bool TryCastShot()
        {
            Pawn casterPawn = this.CasterPawn;
            bool flag = !casterPawn.Spawned;
            bool result;
            if (flag)
            {
                result = false;
            }
            else
            {
                bool fullBodyBusy = casterPawn.stances.FullBodyBusy;
                if (fullBodyBusy)
                {
                    result = false;
                }
                else
                {
                    Thing thing = this.currentTarget.Thing;
                    bool flag2 = !this.CanHitTarget(thing);
                    if (flag2)
                    {
                        Log.Warning(string.Concat(new object[]
                        {
                            casterPawn,
                            " meleed ",
                            thing,
                            " from out of melee position."
                        }));
                    }
                    casterPawn.rotationTracker.Face(thing.DrawPos);
                    List<Pawn> list = this.TargetPawns();
                    List<Thing> list2 = new List<Thing>();
                    bool flag3 = list.NullOrEmpty<Pawn>();
                    if (flag3)
                    {
                        result = base.TryCastShot();
                    }
                    else
                    {
                        bool flag4 = list.Count > this.Extension.maxHitTarget - 1;
                        if (flag4)
                        {
                            for (int i = 0; i < this.Extension.maxHitTarget - 1; i++)
                            {
                                Pawn item = list.RandomElement<Pawn>();
                                list2.Add(item);
                                list.Remove(item);
                            }
                        }
                        else
                        {
                            list2.AddRange(list);
                        }
                        list2.Add(thing);
                        bool flag5 = !this.IsTargetImmobile(this.currentTarget) && casterPawn.skills != null && (this.currentTarget.Pawn == null || !this.currentTarget.Pawn.IsColonyMech);
                        if (flag5)
                        {
                            casterPawn.skills.Learn(SkillDefOf.Melee, 200f * this.verbProps.AdjustedFullCycleTime(this, casterPawn), false, false);
                        }
                        bool flag6 = false;
                        foreach (Thing thing2 in list2)
                        {
                            Pawn pawn = thing2 as Pawn;
                            bool flag7 = pawn != null && !pawn.Dead && (casterPawn.MentalStateDef != MentalStateDefOf.SocialFighting || pawn.MentalStateDef != MentalStateDefOf.SocialFighting) && (casterPawn.story == null || !casterPawn.story.traits.DisableHostilityFrom(pawn));
                            if (flag7)
                            {
                                pawn.mindState.meleeThreat = casterPawn;
                                pawn.mindState.lastMeleeThreatHarmTick = Find.TickManager.TicksGame;
                            }
                            Map map = thing2.Map;
                            Vector3 drawPos = thing2.DrawPos;
                            SoundDef soundDef = null;
                            bool flag8 = thing2 == thing;
                            bool flag9 = Rand.Chance(this.GetNonMissChance(thing2));
                            if (flag9)
                            {
                                bool flag10 = !Rand.Chance(this.GetDodgeChance(thing2));
                                if (flag10)
                                {
                                    bool flag11 = thing2.def.category == ThingCategory.Building;
                                    if (flag11)
                                    {
                                        soundDef = this.SoundHitBuilding();
                                    }
                                    else
                                    {
                                        soundDef = this.SoundHitPawn();
                                    }
                                    bool flag12 = this.verbProps.impactMote != null;
                                    if (flag12)
                                    {
                                        MoteMaker.MakeStaticMote(drawPos, map, this.verbProps.impactMote, 1f, false, 0f);
                                    }
                                    bool flag13 = this.verbProps.impactFleck != null;
                                    if (flag13)
                                    {
                                        FleckMaker.Static(drawPos, map, this.verbProps.impactFleck, 1f);
                                    }
                                    BattleLogEntry_MeleeCombat battleLogEntry_MeleeCombat = this.CreateCombatLog((ManeuverDef maneuver) => maneuver.combatLogRulesHit, true, thing2);
                                    bool flag14 = flag8;
                                    if (flag14)
                                    {
                                        flag6 = true;
                                    }
                                    DamageWorker.DamageResult damageResult = this.ApplyMeleeDamageToTarget(thing2);
                                    bool flag15 = pawn != null && damageResult.totalDamageDealt > 0f;
                                    if (flag15)
                                    {
                                        this.ApplyMeleeSlaveSuppression(pawn, damageResult.totalDamageDealt);
                                    }
                                    bool flag16 = damageResult.stunned && damageResult.parts.NullOrEmpty<BodyPartRecord>();
                                    if (flag16)
                                    {
                                        Find.BattleLog.RemoveEntry(battleLogEntry_MeleeCombat);
                                    }
                                    else
                                    {
                                        damageResult.AssociateWithLog(battleLogEntry_MeleeCombat);
                                        bool deflected = damageResult.deflected;
                                        if (deflected)
                                        {
                                            battleLogEntry_MeleeCombat.RuleDef = this.maneuver.combatLogRulesDeflect;
                                            battleLogEntry_MeleeCombat.alwaysShowInCompact = false;
                                        }
                                    }
                                }
                                else
                                {
                                    bool flag17 = flag8;
                                    if (flag17)
                                    {
                                        flag6 = false;
                                        soundDef = this.SoundDodge(thing2);
                                    }
                                    MoteMaker.ThrowText(drawPos, map, "TextMote_Dodge".Translate(), 1.9f);
                                    base.CreateCombatLog((ManeuverDef maneuver) => maneuver.combatLogRulesDodge, false);
                                }
                            }
                            else
                            {
                                bool flag18 = flag8;
                                if (flag18)
                                {
                                    flag6 = false;
                                    soundDef = this.SoundMiss();
                                }
                                base.CreateCombatLog((ManeuverDef maneuver) => maneuver.combatLogRulesMiss, false);
                            }
                            bool flag19 = soundDef != null && flag8;
                            if (flag19)
                            {
                                soundDef.PlayOneShot(new TargetInfo(thing2.Position, map, false));
                            }
                            bool flag20 = casterPawn.Spawned && flag8;
                            if (flag20)
                            {
                                casterPawn.Drawer.Notify_MeleeAttackOn(thing2);
                            }
                            bool flag21 = pawn != null && !pawn.Dead && pawn.Spawned;
                            if (flag21)
                            {
                                pawn.stances.stagger.StaggerFor(95, 0.17f);
                            }
                            bool flag22 = casterPawn.Spawned && flag8;
                            if (flag22)
                            {
                                casterPawn.rotationTracker.FaceCell(thing.Position);
                            }
                            bool flag23 = casterPawn.caller != null && flag8;
                            if (flag23)
                            {
                                casterPawn.caller.Notify_DidMeleeAttack();
                            }
                        }
                        result = flag6;
                    }
                }
            }
            return result;
        }

        // Token: 0x060006D9 RID: 1753 RVA: 0x00026734 File Offset: 0x00024934
        private bool CanApplyMeleeSlaveSuppression(Pawn targetPawn)
        {
            return this.CasterPawn != null && this.CasterPawn.IsColonist && !this.CasterPawn.IsSlave && targetPawn != null && targetPawn.IsSlaveOfColony && targetPawn.health.capacities.CanBeAwake && !SlaveRebellionUtility.IsRebelling(targetPawn);
        }

        // Token: 0x060006DA RID: 1754 RVA: 0x00026794 File Offset: 0x00024994
        private void ApplyMeleeSlaveSuppression(Pawn targetPawn, float damageDealt)
        {
            bool flag = this.CanApplyMeleeSlaveSuppression(targetPawn);
            if (flag)
            {
                SlaveRebellionUtility.IncrementMeleeSuppression(this.CasterPawn, targetPawn, damageDealt);
            }
        }

        // Token: 0x060006DB RID: 1755 RVA: 0x000267C0 File Offset: 0x000249C0
        private float GetNonMissChance(LocalTargetInfo target)
        {
            bool surpriseAttack = this.surpriseAttack;
            float result;
            if (surpriseAttack)
            {
                result = 1f;
            }
            else
            {
                bool flag = this.IsTargetImmobile(target);
                if (flag)
                {
                    result = 1f;
                }
                else
                {
                    float num = this.CasterPawn.GetStatValue(StatDefOf.MeleeHitChance, true, -1);
                    bool flag2 = ModsConfig.IdeologyActive && target.HasThing;
                    if (flag2)
                    {
                        bool flag3 = DarknessCombatUtility.IsOutdoorsAndLit(target.Thing);
                        if (flag3)
                        {
                            num += this.caster.GetStatValue(StatDefOf.MeleeHitChanceOutdoorsLitOffset, true, -1);
                        }
                        else
                        {
                            bool flag4 = DarknessCombatUtility.IsOutdoorsAndDark(target.Thing);
                            if (flag4)
                            {
                                num += this.caster.GetStatValue(StatDefOf.MeleeHitChanceOutdoorsDarkOffset, true, -1);
                            }
                            else
                            {
                                bool flag5 = DarknessCombatUtility.IsIndoorsAndDark(target.Thing);
                                if (flag5)
                                {
                                    num += this.caster.GetStatValue(StatDefOf.MeleeHitChanceIndoorsDarkOffset, true, -1);
                                }
                                else
                                {
                                    bool flag6 = DarknessCombatUtility.IsIndoorsAndLit(target.Thing);
                                    if (flag6)
                                    {
                                        num += this.caster.GetStatValue(StatDefOf.MeleeHitChanceIndoorsLitOffset, true, -1);
                                    }
                                }
                            }
                        }
                    }
                    result = num;
                }
            }
            return result;
        }

        // Token: 0x060006DC RID: 1756 RVA: 0x000268DC File Offset: 0x00024ADC
        private float GetDodgeChance(LocalTargetInfo target)
        {
            bool surpriseAttack = this.surpriseAttack;
            float result;
            if (surpriseAttack)
            {
                result = 0f;
            }
            else
            {
                bool flag = this.IsTargetImmobile(target);
                if (flag)
                {
                    result = 0f;
                }
                else
                {
                    Pawn pawn = target.Thing as Pawn;
                    bool flag2 = pawn == null;
                    if (flag2)
                    {
                        result = 0f;
                    }
                    else
                    {
                        Stance_Busy stance_Busy = pawn.stances.curStance as Stance_Busy;
                        bool flag3 = stance_Busy != null && stance_Busy.verb != null && !stance_Busy.verb.verbProps.IsMeleeAttack;
                        if (flag3)
                        {
                            result = 0f;
                        }
                        else
                        {
                            float num = pawn.GetStatValue(StatDefOf.MeleeDodgeChance, true, -1);
                            bool ideologyActive = ModsConfig.IdeologyActive;
                            if (ideologyActive)
                            {
                                bool flag4 = DarknessCombatUtility.IsOutdoorsAndLit(target.Thing);
                                if (flag4)
                                {
                                    num += pawn.GetStatValue(StatDefOf.MeleeDodgeChanceOutdoorsLitOffset, true, -1);
                                }
                                else
                                {
                                    bool flag5 = DarknessCombatUtility.IsOutdoorsAndDark(target.Thing);
                                    if (flag5)
                                    {
                                        num += pawn.GetStatValue(StatDefOf.MeleeDodgeChanceOutdoorsDarkOffset, true, -1);
                                    }
                                    else
                                    {
                                        bool flag6 = DarknessCombatUtility.IsIndoorsAndDark(target.Thing);
                                        if (flag6)
                                        {
                                            num += pawn.GetStatValue(StatDefOf.MeleeDodgeChanceIndoorsDarkOffset, true, -1);
                                        }
                                        else
                                        {
                                            bool flag7 = DarknessCombatUtility.IsIndoorsAndLit(target.Thing);
                                            if (flag7)
                                            {
                                                num += pawn.GetStatValue(StatDefOf.MeleeDodgeChanceIndoorsLitOffset, true, -1);
                                            }
                                        }
                                    }
                                }
                            }
                            result = num;
                        }
                    }
                }
            }
            return result;
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
            bool flag = base.EquipmentSource != null && !base.EquipmentSource.def.meleeHitSound.NullOrUndefined();
            SoundDef result;
            if (flag)
            {
                result = base.EquipmentSource.def.meleeHitSound;
            }
            else
            {
                bool flag2 = this.tool != null && !this.tool.soundMeleeHit.NullOrUndefined();
                if (flag2)
                {
                    result = this.tool.soundMeleeHit;
                }
                else
                {
                    bool flag3 = base.EquipmentSource != null && base.EquipmentSource.Stuff != null;
                    if (flag3)
                    {
                        bool flag4 = this.verbProps.meleeDamageDef.armorCategory == DamageArmorCategoryDefOf.Sharp;
                        if (flag4)
                        {
                            bool flag5 = !base.EquipmentSource.Stuff.stuffProps.soundMeleeHitSharp.NullOrUndefined();
                            if (flag5)
                            {
                                return base.EquipmentSource.Stuff.stuffProps.soundMeleeHitSharp;
                            }
                        }
                        else
                        {
                            bool flag6 = !base.EquipmentSource.Stuff.stuffProps.soundMeleeHitBlunt.NullOrUndefined();
                            if (flag6)
                            {
                                return base.EquipmentSource.Stuff.stuffProps.soundMeleeHitBlunt;
                            }
                        }
                    }
                    bool flag7 = this.CasterPawn != null && !this.CasterPawn.def.race.soundMeleeHitPawn.NullOrUndefined();
                    if (flag7)
                    {
                        result = this.CasterPawn.def.race.soundMeleeHitPawn;
                    }
                    else
                    {
                        result = SoundDefOf.Pawn_Melee_Punch_HitPawn;
                    }
                }
            }
            return result;
        }

        // Token: 0x060006DF RID: 1759 RVA: 0x00026C14 File Offset: 0x00024E14
        private SoundDef SoundHitBuilding()
        {
            Building building = this.currentTarget.Thing as Building;
            bool flag = building != null && !building.def.building.soundMeleeHitOverride.NullOrUndefined();
            SoundDef result;
            if (flag)
            {
                result = building.def.building.soundMeleeHitOverride;
            }
            else
            {
                bool flag2 = base.EquipmentSource != null && !base.EquipmentSource.def.meleeHitSound.NullOrUndefined();
                if (flag2)
                {
                    result = base.EquipmentSource.def.meleeHitSound;
                }
                else
                {
                    bool flag3 = this.tool != null && !this.tool.soundMeleeHit.NullOrUndefined();
                    if (flag3)
                    {
                        result = this.tool.soundMeleeHit;
                    }
                    else
                    {
                        bool flag4 = base.EquipmentSource != null && base.EquipmentSource.Stuff != null;
                        if (flag4)
                        {
                            bool flag5 = this.verbProps.meleeDamageDef.armorCategory == DamageArmorCategoryDefOf.Sharp;
                            if (flag5)
                            {
                                bool flag6 = !base.EquipmentSource.Stuff.stuffProps.soundMeleeHitSharp.NullOrUndefined();
                                if (flag6)
                                {
                                    return base.EquipmentSource.Stuff.stuffProps.soundMeleeHitSharp;
                                }
                            }
                            else
                            {
                                bool flag7 = !base.EquipmentSource.Stuff.stuffProps.soundMeleeHitBlunt.NullOrUndefined();
                                if (flag7)
                                {
                                    return base.EquipmentSource.Stuff.stuffProps.soundMeleeHitBlunt;
                                }
                            }
                        }
                        bool flag8 = this.CasterPawn != null && !this.CasterPawn.def.race.soundMeleeHitBuilding.NullOrUndefined();
                        if (flag8)
                        {
                            result = this.CasterPawn.def.race.soundMeleeHitBuilding;
                        }
                        else
                        {
                            result = SoundDefOf.MeleeHit_Unarmed;
                        }
                    }
                }
            }
            return result;
        }

        // Token: 0x060006E0 RID: 1760 RVA: 0x00026DF0 File Offset: 0x00024FF0
        private SoundDef SoundMiss()
        {
            bool flag = this.CasterPawn != null;
            if (flag)
            {
                bool flag2 = this.tool != null && !this.tool.soundMeleeMiss.NullOrUndefined();
                if (flag2)
                {
                    return this.tool.soundMeleeMiss;
                }
                bool flag3 = !this.CasterPawn.def.race.soundMeleeMiss.NullOrUndefined();
                if (flag3)
                {
                    return this.CasterPawn.def.race.soundMeleeMiss;
                }
            }
            return SoundDefOf.Pawn_Melee_Punch_Miss;
        }

        private  SoundDef SoundDodge(Thing target)
        {
            bool flag = target.def.race != null && target.def.race.soundMeleeDodge != null;
            SoundDef result;
            if (flag)
            {
                result = target.def.race.soundMeleeDodge;
            }
            else
            {
                result = this.SoundMiss();
            }
            return result;
        }

        public BattleLogEntry_MeleeCombat CreateCombatLog(Func<ManeuverDef, RulePackDef> rulePackGetter, bool alwaysShow, Thing target)
        {
            bool flag = this.maneuver == null;
            BattleLogEntry_MeleeCombat result;
            if (flag)
            {
                result = null;
            }
            else
            {
                bool flag2 = this.tool == null;
                if (flag2)
                {
                    result = null;
                }
                else
                {
                    BattleLogEntry_MeleeCombat battleLogEntry_MeleeCombat = new BattleLogEntry_MeleeCombat(rulePackGetter(this.maneuver), alwaysShow, this.CasterPawn, target, base.ImplementOwnerType, this.tool.labelUsedInLogging ? this.tool.label : "", (base.EquipmentSource == null) ? null : base.EquipmentSource.def, (base.HediffCompSource == null) ? null : base.HediffCompSource.Def, this.maneuver.logEntryDef);
                    Find.BattleLog.Add(battleLogEntry_MeleeCombat);
                    result = battleLogEntry_MeleeCombat;
                }
            }
            return result;
        }
    }
}
