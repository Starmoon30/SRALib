using RimWorld.Planet;
using System.Collections.Generic;
using Verse;

namespace SRA
{
    [StaticConstructorOnStartup]
    public static class RemoteMonitoringMapCache
    {
        // 保留旧集合接口，避免已有子 mod 依赖 RemoteMonitoringMapCache.ObservedMaps 时失效。
        public static readonly HashSet<MapParent> ObservedMaps = new HashSet<MapParent>();

        // MapParent 会在据点被击败时替换为 DestroyedSettlement；Map 才是稳定身份。
        public static readonly HashSet<Map> ObservedMapInstances = new HashSet<Map>();

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
            ObservedMapInstances.Clear();

            foreach (CompRemoteMapMonitor monitor in RemoteMonitoringUtility.GetAllRemoteMapMonitors())
            {
                if (monitor.ObservedMapParent != null && !monitor.ObservedMapParent.Destroyed)
                {
                    ObservedMaps.Add(monitor.ObservedMapParent);
                }

                if (monitor.ObservedMap != null && !monitor.ObservedMap.Disposed)
                {
                    ObservedMapInstances.Add(monitor.ObservedMap);
                }
            }
        }

        public static void Add(Map map)
        {
            if (map != null && !map.Disposed)
            {
                ObservedMapInstances.Add(map);
                if (map.Parent != null && !map.Parent.Destroyed)
                {
                    ObservedMaps.Add(map.Parent);
                }
            }
        }

        public static void Remove(Map map)
        {
            if (map != null)
            {
                ObservedMapInstances.Remove(map);
                if (map.Parent != null)
                {
                    ObservedMaps.Remove(map.Parent);
                }
            }
        }

        // Legacy overloads retained for consumers using the original cache API.
        public static void Add(MapParent mapParent)
        {
            if (mapParent != null && !mapParent.Destroyed)
            {
                ObservedMaps.Add(mapParent);
                if (mapParent.Map != null && !mapParent.Map.Disposed)
                {
                    ObservedMapInstances.Add(mapParent.Map);
                }
            }
        }

        public static void Remove(MapParent mapParent)
        {
            if (mapParent != null)
            {
                ObservedMaps.Remove(mapParent);
                if (mapParent.Map != null)
                {
                    ObservedMapInstances.Remove(mapParent.Map);
                }
            }
        }

        public static void CleanupInvalidEntries()
        {
            ObservedMaps.RemoveWhere(mapParent => mapParent == null || mapParent.Destroyed);
            ObservedMapInstances.RemoveWhere(map => map == null || map.Disposed);
        }
    }
}
