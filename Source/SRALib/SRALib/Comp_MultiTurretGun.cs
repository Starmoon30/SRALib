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
    public class Comp_MultiTurretGun : CompTurretGun
    {
        private bool fireAtWill = true;
        public new CompProperties_MultiTurretGun Props => (CompProperties_MultiTurretGun)props;
        public override void CompTick()
        {
            base.CompTick();
            if (!currentTarget.IsValid && burstCooldownTicksLeft <= 0)
            {
                // 在其他情况下没有目标且冷却结束时也回正
                curRotation = parent.Rotation.AsAngle + Props.angleOffset;
            }
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
