using Verse;
using RimWorld;
using static RimWorld.CompProperties_Power;
using System;

namespace SRA
{
    public abstract class Condition
    {
        public abstract bool IsMet(out string reason);
    }

    public class Condition_VariableEquals : Condition
    {
        public string name;
        public string value;
        public string valueVariableName;

        public override bool IsMet(out string reason)
        {
            var eventVarManager = Find.World.GetComponent<EventVariableManager>();
            if (!eventVarManager.HasVariable(name))
            {
                reason = $"Variable '{name}' not found.";
                return false;
            }

            object variable = eventVarManager.GetVariable<object>(name);
            string compareValueStr = value;

            if (!string.IsNullOrEmpty(valueVariableName))
            {
                compareValueStr = eventVarManager.GetVariable<object>(valueVariableName)?.ToString();
                if (compareValueStr == null)
                {
                    reason = $"Comparison variable '{valueVariableName}' not set.";
                    return false;
                }
            }

            bool met = false;
            try
            {
                if (variable is int)
                {
                    met = (int)variable == int.Parse(compareValueStr);
                }
                else if (variable is float)
                {
                    met = (float)variable == float.Parse(compareValueStr);
                }
                else if (variable is bool)
                {
                    met = (bool)variable == bool.Parse(compareValueStr);
                }
                else
                {
                    met = variable?.ToString() == compareValueStr;
                }
            }
            catch (System.Exception e)
            {
                Log.Warning($"[EventSystem] Condition_VariableEquals: Could not compare '{variable}' and '{compareValueStr}'. Error: {e.Message}");
                reason = "Type mismatch or parsing error during comparison.";
                return false;
            }

            if (!met)
            {
                reason = $"Requires {name} = {compareValueStr} (Current: {variable})";
            }
            else
            {
                reason = "";
            }
            return met;
        }
    }

    public abstract class Condition_CompareVariable : Condition
    {
        public string name;
        public float value;
        public string valueVariableName;

        protected abstract bool Compare(float var1, float var2);
        protected abstract string GetOperatorString();

        public override bool IsMet(out string reason)
        {
            var eventVarManager = Find.World.GetComponent<EventVariableManager>();
            if (!eventVarManager.HasVariable(name))
            {
                Log.Message($"[EventSystem] {GetType().Name}: Variable '{name}' not found, defaulting to 0f.");
                eventVarManager.SetVariable(name, 0f);
            }
            
            float variable = eventVarManager.GetVariable<float>(name);

            float compareValue = value;
            if (!string.IsNullOrEmpty(valueVariableName))
            {
                compareValue = eventVarManager.GetVariable<float>(valueVariableName, float.NaN);
                if (float.IsNaN(compareValue))
                {
                    reason = $"Comparison variable '{valueVariableName}' not set or not a number.";
                    Log.Warning($"[EventSystem] {GetType().Name} check for '{name}' failed: {reason}");
                    return false;
                }
            }

            bool met = Compare(variable, compareValue);
            Log.Message($"[EventSystem] {GetType().Name} check: Name='{name}', CurrentValue='{variable}', CompareValue='{compareValue}', Met={met}");
            if (!met)
            {
                reason = $"Requires {name} {GetOperatorString()} {compareValue} (Current: {variable})";
            }
            else
            {
                reason = "";
            }
            return met;
        }
    }

    public class Condition_VariableGreaterThan : Condition_CompareVariable
    {
        protected override bool Compare(float var1, float var2) => var1 > var2;
        protected override string GetOperatorString() => ">";
    }

    public class Condition_VariableLessThan : Condition_CompareVariable
    {
        protected override bool Compare(float var1, float var2) => var1 < var2;
        protected override string GetOperatorString() => "<";
    }

    public class Condition_VariableGreaterThanOrEqual : Condition_CompareVariable
    {
        protected override bool Compare(float var1, float var2) => var1 >= var2;
        protected override string GetOperatorString() => ">=";
    }

    public class Condition_VariableLessThanOrEqual : Condition_CompareVariable
    {
        protected override bool Compare(float var1, float var2) => var1 <= var2;
        protected override string GetOperatorString() => "<=";
    }

    public class Condition_VariableNotEqual : Condition
    {
        public string name;
        public string value;
        public string valueVariableName;

        public override bool IsMet(out string reason)
        {
            var eventVarManager = Find.World.GetComponent<EventVariableManager>();
            if (!eventVarManager.HasVariable(name))
            {
                reason = $"Variable '{name}' not found.";
                return true;
            }

            object variable = eventVarManager.GetVariable<object>(name);
            string compareValueStr = value;

            if (!string.IsNullOrEmpty(valueVariableName))
            {
                compareValueStr = eventVarManager.GetVariable<object>(valueVariableName)?.ToString();
                if (compareValueStr == null)
                {
                    reason = $"Comparison variable '{valueVariableName}' not set.";
                    return true;
                }
            }

            bool met = false;
            try
            {
                if (variable is int)
                {
                    met = (int)variable != int.Parse(compareValueStr);
                }
                else if (variable is float)
                {
                    met = (float)variable != float.Parse(compareValueStr);
                }
                else if (variable is bool)
                {
                    met = (bool)variable != bool.Parse(compareValueStr);
                }
                else
                {
                    met = variable?.ToString() != compareValueStr;
                }
            }
            catch (System.Exception e)
            {
                Log.Warning($"[EventSystem] Condition_VariableNotEqual: Could not compare '{variable}' and '{compareValueStr}'. Error: {e.Message}");
                reason = "Type mismatch or parsing error during comparison.";
                return false;
            }
            
            Log.Message($"[EventSystem] Condition_VariableNotEqual check: Name='{name}', Type='{variable?.GetType().Name ?? "null"}', CurrentValue='{variable}', CompareValue='{compareValueStr}', Met={met}");
            if (!met)
            {
                reason = $"Requires {name} != {compareValueStr} (Current: {variable})";
            }
            else
            {
                reason = "";
            }
            return met;
        }
    }
    
    public class Condition_FactionExists : Condition
    {
        public FactionDef factionDef;

        public override bool IsMet(out string reason)
        {
            if (factionDef == null)
            {
                reason = "FactionDef not specified in Condition_FactionExists.";
                return false;
            }

            bool exists = Find.FactionManager.FirstFactionOfDef(factionDef) != null;
            if (!exists)
            {
                reason = $"Faction '{factionDef.label}' does not exist in the world.";
            }
            else
            {
                reason = "";
            }
            return exists;
        }
    }

    public class Condition_HasThing : Condition
    {
        public ThingDef thingDef;
        public int count = 1;
        public override bool IsMet(out string reason)
        {
            if (thingDef == null)
            {
                reason = "thingDef not specified in Condition_HasThing.";
                return false;
            }
            Map currentMap = Find.CurrentMap;
            if (currentMap == null)
            {
                reason = "[SRA] Condition_HasThing cannot execute without a current map.";
                return false;
            }
            int playerAmount = currentMap.resourceCounter.GetCount(thingDef);
            if (playerAmount < count)
            {
                reason = "not has enough thing.";
                return false;
            }
            else
            {
                reason = "has enough thing.";
                return true;
            }
        }
    }
    public class Condition_HasResearchProject : Condition
    {
        public ResearchProjectDef researchProject;
        public override bool IsMet(out string reason)
        {
            if (researchProject == null)
            {
                reason = "researchProject not specified in Condition_HasResearchProject.";
                return false;
            }
            if (!researchProject.IsFinished)
            {
                reason = "researchProject not IsFinished.";
                return false;
            }
            else
            {
                reason = "has researchProject.";
                return true;
            }
        }
    }
}
