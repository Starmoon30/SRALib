using System; // Required for Activator
using RimWorld;
using Verse;
using System.Collections.Generic;
using Verse.AI;

namespace SRA
{
    public class CompProperties_OpenCustomUI : CompProperties
    {
        public string uiDefName;
        public string label; // The text to display in the float menu
        public string failReason; // Optional: Custom text to show if the pawn can't reach the building

        public CompProperties_OpenCustomUI()
        {
            this.compClass = typeof(CompOpenCustomUI);
        }
    }

    public class CompOpenCustomUI : ThingComp
    {
        public CompProperties_OpenCustomUI Props => (CompProperties_OpenCustomUI)this.props;

        public override IEnumerable<FloatMenuOption> CompFloatMenuOptions(Pawn selPawn)
        {
            // Check if the pawn can interact with the building
            if (!selPawn.CanReserveAndReach(this.parent, PathEndMode.InteractionCell, Danger.Deadly))
            {
                string reason = Props.failReason ?? "CannotUseNoPath".Translate();
                yield return new FloatMenuOption(reason, null);
                yield break;
            }

            // Check for power if the building has a power component
            CompPowerTrader powerComp = this.parent.GetComp<CompPowerTrader>();
            if (powerComp != null && !powerComp.PowerOn)
            {
                yield return new FloatMenuOption("CannotUseNoPower".Translate(), null);
                yield break;
            }

            string label = Props.label ?? "Open Custom UI"; // Use default label if not provided
            
            FloatMenuOption option = new FloatMenuOption(label, delegate()
            {
                EventDef uiDef = DefDatabase<EventDef>.GetNamed(Props.uiDefName, false);
                if (uiDef != null)
                {
                    Find.WindowStack.Add((Window)Activator.CreateInstance(uiDef.windowType, uiDef));
                }
                else
                {
                    Log.Error($"[CompOpenCustomUI] Could not find EventDef named '{Props.uiDefName}'.");
                }
            });

            yield return option;
        }
    }
}
