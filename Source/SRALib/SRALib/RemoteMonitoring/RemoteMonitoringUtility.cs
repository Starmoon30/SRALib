using RimWorld;
using RimWorld.Planet;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace SRA
{
    public static class RemoteMonitoringUtility
    {
        public static IEnumerable<CompRemoteMapMonitor> GetAllRemoteMapMonitors()
        {
            if (Find.Maps == null)
            {
                yield break;
            }

            foreach (Map map in Find.Maps)
            {
                if (map?.listerBuildings == null)
                {
                    continue;
                }

                foreach (Building building in map.listerBuildings.allBuildingsColonist)
                {
                    if (building == null || building.Destroyed)
                    {
                        continue;
                    }

                    if (!(building is ThingWithComps thingWithComps) || thingWithComps.AllComps == null)
                    {
                        continue;
                    }

                    foreach (ThingComp comp in thingWithComps.AllComps)
                    {
                        if (comp is CompRemoteMapMonitor monitor && monitor.ShouldKeepTargetAlive)
                        {
                            yield return monitor;
                        }
                    }
                }
            }
        }

        public static bool IsMapPawnsRemotelyObserved(MapPawns mapPawns)
        {
            if (mapPawns == null)
            {
                return false;
            }

            RemoteMonitoringMapCache.CleanupInvalidEntries();
            return RemoteMonitoringMapCache.ObservedMaps.Any(mapParent => mapParent.HasMap && mapParent.Map != null && mapParent.Map.mapPawns == mapPawns);
        }

        public static Texture2D ResolveCommandIcon(string primaryPath, string fallbackPath)
        {
            Texture2D icon = null;

            if (!primaryPath.NullOrEmpty())
            {
                icon = ContentFinder<Texture2D>.Get(primaryPath, false);
            }

            if (icon != null)
            {
                return icon;
            }

            if (!fallbackPath.NullOrEmpty())
            {
                icon = ContentFinder<Texture2D>.Get(fallbackPath, false);
            }

            return icon ?? BaseContent.BadTex;
        }

        public static bool IsForbiddenSettlementTarget(MapParent target)
        {
            return target is Settlement settlement &&
                   settlement.Faction != null &&
                   !settlement.Faction.IsPlayer &&
                   !settlement.Faction.HostileTo(Faction.OfPlayer);
        }

        public static string GetNonHostileSettlementReason(MapParent target, string messageKey = "SRA_RemoteMonitoring_NonHostileSettlementMessage")
        {
            if (target is Settlement settlement && settlement.Faction != null)
            {
                return messageKey.Translate(settlement.Faction.Name);
            }

            return messageKey.Translate(target?.LabelCap ?? "SRA_RemoteArtillery_UnknownLabel".Translate());
        }
    }
}
