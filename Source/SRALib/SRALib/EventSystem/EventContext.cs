using System.Collections.Generic;
using Verse;

namespace SRA
{
    public static class EventContext
    {
        private static Dictionary<string, object> variables = new Dictionary<string, object>();

        public static void SetVariable(string name, object value)
        {
            if (variables.ContainsKey(name))
            {
                variables[name] = value;
            }
            else
            {
                variables.Add(name, value);
            }
            Log.Message($"[EventContext] Set variable '{name}' to '{value}'.");
        }

        public static T GetVariable<T>(string name, T defaultValue = default)
        {
            if (variables.TryGetValue(name, out object value))
            {
                if (value is T typedValue)
                {
                    return typedValue;
                }
                // Try to convert, e.g., from int to float
                try
                {
                    return (T)System.Convert.ChangeType(value, typeof(T));
                }
                catch (System.Exception)
                {
                    Log.Warning($"[EventContext] Variable '{name}' is of type {value.GetType()} but could not be converted to {typeof(T)}.");
                    return defaultValue;
                }
            }
            Log.Warning($"[EventContext] Variable '{name}' not found. Returning default value.");
            return defaultValue;
        }

        public static bool HasVariable(string name)
        {
            return variables.ContainsKey(name);
        }

        public static void Clear()
        {
            variables.Clear();
            Log.Message("[EventContext] All variables cleared.");
        }

        public static void ClearVariable(string name)
        {
            if (variables.Remove(name))
            {
                Log.Message($"[EventContext] Cleared variable '{name}'.");
            }
            else
            {
                Log.Warning($"[EventContext] Tried to clear variable '{name}' but it was not found.");
            }
        }
    }
}
