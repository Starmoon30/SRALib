using System;
using RimWorld;
using Verse;
using Verse.AI;

namespace SRA
{
    public class CompProperties_SustainedShoot : CompProperties
    {
        public CompProperties_SustainedShoot()
        {
            this.compClass = typeof(CompSustainedShoot);
        }
    }
    public class CompSustainedShoot : ThingComp
    {
        public CompProperties_SustainedShoot Props
        {
            get
            {
                return (CompProperties_SustainedShoot)this.props;
            }
        }
        public CompEquippable CompEquippable
        {
            get
            {
                return this.parent.TryGetComp<CompEquippable>();
            }
        }
        public Verb_ShootSustained Verb
        {
            get
            {
                return this.CompEquippable.PrimaryVerb as Verb_ShootSustained;
            }
        }

        private Pawn CasterPawn
        {
            get
            {
                return this.Verb.CasterPawn;
            }
        }

        public override void Notify_Equipped(Pawn pawn)
        {
            this.VerbReset();
        }

        public override void Notify_Unequipped(Pawn pawn)
        {
            this.VerbReset();
        }

        public override void Notify_UsedWeapon(Pawn pawn)
        {
            base.Notify_UsedWeapon(pawn);
            this.isActive = true;
        }

        public override void CompTick()
        {
            base.CompTick();
            bool isActiveAndVerbNotNull = this.isActive && this.Verb != null;
            if (isActiveAndVerbNotNull)
            {
                Job curJob = this.CasterPawn.CurJob;
                bool hasValidAttackJob = curJob != null && curJob.def != JobDefOf.AttackStatic && curJob.def != JobDefOf.Wait_Combat && curJob.def != JobDefOf.Wait_MaintainPosture;
                if (hasValidAttackJob)
                {
                    this.ForceStopBurst();
                }
                else
                {
                    bool isBursting = this.Verb.state == VerbState.Bursting;
                    if (isBursting)
                    {
                        this.idelTicks = 0;
                        this.cachedBurstShotsLeft = this.Verb.BurstShotsLeft;
                        Pawn targetPawn = this.Verb.CurrentTarget.Pawn;
                        bool isTargetDowned = targetPawn != null && targetPawn.Downed;
                        if (isTargetDowned)
                        {
                            bool isNotForcedTarget = targetPawn != this.Verb.forceTargetedDownedPawn;
                            if (isNotForcedTarget)
                            {
                                this.Verb.Reset();
                            }
                        }
                    }
                    bool isIdle = this.Verb.state == VerbState.Idle;
                    if (isIdle)
                    {
                        this.idelTicks++;
                        bool hasExceededIdleThreshold = this.idelTicks > this.Verb.TicksBetweenBurstShots;
                        if (hasExceededIdleThreshold)
                        {
                            this.ForceStopBurst();
                        }
                        bool hasRemainingShots = this.cachedBurstShotsLeft >= 1;
                        if (hasRemainingShots)
                        {
                            Stance currentStance = this.Verb.CasterPawn.stances.curStance;
                            bool isCooldownOrWarmup = currentStance is Stance_Cooldown || currentStance is Stance_Warmup;
                            if (isCooldownOrWarmup)
                            {
                                Stance_Busy busyStance = currentStance as Stance_Busy;
                                bool isSustainedShootVerb = busyStance.verb is Verb_ShootSustained;
                                if (isSustainedShootVerb)
                                {
                                    busyStance.ticksLeft = 0;
                                }
                            }
                        }
                    }
                    bool hasNoRemainingShots = this.cachedBurstShotsLeft < 1;
                    if (hasNoRemainingShots)
                    {
                        this.isActive = false;
                    }
                }
            }
        }
        public void VerbReset()
        {
            this.Verb.Reset();
            this.Verb.CasterPawn.stances.CancelBusyStanceSoft();
            this.cachedBurstShotsLeft = 0;
            this.isActive = false;
            this.idelTicks = 0;
        }
        public void ForceStopBurst()
        {
            this.VerbReset();
            Pawn caster = this.CasterPawn;
            if (caster != null)
            {
                Pawn_StanceTracker stances = caster.stances;
                if (stances != null)
                {
                    stances.SetStance(new Stance_Cooldown(this.Verb.verbProps.AdjustedCooldownTicks(this.Verb, this.CasterPawn), this.Verb.CurrentTarget, this.Verb));
                }
            }
        }
        public void ResetCached()
        {
            this.cachedBurstShotsLeft = 0;
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look<int>(ref this.idelTicks, "idelTicks", 0, false);
            Scribe_Values.Look<bool>(ref this.isActive, "isActive", false, false);
            Scribe_Values.Look<int>(ref this.cachedBurstShotsLeft, "cachedBurstShotsLeft", 0, false);
            Scribe_Values.Look<int>(ref this.activeTickLeft, "activeTickLeft", 0, false);
        }

        public bool isActive;

        public int cachedBurstShotsLeft;

        public int activeTickLeft;

        public int idelTicks = 0;
    }

    public class Verb_ShootSustained : Verb_ShootWithOffset
    {
        public CompSustainedShoot CompSustainedShoot
        {
            get
            {
                return base.EquipmentSource.TryGetComp<CompSustainedShoot>();
            }
        }
        protected override int ShotsPerBurst
        {
            get
            {
                bool hasCachedShots = this.state == VerbState.Idle && this.CompSustainedShoot.cachedBurstShotsLeft >= 1;
                int result;
                if (hasCachedShots)
                {
                    result = this.CompSustainedShoot.cachedBurstShotsLeft;
                }
                else
                {
                    result = base.BurstShotCount;
                }
                return result;
            }
        }
        public int BurstShotsLeft
        {
            get
            {
                return this.burstShotsLeft;
            }
        }
        public override void OrderForceTarget(LocalTargetInfo target)
        {
            this.forceTargetedDownedPawn = null;
            base.OrderForceTarget(target);
            if (target.Pawn != null && target.Pawn.Downed && target.Pawn.Spawned)
            {
                this.forceTargetedDownedPawn = target.Pawn;
            }
            this.currentTarget = target;
        }

        public override void WarmupComplete()
        {
            this.burstShotsLeft = this.ShotsPerBurst;
            this.state = VerbState.Bursting;
            base.TryCastNextBurstShot();
            Pawn pawn = this.currentTarget.Thing as Pawn;
            if (pawn != null && !pawn.Downed && !pawn.IsColonyMech && this.CasterIsPawn && this.CasterPawn.skills != null && this.burstShotsLeft == base.BurstShotCount)
            {
                float baseExperience = pawn.HostileTo(this.caster) ? 170f : 20f;
                float cycleTime = this.verbProps.AdjustedFullCycleTime(this, this.CasterPawn);
                this.CasterPawn.skills.Learn(SkillDefOf.Shooting, baseExperience * cycleTime, false, false);
            }
        }
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look<Pawn>(ref this.forceTargetedDownedPawn, "forceTargetedDownedPawn", false);
        }
        public Pawn forceTargetedDownedPawn;
    }
}