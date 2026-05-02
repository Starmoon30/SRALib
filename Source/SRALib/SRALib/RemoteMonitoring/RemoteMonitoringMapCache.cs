using RimWorld.Planet;
using System.Collections.Generic;
using Verse;

namespace SRA
{
    [StaticConstructorOnStartup]
    public static class RemoteMonitoringMapCache
    {
        public static readonly HashSet<MapParent> ObservedMaps = new HashSet<MapParent>();

        static RemoteMonitoringMapCache()
        {
            LongEventHandler.ExecuteWhenFinished(RebuildCache);
        }

        public static void RebuildCache()
        {
            if (Current.Game == null || Find.Maps == null)
            {
                return;
            }

            ObservedMaps.Clear();

            foreach (CompRemoteMapMonitor monitor in RemoteMonitoringUtility.GetAllRemoteMapMonitors())
            {
                if (monitor.ObservedMapParent != null && !monitor.ObservedMapParent.Destroyed)
                {
                    ObservedMaps.Add(monitor.ObservedMapParent);
                }
            }
        }

        public static void Add(MapParent mapParent)
        {
            if (mapParent != null && !mapParent.Destroyed)
            {
                ObservedMaps.Add(mapParent);
            }
        }

        public static void Remove(MapParent mapParent)
        {
            if (mapParent != null)
            {
                ObservedMaps.Remove(mapParent);
            }
        }

        public static void CleanupInvalidEntries()
        {
            ObservedMaps.RemoveWhere(mapParent => mapParent == null || mapParent.Destroyed);
        }
    }
}
