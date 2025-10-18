using RimWorld;
using RimWorld.QuestGen;
using System;
using System.Collections.Generic;
using Verse;

namespace SRA
{
    public class Letter_EventChoice : ChoiceLetter
    {
        // These fields are now inherited from the base Letter class
        // public string letterLabel;
        // public string letterTitle;
        // public string letterText;
        public List<QuestNode_Root_EventLetter.Option> options;
        public new Quest quest;

        public override IEnumerable<DiaOption> Choices
        {
            get
            {
                if (options.NullOrEmpty())
                {
                    yield break;
                }

                foreach (var optionDef in options)
                {
                    var currentOption = optionDef;
                    Action choiceAction = delegate
                    {
                        if (!currentOption.optionEffects.NullOrEmpty())
                        {
                            foreach (var conditionalEffect in currentOption.optionEffects)
                            {
                                string reason;
                                if (AreConditionsMet(conditionalEffect.conditions, out reason))
                                {
                                    conditionalEffect.Execute(null);
                                }
                            }
                        }
                        if (quest != null && !quest.hidden && !quest.Historical)
                        {
                            quest.End(QuestEndOutcome.Success);
                        }
                        Find.LetterStack.RemoveLetter(this);
                    };

                    var diaOption = new DiaOption(currentOption.label)
                    {
                        action = choiceAction,
                        resolveTree = true
                    };
                    yield return diaOption;
                }
            }
        }

        public override bool CanDismissWithRightClick => false;

        private bool AreConditionsMet(List<Condition> conditions, out string reason)
        {
            reason = "";
            if (conditions.NullOrEmpty())
            {
                return true;
            }

            foreach (var condition in conditions)
            {
                if (!condition.IsMet(out string singleReason))
                {
                    reason = singleReason;
                    return false;
                }
            }
            return true;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            // Scribe_Values.Look(ref letterLabel, "letterLabel"); // Now uses base.label
            // Scribe_Values.Look(ref letterTitle, "letterTitle"); // Now uses base.title
            // Scribe_Values.Look(ref letterText, "letterText"); // Now uses base.text
            Scribe_Collections.Look(ref options, "options", LookMode.Deep);
            if (Scribe.mode != LoadSaveMode.Saving || quest != null)
            {
                Scribe_References.Look(ref quest, "quest");
            }
        }
    }
}