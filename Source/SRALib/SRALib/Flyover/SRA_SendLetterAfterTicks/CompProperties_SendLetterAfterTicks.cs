using RimWorld;
using Verse;

namespace SRA
{
    public class CompProperties_SendLetterAfterTicks : CompProperties
    {
        public int ticksDelay = 600; // 默认10秒 (60 ticks/秒)
        public string letterLabel;
        public string letterText;
        public LetterDef letterDef = LetterDefOf.NeutralEvent;
        public bool onlySendOnce = true;
        public bool requireOnMap = true;
        public bool destroyAfterSending = false;

        public CompProperties_SendLetterAfterTicks()
        {
            compClass = typeof(CompSendLetterAfterTicks);
        }
    }
}
