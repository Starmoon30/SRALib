using RimWorld;
using RimWorld.QuestGen;
using System;
using System.Collections.Generic;
using Verse;

namespace SRA
{
    public class QuestNode_Root_EventLetter : QuestNode
    {
        // Fields to be set from the QuestScriptDef XML
        public SlateRef<string> letterLabel;
        public SlateRef<string> letterTitle;
        public SlateRef<string> letterText;
        public List<Option> options = new List<Option>();

        // This is a root node, so it doesn't have a parent signal.
        // It runs immediately when the quest starts.
        protected override void RunInt()
        {
            // Get the current slate
            Slate slate = QuestGen.slate;

            var letter = (Letter_EventChoice)LetterMaker.MakeLetter(DefDatabase<LetterDef>.GetNamed("SRA_EventChoiceLetter"));
            letter.Label = letterLabel.GetValue(slate);
            letter.title = letterTitle.GetValue(slate);
            letter.Text = letterText.GetValue(slate);
            letter.options = options;
            letter.quest = QuestGen.quest;
            letter.lookTargets = slate.Get<LookTargets>("lookTargets");

            Find.LetterStack.ReceiveLetter(letter);
        }

        protected override bool TestRunInt(Slate slate)
        {
            // This node can always run as long as the slate refs are valid.
            // We can add more complex checks here if needed.
            return true;
        }

        // Inner class to hold option data from XML
        public class Option
        {
            public string label;
            public List<ConditionalEffects> optionEffects;
        }
    }
}