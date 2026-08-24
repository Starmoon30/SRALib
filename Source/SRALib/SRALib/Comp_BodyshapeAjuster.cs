using RimWorld;
using Verse;

namespace SRA
{
    public class CompProperties_BodyShapeAjuster : CompProperties
    {
        public CompProperties_BodyShapeAjuster()
        {
            this.compClass = typeof(Comp_BodyshapeAjuster);
        }
    }
    public class Comp_BodyshapeAjuster : ThingComp
    {
        private BodyTypeDef originalBodyType;
        private bool bodyTypeChanged;

        public override void Notify_Equipped(Pawn pawn)
        {
            base.Notify_Equipped(pawn);
            if (pawn.story != null && pawn.story.bodyType != BodyTypeDefOf.Thin)
            {
                originalBodyType = pawn.story.bodyType;
                bodyTypeChanged = true;
                pawn.story.bodyType = BodyTypeDefOf.Thin;
            }
        }

        public override void Notify_Unequipped(Pawn pawn)
        {
            base.Notify_Unequipped(pawn);
            if (bodyTypeChanged && originalBodyType != null)
            {
                pawn.story.bodyType = originalBodyType;
                bodyTypeChanged = false;
                originalBodyType = null;
            }
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Defs.Look(ref originalBodyType, "originalBodyType");
            Scribe_Values.Look(ref bodyTypeChanged, "bodyTypeChanged", false);
        }
    }
}