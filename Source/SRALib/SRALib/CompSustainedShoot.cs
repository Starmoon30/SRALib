using System;
using RimWorld;
using Verse;
using Verse.AI;

namespace SRA
{
    public interface ISustainedShootTurretDriver
    {
        LocalTargetInfo SustainedShootCurrentTarget { get; }

        LocalTargetInfo TryFindSustainedShootTarget();

        void ClearSustainedShootDelay();

        void PrepareSustainedShootTarget(LocalTargetInfo target);

        bool CanStartSustainedShoot(LocalTargetInfo target);

        void Notify_SustainedShootStarted(LocalTargetInfo target);
    }

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
                return this.parent?.TryGetComp<CompEquippable>();
            }
        }
        public Verb_ShootWithOffset Verb
        {
            get
            {
                return this.CompEquippable?.PrimaryVerb as Verb_ShootWithOffset;
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
            bool startedCast;
            this.TickSustainedShoot(LocalTargetInfo.Invalid, false, null, out startedCast);
        }

        public bool TickSustainedShootForTurret(ISustainedShootTurretDriver turretDriver, out bool startedCast)
        {
            // The driver gives this gun comp permission to update the outer turret state.
            LocalTargetInfo fallbackTarget = turretDriver?.SustainedShootCurrentTarget ?? LocalTargetInfo.Invalid;
            return this.TickSustainedShoot(fallbackTarget, true, turretDriver, out startedCast);
        }

        public void Notify_SustainedVerbStarted()
        {
            this.isActive = true;
            this.idelTicks = 0;
        }

        public void Notify_SustainedBurstProgress(int remainingShots)
        {
            this.cachedBurstShotsLeft = Math.Max(0, remainingShots);
            if (this.cachedBurstShotsLeft < 1)
            {
                this.isActive = false;
                this.idelTicks = 0;
            }
        }

        private bool TickSustainedShoot(LocalTargetInfo fallbackTarget, bool forceTurretDriver, ISustainedShootTurretDriver turretDriver, out bool startedCast)
        {
            startedCast = false;
            int currentTick = Find.TickManager.TicksGame;
            bool alreadyProcessedThisTick = this.lastSustainedShootTick == currentTick;
            if (alreadyProcessedThisTick && !forceTurretDriver)
            {
                return this.isActive;
            }

            bool countIdleTick = !alreadyProcessedThisTick;
            this.lastSustainedShootTick = currentTick;

            Verb_ShootWithOffset verb = this.Verb;
            if (verb == null)
            {
                this.ResetCached();
                this.isActive = false;
                return false;
            }

            if (verb.state == VerbState.Bursting)
            {
                this.isActive = true;
            }

            if (!this.isActive)
            {
                return false;
            }

            if (!forceTurretDriver && this.UsesPawnStanceDriver(verb))
            {
                this.TickPawnCaster(verb);
            }
            else
            {
                this.TickTurretCaster(verb, fallbackTarget, countIdleTick, turretDriver, out startedCast);
            }

            return this.isActive;
        }

        private void TickPawnCaster(Verb_ShootWithOffset verb)
        {
            Pawn casterPawn = verb.CasterPawn;
            if (casterPawn == null)
            {
                bool startedCast;
                this.TickTurretCaster(verb, LocalTargetInfo.Invalid, true, null, out startedCast);
                return;
            }

            Job curJob = casterPawn.CurJob;
            bool shouldStopForJob = curJob != null && curJob.def != JobDefOf.AttackStatic && curJob.def != JobDefOf.Wait_Combat && curJob.def != JobDefOf.Wait_MaintainPosture;
            if (shouldStopForJob)
            {
                this.ForceStopBurst();
                return;
            }

            this.CacheBurstingVerbState(verb, false);

            if (verb.state == VerbState.Idle)
            {
                this.idelTicks++;
                if (this.idelTicks > verb.TicksBetweenBurstShots)
                {
                    this.ForceStopBurst();
                    return;
                }

                if (this.cachedBurstShotsLeft >= 1)
                {
                    this.ClearPawnSustainedStance(casterPawn);
                }
            }

            if (this.cachedBurstShotsLeft < 1)
            {
                this.isActive = false;
            }
        }

        private void TickTurretCaster(Verb_ShootWithOffset verb, LocalTargetInfo fallbackTarget, bool countIdleTick, ISustainedShootTurretDriver turretDriver, out bool startedCast)
        {
            startedCast = false;
            this.CacheBurstingVerbState(verb, true);

            if (this.cachedBurstShotsLeft >= 1)
            {
                // The weapon owns the sustained-burst state. While it still has
                // cached burst shots, keep removing the turret's outer warmup/cooldown.
                turretDriver?.ClearSustainedShootDelay();
            }

            if (verb.state != VerbState.Bursting)
            {
                if (this.cachedBurstShotsLeft < 1)
                {
                    this.isActive = false;
                    this.idelTicks = 0;
                    return;
                }
                TurretResumeResult resumeResult = this.TryResumeTurretBurst(verb, fallbackTarget, turretDriver);
                if (resumeResult == TurretResumeResult.Started)
                {
                    this.idelTicks = 0;
                    startedCast = true;
                    return;
                }

                if (resumeResult == TurretResumeResult.WaitingForReadyTarget)
                {
                    // Rotation-limited turrets can need longer than one burst interval
                    // to line up. Keep the cached shots alive while the driver aims.
                    this.idelTicks = 0;
                    return;
                }

                if (countIdleTick)
                {
                    this.idelTicks++;
                }

                if (this.idelTicks > verb.TicksBetweenBurstShots)
                {
                    this.ForceStopBurst();
                    return;
                }
            }
        }

        private void CacheBurstingVerbState(Verb_ShootWithOffset verb, bool preserveCastCompleteCallback)
        {
            if (verb.state != VerbState.Bursting)
            {
                return;
            }

            this.idelTicks = 0;
            this.cachedBurstShotsLeft = Math.Max(0, verb.BurstShotsLeft);
            if (this.ShouldInterruptBurstForTarget(verb, verb.CurrentTarget))
            {
                this.ResetVerb(verb, preserveCastCompleteCallback);
            }
        }

        private void ClearPawnSustainedStance(Pawn casterPawn)
        {
            Stance currentStance = casterPawn.stances?.curStance;
            bool isCooldownOrWarmup = currentStance is Stance_Cooldown || currentStance is Stance_Warmup;
            if (!isCooldownOrWarmup)
            {
                return;
            }

            Stance_Busy busyStance = currentStance as Stance_Busy;
            if (busyStance?.verb is Verb_ShootWithOffset)
            {
                busyStance.ticksLeft = 0;
            }
        }

        private TurretResumeResult TryResumeTurretBurst(Verb_ShootWithOffset verb, LocalTargetInfo fallbackTarget, ISustainedShootTurretDriver turretDriver)
        {
            LocalTargetInfo target = this.GetResumeTarget(verb, fallbackTarget, turretDriver);
            if (!this.IsValidResumeTarget(verb, target))
            {
                return TurretResumeResult.Failed;
            }

            turretDriver?.PrepareSustainedShootTarget(target);
            if (turretDriver != null && !turretDriver.CanStartSustainedShoot(target))
            {
                return TurretResumeResult.WaitingForReadyTarget;
            }

            // Keep the same mechanism as pawn sustained fire: clear the outer delay,
            // then let WarmupComplete read cachedBurstShotsLeft through ShotsPerBurst.
            bool started = verb.TryStartCastOn(target, LocalTargetInfo.Invalid, false, true, false, true);
            if (started)
            {
                turretDriver?.Notify_SustainedShootStarted(target);
            }

            return started ? TurretResumeResult.Started : TurretResumeResult.Failed;
        }

        private LocalTargetInfo GetResumeTarget(Verb_ShootWithOffset verb, LocalTargetInfo fallbackTarget, ISustainedShootTurretDriver turretDriver)
        {
            if (this.IsValidResumeTarget(verb, verb.CurrentTarget))
            {
                return verb.CurrentTarget;
            }

            if (this.IsValidResumeTarget(verb, fallbackTarget))
            {
                return fallbackTarget;
            }

            Building_Turret turret = verb.Caster as Building_Turret;
            if (turret != null && this.IsValidResumeTarget(verb, turret.CurrentTarget))
            {
                return turret.CurrentTarget;
            }

            LocalTargetInfo retargeted = turretDriver?.TryFindSustainedShootTarget() ?? LocalTargetInfo.Invalid;
            if (this.IsValidResumeTarget(verb, retargeted))
            {
                return retargeted;
            }

            return LocalTargetInfo.Invalid;
        }

        private bool IsValidResumeTarget(Verb_ShootWithOffset verb, LocalTargetInfo target)
        {
            return target.IsValid && !this.ShouldInterruptBurstForTarget(verb, target) && verb.CanHitTarget(target);
        }

        private bool ShouldInterruptBurstForTarget(Verb_ShootWithOffset verb, LocalTargetInfo target)
        {
            Pawn targetPawn = target.Pawn;
            if (targetPawn != null)
            {
                if (targetPawn.Dead || !targetPawn.Spawned)
                {
                    return true;
                }

                return targetPawn.Downed && targetPawn != verb.forceTargetedDownedPawn;
            }

            Thing targetThing = target.Thing;
            return targetThing != null && (targetThing.Destroyed || !targetThing.Spawned);
        }

        private bool UsesPawnStanceDriver(Verb_ShootWithOffset verb)
        {
            Pawn casterPawn = verb.CasterPawn;
            if (casterPawn?.equipment == null)
            {
                return false;
            }

            // Only actually equipped weapons should use the pawn job/stance recovery path.
            return casterPawn.equipment.AllEquipmentListForReading.Contains(this.parent);
        }

        private void ResetVerb(Verb_ShootWithOffset verb, bool preserveCastCompleteCallback)
        {
            if (preserveCastCompleteCallback)
            {
                verb.ResetPreservingCastCompleteCallback();
            }
            else
            {
                verb.Reset();
            }
        }
        public void VerbReset()
        {
            Verb_ShootWithOffset verb = this.Verb;
            if (verb == null)
            {
                this.ResetCached();
                this.isActive = false;
                this.idelTicks = 0;
                return;
            }

            bool usesPawnStanceDriver = this.UsesPawnStanceDriver(verb);
            this.ResetVerb(verb, !usesPawnStanceDriver);
            Pawn pawn = verb.CasterPawn;
            if (usesPawnStanceDriver)
            {
                pawn?.stances?.CancelBusyStanceSoft();
            }

            this.cachedBurstShotsLeft = 0;
            this.isActive = false;
            this.idelTicks = 0;
        }
        public void ForceStopBurst()
        {
            Verb_ShootWithOffset verb = this.Verb;
            if (verb == null)
            {
                this.VerbReset();
                return;
            }

            bool usesPawnStanceDriver = this.UsesPawnStanceDriver(verb);
            LocalTargetInfo currentTarget = verb.CurrentTarget;
            Action castCompleteCallback = usesPawnStanceDriver ? null : verb.castCompleteCallback;
            this.ResetVerb(verb, !usesPawnStanceDriver);
            this.cachedBurstShotsLeft = 0;
            this.isActive = false;
            this.idelTicks = 0;

            Pawn caster = verb.CasterPawn;
            if (usesPawnStanceDriver && caster != null)
            {
                Pawn_StanceTracker stances = caster.stances;
                if (stances != null)
                {
                    stances.SetStance(new Stance_Cooldown(verb.verbProps.AdjustedCooldownTicks(verb, caster), currentTarget, verb));
                }
            }
            else
            {
                castCompleteCallback?.Invoke();
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
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                this.cachedBurstShotsLeft = Math.Max(0, this.cachedBurstShotsLeft);
                this.idelTicks = Math.Max(0, this.idelTicks);
                this.isActive = this.isActive || this.cachedBurstShotsLeft > 0;
                this.lastSustainedShootTick = -1;
            }
        }

        public bool isActive;

        public int cachedBurstShotsLeft;

        public int idelTicks = 0;

        private int lastSustainedShootTick = -1;

        private enum TurretResumeResult
        {
            Failed,
            WaitingForReadyTarget,
            Started
        }
    }

    public class Verb_ShootSustained : Verb_ShootWithOffset
    {
    }
}
