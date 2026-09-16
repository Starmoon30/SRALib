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
            // 先停止计时，避免 Destroy 触发的后续生命周期回调中重复执行。
            activated = false;

            // WarUnitSpawner 生成的限时单位需要真正消失而非留下尸体。Pawn.Kill 会进入
            // 常规死亡流程，可能被其他死亡拦截机制处理；Vanish 则直接移除 Pawn 及其携带物。
            if (Pawn != null && !Pawn.Destroyed)
            {
                Pawn.Destroy(DestroyMode.Vanish);
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
