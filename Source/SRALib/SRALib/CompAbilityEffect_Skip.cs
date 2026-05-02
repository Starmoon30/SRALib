using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using Verse.Sound;
using RimWorld;

namespace SRA
{
    public class CompAbilityEffect_Skip : CompAbilityEffect
    {
        // Token: 0x17001D74 RID: 7540
        // (get) Token: 0x0600C0A0 RID: 49312 RVA: 0x0037C6C7 File Offset: 0x0037A8C7
        public new CompProperties_AbilityEffect Props
        {
            get
            {
                return (CompProperties_AbilityEffect)this.props;
            }
        }

        // Token: 0x0600C0A2 RID: 49314 RVA: 0x0037C6E4 File Offset: 0x0037A8E4
        public override void Apply(LocalTargetInfo target, LocalTargetInfo dest)
        {
            Pawn caster = parent.pawn;
            base.Apply(target, dest);
            if (target.IsValid)
            {
                if (!this.parent.def.HasAreaOfEffect)
                {
                    this.parent.AddEffecterToMaintain(EffecterDefOf.Skip_Entry.Spawn(caster, caster.Map, 1f), caster.Position, 60, null);
                }
                else
                {
                    this.parent.AddEffecterToMaintain(EffecterDefOf.Skip_EntryNoDelay.Spawn(caster, caster.Map, 1f), caster.Position, 60, null);
                }
                this.parent.AddEffecterToMaintain(EffecterDefOf.Skip_ExitNoDelay.Spawn(target.Cell, caster.Map, 1f), target.Cell, 60, null);
                CompCanBeDormant compCanBeDormant = target.Thing.TryGetComp<CompCanBeDormant>();
                if (compCanBeDormant != null)
                {
                    compCanBeDormant.WakeUp();
                }
                caster.Position = target.Cell;
                if (caster != null)
                {
                    if ((caster.Faction == Faction.OfPlayer || caster.IsPlayerControlled) && caster.Position.Fogged(caster.Map))
                    {
                        FloodFillerFog.FloodUnfog(caster.Position, caster.Map);
                    }
                    caster.Notify_Teleported(true, true);
                    CompAbilityEffect_Skip.SendSkipUsedSignal(caster.Position, caster);
                }
            }
        }
        // Token: 0x0600C0A4 RID: 49316 RVA: 0x0037C928 File Offset: 0x0037AB28
        public override bool Valid(LocalTargetInfo target, bool showMessages = true)
        {
            return base.Valid(target, showMessages);
        }
        // Token: 0x0600C0A7 RID: 49319 RVA: 0x0037CA34 File Offset: 0x0037AC34
        public static void SendSkipUsedSignal(LocalTargetInfo target, Thing initiator)
        {
            Find.SignalManager.SendSignal(new Signal(CompAbilityEffect_Skip.SkipUsedSignalTag, target.Named("POSITION"), initiator.Named("SUBJECT")));
        }

        // Token: 0x040083DE RID: 33758
        public static string SkipUsedSignalTag = "CompAbilityEffect.SkipUsed";
    }
}
