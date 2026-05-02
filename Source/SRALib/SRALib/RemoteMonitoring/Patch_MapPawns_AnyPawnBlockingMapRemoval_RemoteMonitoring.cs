using HarmonyLib;
using Verse;

namespace SRA
{
    [HarmonyPatch(typeof(MapPawns), "get_AnyPawnBlockingMapRemoval")]
    public static class Patch_MapPawns_AnyPawnBlockingMapRemoval_RemoteMonitoring
    {
        public static void Postfix(MapPawns __instance, ref bool __result)
        {
            if (__result)
            {
                return;
            }

            if (RemoteMonitoringUtility.IsMapPawnsRemotelyObserved(__instance))
            {
                __result = true;
            }
        }
    }
}
