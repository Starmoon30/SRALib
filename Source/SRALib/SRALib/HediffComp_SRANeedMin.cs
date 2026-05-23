using Verse;

namespace SRA
{
    public class HediffCompProperties_SRANeedMin : HediffCompProperties
    {
        public HediffCompProperties_SRANeedMin()
        {
            compClass = typeof(HediffComp_SRANeedMin);
        }
    }

    public class HediffComp_SRANeedMin : HediffComp
    {
        private const float MinimumNeedLevel = 0.05f;

        public override void CompPostTick(ref float severityAdjustment)
        {
            if (Pawn == null || Pawn.Dead || Pawn.needs?.AllNeeds == null)
            {
                return;
            }

            for (int i = 0; i < Pawn.needs.AllNeeds.Count; i++)
            {
                var need = Pawn.needs.AllNeeds[i];
                if (need != null && need.CurLevel < MinimumNeedLevel)
                {
                    need.CurLevel = MinimumNeedLevel;
                }
            }
        }

        public override string CompTipStringExtra => "SRA_NeedMinTipExtra".Translate();
    }
}
