using RimWorld;
using RimWorld.BaseGen;
using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace SRA
{
    public class HediffCompProperties_SRANeedMin : HediffCompProperties
    {
        // Token: 0x060000DC RID: 220 RVA: 0x00005D44 File Offset: 0x00003F44
        public HediffCompProperties_SRANeedMin()
        {
            this.compClass = typeof(HediffComp_SRANeedMin);
        }

    }
    public class HediffComp_SRANeedMin : HediffComp
    {
        public override void CompPostTick(ref float severityAdjustment)
        {
            if (!Pawn.Spawned || Pawn.Dead) return;
            if (Pawn.needs.food.CurLevel < 0.05f) {
                Pawn.needs.food.CurLevel = 0.05f;
            }
            if (Pawn.needs.joy.CurLevel < 0.05f)
            {
                Pawn.needs.joy.CurLevel = 0.05f;
            }
            if (Pawn.needs.rest.CurLevel < 0.05f)
            {
                Pawn.needs.rest.CurLevel = 0.05f;
            }
        }

        public override string CompTipStringExtra
        {
            get
            {
                return "SRA_RegenTipExtra".Translate();
            }
        }

    }
}
