using System;
using System.Collections.Generic;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;

namespace SRA
{
    public class SRAWarUnitSpawnerUnitProperties
    {
        // Unit type to generate. If this is missing, the entry is ignored.
        public PawnKindDef pawnKindDef;

        // Optional keyed translation used instead of the pawn kind label in gizmos and inspect strings.
        public string labelKey;

        // Ticks required to produce one stored unit of this type.
        public int generationTicks = 3000;

        // Maximum stored units for this unit type.
        public int maxStored = 1;

        public bool Valid => pawnKindDef?.race != null && generationTicks > 0 && maxStored > 0;

        public string Label => !labelKey.NullOrEmpty() ? labelKey.Translate().ToString() : pawnKindDef?.LabelCap.Resolve() ?? "Unknown".Translate().ToString();
    }

    public class SRAWarUnitSpawnerLocalizationProperties
    {
        private const string DefaultKeyPrefix = "SRA_WarUnitSpawner";

        // Prefix used to build all unspecified keyed translation names.
        public string keyPrefix = DefaultKeyPrefix;

        // Key for the auto deployment toggle label.
        public string autoModeLabelKey;

        // Key for the auto deployment toggle description.
        public string autoModeDescKey;

        // Key for the manual deploy command label. Argument {0}: selected unit label.
        public string deployLabelKey;

        // Key for the manual deploy command description.
        public string deployDescKey;

        // Key for the unit selection command label.
        public string changeUnitLabelKey;

        // Key for the unit selection command description. Argument {0}: selected unit label.
        public string changeUnitDescKey;

        // Key for the inspect string header.
        public string statusHeaderKey;

        // Key for each inspect string unit line. Arguments: {0} unit label, {1} stored, {2} max, {3} progress percent.
        public string statusLineKey;

        // Key for each float menu selection row. Arguments: {0} unit label, {1} stored, {2} max.
        public string selectionOptionKey;

        // Key used when no valid unit definition is available.
        public string disabledNoUnitKey;

        // Key used when power is required but unavailable.
        public string disabledNoPowerKey;

        // Key used when the selected unit has no stored stock. Argument {0}: unit label.
        public string disabledNoStockKey;

        // Key used when no unit is selected.
        public string noUnitKey;

        public string AutoModeLabelKey => ResolveKey(autoModeLabelKey, "AutoMode");
        public string AutoModeDescKey => ResolveKey(autoModeDescKey, "AutoModeDesc");
        public string DeployLabelKey => ResolveKey(deployLabelKey, "Deploy");
        public string DeployDescKey => ResolveKey(deployDescKey, "DeployDesc");
        public string ChangeUnitLabelKey => ResolveKey(changeUnitLabelKey, "ChangeUnit");
        public string ChangeUnitDescKey => ResolveKey(changeUnitDescKey, "ChangeUnitDesc");
        public string StatusHeaderKey => ResolveKey(statusHeaderKey, "StatusHeader");
        public string StatusLineKey => ResolveKey(statusLineKey, "StatusLine");
        public string SelectionOptionKey => ResolveKey(selectionOptionKey, "SelectionOption");
        public string DisabledNoUnitKey => ResolveKey(disabledNoUnitKey, "DisabledNoUnit");
        public string DisabledNoPowerKey => ResolveKey(disabledNoPowerKey, "DisabledNoPower");
        public string DisabledNoStockKey => ResolveKey(disabledNoStockKey, "DisabledNoStock");
        public string NoUnitKey => ResolveKey(noUnitKey, "NoUnit");

        private string ResolveKey(string overrideKey, string suffix)
        {
            if (!overrideKey.NullOrEmpty())
            {
                return overrideKey;
            }

            string prefix = keyPrefix.NullOrEmpty() ? DefaultKeyPrefix : keyPrefix;
            return prefix + "_" + suffix;
        }
    }

    public class CompProperties_SRAWarUnitSpawner : CompProperties
    {
        // Preferred modern configuration. Each unit type has its own generation time and storage cap.
        public List<SRAWarUnitSpawnerUnitProperties> units = new List<SRAWarUnitSpawnerUnitProperties>();

        // Keyed localization configuration. Defs should provide translation keys here, not literal display text.
        public SRAWarUnitSpawnerLocalizationProperties localization = new SRAWarUnitSpawnerLocalizationProperties();

        // Optional hediff applied to each generated unit. Leave null to avoid binding SRALib to child mod hediff defs.
        public HediffDef deathCountdownHediffDef;

        // Severity used when deathCountdownHediffDef is applied.
        public float deathCountdownHediffSeverity = 1f;

        // Default state for the auto-deploy toggle.
        public bool autoModeDefault = true;

        // Whether the building should respect CompPowerTrader when present.
        public bool requirePower = true;

        // How often production state is advanced. Larger values reduce tick overhead.
        public int productionCheckIntervalTicks = 300;

        // How often auto mode checks for map threats.
        public int threatCheckIntervalTicks = 300;

        // Radius used to find a nearby spawn cell.
        public float spawnRadius = 3.9f;

        // Allows manual deployment gizmo.
        public bool allowManualSpawn = true;

        // Allows unit selection gizmo when multiple unit types are configured.
        public bool allowUnitSelection = true;

        // Pawn generation options.
        public bool forceGenerateNewPawn = true;
        public bool allowDead = false;
        public bool allowDowned = false;
        public bool canGeneratePawnRelations = false;
        public bool mustBeCapableOfViolence = true;
        public PawnGenerationContext generationContext = PawnGenerationContext.NonPlayer;

        private List<SRAWarUnitSpawnerUnitProperties> cachedUnits;

        public CompProperties_SRAWarUnitSpawner()
        {
            compClass = typeof(Comp_SRAWarUnitSpawner);
        }

        public List<SRAWarUnitSpawnerUnitProperties> ConfiguredUnits
        {
            get
            {
                if (cachedUnits == null)
                {
                    cachedUnits = BuildConfiguredUnits();
                }

                return cachedUnits;
            }
        }

        public int ProductionCheckIntervalTicks => Mathf.Max(1, productionCheckIntervalTicks);
        public int ThreatCheckIntervalTicks => Mathf.Max(1, threatCheckIntervalTicks);
        public SRAWarUnitSpawnerLocalizationProperties Localization => localization ?? (localization = new SRAWarUnitSpawnerLocalizationProperties());

        private List<SRAWarUnitSpawnerUnitProperties> BuildConfiguredUnits()
        {
            List<SRAWarUnitSpawnerUnitProperties> result = new List<SRAWarUnitSpawnerUnitProperties>();
            if (!units.NullOrEmpty())
            {
                AddValidUnits(result, units);
            }

            return result;
        }

        private static void AddValidUnits(List<SRAWarUnitSpawnerUnitProperties> result, List<SRAWarUnitSpawnerUnitProperties> source)
        {
            for (int i = 0; i < source.Count; i++)
            {
                AddValidUnit(result, source[i]);
            }
        }

        private static void AddValidUnit(List<SRAWarUnitSpawnerUnitProperties> result, SRAWarUnitSpawnerUnitProperties unit)
        {
            if (unit == null || !unit.Valid || ContainsPawnKind(result, unit.pawnKindDef))
            {
                return;
            }

            result.Add(unit);
        }

        private static bool ContainsPawnKind(List<SRAWarUnitSpawnerUnitProperties> units, PawnKindDef pawnKindDef)
        {
            for (int i = 0; i < units.Count; i++)
            {
                if (units[i].pawnKindDef == pawnKindDef)
                {
                    return true;
                }
            }

            return false;
        }
    }

    public class SRAWarUnitSpawnerUnitState : IExposable
    {
        public PawnKindDef pawnKindDef;
        public int storedCount;
        public int progressTicks;

        public SRAWarUnitSpawnerUnitState()
        {
        }

        public SRAWarUnitSpawnerUnitState(PawnKindDef pawnKindDef)
        {
            this.pawnKindDef = pawnKindDef;
        }

        public void ExposeData()
        {
            Scribe_Defs.Look(ref pawnKindDef, "pawnKindDef");
            Scribe_Values.Look(ref storedCount, "storedCount");
            Scribe_Values.Look(ref progressTicks, "progressTicks");
        }
    }

    public class Comp_SRAWarUnitSpawner : ThingComp
    {
        private bool autoMode;
        private PawnKindDef selectedPawnKindDef;
        private List<SRAWarUnitSpawnerUnitState> unitStates = new List<SRAWarUnitSpawnerUnitState>();
        private int lastProductionTick = -1;
        private int nextProductionTick = -1;
        private int nextThreatCheckTick = -1;

        public CompProperties_SRAWarUnitSpawner Props => (CompProperties_SRAWarUnitSpawner)props;

        private bool HasConfiguredUnits => Props.ConfiguredUnits.Count > 0;

        public override void PostPostMake()
        {
            base.PostPostMake();
            autoMode = Props.autoModeDefault;
            EnsureStateIntegrity();
        }

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            EnsureStateIntegrity();
            EnsureTickSchedule();
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref autoMode, "autoMode", Props.autoModeDefault);
            Scribe_Defs.Look(ref selectedPawnKindDef, "selectedPawnKindDef");
            Scribe_Collections.Look(ref unitStates, "unitStates", LookMode.Deep);
            Scribe_Values.Look(ref lastProductionTick, "lastProductionTick", -1);
            Scribe_Values.Look(ref nextProductionTick, "nextProductionTick", -1);
            Scribe_Values.Look(ref nextThreatCheckTick, "nextThreatCheckTick", -1);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (unitStates == null)
                {
                    unitStates = new List<SRAWarUnitSpawnerUnitState>();
                }

                EnsureStateIntegrity();
            }
        }

        public override void CompTick()
        {
            base.CompTick();
            if (!parent.Spawned || !HasConfiguredUnits)
            {
                return;
            }

            EnsureTickSchedule();
            int now = Find.TickManager.TicksGame;
            if (now >= nextProductionTick)
            {
                UpdateProduction(now);
                nextProductionTick = now + Props.ProductionCheckIntervalTicks;
            }

            if (autoMode && now >= nextThreatCheckTick)
            {
                nextThreatCheckTick = now + Props.ThreatCheckIntervalTicks;
                TryAutoDeploy();
            }
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (Gizmo gizmo in base.CompGetGizmosExtra())
            {
                yield return gizmo;
            }

            if (!HasConfiguredUnits)
            {
                yield break;
            }

            yield return new Command_Toggle
            {
                defaultLabel = Props.Localization.AutoModeLabelKey.Translate(),
                defaultDesc = Props.Localization.AutoModeDescKey.Translate(),
                icon = TexCommand.Attack,
                isActive = () => autoMode,
                toggleAction = () => autoMode = !autoMode
            };

            if (Props.allowManualSpawn)
            {
                Command_Action deployCommand = new Command_Action
                {
                    defaultLabel = Props.Localization.DeployLabelKey.Translate(GetSelectedUnitLabel()),
                    defaultDesc = Props.Localization.DeployDescKey.Translate(),
                    icon = GetSelectedUnitIcon(),
                    action = () => TryDeploySelected()
                };

                if (!CanDeploySelected(out string disabledReason))
                {
                    deployCommand.Disable(disabledReason);
                }

                yield return deployCommand;
            }

            if (Props.allowUnitSelection && Props.ConfiguredUnits.Count > 1)
            {
                yield return new Command_Action
                {
                    defaultLabel = Props.Localization.ChangeUnitLabelKey.Translate(),
                    defaultDesc = Props.Localization.ChangeUnitDescKey.Translate(GetSelectedUnitLabel()),
                    icon = GetSelectedUnitIcon(),
                    action = OpenUnitSelectionMenu
                };
            }
        }

        public override string CompInspectStringExtra()
        {
            string baseString = base.CompInspectStringExtra();
            if (!HasConfiguredUnits)
            {
                return baseString;
            }

            StringBuilder builder = new StringBuilder();
            if (!baseString.NullOrEmpty())
            {
                builder.AppendLine(baseString);
            }

            builder.AppendLine(Props.Localization.StatusHeaderKey.Translate());
            SRAWarUnitSpawnerUnitProperties selectedUnit = GetUnitProperties(selectedPawnKindDef);
            if (selectedUnit == null)
            {
                builder.AppendLine(Props.Localization.NoUnitKey.Translate());
            }
            else
            {
                builder.AppendLine(GetUnitStatusLine(selectedUnit));
            }

            return builder.ToString().TrimEndNewlines();
        }

        private string GetUnitStatusLine(SRAWarUnitSpawnerUnitProperties unit)
        {
            SRAWarUnitSpawnerUnitState state = GetState(unit.pawnKindDef);
            int stored = state?.storedCount ?? 0;
            int progress = state != null ? Mathf.Clamp(state.progressTicks, 0, unit.generationTicks) : 0;
            float progressPercent = stored >= unit.maxStored ? 1f : progress / (float)unit.generationTicks;
            return Props.Localization.StatusLineKey.Translate(
                unit.Label,
                stored.ToString(),
                unit.maxStored.ToString(),
                progressPercent.ToStringPercent());
        }

        private void EnsureTickSchedule()
        {
            int now = Find.TickManager.TicksGame;
            if (lastProductionTick < 0)
            {
                lastProductionTick = now;
            }

            if (nextProductionTick <= 0)
            {
                nextProductionTick = now + HashOffset(Props.ProductionCheckIntervalTicks);
            }

            if (nextThreatCheckTick <= 0)
            {
                nextThreatCheckTick = now + HashOffset(Props.ThreatCheckIntervalTicks);
            }
        }

        private int HashOffset(int interval)
        {
            if (interval <= 1)
            {
                return 1;
            }

            return Mathf.Abs(parent.thingIDNumber) % interval;
        }

        private void EnsureStateIntegrity()
        {
            if (unitStates == null)
            {
                unitStates = new List<SRAWarUnitSpawnerUnitState>();
            }

            List<SRAWarUnitSpawnerUnitProperties> units = Props.ConfiguredUnits;
            for (int i = unitStates.Count - 1; i >= 0; i--)
            {
                SRAWarUnitSpawnerUnitState state = unitStates[i];
                SRAWarUnitSpawnerUnitProperties unit = state != null ? GetUnitProperties(state.pawnKindDef) : null;
                if (unit == null)
                {
                    unitStates.RemoveAt(i);
                    continue;
                }

                state.storedCount = Mathf.Clamp(state.storedCount, 0, unit.maxStored);
                state.progressTicks = Mathf.Clamp(state.progressTicks, 0, unit.generationTicks - 1);
            }

            for (int i = 0; i < units.Count; i++)
            {
                if (GetState(units[i].pawnKindDef) == null)
                {
                    unitStates.Add(new SRAWarUnitSpawnerUnitState(units[i].pawnKindDef));
                }
            }

            if (GetUnitProperties(selectedPawnKindDef) == null)
            {
                selectedPawnKindDef = units.Count > 0 ? units[0].pawnKindDef : null;
            }

            ClearNonSelectedUnitStock();
        }

        private void UpdateProduction(int now)
        {
            if (!CanOperate())
            {
                lastProductionTick = now;
                return;
            }

            int elapsedTicks = lastProductionTick >= 0 ? now - lastProductionTick : 0;
            lastProductionTick = now;
            if (elapsedTicks <= 0)
            {
                return;
            }

            SRAWarUnitSpawnerUnitProperties selectedUnit = GetUnitProperties(selectedPawnKindDef);
            if (selectedUnit == null)
            {
                return;
            }

            ProduceUnit(selectedUnit, elapsedTicks);
        }

        private void ProduceUnit(SRAWarUnitSpawnerUnitProperties unit, int elapsedTicks)
        {
            SRAWarUnitSpawnerUnitState state = GetState(unit.pawnKindDef);
            if (state == null || state.storedCount >= unit.maxStored)
            {
                if (state != null)
                {
                    state.progressTicks = 0;
                }
                return;
            }

            state.progressTicks += elapsedTicks;
            while (state.progressTicks >= unit.generationTicks && state.storedCount < unit.maxStored)
            {
                state.storedCount++;
                state.progressTicks -= unit.generationTicks;
            }

            if (state.storedCount >= unit.maxStored)
            {
                state.progressTicks = 0;
            }
        }

        private void TryAutoDeploy()
        {
            if (!CanOperate() || parent.Map == null || parent.Faction == null || !SelectedUnitHasStock())
            {
                return;
            }

            if (GenHostility.AnyHostileActiveThreatTo(parent.Map, parent.Faction, false, false))
            {
                TryDeploySelected();
            }
        }

        private bool SelectedUnitHasStock()
        {
            SRAWarUnitSpawnerUnitState state = GetState(selectedPawnKindDef);
            return state != null && state.storedCount > 0;
        }

        private bool TryDeploySelected()
        {
            SRAWarUnitSpawnerUnitProperties unit = GetUnitProperties(selectedPawnKindDef);
            return unit != null && TryDeployUnit(unit);
        }

        private bool TryDeployUnit(SRAWarUnitSpawnerUnitProperties unit)
        {
            if (unit == null || !CanDeployUnit(unit, out _))
            {
                return false;
            }

            SRAWarUnitSpawnerUnitState state = GetState(unit.pawnKindDef);
            if (!TryFindSpawnCell(out IntVec3 spawnCell))
            {
                return false;
            }

            try
            {
                Pawn pawn = PawnGenerator.GeneratePawn(new PawnGenerationRequest(
                    unit.pawnKindDef,
                    parent.Faction,
                    Props.generationContext,
                    forceGenerateNewPawn: Props.forceGenerateNewPawn,
                    allowDead: Props.allowDead,
                    allowDowned: Props.allowDowned,
                    canGeneratePawnRelations: Props.canGeneratePawnRelations,
                    mustBeCapableOfViolence: Props.mustBeCapableOfViolence));

                GenSpawn.Spawn(pawn, spawnCell, parent.Map, WipeMode.Vanish);
                ApplyDeathCountdownHediff(pawn);
                state.storedCount--;
                return true;
            }
            catch (Exception ex)
            {
                Log.Error("[SRA] Failed to spawn war unit from " + parent.ToStringSafe() + ": " + ex);
                return false;
            }
        }

        private void ApplyDeathCountdownHediff(Pawn pawn)
        {
            HediffDef hediffDef = Props.deathCountdownHediffDef;
            if (hediffDef == null || pawn?.health == null)
            {
                return;
            }

            Hediff hediff = HediffMaker.MakeHediff(hediffDef, pawn);
            if (Props.deathCountdownHediffSeverity >= 0f)
            {
                hediff.Severity = Props.deathCountdownHediffSeverity;
            }

            pawn.health.AddHediff(hediff);
        }

        private bool CanDeploySelected(out string disabledReason)
        {
            SRAWarUnitSpawnerUnitProperties unit = GetUnitProperties(selectedPawnKindDef);
            return CanDeployUnit(unit, out disabledReason);
        }

        private bool CanDeployUnit(SRAWarUnitSpawnerUnitProperties unit, out string disabledReason)
        {
            disabledReason = null;
            if (unit == null)
            {
                disabledReason = Props.Localization.DisabledNoUnitKey.Translate();
                return false;
            }

            if (!CanOperate())
            {
                disabledReason = Props.Localization.DisabledNoPowerKey.Translate();
                return false;
            }

            if (parent.Faction == null || parent.Map == null)
            {
                disabledReason = Props.Localization.DisabledNoUnitKey.Translate();
                return false;
            }

            SRAWarUnitSpawnerUnitState state = GetState(unit.pawnKindDef);
            if (state == null || state.storedCount <= 0)
            {
                disabledReason = Props.Localization.DisabledNoStockKey.Translate(unit.Label);
                return false;
            }

            return true;
        }

        private bool CanOperate()
        {
            if (!Props.requirePower)
            {
                return true;
            }

            CompPowerTrader power = parent.TryGetComp<CompPowerTrader>();
            return power == null || power.PowerOn;
        }

        private bool TryFindSpawnCell(out IntVec3 spawnCell)
        {
            Map map = parent.Map;
            if (map == null)
            {
                spawnCell = IntVec3.Invalid;
                return false;
            }

            if (CanSpawnAt(parent.Position, map))
            {
                spawnCell = parent.Position;
                return true;
            }

            foreach (IntVec3 cell in GenRadial.RadialCellsAround(parent.Position, Props.spawnRadius, true))
            {
                if (CanSpawnAt(cell, map))
                {
                    spawnCell = cell;
                    return true;
                }
            }

            spawnCell = IntVec3.Invalid;
            return false;
        }

        private static bool CanSpawnAt(IntVec3 cell, Map map)
        {
            return cell.InBounds(map) && !cell.Fogged(map) && cell.Standable(map);
        }

        private void OpenUnitSelectionMenu()
        {
            List<FloatMenuOption> options = new List<FloatMenuOption>();
            List<SRAWarUnitSpawnerUnitProperties> units = Props.ConfiguredUnits;
            for (int i = 0; i < units.Count; i++)
            {
                SRAWarUnitSpawnerUnitProperties unit = units[i];
                SRAWarUnitSpawnerUnitState state = GetState(unit.pawnKindDef);
                int stored = state?.storedCount ?? 0;
                string label = Props.Localization.SelectionOptionKey.Translate(unit.Label, stored.ToString(), unit.maxStored.ToString());
                PawnKindDef capturedPawnKind = unit.pawnKindDef;
                options.Add(new FloatMenuOption(
                    label,
                    () => SelectUnit(capturedPawnKind),
                    unit.pawnKindDef.race?.uiIcon,
                    Color.white));
            }

            Find.WindowStack.Add(new FloatMenu(options));
        }

        private void SelectUnit(PawnKindDef pawnKindDef)
        {
            if (GetUnitProperties(pawnKindDef) == null || selectedPawnKindDef == pawnKindDef)
            {
                return;
            }

            selectedPawnKindDef = pawnKindDef;
            ClearAllUnitStock();
            lastProductionTick = Find.TickManager.TicksGame;
        }

        private void ClearAllUnitStock()
        {
            if (unitStates == null)
            {
                return;
            }

            for (int i = 0; i < unitStates.Count; i++)
            {
                SRAWarUnitSpawnerUnitState state = unitStates[i];
                if (state == null)
                {
                    continue;
                }

                state.storedCount = 0;
                state.progressTicks = 0;
            }
        }

        private void ClearNonSelectedUnitStock()
        {
            if (unitStates == null)
            {
                return;
            }

            for (int i = 0; i < unitStates.Count; i++)
            {
                SRAWarUnitSpawnerUnitState state = unitStates[i];
                if (state == null || state.pawnKindDef == selectedPawnKindDef)
                {
                    continue;
                }

                state.storedCount = 0;
                state.progressTicks = 0;
            }
        }

        private SRAWarUnitSpawnerUnitProperties GetUnitProperties(PawnKindDef pawnKindDef)
        {
            if (pawnKindDef == null)
            {
                return null;
            }

            List<SRAWarUnitSpawnerUnitProperties> units = Props.ConfiguredUnits;
            for (int i = 0; i < units.Count; i++)
            {
                if (units[i].pawnKindDef == pawnKindDef)
                {
                    return units[i];
                }
            }

            return null;
        }

        private SRAWarUnitSpawnerUnitState GetState(PawnKindDef pawnKindDef)
        {
            if (pawnKindDef == null || unitStates == null)
            {
                return null;
            }

            for (int i = 0; i < unitStates.Count; i++)
            {
                if (unitStates[i]?.pawnKindDef == pawnKindDef)
                {
                    return unitStates[i];
                }
            }

            return null;
        }

        private string GetSelectedUnitLabel()
        {
            SRAWarUnitSpawnerUnitProperties unit = GetUnitProperties(selectedPawnKindDef);
            return unit?.Label ?? Props.Localization.NoUnitKey.Translate();
        }

        private Texture2D GetSelectedUnitIcon()
        {
            SRAWarUnitSpawnerUnitProperties unit = GetUnitProperties(selectedPawnKindDef);
            return unit?.pawnKindDef?.race?.uiIcon ?? BaseContent.BadTex;
        }
    }
}
