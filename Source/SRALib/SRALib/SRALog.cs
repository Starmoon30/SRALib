using Verse;

namespace SRA
{
    /// <summary>
    /// Centralized debug logging controlled by mod settings.
    /// Only shows when mod option is enabled, independent of DevMode.
    /// </summary>
    public static class SRALog
    {
        private static bool DebugEnabled =>
            SRALib.settings?.enableDebugLogs ?? false;

        public static void Debug(string message)
        {
            if (DebugEnabled)
            {
                Log.Message(message);
            }
        }
    }
}
