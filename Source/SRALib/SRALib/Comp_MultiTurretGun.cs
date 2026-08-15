using System;
using System.Collections.Generic;
using Verse;
using Verse.AI;
using RimWorld;
using UnityEngine;

namespace SRA
{
    public class CompProperties_MultiTurretGun : CompProperties_TurretGun
    {
        public int ID;
        public CompProperties_MultiTurretGun()
        {
            compClass = typeof(Comp_MultiTurretGun);
        }
    }
    public class Comp_MultiTurretGun : CompTurretGun, ISustainedShootTurretDriver
    {
        private bool fireAtWill = true;
        public new CompProperties_MultiTurretGun Props => (CompProperties_MultiTurretGun)props;

        public LocalTargetInfo SustainedShootCurrentTarget => currentTarget;

        public override void CompTick()
        {
            CompSustainedShoot compSustainedShoot = GetSustainedShootComp();
            bool sustainedShootStarted;
            bool sustainedShootActive = TickSustainedShootComp(compSustainedShoot, out sustainedShootStarted);
            if (!sustainedShootStarted)
            {
                base.CompTick();
                sustainedShootActive = TickSustainedShootComp(compSustainedShoot, out sustainedShootStarted);
            }

            if (!sustainedShootActive && !sustainedShootStarted && !currentTarget.IsValid && burstCooldownTicksLeft <= 0)
            {
                // 在其他情况下没有目标且冷却结束时也回正
                curRotation = parent.Rotation.AsAngle + Props.angleOffset;
            }
        }

        private CompSustainedShoot GetSustainedShootComp()
        {
            if (!(AttackVerb is Verb_ShootWithOffset))
            {
                return null;
            }

            return gun?.TryGetComp<CompSustainedShoot>();
        }

        private bool TickSustainedShootComp(CompSustainedShoot compSustainedShoot, out bool startedCast)
        {
            startedCast = false;
            if (compSustainedShoot == null)
            {
                return false;
            }

            return compSustainedShoot.TickSustainedShootForTurret((ISustainedShootTurretDriver)this, out startedCast);
        }

        public LocalTargetInfo TryFindSustainedShootTarget()
        {
            if (!fireAtWill)
            {
                return LocalTargetInfo.Invalid;
            }

            return (Thing)AttackTargetFinder.BestShootTargetFromCurrentPosition(this, TargetScanFlags.NeedThreat | TargetScanFlags.NeedAutoTargetable);
        }

        public void ClearSustainedShootDelay()
        {
            burstWarmupTicksLeft = 0;
            burstCooldownTicksLeft = 0;
        }

        public void PrepareSustainedShootTarget(LocalTargetInfo target)
        {
            ClearSustainedShootDelay();
            currentTarget = target;
            if (target.IsValid)
            {
                curRotation = (target.Cell.ToVector3Shifted() - parent.DrawPos).AngleFlat() + Props.angleOffset;
            }
        }

        public bool CanStartSustainedShoot(LocalTargetInfo target)
        {
            return true;
        }

        public void Notify_SustainedShootStarted(LocalTargetInfo target)
        {
        }

        private void MakeGun()
        {
            gun = ThingMaker.MakeThing(Props.turretDef);
            UpdateGunVerbs();
        }
        private void UpdateGunVerbs()
        {
            List<Verb> allVerbs = gun.TryGetComp<CompEquippable>().AllVerbs;
            for (int i = 0; i < allVerbs.Count; i++)
            {
                Verb verb = allVerbs[i];
                verb.caster = parent;
                verb.castCompleteCallback = delegate
                {
                    burstCooldownTicksLeft = AttackVerb.verbProps.defaultCooldownTime.SecondsToTicks();
                };
            }
        }
        public override void PostExposeData()
        {
            Scribe_Values.Look(ref burstCooldownTicksLeft, "burstCooldownTicksLeft", 0);
            Scribe_Values.Look(ref burstWarmupTicksLeft, "burstWarmupTicksLeft", 0);
            Scribe_TargetInfo.Look(ref currentTarget, "currentTarget_" + Props.ID);
            Scribe_Deep.Look(ref gun, "gun_" + Props.ID);
            Scribe_Values.Look(ref fireAtWill, "fireAtWill", defaultValue: true);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (gun == null)
                {
                    Log.Error("CompTurrentGun had null gun after loading. Recreating.");
                    MakeGun();
                }
                else
                {
                    UpdateGunVerbs();
                }
            }
        }
    }

}
