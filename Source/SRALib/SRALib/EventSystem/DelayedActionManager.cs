using System;
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace SRA
{
    public class DelayedActionManager : WorldComponent
    {
        // Nested class must be public to be accessible for serialization
        public class DelayedAction : IExposable
        {
            public int TicksRemaining;
            public string eventDefName;

            // Parameterless constructor for Scribe
            public DelayedAction() { }

            public DelayedAction(string eventDefName, int ticks)
            {
                this.eventDefName = eventDefName;
                this.TicksRemaining = ticks;
            }

            public void ExposeData()
            {
                Scribe_Values.Look(ref TicksRemaining, "ticksRemaining", 0);
                Scribe_Values.Look(ref eventDefName, "eventDefName");
            }
        }

        private List<DelayedAction> actions = new List<DelayedAction>();

        public DelayedActionManager(World world) : base(world)
        {
        }

        public void AddAction(string eventDefName, int delayTicks)
        {
            if (string.IsNullOrEmpty(eventDefName) || delayTicks <= 0)
            {
                return;
            }
            actions.Add(new DelayedAction(eventDefName, delayTicks));
        }

        public override void WorldComponentTick()
        {
            base.WorldComponentTick();
            for (int i = actions.Count - 1; i >= 0; i--)
            {
                DelayedAction delayedAction = actions[i];
                delayedAction.TicksRemaining--;
                if (delayedAction.TicksRemaining <= 0)
                {
                    try
                    {
                        ExecuteAction(delayedAction.eventDefName);
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"[SRA] Error executing delayed action for event '{delayedAction.eventDefName}': {ex}");
                    }
                    actions.RemoveAt(i);
                }
            }
        }

        private void ExecuteAction(string defName)
        {
            EventDef nextDef = DefDatabase<EventDef>.GetNamed(defName, false);
            if (nextDef != null)
            {
                // This logic is simplified from Effect_OpenCustomUI.OpenUI
                // It assumes delayed actions always open a new dialog.
                Find.WindowStack.Add((Window)Activator.CreateInstance(nextDef.windowType, nextDef));
            }
            else
            {
                Log.Error($"[SRA] DelayedActionManager could not find EventDef named '{defName}'");
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref actions, "delayedActions", LookMode.Deep);
            if (actions == null)
            {
                actions = new List<DelayedAction>();
            }
        }
    }
}