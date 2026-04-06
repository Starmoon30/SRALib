using RimWorld;
using Verse;

namespace SRA
{
    public class CompSendLetterAfterTicks : ThingComp
    {
        public CompProperties_SendLetterAfterTicks Props => (CompProperties_SendLetterAfterTicks)props;

        private int ticksPassed = 0;
        private bool letterSent = false;

        public override void CompTick()
        {
            base.CompTick();

            // 如果已经发送过且只发送一次，则跳过
            if (letterSent && Props.onlySendOnce)
                return;

            // 如果需要在地图上但父物体不在有效地图上，则跳过
            if (Props.requireOnMap && (parent.Map == null || !parent.Spawned))
                return;

            ticksPassed++;

            // 检查是否达到延迟时间
            if (ticksPassed >= Props.ticksDelay)
            {
                SendLetter();
                
                if (Props.destroyAfterSending)
                {
                    parent.Destroy();
                }
            }
        }

        private void SendLetter()
        {
            try
            {
                // 检查是否有有效的信件内容
                if (Props.letterLabel.NullOrEmpty() && Props.letterText.NullOrEmpty())
                {
                    SRALog.Debug($"CompSendLetterAfterTicks: No letter content defined for {parent.def.defName}");
                    return;
                }

                string label = Props.letterLabel ?? "DefaultLetterLabel".Translate();
                string text = Props.letterText ?? "DefaultLetterText".Translate();

                // 创建信件
                Letter letter = LetterMaker.MakeLetter(
                    label,
                    text,
                    Props.letterDef,
                    lookTargets: new LookTargets(parent)
                );

                // 发送信件
                Find.LetterStack.ReceiveLetter(letter);

                letterSent = true;

                SRALog.Debug($"Letter sent from {parent.def.defName} after {ticksPassed} ticks");
            }
            catch (System.Exception ex)
            {
                SRALog.Debug($"Error sending letter from {parent.def.defName}: {ex}");
            }
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref ticksPassed, "ticksPassed", 0);
            Scribe_Values.Look(ref letterSent, "letterSent", false);
        }

        public override string CompInspectStringExtra()
        {
            if (!letterSent && Props.requireOnMap && parent.Spawned)
            {
                int ticksRemaining = Props.ticksDelay - ticksPassed;
                if (ticksRemaining > 0)
                {
                    return $"LetterInspection_TimeRemaining".Translate(ticksRemaining.ToStringTicksToPeriod());
                }
            }
            return base.CompInspectStringExtra();
        }
    }
}
