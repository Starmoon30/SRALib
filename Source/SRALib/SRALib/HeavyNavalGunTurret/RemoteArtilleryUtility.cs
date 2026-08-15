using System;
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace SRA
{
    public sealed class RemoteArtilleryGroup
    {
        public string key;
        public string label;
        public string description;
        public Texture2D icon;
        public readonly List<Comp_HNGT_GlobalBallisticAttack> artillery = new List<Comp_HNGT_GlobalBallisticAttack>();

        public int TotalCount(Map targetMap = null)
        {
            int count = 0;
            for (int i = 0; i < artillery.Count; i++)
            {
                if (artillery[i].CanEverDispatchTo(targetMap))
                {
                    count++;
                }
            }

            return count;
        }

        public int AvailableCount(Map targetMap = null)
        {
            int count = 0;
            for (int i = 0; i < artillery.Count; i++)
            {
                if (artillery[i].CanDispatchTo(targetMap))
                {
                    count++;
                }
            }

            return count;
        }

        public Comp_HNGT_GlobalBallisticAttack FirstAvailable(Map targetMap)
        {
            Comp_HNGT_GlobalBallisticAttack best = null;
            for (int i = 0; i < artillery.Count; i++)
            {
                Comp_HNGT_GlobalBallisticAttack comp = artillery[i];
                if (!comp.CanDispatchTo(targetMap))
                {
                    continue;
                }

                if (best == null || CompareArtillery(comp, best) < 0)
                {
                    best = comp;
                }
            }

            return best;
        }

        public string FirstUnavailableReason(Map targetMap = null)
        {
            for (int i = 0; i < artillery.Count; i++)
            {
                Comp_HNGT_GlobalBallisticAttack comp = artillery[i];
                if (comp == null || !comp.CanEverDispatchTo(targetMap))
                {
                    continue;
                }

                if (!comp.CanDispatchTo(targetMap, out string disabledReason) && !disabledReason.NullOrEmpty())
                {
                    return disabledReason;
                }
            }

            return null;
        }

        private static int CompareArtillery(Comp_HNGT_GlobalBallisticAttack a, Comp_HNGT_GlobalBallisticAttack b)
        {
            int aMapId = a.parent?.Map?.uniqueID ?? int.MaxValue;
            int bMapId = b.parent?.Map?.uniqueID ?? int.MaxValue;
            int mapCompare = aMapId.CompareTo(bMapId);
            if (mapCompare != 0)
            {
                return mapCompare;
            }

            int aThingId = a.parent?.thingIDNumber ?? int.MaxValue;
            int bThingId = b.parent?.thingIDNumber ?? int.MaxValue;
            return aThingId.CompareTo(bThingId);
        }
    }

    public static class RemoteArtilleryUtility
    {
        private const float DefaultImpactRadius = 15f;

        private static readonly List<RemoteArtilleryGroup> CachedGroups = new List<RemoteArtilleryGroup>();
        private static readonly Dictionary<string, RemoteArtilleryGroup> TmpGroupsByKey = new Dictionary<string, RemoteArtilleryGroup>();
        private static int cachedGroupsTick = -1;
        private static string pendingRestartGroupKey;
        private static Map pendingRestartMap;
        private static int pendingRestartFrame = -1;

        public static void InvalidateCache()
        {
            cachedGroupsTick = -1;
        }

        public static void TickPendingTargetingRestart()
        {
            if (pendingRestartFrame < 0 || Time.frameCount < pendingRestartFrame)
            {
                return;
            }

            string groupKey = pendingRestartGroupKey;
            Map map = pendingRestartMap;
            ClearPendingTargetingRestart();
            if (map == null || map.Disposed || groupKey.NullOrEmpty())
            {
                return;
            }

            RemoteArtilleryGroup group = GetGroup(groupKey);
            if (group != null && group.AvailableCount(map) > 0)
            {
                BeginMapTargeting(groupKey, map);
            }
        }

        public static IEnumerable<Gizmo> BuildRemoteArtilleryGizmos(CompRemoteMapMonitor monitor)
        {
            if (monitor == null || !monitor.Props.allowRemoteArtilleryCommands)
            {
                yield break;
            }

            List<RemoteArtilleryGroup> groups = GetGroups();
            for (int i = 0; i < groups.Count; i++)
            {
                RemoteArtilleryGroup group = groups[i];
                int total = group.TotalCount();
                if (total <= 0)
                {
                    continue;
                }

                int available = group.AvailableCount();
                string groupKey = group.key;
                Command_Action command = new Command_Action
                {
                    defaultLabel = "SRA_RemoteArtillery_CommandLabel".Translate(group.label, available, total),
                    defaultDesc = group.description,
                    icon = group.icon ?? BaseContent.BadTex,
                    action = () => BeginWorldTargeting(monitor, groupKey)
                };

                string disabledReason;
                if (!monitor.CanUseRemoteMonitoringForRemoteAction(out disabledReason))
                {
                    command.Disable(disabledReason);
                }
                else if (available <= 0)
                {
                    command.Disable(group.FirstUnavailableReason() ?? "SRA_RemoteArtillery_NoAvailable".Translate(group.label));
                }

                yield return command;
            }
        }

        public static bool HasExistingTargetMap(Comp_HNGT_GlobalBallisticAttack comp)
        {
            if (comp == null || Find.Maps == null)
            {
                return false;
            }

            for (int i = 0; i < Find.Maps.Count; i++)
            {
                if (IsExistingTargetMap(comp, Find.Maps[i]))
                {
                    return true;
                }
            }

            return false;
        }

        public static void BeginSingleArtilleryExistingMapTargeting(Comp_HNGT_GlobalBallisticAttack comp)
        {
            if (comp == null)
            {
                return;
            }

            if (!comp.CanDispatchTo(null, out string disabledReason))
            {
                Messages.Message(disabledReason, MessageTypeDefOf.RejectInput, false);
                return;
            }

            List<FloatMenuOption> options = new List<FloatMenuOption>();
            if (Find.Maps != null)
            {
                for (int i = 0; i < Find.Maps.Count; i++)
                {
                    Map map = Find.Maps[i];
                    if (!IsExistingTargetMap(comp, map))
                    {
                        continue;
                    }

                    Map targetMap = map;
                    string label = GlobalAttackMapLabelUtility.GetMapLabel(targetMap);
                    if (comp.CanDispatchTo(targetMap, out string mapDisabledReason))
                    {
                        options.Add(new FloatMenuOption(label, () => BeginSingleMapTargeting(comp, targetMap)));
                    }
                    else
                    {
                        options.Add(new FloatMenuOption(label + " (" + mapDisabledReason + ")", null));
                    }
                }
            }

            if (options.Count <= 0)
            {
                Messages.Message("SRA_RemoteArtillery_NoExistingTargetMap".Translate(), MessageTypeDefOf.RejectInput, false);
                return;
            }

            Find.WindowStack.Add(new FloatMenu(options));
        }

        public static List<RemoteArtilleryGroup> GetGroups()
        {
            int currentTick = Find.TickManager?.TicksGame ?? 0;
            if (cachedGroupsTick == currentTick)
            {
                return CachedGroups;
            }

            cachedGroupsTick = currentTick;
            CachedGroups.Clear();
            TmpGroupsByKey.Clear();

            if (Find.Maps == null)
            {
                return CachedGroups;
            }

            for (int i = 0; i < Find.Maps.Count; i++)
            {
                Map map = Find.Maps[i];
                if (map?.listerBuildings?.allBuildingsColonist == null)
                {
                    continue;
                }

                List<Building> buildings = map.listerBuildings.allBuildingsColonist;
                for (int j = 0; j < buildings.Count; j++)
                {
                    ThingWithComps thingWithComps = buildings[j] as ThingWithComps;
                    if (thingWithComps == null || thingWithComps.Destroyed)
                    {
                        continue;
                    }

                    Comp_HNGT_GlobalBallisticAttack comp = thingWithComps.GetComp<Comp_HNGT_GlobalBallisticAttack>();
                    if (comp == null || !comp.CanEverDispatchTo())
                    {
                        continue;
                    }

                    AddToGroup(comp);
                }
            }

            CachedGroups.Sort((a, b) => string.Compare(a.label, b.label, System.StringComparison.Ordinal));
            return CachedGroups;
        }

        public static RemoteArtilleryGroup GetGroup(string groupKey)
        {
            List<RemoteArtilleryGroup> groups = GetGroups();
            for (int i = 0; i < groups.Count; i++)
            {
                if (groups[i].key == groupKey)
                {
                    return groups[i];
                }
            }

            return null;
        }

        private static void AddToGroup(Comp_HNGT_GlobalBallisticAttack comp)
        {
            string key = comp.CategoryKey;
            if (!TmpGroupsByKey.TryGetValue(key, out RemoteArtilleryGroup group))
            {
                group = new RemoteArtilleryGroup
                {
                    key = key,
                    label = comp.CategoryLabel,
                    description = comp.CategoryDesc,
                    icon = comp.CommandIcon
                };
                TmpGroupsByKey.Add(key, group);
                CachedGroups.Add(group);
            }

            group.artillery.Add(comp);
        }

        private static void BeginWorldTargeting(CompRemoteMapMonitor monitor, string groupKey)
        {
            RemoteArtilleryGroup group = GetGroup(groupKey);
            if (group == null || group.AvailableCount() <= 0)
            {
                Messages.Message(group?.FirstUnavailableReason() ?? "SRA_RemoteArtillery_NoAvailable".Translate(group?.label ?? "SRA_RemoteArtillery_UnknownLabel".Translate()), MessageTypeDefOf.RejectInput, false);
                return;
            }

            CameraJumper.TryShowWorld();
            Texture2D icon = group.icon ?? BaseContent.BadTex;
            Find.WorldTargeter.BeginTargeting(
                target => ChooseWorldTarget(monitor, groupKey, target),
                true,
                icon,
                true,
                null,
                target => "SRA_RemoteArtillery_TargetPrompt".Translate(group.label),
                target => CanSelectWorldTarget(monitor, target),
                null,
                true);
        }

        private static bool CanSelectWorldTarget(CompRemoteMapMonitor monitor, GlobalTargetInfo target)
        {
            return target.WorldObject is MapParent mapParent &&
                   monitor != null &&
                   monitor.CanSelectRemoteMapTarget(mapParent);
        }

        private static bool ChooseWorldTarget(CompRemoteMapMonitor monitor, string groupKey, GlobalTargetInfo target)
        {
            if (!(target.WorldObject is MapParent mapParent))
            {
                Messages.Message("SRA_RemoteArtillery_InvalidMapSelection".Translate(), MessageTypeDefOf.RejectInput, false);
                return false;
            }

            if (!monitor.TrySetObservedMap(mapParent, openAfterLink: false, notify: true))
            {
                return false;
            }

            monitor.OpenObservedMap(delegate (Map map)
            {
                BeginMapTargeting(groupKey, map);
            });
            return true;
        }

        private static void BeginMapTargeting(string groupKey, Map map)
        {
            if (map == null)
            {
                Messages.Message("SRA_RemoteMonitoring_OpenFailedMessage".Translate(), MessageTypeDefOf.RejectInput, false);
                return;
            }

            RemoteArtilleryGroup group = GetGroup(groupKey);
            if (group == null)
            {
                return;
            }

            int available = group.AvailableCount(map);
            int total = group.TotalCount(map);
            if (available <= 0)
            {
                Messages.Message(group.FirstUnavailableReason(map) ?? "SRA_RemoteArtillery_NoAvailableForTarget".Translate(group.label), MessageTypeDefOf.RejectInput, false);
                return;
            }

            bool switchedMap = Find.CurrentMap != map;
            if (switchedMap)
            {
                Current.Game.CurrentMap = map;
                CameraJumper.TryJump(new GlobalTargetInfo(map.Center, map));
            }

            Texture2D icon = group.icon ?? BaseContent.BadTex;
            TargetingParameters targetingParameters = new TargetingParameters
            {
                canTargetLocations = true,
                canTargetBuildings = true,
                canTargetPawns = true,
                mapObjectTargetsMustBeAutoAttackable = false
            };

            Find.Targeter.BeginTargeting(
                targetingParameters,
                delegate (LocalTargetInfo target)
                {
                    ClearPendingTargetingRestart();
                    if (!IsValidBombardmentTarget(map, target))
                    {
                        Messages.Message("SRA_RemoteArtillery_InvalidTarget".Translate(), MessageTypeDefOf.RejectInput, false);
                        return;
                    }

                    if (TryDispatchOne(groupKey, map, target.Cell))
                    {
                        RemoteArtilleryGroup updatedGroup = GetGroup(groupKey);
                        if (updatedGroup != null && updatedGroup.AvailableCount(map) > 0)
                        {
                            ScheduleTargetingRestart(groupKey, map);
                        }
                    }
                },
                target => HighlightTarget(groupKey, map, target),
                target => IsValidBombardmentTarget(map, target),
                null,
                null,
                icon,
                true,
                target => DrawAimingMouseAttachment(groupKey, map, icon),
                null);
        }

        private static void BeginSingleMapTargeting(Comp_HNGT_GlobalBallisticAttack comp, Map map)
        {
            if (map == null)
            {
                Messages.Message("SRA_RemoteMonitoring_OpenFailedMessage".Translate(), MessageTypeDefOf.RejectInput, false);
                return;
            }

            if (comp == null)
            {
                Messages.Message("SRA_RemoteArtillery_NoAvailableForTarget".Translate("SRA_RemoteArtillery_UnknownLabel".Translate()), MessageTypeDefOf.RejectInput, false);
                return;
            }

            if (!comp.CanDispatchTo(map, out string disabledReason))
            {
                Messages.Message(disabledReason, MessageTypeDefOf.RejectInput, false);
                return;
            }

            bool switchedMap = Find.CurrentMap != map;
            if (switchedMap)
            {
                Current.Game.CurrentMap = map;
                CameraJumper.TryJump(new GlobalTargetInfo(map.Center, map));
            }

            Texture2D icon = comp.CommandIcon ?? BaseContent.BadTex;
            TargetingParameters targetingParameters = new TargetingParameters
            {
                canTargetLocations = true,
                canTargetBuildings = true,
                canTargetPawns = true,
                mapObjectTargetsMustBeAutoAttackable = false
            };

            Find.Targeter.BeginTargeting(
                targetingParameters,
                delegate (LocalTargetInfo target)
                {
                    if (!IsValidBombardmentTarget(map, target))
                    {
                        Messages.Message("SRA_RemoteArtillery_InvalidTarget".Translate(), MessageTypeDefOf.RejectInput, false);
                        return;
                    }

                    TryDispatchSingle(comp, map, target.Cell);
                },
                target => HighlightSingleTarget(comp, map, target),
                target => IsValidBombardmentTarget(map, target),
                null,
                null,
                icon,
                true,
                target => DrawSingleAimingMouseAttachment(comp, map, icon),
                null);
        }

        private static void ScheduleTargetingRestart(string groupKey, Map map)
        {
            pendingRestartGroupKey = groupKey;
            pendingRestartMap = map;
            pendingRestartFrame = Time.frameCount + 1;
        }

        private static void ClearPendingTargetingRestart()
        {
            pendingRestartGroupKey = null;
            pendingRestartMap = null;
            pendingRestartFrame = -1;
        }

        private static bool IsExistingTargetMap(Comp_HNGT_GlobalBallisticAttack comp, Map map)
        {
            return comp != null &&
                   map != null &&
                   !map.Disposed &&
                   comp.CanEverDispatchTo(map);
        }

        private static bool TryDispatchOne(string groupKey, Map targetMap, IntVec3 targetCell)
        {
            RemoteArtilleryGroup group = GetGroup(groupKey);
            Comp_HNGT_GlobalBallisticAttack comp = group?.FirstAvailable(targetMap);
            if (comp == null)
            {
                Messages.Message(group?.FirstUnavailableReason(targetMap) ?? "SRA_RemoteArtillery_NoAvailableForTarget".Translate(group?.label ?? "SRA_RemoteArtillery_UnknownLabel".Translate()), MessageTypeDefOf.RejectInput, false);
                return false;
            }

            if (!comp.TryStartRemoteFire(targetMap, targetCell, out string rejectReason))
            {
                Messages.Message(rejectReason ?? "SRA_RemoteArtillery_InvalidTarget".Translate(), MessageTypeDefOf.RejectInput, false);
                return false;
            }

            return true;
        }

        private static bool TryDispatchSingle(Comp_HNGT_GlobalBallisticAttack comp, Map targetMap, IntVec3 targetCell)
        {
            if (comp == null)
            {
                Messages.Message("SRA_RemoteArtillery_InvalidTarget".Translate(), MessageTypeDefOf.RejectInput, false);
                return false;
            }

            if (!comp.TryStartRemoteFire(targetMap, targetCell, out string rejectReason))
            {
                Messages.Message(rejectReason ?? "SRA_RemoteArtillery_InvalidTarget".Translate(), MessageTypeDefOf.RejectInput, false);
                return false;
            }

            return true;
        }

        private static bool IsValidBombardmentTarget(Map map, LocalTargetInfo target)
        {
            return map != null &&
                   target.IsValid &&
                   target.Cell.IsValid &&
                   target.Cell.InBounds(map) &&
                   !map.fogGrid.IsFogged(target.Cell);
        }

        private static void HighlightTarget(string groupKey, Map map, LocalTargetInfo target)
        {
            if (!IsValidBombardmentTarget(map, target))
            {
                return;
            }

            GenDraw.DrawTargetHighlight(target);
            float radius = GetImpactRadius(groupKey);
            if (radius > 0f)
            {
                GenDraw.DrawRadiusRing(target.Cell, radius);
            }
        }

        private static float GetImpactRadius(string groupKey)
        {
            RemoteArtilleryGroup group = GetGroup(groupKey);
            if (group == null)
            {
                return DefaultImpactRadius;
            }

            for (int i = 0; i < group.artillery.Count; i++)
            {
                ThingDef payloadDef = group.artillery[i].ResolvedPayloadThingDef;
                ModExtension_HighOrbitAttack ext = payloadDef?.GetModExtension<ModExtension_HighOrbitAttack>();
                if (ext != null)
                {
                    return ext.impactAreaRadius;
                }
            }

            return DefaultImpactRadius;
        }

        private static float GetImpactRadius(Comp_HNGT_GlobalBallisticAttack comp)
        {
            ModExtension_HighOrbitAttack ext = comp?.ResolvedPayloadThingDef?.GetModExtension<ModExtension_HighOrbitAttack>();
            return ext != null ? ext.impactAreaRadius : DefaultImpactRadius;
        }

        private static void HighlightSingleTarget(Comp_HNGT_GlobalBallisticAttack comp, Map map, LocalTargetInfo target)
        {
            if (!IsValidBombardmentTarget(map, target))
            {
                return;
            }

            GenDraw.DrawTargetHighlight(target);
            float radius = GetImpactRadius(comp);
            if (radius > 0f)
            {
                GenDraw.DrawRadiusRing(target.Cell, radius);
            }
        }

        private static void DrawAimingMouseAttachment(string groupKey, Map targetMap, Texture2D icon)
        {
            RemoteArtilleryGroup group = GetGroup(groupKey);
            if (group == null)
            {
                return;
            }

            int available = group.AvailableCount(targetMap);
            int total = group.TotalCount(targetMap);
            string label = "SRA_RemoteArtillery_AimingMouse".Translate(group.label, available, total);
            GenUI.DrawMouseAttachment(icon, label, 0f, Vector2.zero, null, null, true, new Color(0f, 0f, 0f, 0.55f), null, null);
        }

        private static void DrawSingleAimingMouseAttachment(Comp_HNGT_GlobalBallisticAttack comp, Map targetMap, Texture2D icon)
        {
            if (comp == null)
            {
                return;
            }

            int available = comp.CanDispatchTo(targetMap) ? 1 : 0;
            int total = comp.CanEverDispatchTo(targetMap) ? 1 : 0;
            string label = "SRA_RemoteArtillery_AimingMouse".Translate(comp.CategoryLabel, available, total);
            GenUI.DrawMouseAttachment(icon, label, 0f, Vector2.zero, null, null, true, new Color(0f, 0f, 0f, 0.55f), null, null);
        }
    }

    public class GameComponent_RemoteArtilleryTargeting : GameComponent
    {
        public GameComponent_RemoteArtilleryTargeting(Game game)
        {
        }

        public override void GameComponentUpdate()
        {
            base.GameComponentUpdate();
            RemoteArtilleryUtility.TickPendingTargetingRestart();
        }
    }
}
