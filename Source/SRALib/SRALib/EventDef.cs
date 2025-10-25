using System; // Add this line
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace SRA
{
    public enum DescriptionSelectionMode
    {
        Random,
        Sequential
    }

    public class EventDef : Def
    {
        public string portraitPath;
        public string characterName;
        
        // New system: list of descriptions
        public List<string> descriptions;
        public DescriptionSelectionMode descriptionMode = DescriptionSelectionMode.Random;
        public bool hiddenWindow = false;

        // Backwards compatibility: old single description field
        public new string description = null;

        public Vector2 windowSize = Vector2.zero;

        public Type windowType = typeof(Dialog_CustomDisplay); // 默认窗口类型
        public List<EventOption> options;
        public string backgroundImagePath;
        public List<ConditionalEffects> immediateEffects;
        public List<ConditionalEffects> dismissEffects;
        public List<ConditionalDescription> conditionalDescriptions;
        public EventUIConfigDef eventUIConfig;
        public override void PostLoad()
        {
            base.PostLoad();
            // If the old description field is used, move its value to the new list for processing.
            if (!description.NullOrEmpty())
            {
                if (descriptions.NullOrEmpty())
                {
                    descriptions = new List<string>();
                }
                descriptions.Insert(0, description);
                description = null; // Clear the old field to prevent confusion
            }
            // If hiddenWindow is true, merge immediateEffects into dismissEffects at load time.
            if (hiddenWindow && !immediateEffects.NullOrEmpty())
            {
                if (dismissEffects.NullOrEmpty())
                {
                    dismissEffects = new List<ConditionalEffects>();
                }
                dismissEffects.AddRange(immediateEffects);
                immediateEffects = null; // Clear to prevent double execution
            }
        }
    }

    public class EventOption
    {
        public string label;
        public List<ConditionalEffects> optionEffects;
        public List<Condition> conditions;
        public string disabledReason;
        public bool hideWhenDisabled = false;
    }

    public class LoopEffects
    {
        public int count = 1;
        public string countVariableName;
        public List<Effect> effects;
    }

    public class ConditionalEffects
    {
        public List<Condition> conditions;
        public List<Effect> effects;
        public List<Effect> randomlistEffects;
        public List<LoopEffects> loopEffects;

        public void Execute(Window dialog)
        {
            // Execute all standard effects
            if (!effects.NullOrEmpty())
            {
                foreach (var effect in effects)
                {
                    effect.Execute(dialog);
                }
            }

            // Execute one random effect from the random list
            if (!randomlistEffects.NullOrEmpty())
            {
                float totalWeight = randomlistEffects.Sum(e => e.weight);
                float randomPoint = Rand.Value * totalWeight;

                foreach (var effect in randomlistEffects)
                {
                    if (randomPoint < effect.weight)
                    {
                        effect.Execute(dialog);
                        break;
                    }
                    randomPoint -= effect.weight;
                }
            }

            // Execute looped effects
            if (!loopEffects.NullOrEmpty())
            {
                var eventVarManager = Find.World.GetComponent<EventVariableManager>();
                foreach (var loop in loopEffects)
                {
                    int loopCount = loop.count;
                    if (!loop.countVariableName.NullOrEmpty() && eventVarManager.HasVariable(loop.countVariableName))
                    {
                        loopCount = eventVarManager.GetVariable<int>(loop.countVariableName);
                    }

                    for (int i = 0; i < loopCount; i++)
                    {
                        if (!loop.effects.NullOrEmpty())
                        {
                            foreach (var effect in loop.effects)
                            {
                                effect.Execute(dialog);
                            }
                        }
                    }
                }
            }
        }
    }

    public class ConditionalDescription
    {
        public List<Condition> conditions;
        public string text;
    }
}
