using System.Collections.Generic;
using Verse;

namespace SRA
{
    public class CompProperties_ClearTimedGameConditions : CompProperties
    {
        public string buttonLabelKey = "SRA_ClearTimedGameConditions_Label";
        public string buttonDescKey = "SRA_ClearTimedGameConditions_Desc";
        public string noTargetMessageKey = "SRA_ClearTimedGameConditions_NoTargetMessage";
        public string clearedMessageKey = "SRA_ClearTimedGameConditions_ClearedMessage";
        public string powerRequiredMessageKey = "SRA_ClearTimedGameConditions_PowerRequiredMessage";
        public string iconPath = "SRA/UI/Commands/UI_SRA_ClearTimedGameConditions";
        public bool requirePower = true;
        public List<GameConditionDef> gameConditionWhitelist;
        public List<WeatherDef> weatherWhitelist;

        public CompProperties_ClearTimedGameConditions()
        {
            compClass = typeof(CompClearTimedGameConditions);
        }
    }
}
