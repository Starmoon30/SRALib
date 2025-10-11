using System;
using RimWorld;
using Verse;

namespace SRA
{
    public class Projectile_BeamWithEffect_Extension : DefModExtension
    {
        public EffecterDef impactEffecter;
    }
    public class Projectile_BeamWithEffect : Beam
    {
        public Projectile_BeamWithEffect_Extension Props
        {
            get
            {
                return this.def.GetModExtension<Projectile_BeamWithEffect_Extension>();
            }
        }
        protected override void Impact(Thing hitThing, bool blockedByShield = false)
        {
            base.Impact(hitThing, blockedByShield);
            if (this.Props.impactEffecter != null)
            {
                this.Props.impactEffecter.Spawn().Trigger(new TargetInfo(this.ExactPosition.ToIntVec3(), this.launcher.Map, false), this.launcher, -1);
            }
        }
    }
}
