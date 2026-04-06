using System; // Required for Activator
using System.Collections.Generic;
using LudeonTK;
using Verse;
using RimWorld;

namespace SRA
{
    public static class SRADebugActions
    {
        [DebugAction("SRA", "Open Custom UI...", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.Playing)]
        private static void OpenCustomUI()
        {
            List<DebugMenuOption> list = new List<DebugMenuOption>();
            foreach (EventDef localDef in DefDatabase<EventDef>.AllDefs)
            {
                EventDef currentDef = localDef;
                list.Add(new DebugMenuOption(currentDef.defName, DebugMenuOptionMode.Action, delegate
                {
                    if (currentDef.hiddenWindow)
                    {
                        if (!currentDef.dismissEffects.NullOrEmpty())
                        {
                            foreach (var conditionalEffect in currentDef.dismissEffects)
                            {
                                string reason;
                                bool conditionsMet = true;
                                if (!conditionalEffect.conditions.NullOrEmpty())
                                {
                                    foreach (var condition in conditionalEffect.conditions)
                                    {
                                        if (!condition.IsMet(out reason))
                                        {
                                            conditionsMet = false;
                                            break;
                                        }
                                    }
                                }

                                if (conditionsMet)
                                {
                                    conditionalEffect.Execute(null);
                                }
                            }
                        }
                    }
                    else
                    {
                        Find.WindowStack.Add((Window)Activator.CreateInstance(currentDef.windowType, currentDef));
                    }
                }));
            }
            Find.WindowStack.Add(new Dialog_DebugOptionListLister(list));
        }
    }

    public static class SRADebugActionsVariables
    {
        [DebugAction("SRA", "Manage Event Variables", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void ManageEventVariables()
        {
            Find.WindowStack.Add(new Dialog_ManageEventVariables());
        }
    }
}