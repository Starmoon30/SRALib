using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace SRA
{
    public class CompProperties_ResurrectionBeacon : CompProperties
    {
        public CompProperties_ResurrectionBeacon()
        {
            compClass = typeof(CompResurrectionBeacon);
        }

        // Optional status hediff applied while the pawn is bound to this beacon.
        public HediffDef boundHediffDef;

        // Optional hediff applied after death is intercepted. The hediff can provide healing, preventsDeath, or incapacity.
        public HediffDef resurrectionHediffDef;

        // Re-adds the resurrection hediff from a clean duration when the pawn is rescued again.
        public bool replaceExistingResurrectionHediff = true;

        // Severity assigned to the optional resurrection hediff.
        public float resurrectionHediffSeverity = 1f;

        // Binding is restricted to pawns the player can normally command.
        public bool requireCanTakeOrderToBind = true;

        // If false, non-player-owned beacons do not expose the binding gizmo.
        public bool requirePlayerFactionForGizmo = true;

        // If true, power must be available for both UI use and death interception.
        public bool requirePower = false;

        // If true, a missing target pawn for an existing binding is removed during cleanup.
        public bool removeDeadBindings = true;

        // Removes boundHediffDef when the pawn is no longer bound to any active beacon using that same hediff.
        public bool removeBoundHediffOnUnbind = true;

        // Prefer a walkable nearby cell instead of placing the pawn directly on the building cell.
        public bool placeNearBeacon = true;

        // Search radius used when placeNearBeacon is true.
        public int teleportCellRadius = 4;

        // Visual feedback at the arrival cell.
        public bool useTeleportFlecks = true;

        // Higher priority beacons are preferred when several active beacons are bound to the same pawn.
        public int resurrectionPriority = 0;

        public string commandIconPath = "UI/Commands/DropCarriedPawn";
        public string bindCommandLabelKey = "SRA_ResurrectionBeacon_BindCommandLabel";
        public string bindCommandDescKey = "SRA_ResurrectionBeacon_BindCommandDesc";
        public string disabledNoPowerKey = "SRA_ResurrectionBeacon_DisabledNoPower";
        public string windowTitleKey = "SRA_ResurrectionBeacon_WindowTitle";
        public string searchLabelKey = "SRA_ResurrectionBeacon_SearchLabel";
        public string boundCountKey = "SRA_ResurrectionBeacon_BoundCount";
        public string bindVisibleKey = "SRA_ResurrectionBeacon_BindVisible";
        public string unbindVisibleKey = "SRA_ResurrectionBeacon_UnbindVisible";
        public string noBindablePawnsKey = "SRA_ResurrectionBeacon_NoBindablePawns";
        public string noSearchResultsKey = "SRA_ResurrectionBeacon_NoSearchResults";
        public string boundStatusKey = "SRA_ResurrectionBeacon_BoundStatus";
        public string unboundStatusKey = "SRA_ResurrectionBeacon_UnboundStatus";
        public string inspectStringKey = "SRA_ResurrectionBeacon_InspectString";
        public string resurrectedMessageKey = "SRA_ResurrectionBeacon_ResurrectedMessage";
    }

    public class CompResurrectionBeacon : ThingComp
    {
        private List<Pawn> boundPawns = new List<Pawn>();
        private CompPowerTrader powerComp;

        public CompProperties_ResurrectionBeacon Props => (CompProperties_ResurrectionBeacon)props;
        public IReadOnlyList<Pawn> BoundPawns => boundPawns;
        public int BoundCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < boundPawns.Count; i++)
                {
                    Pawn pawn = boundPawns[i];
                    if (pawn != null && !pawn.Destroyed)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            powerComp = parent.GetComp<CompPowerTrader>();
            CleanupBoundPawns(updateManager: false);
            parent.Map?.GetComponent<MapComponent_ResurrectionBeaconManager>()?.Register(this);
        }

        public override void PostDeSpawn(Map map, DestroyMode mode = DestroyMode.Vanish)
        {
            base.PostDeSpawn(map);
            map?.GetComponent<MapComponent_ResurrectionBeaconManager>()?.Deregister(this);
        }

        public override void PostDestroy(DestroyMode mode, Map previousMap)
        {
            previousMap?.GetComponent<MapComponent_ResurrectionBeaconManager>()?.Deregister(this);
            UnbindAll(removeBoundHediffs: true);
            base.PostDestroy(mode, previousMap);
        }

        public override void CompTickRare()
        {
            base.CompTickRare();
            CleanupBoundPawns(updateManager: true);
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Collections.Look(ref boundPawns, "boundPawns", LookMode.Reference);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (boundPawns == null)
                {
                    boundPawns = new List<Pawn>();
                }

                CleanupBoundPawns(updateManager: false);
            }
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (Gizmo gizmo in base.CompGetGizmosExtra())
            {
                yield return gizmo;
            }

            if (!ShouldShowBindingGizmo())
            {
                yield break;
            }

            Command_Action command = new Command_Action
            {
                defaultLabel = Props.bindCommandLabelKey.Translate(BoundCount),
                defaultDesc = Props.bindCommandDescKey.Translate(),
                icon = ResolveCommandIcon(),
                action = () => Find.WindowStack.Add(new Dialog_ResurrectionBeaconBindings(this))
            };

            if (!CanUseBeacon())
            {
                command.Disable(Props.disabledNoPowerKey.Translate());
            }

            yield return command;
        }

        public override string CompInspectStringExtra()
        {
            if (BoundCount <= 0)
            {
                return base.CompInspectStringExtra();
            }

            return Props.inspectStringKey.Translate(BoundCount);
        }

        public bool CanBindPawn(Pawn pawn)
        {
            if (pawn == null || pawn.Destroyed || pawn.Dead)
            {
                return false;
            }

            if (Props.requireCanTakeOrderToBind && !pawn.CanTakeOrder)
            {
                return false;
            }

            return true;
        }

        public bool IsBound(Pawn pawn)
        {
            return pawn != null && boundPawns.Contains(pawn);
        }

        public bool BindPawn(Pawn pawn)
        {
            if (!CanBindPawn(pawn) || IsBound(pawn))
            {
                return false;
            }

            boundPawns.Add(pawn);
            ApplyBoundHediff(pawn);
            NotifyManagerPawnBound(pawn);
            return true;
        }

        public bool UnbindPawn(Pawn pawn, bool removeBoundHediff = true)
        {
            if (pawn == null || !boundPawns.Remove(pawn))
            {
                return false;
            }

            NotifyManagerPawnUnbound(pawn);
            if (removeBoundHediff)
            {
                RemoveBoundHediffIfUnused(pawn);
            }

            return true;
        }

        public void BindPawns(IEnumerable<Pawn> pawns)
        {
            if (pawns == null)
            {
                return;
            }

            foreach (Pawn pawn in pawns)
            {
                BindPawn(pawn);
            }
        }

        public void UnbindPawns(IEnumerable<Pawn> pawns)
        {
            if (pawns == null)
            {
                return;
            }

            foreach (Pawn pawn in pawns)
            {
                UnbindPawn(pawn);
            }
        }

        public bool CanResurrectPawn(Pawn pawn)
        {
            return parent != null &&
                   parent.Spawned &&
                   !parent.Destroyed &&
                   parent.Map != null &&
                   IsBound(pawn) &&
                   CanUseBeacon();
        }

        public bool TryResurrectPawn(Pawn pawn, DamageInfo? dinfo, Hediff exactCulprit)
        {
            if (!CanResurrectPawn(pawn))
            {
                return false;
            }

            if (!ResurrectionBeaconUtility.TryTeleportPawnToBeacon(pawn, this))
            {
                return false;
            }

            ApplyResurrectionHediff(pawn);
            if (!Props.resurrectedMessageKey.NullOrEmpty())
            {
                Messages.Message(Props.resurrectedMessageKey.Translate(pawn.LabelShortCap, parent.LabelCap), pawn, MessageTypeDefOf.PositiveEvent, false);
            }

            return true;
        }

        public float GetResurrectionScore(Pawn pawn)
        {
            float score = Props.resurrectionPriority * 100000f;
            if (pawn != null && pawn.Spawned && pawn.Map == parent.Map)
            {
                score += 10000f - pawn.Position.DistanceToSquared(parent.Position);
            }

            return score;
        }

        private bool ShouldShowBindingGizmo()
        {
            if (parent.Map == null)
            {
                return false;
            }

            return !Props.requirePlayerFactionForGizmo || parent.Faction == Faction.OfPlayer;
        }

        private bool CanUseBeacon()
        {
            if (!Props.requirePower)
            {
                return true;
            }

            powerComp ??= parent.GetComp<CompPowerTrader>();
            return powerComp == null || powerComp.PowerOn;
        }

        private void ApplyBoundHediff(Pawn pawn)
        {
            HediffDef hediffDef = Props.boundHediffDef;
            if (hediffDef == null || pawn?.health?.hediffSet == null || pawn.health.hediffSet.HasHediff(hediffDef))
            {
                return;
            }

            pawn.health.AddHediff(hediffDef);
        }

        private void ApplyResurrectionHediff(Pawn pawn)
        {
            HediffDef hediffDef = Props.resurrectionHediffDef;
            if (hediffDef == null || pawn?.health == null)
            {
                return;
            }

            if (Props.replaceExistingResurrectionHediff)
            {
                Hediff existing = pawn.health.hediffSet.GetFirstHediffOfDef(hediffDef);
                if (existing != null)
                {
                    pawn.health.RemoveHediff(existing);
                }
            }

            Hediff hediff = HediffMaker.MakeHediff(hediffDef, pawn);
            hediff.Severity = Props.resurrectionHediffSeverity;
            pawn.health.AddHediff(hediff);
        }

        private void RemoveBoundHediffIfUnused(Pawn pawn)
        {
            HediffDef hediffDef = Props.boundHediffDef;
            if (!Props.removeBoundHediffOnUnbind || hediffDef == null || pawn?.health?.hediffSet == null)
            {
                return;
            }

            if (ResurrectionBeaconUtility.AnyOtherActiveBeaconUsesBoundHediff(pawn, this, hediffDef))
            {
                return;
            }

            Hediff hediff = pawn.health.hediffSet.GetFirstHediffOfDef(hediffDef);
            if (hediff != null)
            {
                pawn.health.RemoveHediff(hediff);
            }
        }

        private void UnbindAll(bool removeBoundHediffs)
        {
            List<Pawn> pawns = new List<Pawn>(boundPawns);
            for (int i = 0; i < pawns.Count; i++)
            {
                UnbindPawn(pawns[i], removeBoundHediffs);
            }
        }

        private void CleanupBoundPawns(bool updateManager)
        {
            if (boundPawns == null || boundPawns.Count == 0)
            {
                return;
            }

            for (int i = boundPawns.Count - 1; i >= 0; i--)
            {
                Pawn pawn = boundPawns[i];
                if (pawn == null || pawn.Destroyed || (Props.removeDeadBindings && pawn.Dead))
                {
                    boundPawns.RemoveAt(i);
                    if (updateManager && pawn != null)
                    {
                        NotifyManagerPawnUnbound(pawn);
                    }
                }
            }
        }

        private void NotifyManagerPawnBound(Pawn pawn)
        {
            parent.Map?.GetComponent<MapComponent_ResurrectionBeaconManager>()?.NotifyPawnBound(this, pawn);
        }

        private void NotifyManagerPawnUnbound(Pawn pawn)
        {
            parent.Map?.GetComponent<MapComponent_ResurrectionBeaconManager>()?.NotifyPawnUnbound(this, pawn);
        }

        private Texture2D ResolveCommandIcon()
        {
            if (Props.commandIconPath.NullOrEmpty())
            {
                return BaseContent.BadTex;
            }

            return ContentFinder<Texture2D>.Get(Props.commandIconPath, false) ?? BaseContent.BadTex;
        }
    }

    public class MapComponent_ResurrectionBeaconManager : MapComponent
    {
        private readonly List<CompResurrectionBeacon> beacons = new List<CompResurrectionBeacon>();
        private readonly Dictionary<Pawn, List<CompResurrectionBeacon>> beaconsByPawn = new Dictionary<Pawn, List<CompResurrectionBeacon>>();

        public MapComponent_ResurrectionBeaconManager(Map map) : base(map)
        {
        }

        public void Register(CompResurrectionBeacon beacon)
        {
            if (beacon == null || beacons.Contains(beacon))
            {
                return;
            }

            beacons.Add(beacon);
            IReadOnlyList<Pawn> boundPawns = beacon.BoundPawns;
            for (int i = 0; i < boundPawns.Count; i++)
            {
                NotifyPawnBound(beacon, boundPawns[i]);
            }
        }

        public void Deregister(CompResurrectionBeacon beacon)
        {
            if (beacon == null)
            {
                return;
            }

            beacons.Remove(beacon);
            IReadOnlyList<Pawn> boundPawns = beacon.BoundPawns;
            for (int i = 0; i < boundPawns.Count; i++)
            {
                NotifyPawnUnbound(beacon, boundPawns[i]);
            }
        }

        public void NotifyPawnBound(CompResurrectionBeacon beacon, Pawn pawn)
        {
            if (beacon == null || pawn == null)
            {
                return;
            }

            if (!beaconsByPawn.TryGetValue(pawn, out List<CompResurrectionBeacon> pawnBeacons))
            {
                pawnBeacons = new List<CompResurrectionBeacon>();
                beaconsByPawn[pawn] = pawnBeacons;
            }

            if (!pawnBeacons.Contains(beacon))
            {
                pawnBeacons.Add(beacon);
            }
        }

        public void NotifyPawnUnbound(CompResurrectionBeacon beacon, Pawn pawn)
        {
            if (beacon == null || pawn == null || !beaconsByPawn.TryGetValue(pawn, out List<CompResurrectionBeacon> pawnBeacons))
            {
                return;
            }

            pawnBeacons.Remove(beacon);
            if (pawnBeacons.Count == 0)
            {
                beaconsByPawn.Remove(pawn);
            }
        }

        public bool TryFindBeaconFor(Pawn pawn, out CompResurrectionBeacon beacon)
        {
            beacon = null;
            if (pawn == null || !beaconsByPawn.TryGetValue(pawn, out List<CompResurrectionBeacon> pawnBeacons))
            {
                return false;
            }

            float bestScore = float.MinValue;
            for (int i = pawnBeacons.Count - 1; i >= 0; i--)
            {
                CompResurrectionBeacon candidate = pawnBeacons[i];
                if (candidate == null || candidate.parent == null || candidate.parent.Destroyed || !candidate.IsBound(pawn))
                {
                    pawnBeacons.RemoveAt(i);
                    continue;
                }

                if (!candidate.CanResurrectPawn(pawn))
                {
                    continue;
                }

                float score = candidate.GetResurrectionScore(pawn);
                if (beacon == null || score > bestScore)
                {
                    beacon = candidate;
                    bestScore = score;
                }
            }

            if (pawnBeacons.Count == 0)
            {
                beaconsByPawn.Remove(pawn);
            }

            return beacon != null;
        }

        public bool AnyOtherActiveBeaconUsesBoundHediff(Pawn pawn, CompResurrectionBeacon excludedBeacon, HediffDef hediffDef)
        {
            if (pawn == null || hediffDef == null || !beaconsByPawn.TryGetValue(pawn, out List<CompResurrectionBeacon> pawnBeacons))
            {
                return false;
            }

            for (int i = 0; i < pawnBeacons.Count; i++)
            {
                CompResurrectionBeacon beacon = pawnBeacons[i];
                if (beacon != null &&
                    beacon != excludedBeacon &&
                    beacon.parent != null &&
                    beacon.parent.Spawned &&
                    beacon.Props.boundHediffDef == hediffDef &&
                    beacon.IsBound(pawn))
                {
                    return true;
                }
            }

            return false;
        }
    }

    public static class ResurrectionBeaconUtility
    {
        public static bool TryInterceptPawnDeath(Pawn pawn, DamageInfo? dinfo, Hediff exactCulprit)
        {
            if (pawn == null || pawn.Destroyed || pawn.Dead || Find.Maps == null)
            {
                return false;
            }

            if (TryFindActiveBeacon(pawn, out CompResurrectionBeacon beacon))
            {
                return beacon.TryResurrectPawn(pawn, dinfo, exactCulprit);
            }

            return false;
        }

        public static bool TryTeleportPawnToBeacon(Pawn pawn, CompResurrectionBeacon beacon)
        {
            if (pawn == null || beacon?.parent == null || !beacon.parent.Spawned || beacon.parent.Map == null)
            {
                return false;
            }

            Map targetMap = beacon.parent.Map;
            IntVec3 targetCell = GetArrivalCell(beacon);
            if (!targetCell.IsValid)
            {
                return false;
            }

            bool wasSpawned = pawn.Spawned;
            Map originalMap = pawn.Map;
            IntVec3 originalCell = pawn.Position;

            if (pawn.Spawned)
            {
                pawn.DeSpawn(DestroyMode.Vanish);
            }

            bool placed = GenPlace.TryPlaceThing(pawn, targetCell, targetMap, ThingPlaceMode.Near);
            if (!placed)
            {
                TryRestorePawnToOriginalPosition(pawn, wasSpawned, originalCell, originalMap);
                return false;
            }

            if (placed && beacon.Props.useTeleportFlecks)
            {
                FleckMaker.ThrowLightningGlow(pawn.Position.ToVector3Shifted(), targetMap, 2f);
                FleckMaker.ThrowDustPuffThick(pawn.Position.ToVector3Shifted(), targetMap, 4f, new Color(0.8f, 0.2f, 1f));
            }

            return true;
        }

        public static bool AnyOtherActiveBeaconUsesBoundHediff(Pawn pawn, CompResurrectionBeacon excludedBeacon, HediffDef hediffDef)
        {
            List<Map> maps = Find.Maps;
            if (maps == null)
            {
                return false;
            }

            for (int i = 0; i < maps.Count; i++)
            {
                MapComponent_ResurrectionBeaconManager manager = maps[i].GetComponent<MapComponent_ResurrectionBeaconManager>();
                if (manager != null && manager.AnyOtherActiveBeaconUsesBoundHediff(pawn, excludedBeacon, hediffDef))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryFindActiveBeacon(Pawn pawn, out CompResurrectionBeacon beacon)
        {
            beacon = null;
            Map pawnMap = pawn.MapHeld;
            if (pawnMap != null && TryFindActiveBeaconOnMap(pawnMap, pawn, out beacon))
            {
                return true;
            }

            List<Map> maps = Find.Maps;
            for (int i = 0; i < maps.Count; i++)
            {
                Map map = maps[i];
                if (map == pawnMap)
                {
                    continue;
                }

                if (TryFindActiveBeaconOnMap(map, pawn, out beacon))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryFindActiveBeaconOnMap(Map map, Pawn pawn, out CompResurrectionBeacon beacon)
        {
            beacon = null;
            MapComponent_ResurrectionBeaconManager manager = map?.GetComponent<MapComponent_ResurrectionBeaconManager>();
            return manager != null && manager.TryFindBeaconFor(pawn, out beacon);
        }

        private static IntVec3 GetArrivalCell(CompResurrectionBeacon beacon)
        {
            if (!beacon.Props.placeNearBeacon)
            {
                return beacon.parent.Position;
            }

            return CellFinder.RandomClosewalkCellNear(beacon.parent.Position, beacon.parent.Map, Mathf.Max(1, beacon.Props.teleportCellRadius));
        }

        private static void TryRestorePawnToOriginalPosition(Pawn pawn, bool wasSpawned, IntVec3 originalCell, Map originalMap)
        {
            if (!wasSpawned || pawn == null || pawn.Spawned || originalMap == null || !originalCell.IsValid)
            {
                return;
            }

            GenPlace.TryPlaceThing(pawn, originalCell, originalMap, ThingPlaceMode.Near);
        }
    }

    public class Dialog_ResurrectionBeaconBindings : Window
    {
        private const float RowHeight = 60f;
        private readonly CompResurrectionBeacon beacon;
        private readonly List<Pawn> candidatePawns = new List<Pawn>();
        private readonly List<Pawn> visiblePawns = new List<Pawn>();
        private Vector2 scrollPosition;
        private string searchText = "";
        private int lastRefreshTick = -9999;

        public Dialog_ResurrectionBeaconBindings(CompResurrectionBeacon beacon)
        {
            this.beacon = beacon;
            forcePause = true;
            doCloseButton = true;
            absorbInputAroundWindow = false;
            closeOnClickedOutside = true;
            RefreshPawnCache(force: true);
        }

        public override Vector2 InitialSize => new Vector2(560f, 720f);

        public override void DoWindowContents(Rect inRect)
        {
            RefreshPawnCache(force: false);
            Rect titleRect = new Rect(inRect.x, inRect.y, inRect.width, 34f);
            Text.Font = GameFont.Medium;
            Widgets.Label(titleRect, beacon.Props.windowTitleKey.Translate());
            Text.Font = GameFont.Small;

            Rect countRect = new Rect(inRect.x, titleRect.yMax + 2f, inRect.width, 24f);
            Widgets.Label(countRect, beacon.Props.boundCountKey.Translate(beacon.BoundCount, candidatePawns.Count));

            Rect searchLabelRect = new Rect(inRect.x, countRect.yMax + 8f, 80f, 28f);
            Widgets.Label(searchLabelRect, beacon.Props.searchLabelKey.Translate());
            Rect searchRect = new Rect(searchLabelRect.xMax + 8f, searchLabelRect.y, inRect.width - searchLabelRect.width - 8f, 28f);
            string newSearchText = Widgets.TextField(searchRect, searchText);
            if (newSearchText != searchText)
            {
                searchText = newSearchText;
                RefreshVisiblePawns();
            }

            Rect buttonRect = new Rect(inRect.x, inRect.yMax - 78f, inRect.width, 32f);
            DrawBatchButtons(buttonRect);

            Rect listRect = new Rect(inRect.x, searchRect.yMax + 10f, inRect.width, buttonRect.yMin - searchRect.yMax - 16f);
            DrawPawnList(listRect);
        }

        private void DrawBatchButtons(Rect rect)
        {
            float gap = 8f;
            float buttonWidth = (rect.width - gap) / 2f;
            Rect bindRect = new Rect(rect.x, rect.y, buttonWidth, rect.height);
            Rect unbindRect = new Rect(bindRect.xMax + gap, rect.y, buttonWidth, rect.height);

            if (Widgets.ButtonText(bindRect, beacon.Props.bindVisibleKey.Translate()))
            {
                beacon.BindPawns(visiblePawns);
                RefreshPawnCache(force: true);
            }

            if (Widgets.ButtonText(unbindRect, beacon.Props.unbindVisibleKey.Translate()))
            {
                beacon.UnbindPawns(visiblePawns);
                RefreshPawnCache(force: true);
            }
        }

        private void DrawPawnList(Rect rect)
        {
            Widgets.DrawMenuSection(rect);

            if (candidatePawns.Count == 0)
            {
                DrawCenteredMessage(rect, beacon.Props.noBindablePawnsKey.Translate());
                return;
            }

            if (visiblePawns.Count == 0)
            {
                DrawCenteredMessage(rect, beacon.Props.noSearchResultsKey.Translate());
                return;
            }

            Rect viewRect = new Rect(0f, 0f, rect.width - 16f, visiblePawns.Count * RowHeight);
            Widgets.BeginScrollView(rect.ContractedBy(4f), ref scrollPosition, viewRect);

            for (int i = 0; i < visiblePawns.Count; i++)
            {
                Rect rowRect = new Rect(0f, i * RowHeight, viewRect.width, RowHeight);
                DrawPawnRow(rowRect, visiblePawns[i]);
            }

            Widgets.EndScrollView();
        }

        private void DrawPawnRow(Rect rect, Pawn pawn)
        {
            bool isBound = beacon.IsBound(pawn);
            if (isBound)
            {
                Widgets.DrawBoxSolid(rect.ContractedBy(1f), new Color(0.15f, 0.45f, 0.35f, 0.25f));
            }

            Widgets.DrawHighlightIfMouseover(rect);

            Rect portraitRect = new Rect(rect.x + 6f, rect.y + 8f, 44f, 44f);
            RenderTexture portrait = PortraitsCache.Get(pawn, new Vector2(44f, 44f), Rot4.South, Vector3.zero, 1.25f);
            GUI.DrawTexture(portraitRect, portrait);

            Rect nameRect = new Rect(portraitRect.xMax + 10f, rect.y + 7f, rect.width - 170f, 24f);
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(nameRect, pawn.LabelShortCap);

            Rect detailRect = new Rect(nameRect.x, nameRect.yMax + 2f, nameRect.width, 22f);
            GUI.color = ColoredText.SubtleGrayColor;
            Widgets.Label(detailRect, pawn.kindDef?.LabelCap ?? pawn.def.LabelCap);
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;

            Rect statusRect = new Rect(rect.xMax - 112f, rect.y + 18f, 76f, 24f);
            Text.Anchor = TextAnchor.MiddleRight;
            Widgets.Label(statusRect, (isBound ? beacon.Props.boundStatusKey : beacon.Props.unboundStatusKey).Translate());
            Text.Anchor = TextAnchor.UpperLeft;

            Rect checkRect = new Rect(rect.xMax - 30f, rect.y + 18f, 24f, 24f);
            bool checkValue = isBound;
            Widgets.Checkbox(checkRect.position, ref checkValue, 24f);
            if (checkValue != isBound)
            {
                TogglePawnBinding(pawn);
                RefreshPawnCache(force: true);
                return;
            }

            Rect clickRect = new Rect(rect.x, rect.y, checkRect.xMin - rect.x, rect.height);
            if (Widgets.ButtonInvisible(clickRect))
            {
                TogglePawnBinding(pawn);
                RefreshPawnCache(force: true);
            }
        }

        private void TogglePawnBinding(Pawn pawn)
        {
            if (beacon.IsBound(pawn))
            {
                beacon.UnbindPawn(pawn);
            }
            else
            {
                beacon.BindPawn(pawn);
            }
        }

        private void DrawCenteredMessage(Rect rect, TaggedString text)
        {
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = ColoredText.SubtleGrayColor;
            Widgets.Label(rect, text);
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;
        }

        private void RefreshPawnCache(bool force)
        {
            if (!force && Find.TickManager.TicksGame - lastRefreshTick < 120)
            {
                return;
            }

            lastRefreshTick = Find.TickManager.TicksGame;
            candidatePawns.Clear();
            Map map = beacon.parent.Map;
            if (map?.mapPawns != null)
            {
                List<Pawn> allPawns = map.mapPawns.AllPawns;
                for (int i = 0; i < allPawns.Count; i++)
                {
                    Pawn pawn = allPawns[i];
                    if (beacon.CanBindPawn(pawn) || beacon.IsBound(pawn))
                    {
                        candidatePawns.Add(pawn);
                    }
                }
            }

            IReadOnlyList<Pawn> boundPawns = beacon.BoundPawns;
            for (int i = 0; i < boundPawns.Count; i++)
            {
                Pawn pawn = boundPawns[i];
                if (pawn != null && !candidatePawns.Contains(pawn))
                {
                    candidatePawns.Add(pawn);
                }
            }

            candidatePawns.SortBy(pawn => pawn.LabelShortCap.ToString());
            RefreshVisiblePawns();
        }

        private void RefreshVisiblePawns()
        {
            visiblePawns.Clear();
            for (int i = 0; i < candidatePawns.Count; i++)
            {
                Pawn pawn = candidatePawns[i];
                if (MatchesSearch(pawn))
                {
                    visiblePawns.Add(pawn);
                }
            }
        }

        private bool MatchesSearch(Pawn pawn)
        {
            if (searchText.NullOrEmpty())
            {
                return true;
            }

            return pawn.LabelShortCap.ToString().IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.Kill))]
    public static class Patch_Pawn_Kill_ResurrectionBeacon
    {
        public static bool Prefix(Pawn __instance, DamageInfo? dinfo, Hediff exactCulprit = null)
        {
            return !ResurrectionBeaconUtility.TryInterceptPawnDeath(__instance, dinfo, exactCulprit);
        }
    }
}
