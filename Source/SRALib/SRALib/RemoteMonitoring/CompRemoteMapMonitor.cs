using RimWorld;
using RimWorld.Planet;
using System.Collections.Generic;
using Verse;

namespace SRA
{
    public class CompRemoteMapMonitor : ThingComp
    {
        private MapParent observedMap;
        private CompPowerTrader powerComp;

        public CompProperties_RemoteMapMonitor Props => (CompProperties_RemoteMapMonitor)props;
        public MapParent ObservedMapParent => observedMap;
        public bool ShouldKeepTargetAlive => Props.keepMapAliveWhenLinked && IsResearchUnlocked() && parent.Spawned && observedMap != null && !observedMap.Destroyed;

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            powerComp = parent.GetComp<CompPowerTrader>();
            RefreshCacheRegistration();
        }
        public override void PostDeSpawn(Map map, DestroyMode mode = DestroyMode.Vanish)
        {
            base.PostDeSpawn(map);

            if (observedMap != null)
            {
                RemoteMonitoringMapCache.Remove(observedMap);
            }
        }

        public override void CompTickRare()
        {
            base.CompTickRare();
            RefreshCacheRegistration();
        }

        public override void PostDestroy(DestroyMode mode, Map previousMap)
        {
            base.PostDestroy(mode, previousMap);

            if (observedMap != null)
            {
                RemoteMonitoringMapCache.Remove(observedMap);
            }
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

            if (observedMap != null && observedMap != target)
            {
                RemoteMonitoringMapCache.Remove(observedMap);
            }

            observedMap = target;
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
            if (observedMap != null)
            {
                RemoteMonitoringMapCache.Remove(observedMap);
            }

            observedMap = null;

            if (notify)
            {
                Messages.Message(Props.linkDisconnectedMessageKey.Translate(), MessageTypeDefOf.NeutralEvent);
            }
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
            if (!IsResearchUnlocked())
            {
                Messages.Message(GetResearchDisabledReason(), MessageTypeDefOf.RejectInput, false);
                return;
            }

            if (!TryEnsureObservedTargetIsValid() || observedMap == null)
            {
                return;
            }

            if (observedMap.HasMap && observedMap.Map != null)
            {
                FinalizeOpenedMap(observedMap.Map);
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

                LongEventHandler.ExecuteWhenFinished(() => FinalizeOpenedMap(generatedMap));
            }, "GeneratingMap", false, null, true);
        }

        private void FinalizeOpenedMap(Map map)
        {
            if (map == null)
            {
                Messages.Message(Props.openFailedMessageKey.Translate(), MessageTypeDefOf.RejectInput, false);
                return;
            }

            RefreshCacheRegistration();

            if (Props.jumpToMapAfterOpen)
            {
                CameraJumper.TryJump(new GlobalTargetInfo(map.Center, map));
            }
        }

        private void RefreshCacheRegistration()
        {
            if (observedMap == null || observedMap.Destroyed)
            {
                return;
            }

            if (ShouldKeepTargetAlive)
            {
                RemoteMonitoringMapCache.Add(observedMap);
            }
            else
            {
                RemoteMonitoringMapCache.Remove(observedMap);
            }
        }

        private bool TryEnsureObservedTargetIsValid()
        {
            if (observedMap == null)
            {
                return true;
            }

            if (!observedMap.Destroyed)
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
    }
}
