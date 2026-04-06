using System.Collections.Generic;
using UnityEngine;
using Verse.Sound;
using RimWorld;
using Verse.AI;
using Verse;

//对地压制逻辑
namespace SRA
{
    //对地压制工作组件
    public partial class CompLaserADS : ThingComp
    {
        //对地搜索雷达
        private void TryFindTarget_AntiGround()
        {
            Pawn bestPawn = null;
            float minDistSq = float.MaxValue;
            Vector3 myPos = GetAbsolutePosition();
            float rangeSq = Props.groundRange * Props.groundRange;
            foreach (Pawn p in this.parent.Map.mapPawns.AllPawnsSpawned)
            {
                if (!p.Dead && p.HostileTo(this.parent.Faction) && !p.Position.Fogged(this.parent.Map) && !p.Position.Roofed(this.parent.Map))
                {
                    float distSq = p.Position.DistanceToSquared(myPos.ToIntVec3());
                    if (distSq <= rangeSq && distSq < minDistSq)
                    {
                        minDistSq = distSq;
                        bestPawn = p;
                    }
                }
            }
            if (currentTarget != bestPawn)
            {
                currentTarget = bestPawn;
                currentTargetIrradiationTicks = 0;
                if (currentTarget != null) groundDamageTicksLeft = Props.groundDamageIntervalTicks / 2;
            }
        }
        //开火帧控制器
        private void ProcessAntiGroundTick(float hitAngle)
        {
            currentTargetIrradiationTicks++;
            lastIrradiationTick = Find.TickManager.TicksGame;
            EmitLaserSparks(currentTarget.DrawPos, hitAngle, false);
            if (groundDamageTicksLeft <= 0)
            {
                EmitLaserSparks(currentTarget.DrawPos, hitAngle, true);
                ShootGroundTarget(currentTarget);
                groundDamageTicksLeft = Props.groundDamageIntervalTicks;
                if (currentTarget.Destroyed || (currentTarget is Pawn p && p.Dead))
                {
                    lastInterceptTick = Find.TickManager.TicksGame;
                    lastInterceptPos = currentTarget.DrawPos;
                    cooldownTicksLeft = Props.cooldownTicks;
                    ResetTarget();
                }
            }
            else groundDamageTicksLeft--;
        }
        //造伤逻辑
        private void ShootGroundTarget(Thing target)
        {
            if (target == null || target.Destroyed) return;
            IntVec3 targetPos = target.Position;
            Map targetMap = this.parent.Map;
            DamageDef damDef = DamageDefOf.Burn;
            float armorPen = Props.groundArmorPenetration >= 0f ? Props.groundArmorPenetration : damDef.defaultArmorPenetration;
            DamageInfo dinfo = new DamageInfo(damDef, Props.groundDamageAmount, armorPen, -1f, this.parent, null, this.parent.def);
            DamageWorker.DamageResult res = target.TakeDamage(dinfo);
            if (target is Pawn p)
            {
                if (p.Dead)
                {
                    if (p.RaceProps.IsFlesh && p.Corpse != null && !p.Corpse.Destroyed)
                    {
                        CompRottable compRot = p.Corpse.TryGetComp<CompRottable>();
                        if (compRot != null) compRot.RotProgress += 9999999f;
                        if (Props.groundIgniteSize > 0f)
                        {
                            if (p.Corpse.Position.InBounds(targetMap)) FireUtility.TryStartFireIn(p.Corpse.Position, targetMap, Props.groundIgniteSize, this.parent);
                            p.Corpse.TryAttachFire(Props.groundIgniteSize, this.parent);
                        }
                    }
                }
                else
                {
                    if (Props.groundShatterGearChance > 0f)
                    {
                        TryShatterGear(p, Props.groundShatterGearChance);
                    }
                    if (p.RaceProps.IsFlesh)
                    {
                        if (res != null && res.hediffs != null)
                        {
                            bool dirty = false;
                            foreach (var hediff in res.hediffs)
                            {
                                if (hediff is Hediff_Injury injury)
                                {
                                    var comp = injury.TryGetComp<HediffComp_GetsPermanent>();
                                    if (comp != null) { if (!comp.IsPermanent) { comp.IsPermanent = true; dirty = true; } comp.SetPainCategory(PainCategory.HighPain); }
                                }
                            }
                            if (dirty) p.health.hediffSet.DirtyCache();
                        }
                        if (Props.groundIgniteSize > 0f) p.TryAttachFire(Props.groundIgniteSize, this.parent);
                        if (Props.groundFleeDistance > 0f) TryScarePawn(p, this.parent, Props.groundFleeDistance);
                    }
                }
            }
            else if (Props.groundIgniteSize > 0f)
            {
                if (!target.Destroyed) target.TryAttachFire(Props.groundIgniteSize, this.parent);
                else if (targetPos.InBounds(targetMap)) FireUtility.TryStartFireIn(targetPos, targetMap, Props.groundIgniteSize, this.parent);
            }
            if (Props.groundShootSound != null) Props.groundShootSound.PlayOneShot(new TargetInfo(targetPos, targetMap));
        }
        //外设熔毁逻辑
        private void TryShatterGear(Pawn p, float chance)
        {
            if (chance <= 0f) return;
            bool anyShattered = false;
            if (p.equipment?.Primary != null && !p.equipment.Primary.Destroyed && Rand.Chance(chance))
            {
                p.equipment.Primary.Destroy(DestroyMode.KillFinalize);
                anyShattered = true;
            }
            if (p.apparel != null && p.apparel.WornApparelCount > 0)
            {
                List<Apparel> worn = p.apparel.WornApparel;
                for (int i = worn.Count - 1; i >= 0; i--)
                {
                    if (Rand.Chance(chance))
                    {
                        worn[i].Destroy(DestroyMode.KillFinalize);
                        anyShattered = true;
                    }
                }
            }
            if (anyShattered)
            {
                SoundDefOf.Crunch.PlayOneShot(new TargetInfo(p.Position, p.Map));
            }
        }
        //烧灼逃窜逻辑
        private void TryScarePawn(Pawn p, Thing threat, float fleeDistance)
        {
            if (p == null || !p.Spawned || p.Dead || p.Downed || p.jobs == null) return;
            if (p.CurJob != null && p.CurJob.def == JobDefOf.Flee) return;
            if (p.Faction == Faction.OfPlayer && p.Drafted) return;
            IntVec3 fleeDest;
            if (RCellFinder.TryFindDirectFleeDestination(threat.Position, fleeDistance, p, out fleeDest)) GiveFleeJob(p, threat, fleeDest);
            else
            {
                IntVec3 fallbackDest = CellFinderLoose.GetFleeDest(p, new List<Thing> { threat }, fleeDistance);
                if (fallbackDest.IsValid && fallbackDest != p.Position) GiveFleeJob(p, threat, fallbackDest);
            }
        }
        //发送逃跑命令
        private void GiveFleeJob(Pawn p, Thing threat, IntVec3 dest)
        {
            Job job = JobMaker.MakeJob(JobDefOf.Flee, dest, threat);
            job.locomotionUrgency = LocomotionUrgency.Sprint;
            p.jobs.StartJob(job, JobCondition.InterruptForced);
        }
    }
}