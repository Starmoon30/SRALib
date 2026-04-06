using RimWorld;
using RimWorld.QuestGen;
using System;
using System.Collections.Generic;
using Verse;

namespace SRA
{
    public class QuestNode_Root_EventLetter : QuestNode
    {
        // 直接指定 EventDef 名称
        public SlateRef<string> eventDefName;
        
        // 移除原有的向后兼容字段，因为我们只需要 EventDef 接口
        // public SlateRef<string> letterLabel;
        // public SlateRef<string> letterTitle;
        // public SlateRef<string> letterText;
        // public List<Option> options = new List<Option>();

        protected override void RunInt()
        {
            Slate slate = QuestGen.slate;
            string defName = eventDefName.GetValue(slate);

            if (!defName.NullOrEmpty())
            {
                OpenEventDefWindow(defName);
            }
            else
            {
                Log.Message("[SRA] QuestNode_Root_EventLetter: eventDefName is not specified.");
            }
        }

        protected override bool TestRunInt(Slate slate)
        {
            string defName = eventDefName.GetValue(slate);
            
            if (defName.NullOrEmpty())
            {
                Log.Message("[SRA] QuestNode_Root_EventLetter: eventDefName is not specified.");
                return false;
            }

            EventDef eventDef = DefDatabase<EventDef>.GetNamed(defName, false);
            if (eventDef == null)
            {
                Log.Message($"[SRA] QuestNode_Root_EventLetter: EventDef '{defName}' not found.");
                return false;
            }
            
            return true;
        }

        /// <summary>
        /// 直接打开指定的 EventDef 窗口
        /// </summary>
        private void OpenEventDefWindow(string defName)
        {
            EventDef eventDef = DefDatabase<EventDef>.GetNamed(defName);
            if (eventDef != null)
            {
                Find.WindowStack.Add((Window)Activator.CreateInstance(eventDef.windowType, eventDef));
            }
            else
            {
                Log.Message($"[SRA] QuestNode_Root_EventLetter: Could not find EventDef '{defName}'");
            }
        }
    }
}
