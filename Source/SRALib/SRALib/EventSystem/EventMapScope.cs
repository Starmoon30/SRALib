using System.Collections.Generic;
using RimWorld;
using Verse;

namespace SRA
{
    /// <summary>
    /// Selects the maps used by EventSystem effects and conditions that inspect
    /// or remove player-owned items.
    /// </summary>
    public enum EventMapScope
    {
        /// <summary>The map currently shown to the player.</summary>
        CurrentMap,

        /// <summary>The player's default home map.</summary>
        PlayerHome,

        /// <summary>Every map currently generated in the game.</summary>
        AllMaps
    }

    public static class EventMapScopeUtility
    {
        private static readonly List<Thing> tmpThings = new List<Thing>();

        public static IEnumerable<Map> EnumerateMaps(EventMapScope scope)
        {
            switch (scope)
            {
                case EventMapScope.CurrentMap:
                {
                    Map currentMap = Find.CurrentMap;
                    if (currentMap != null)
                    {
                        yield return currentMap;
                    }
                    yield break;
                }

                case EventMapScope.PlayerHome:
                {
                    Map homeMap = Find.AnyPlayerHomeMap;
                    if (homeMap != null)
                    {
                        yield return homeMap;
                    }
                    yield break;
                }

                case EventMapScope.AllMaps:
                    foreach (Map map in Find.Maps)
                    {
                        if (map != null)
                        {
                            yield return map;
                        }
                    }
                    yield break;
            }
        }

        /// <summary>
        /// Collects real item stacks held by the player in the selected maps.
        /// The result list contains each source stack so callers can remove an
        /// exact amount without relying on a trade beacon or a resource cache.
        /// </summary>
        public static int CollectPlayerOwnedThings(EventMapScope scope, ThingDef thingDef, List<Thing> result)
        {
            result?.Clear();
            if (thingDef == null)
            {
                return 0;
            }

            int total = 0;
            foreach (Map map in EnumerateMaps(scope))
            {
                ThingOwnerUtility.GetAllThingsRecursively(map, tmpThings, false, null);
                for (int i = 0; i < tmpThings.Count; i++)
                {
                    Thing thing = tmpThings[i];
                    if (thing.def != thingDef || thing.def.category != ThingCategory.Item || !IsPlayerOwned(thing, map))
                    {
                        continue;
                    }

                    total += thing.stackCount;
                    result?.Add(thing);
                }
            }

            return total;
        }

        public static int CountPlayerOwnedThings(EventMapScope scope, ThingDef thingDef)
        {
            return CollectPlayerOwnedThings(scope, thingDef, null);
        }

        private static bool IsPlayerOwned(Thing thing, Map map)
        {
            if (thing.Faction != null)
            {
                return thing.Faction == Faction.OfPlayer;
            }

            IThingHolder holder = thing.ParentHolder;
            while (holder != null)
            {
                if (holder is Thing holderThing && holderThing.Faction != null)
                {
                    return holderThing.Faction == Faction.OfPlayer;
                }

                holder = holder.ParentHolder;
            }

            // Unclaimed items on a player home map are part of the player's
            // available stock, while unclaimed items on hostile maps are not.
            return map.IsPlayerHome || map.ParentFaction == Faction.OfPlayer;
        }
    }
}
