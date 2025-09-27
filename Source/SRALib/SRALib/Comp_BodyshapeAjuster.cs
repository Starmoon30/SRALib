using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
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
        public CompProperties_BodyShapeAjuster Props
        {
            get
            {
                return (CompProperties_BodyShapeAjuster)this.props;
            }
        }

        public override void Notify_Equipped(Pawn pawn)
        {
            base.Notify_Equipped(pawn);
            bool flag = pawn.story.bodyType == BodyTypeDefOf.Hulk || pawn.story.bodyType == BodyTypeDefOf.Fat;
            if (flag)
            {
                this.BodyShape = pawn.story.bodyType;
                this.ChangedBS = true;
                bool flag2 = pawn.gender == Gender.Male;
                if (flag2)
                {
                    pawn.story.bodyType = BodyTypeDefOf.Male;
                }
                else
                {
                    pawn.story.bodyType = BodyTypeDefOf.Female;
                }
            }
        }

        public override void Notify_Unequipped(Pawn pawn)
        {
            base.Notify_Unequipped(pawn);
            bool changedBS = this.ChangedBS;
            if (changedBS)
            {
                pawn.story.bodyType = this.BodyShape;
                this.ChangedBS = false;
            }
        }

        public void ExposeData()
        {
            Scribe_Values.Look<BodyTypeDef>(ref this.BodyShape, "original bodytype", BodyTypeDefOf.Thin, false);
            Scribe_Values.Look<bool>(ref this.ChangedBS, "if bodytype changed", false, false);
        }

        private BodyTypeDef BodyShape;

        private bool ChangedBS = false;
    }
}
