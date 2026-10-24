using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using Verse;

namespace SRA
{
    public class CompRemoteMapMonitor : ThingComp
    {
        private MapParent observedMap;
        // MapParent 在据点被击败时会被替换为 DestroyedSettlement。
        // 保留 Map 引用，使监视链接不会因为父世界对象更换而丢失。
        private Map observedMapInstance;
        private CompPowerTrader powerComp;

        public CompProperties_RemoteMapMonitor Props => (CompProperties_RemoteMapMonitor)props;
        public MapParent ObservedMapParent => observedMap;
        public Map ObservedMap => observedMapInstance ?? observedMap?.Map;
        public bool ShouldKeepTargetAlive => Props.keepMapAliveWhenLinked && IsResearchUnlocked() && parent.Spawned && ObservedMap != null && !ObservedMap.Disposed;

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            powerComp = parent.GetComp<CompPowerTrader>();
            RefreshCacheRegistration();
        }
        public override void PostDeSpawn(Map map, DestroyMode mode = DestroyMode.Vanish)
        {
            base.PostDeSpawn(map);

            RemoteMonitoringMapCache.Remove(ObservedMap);
        }

        public override void CompTickRare()
        {
            base.CompTickRare();
            RefreshCacheRegistration();
        }

        public override void PostDestroy(DestroyMode mode, Map previousMap)
        {
            base.PostDestroy(mode, previousMap);

            RemoteMonitoringMapCache.Remove(ObservedMap);
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            if (!CanUseCommands())
            {
                yield break;
            }

            if (!TryEnsureObservedTargetIsValid())
            {
                yield break;
            }

            foreach (Gizmo artilleryGizmo in RemoteArtilleryUtility.BuildRemoteArtilleryGizmos(this))
            {
                yield return artilleryGizmo;
            }

            if (observedMap == null)
            {
                if (Props.allowWorldTargetSelection)
                {
                    Command_Action selectCommand = BuildSelectTargetCommand();

                    if (!IsResearchUnlocked())
                    {
                        selectCommand.Disable(GetResearchDisabledReason());
                    }

                    yield return selectCommand;
                }
                else
                {
                    Command_Action openCommand = BuildOpenTargetCommand();

                    if (!IsResearchUnlocked())
                    {
                        openCommand.Disable(GetResearchDisabledReason());
                    }
                    else
                    {
                        openCommand.Disable(Props.noTargetMessageKey.Translate());
                    }

                    yield return openCommand;
                }

                yield break;
            }

            Command_Action openObservedCommand = BuildOpenTargetCommand();

            if (!IsResearchUnlocked())
            {
                openObservedCommand.Disable(GetResearchDisabledReason());
            }
            else if (IsForbiddenSettlementTarget(observedMap))
            {
                openObservedCommand.Disable(GetNonHostileSettlementReason(observedMap));
            }

            yield return openObservedCommand;

            if (Props.allowDisconnect)
            {
                yield return BuildDisconnectCommand();
            }
        }

        public override string CompInspectStringExtra()
        {
            if (TryEnsureObservedTargetIsValid() && observedMap != null)
            {
                return Props.inspectStringKey.Translate(observedMap.LabelCap);
            }

            return base.CompInspectStringExtra();
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_References.Look(ref observedMap, "observedMap");
            Scribe_References.Look(ref observedMapInstance, "observedMapInstance");

            if (Scribe.mode == LoadSaveMode.PostLoadInit && observedMapInstance == null)
            {
                observedMapInstance = observedMap?.Map;
            }
        }

        public bool TrySetObservedMap(MapParent target, bool openAfterLink = false, bool notify = false)
        {
            if (!IsResearchUnlocked())
            {
                if (notify)
                {
                    Messages.Message(GetResearchDisabledReason(), MessageTypeDefOf.RejectInput, false);
                }

                return false;
            }

            if (target == null || target.Destroyed)
            {
                if (notify)
                {
                    Messages.Message(Props.invalidTargetMessageKey.Translate(), MessageTypeDefOf.RejectInput, false);
                }

                return false;
            }

            if (IsForbiddenSettlementTarget(target))
            {
                if (notify)
                {
                    Messages.Message(GetNonHostileSettlementReason(target), MessageTypeDefOf.RejectInput, false);
                }

                return false;
            }

            if (observedMap != null && observedMap != target)
            {
                RemoteMonitoringMapCache.Remove(ObservedMap);
            }

            observedMap = target;
            observedMapInstance = target.Map;
            RefreshCacheRegistration();

            if (notify)
            {
                Messages.Message(Props.linkEstablishedMessageKey.Translate(observedMap.LabelCap), MessageTypeDefOf.PositiveEvent);
            }

            if (openAfterLink)
            {
                OpenObservedMap();
            }

            return true;
        }

        public void ClearObservedMap(bool notify = true)
        {
            RemoteMonitoringMapCache.Remove(ObservedMap);

            observedMap = null;
            observedMapInstance = null;

            if (notify)
            {
                Messages.Message(Props.linkDisconnectedMessageKey.Translate(), MessageTypeDefOf.NeutralEvent);
            }
        }

        public bool CanUseRemoteMonitoringForRemoteAction(out string disabledReason)
        {
            disabledReason = null;
            if (!CanUseCommands())
            {
                disabledReason = Props.noTargetMessageKey.Translate();
                return false;
            }

            if (!IsResearchUnlocked())
            {
                disabledReason = GetResearchDisabledReason();
                return false;
            }

            return true;
        }

        public bool CanSelectRemoteMapTarget(MapParent target)
        {
            return target != null && !target.Destroyed && IsResearchUnlocked() && !IsForbiddenSettlementTarget(target);
        }

        public void OpenObservedMap(Action<Map> onOpened)
        {
            OpenObservedMapInternal(onOpened);
        }

        private bool CanUseCommands()
        {
            if (parent.Faction != Faction.OfPlayer)
            {
                return false;
            }

            if (!Props.requirePower)
            {
                return true;
            }

            return powerComp == null || powerComp.PowerOn;
        }

        private Command_Action BuildSelectTargetCommand()
        {
            return new Command_Action
            {
                defaultLabel = Props.selectTargetLabelKey.Translate(),
                defaultDesc = Props.selectTargetDescKey.Translate(),
                icon = RemoteMonitoringUtility.ResolveCommandIcon(Props.selectIconPath, "UI/Commands/Attack"),
                action = BeginTargetSelection
            };
        }

        private Command_Action BuildOpenTargetCommand()
        {
            return new Command_Action
            {
                defaultLabel = Props.openTargetLabelKey.Translate(),
                defaultDesc = Props.openTargetDescKey.Translate(),
                icon = RemoteMonitoringUtility.ResolveCommandIcon(Props.openIconPath, "UI/Commands/Attack"),
                action = OpenObservedMap
            };
        }

        private Command_Action BuildDisconnectCommand()
        {
            return new Command_Action
            {
                defaultLabel = Props.disconnectTargetLabelKey.Translate(),
                defaultDesc = Props.disconnectTargetDescKey.Translate(),
                icon = RemoteMonitoringUtility.ResolveCommandIcon(Props.disconnectIconPath, "UI/Designators/Cancel"),
                action = () => ClearObservedMap(true)
            };
        }

        private void BeginTargetSelection()
        {
            CameraJumper.TryShowWorld();
            Find.WorldTargeter.BeginTargeting(
                ChooseWorldTarget,
                true,
                RemoteMonitoringUtility.ResolveCommandIcon(Props.selectIconPath, "UI/Commands/Attack"),
                true,
                null,
                target => Props.targetSelectionPromptKey.Translate(),
                null);
        }

        private bool ChooseWorldTarget(GlobalTargetInfo target)
        {
            if (!(target.WorldObject is MapParent mapParent))
            {
                Messages.Message(Props.invalidSelectionMessageKey.Translate(), MessageTypeDefOf.RejectInput, false);
                return false;
            }

            return TrySetObservedMap(mapParent, openAfterLink: true, notify: true);
        }

        private void OpenObservedMap()
        {
            OpenObservedMapInternal(null);
        }

        private void OpenObservedMapInternal(Action<Map> onOpened)
        {
            if (!IsResearchUnlocked())
            {
                Messages.Message(GetResearchDisabledReason(), MessageTypeDefOf.RejectInput, false);
                return;
            }

            if (!TryEnsureObservedTargetIsValid())
            {
                return;
            }

            if (IsForbiddenSettlementTarget(observedMap))
            {
                Messages.Message(GetNonHostileSettlementReason(observedMap), MessageTypeDefOf.RejectInput, false);
                return;
            }

            if (observedMap == null)
            {
                return;
            }

            Map currentObservedMap = ObservedMap;
            if (currentObservedMap != null)
            {
                FinalizeOpenedMap(currentObservedMap, onOpened);
                return;
            }

            MapParent target = observedMap;
            LongEventHandler.QueueLongEvent(() =>
            {
                Map generatedMap = GetOrGenerateMapUtility.GetOrGenerateMap(target.Tile, null);
                if (generatedMap == null)
                {
                    return;
                }

                LongEventHandler.ExecuteWhenFinished(() => FinalizeOpenedMap(generatedMap, onOpened));
            }, "GeneratingMap", false, null, true);
        }

        private void FinalizeOpenedMap(Map map)
        {
            FinalizeOpenedMap(map, null);
        }

        private void FinalizeOpenedMap(Map map, Action<Map> onOpened)
        {
            if (map == null)
            {
                Messages.Message(Props.openFailedMessageKey.Translate(), MessageTypeDefOf.RejectInput, false);
                return;
            }

            observedMapInstance = map;
            if (map.Parent != null && !map.Parent.Destroyed)
            {
                observedMap = map.Parent;
            }

            RefreshCacheRegistration();

            if (Props.jumpToMapAfterOpen)
            {
                CameraJumper.TryJump(new GlobalTargetInfo(map.Center, map));
            }

            onOpened?.Invoke(map);
        }

        private void RefreshCacheRegistration()
        {
            Map map = ObservedMap;
            if (map == null || map.Disposed)
            {
                return;
            }

            if (ShouldKeepTargetAlive)
            {
                RemoteMonitoringMapCache.Add(map);
            }
            else
            {
                RemoteMonitoringMapCache.Remove(map);
            }
        }

        private bool TryEnsureObservedTargetIsValid()
        {
            if (observedMap == null && observedMapInstance == null)
            {
                return true;
            }

            Map map = ObservedMap;
            if (map != null && !map.Disposed)
            {
                // SettlementDefeatUtility replaces map.info.parent before destroying
                // the old Settlement. Follow that replacement without dropping the link.
                if (map.Parent != null && !map.Parent.Destroyed)
                {
                    observedMap = map.Parent;
                    observedMapInstance = map;
                    RefreshCacheRegistration();
                    return true;
                }

                // 地图尚未绑定新的父对象时，仍可保留一个尚未失效的旧目标。
                if (observedMap != null && !observedMap.Destroyed)
                {
                    observedMapInstance = map;
                    return true;
                }
            }

            // 世界目标已选中但地图尚未生成时，Map 为空是正常状态。
            if (observedMap != null && !observedMap.Destroyed)
            {
                return true;
            }

            ClearObservedMap(false);
            Messages.Message(Props.invalidTargetMessageKey.Translate(), MessageTypeDefOf.RejectInput, false);
            return false;
        }

        private bool IsResearchUnlocked()
        {
            return Props.requiredResearch == null || Props.requiredResearch.IsFinished;
        }

        private string GetResearchDisabledReason()
        {
            if (Props.requiredResearch == null)
            {
                return string.Empty;
            }

            return Props.researchRequiredMessageKey.Translate(Props.requiredResearch.LabelCap);
        }

        private bool IsForbiddenSettlementTarget(MapParent target)
        {
            return RemoteMonitoringUtility.IsForbiddenSettlementTarget(target);
        }

        private string GetNonHostileSettlementReason(MapParent target)
        {
            return RemoteMonitoringUtility.GetNonHostileSettlementReason(target, Props.nonHostileSettlementMessageKey);
        }
    }
}
