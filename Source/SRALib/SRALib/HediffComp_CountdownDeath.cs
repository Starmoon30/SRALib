using RimWorld;
using System.Collections.Generic;
using Verse;

namespace SRA
{
    public class Hediff_CountdownDeath : HediffWithComps
    {
        public HediffComp_CountdownTimer TimerComp => this.TryGetComp<HediffComp_CountdownTimer>();

        public override string TipStringExtra
        {
            get
            {
                string tip = base.TipStringExtra;
                if (TimerComp != null)
                {
                    tip += $"SRAExecuteDeathTip".Translate() + TimerComp.TimeRemainingString;
                }
                return tip;
            }
        }
    }

    public class HediffCompProperties_CountdownTimer : HediffCompProperties
    {
        public int countdownDuration = 60000; // 默认1000秒(以tick为单位)

        public HediffCompProperties_CountdownTimer()
        {
            this.compClass = typeof(HediffComp_CountdownTimer);
        }
    }

    public class HediffComp_CountdownTimer : HediffComp
    {
        private int ticksRemaining;
        private bool activated = false;

        public string TimeRemainingString => ticksRemaining.ToStringTicksToPeriod();
        public override void CompPostMake()
        {
            base.CompPostMake();
            ticksRemaining = Props.countdownDuration;
            activated = true;
        }

        public override void CompPostTick(ref float severityAdjustment)
        {
            base.CompPostTick(ref severityAdjustment);

            if (!activated) return;

            ticksRemaining--;

            if (ticksRemaining <= 0)
            {
                ExecuteDeath();
            }
        }

        public void ExecuteDeath()
        {
            // 立即杀死携带者
            if (Pawn != null && !Pawn.Dead)
            {
                Pawn.Kill(null, null);
            }
        }

        public override IEnumerable<Gizmo> CompGetGizmos()
        {
            if (Pawn != null && Pawn.Faction.IsPlayer)
            {
                yield return new Command_Action
                {
                    defaultLabel = "SRAExecuteDeathLabel".Translate(),
                    defaultDesc = "SRAExecuteDeathDesc".Translate(),
                    icon = TexCommand.DesirePower,
                    action = () => ExecuteDeath()
                };
            }
        }

        public override void CompExposeData()
        {
            base.CompExposeData();
            Scribe_Values.Look(ref ticksRemaining, "ticksRemaining", Props.countdownDuration);
            Scribe_Values.Look(ref activated, "activated", false);
        }

        private HediffCompProperties_CountdownTimer Props => (HediffCompProperties_CountdownTimer)this.props;
    }
}