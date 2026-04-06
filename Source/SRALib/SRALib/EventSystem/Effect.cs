using System; // Required for Activator
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using RimWorld;
using System.Security.AccessControl;
using Verse.Noise;
using static RimWorld.FleshTypeDef;

namespace SRA
{
    public abstract class Effect
    {
        public float weight = 1.0f;
        public abstract void Execute(Window dialog = null);
    }

    public class Effect_OpenCustomUI : Effect
    {
        public string defName;
        public int delayTicks = 0;

        public override void Execute(Window dialog = null)
        {
            if (delayTicks > 0)
            {
                var actionManager = Find.World.GetComponent<DelayedActionManager>();
                if (actionManager != null)
                {
                    actionManager.AddAction(defName, delayTicks);
                }
                else
                {
                    Log.Error("[SRA] DelayedActionManager not found. Cannot schedule delayed UI opening.");
                }
            }
            else
            {
                OpenUI();
            }
        }

        private void OpenUI()
        {
            EventDef nextDef = DefDatabase<EventDef>.GetNamed(defName);
            if (nextDef != null)
            {
                if (nextDef.hiddenWindow)
                {
                    if (!nextDef.dismissEffects.NullOrEmpty())
                    {
                        foreach (var conditionalEffect in nextDef.dismissEffects)
                        {
                            string reason;
                            if (AreConditionsMet(conditionalEffect.conditions, out reason))
                            {
                                conditionalEffect.Execute(null);
                            }
                        }
                    }
                }
                else
                {
                    Find.WindowStack.Add((Window)Activator.CreateInstance(nextDef.windowType, nextDef));
                }
            }
            else
            {
                Log.Error($"[SRA] Effect_OpenCustomUI could not find EventDef named '{defName}'");
            }
        }

        private bool AreConditionsMet(List<Condition> conditions, out string reason)
        {
            reason = "";
            if (conditions.NullOrEmpty())
            {
                return true;
            }

            foreach (var condition in conditions)
            {
                if (!condition.IsMet(out string singleReason))
                {
                    reason = singleReason;
                    return false;
                }
            }
            return true;
        }
    }

    public class Effect_CloseDialog : Effect
    {
        public override void Execute(Window dialog = null)
        {
            dialog?.Close();
        }
    }

    public class Effect_ShowMessage : Effect
    {
        public string message;
        public MessageTypeDef messageTypeDef;

        public override void Execute(Window dialog = null)
        {
            if (messageTypeDef == null)
            {
                messageTypeDef = MessageTypeDefOf.PositiveEvent;
            }
            Messages.Message(message.Translate(), messageTypeDef);
        }
    }

    public class Effect_FireIncident : Effect
    {
        public IncidentDef incident;

        public override void Execute(Window dialog = null)
        {
            if (incident == null)
            {
                Log.Error("[SRA] Effect_FireIncident has a null incident Def.");
                return;
            }

            IncidentParms parms = new IncidentParms
            {
                target = Find.CurrentMap,
                forced = true
            };

            if (!incident.Worker.TryExecute(parms))
            {
                Log.Error($"[SRA] Could not fire incident {incident.defName}");
            }
        }
    }

    public class Effect_ChangeFactionRelation : Effect
    {
        public FactionDef faction;
        public int goodwillChange;

        public override void Execute(Window dialog = null)
        {
            if (faction == null)
            {
                Log.Error("[SRA] Effect_ChangeFactionRelation has a null faction Def.");
                return;
            }

            Faction targetFaction = Find.FactionManager.FirstFactionOfDef(faction);
            if (targetFaction == null)
            {
                Log.Warning($"[SRA] Could not find an active faction for FactionDef '{faction.defName}'.");
                return;
            }

            Faction.OfPlayer.TryAffectGoodwillWith(targetFaction, goodwillChange, canSendMessage: true, canSendHostilityLetter: true, reason: null, lookTarget: null);
        }
    }

    public class Effect_SetVariable : Effect
    {
        public string name;
        public string value;
        public string type; // Int, Float, String, Bool
        public bool forceSet = false;

        public override void Execute(Window dialog = null)
        {
            var eventVarManager = Find.World.GetComponent<EventVariableManager>();
            if (!forceSet && eventVarManager.HasVariable(name))
            {
                return;
            }

            object realValue = value;
            if (!string.IsNullOrEmpty(type))
            {
                if (type.Equals("int", System.StringComparison.OrdinalIgnoreCase) && int.TryParse(value, out int intVal))
                {
                    realValue = intVal;
                }
                else if (type.Equals("float", System.StringComparison.OrdinalIgnoreCase) && float.TryParse(value, out float floatVal))
                {
                    realValue = floatVal;
                }
                else if (type.Equals("bool", System.StringComparison.OrdinalIgnoreCase) && bool.TryParse(value, out bool boolVal))
                {
                    realValue = boolVal;
                }
            }
            eventVarManager.SetVariable(name, realValue);
        }
    }
    
    public class Effect_ChangeFactionRelation_FromVariable : Effect
    {
        public FactionDef faction;
        public string goodwillVariableName;

        public override void Execute(Window dialog = null)
        {
            if (faction == null)
            {
                Log.Error("[SRA] Effect_ChangeFactionRelation_FromVariable has a null faction Def.");
                return;
            }
            
            Faction targetFaction = Find.FactionManager.FirstFactionOfDef(faction);
            if (targetFaction == null)
            {
                Log.Warning($"[SRA] Could not find an active faction for FactionDef '{faction.defName}'.");
                return;
            }

            int goodwillChange = Find.World.GetComponent<EventVariableManager>().GetVariable<int>(goodwillVariableName);
            Faction.OfPlayer.TryAffectGoodwillWith(targetFaction, goodwillChange, canSendMessage: true, canSendHostilityLetter: true, reason: null, lookTarget: null);
        }
    }

    public class Effect_SpawnPawnAndStore : Effect
    {
        public PawnKindDef kindDef;
        public int count = 1;
        public string storeAs;

        public override void Execute(Window dialog = null)
        {
            if (kindDef == null)
            {
                Log.Error("[SRA] Effect_SpawnPawnAndStore has a null kindDef.");
                return;
            }
            if (storeAs.NullOrEmpty())
            {
                Log.Error("[SRA] Effect_SpawnPawnAndStore needs a 'storeAs' variable name.");
                return;
            }

            var eventVarManager = Find.World.GetComponent<EventVariableManager>();
            List<Pawn> spawnedPawns = new List<Pawn>();
            for (int i = 0; i < count; i++)
            {
                Pawn newPawn = PawnGenerator.GeneratePawn(kindDef, Faction.OfPlayer);
                IntVec3 loc = CellFinder.RandomSpawnCellForPawnNear(Find.CurrentMap.mapPawns.FreeColonists.First().Position, Find.CurrentMap, 10);
                GenSpawn.Spawn(newPawn, loc, Find.CurrentMap);
                spawnedPawns.Add(newPawn);
            }

            if (count == 1)
            {
                eventVarManager.SetVariable(storeAs, spawnedPawns.First());
            }
            else
            {
                eventVarManager.SetVariable(storeAs, spawnedPawns);
            }
        }
    }

    public class Effect_GiveThing : Effect
    {
        public ThingDef thingDef;
        public int count = 1;

        public override void Execute(Window dialog = null)
        {
            if (thingDef == null)
            {
                Log.Error("[SRA] Effect_GiveThing has a null thingDef.");
                return;
            }

            Map currentMap = Find.CurrentMap;
            if (currentMap == null)
            {
                Log.Error("[SRA] Effect_GiveThing cannot execute without a current map.");
                return;
            }

            Thing thing = ThingMaker.MakeThing(thingDef);
            thing.stackCount = count;

            IntVec3 dropCenter = DropCellFinder.TradeDropSpot(currentMap);
            DropPodUtility.DropThingsNear(dropCenter, currentMap, new List<Thing> { thing }, 110, false, false, false, false);

            Messages.Message("LetterLabelCargoPodCrash".Translate(), new TargetInfo(dropCenter, currentMap), MessageTypeDefOf.PositiveEvent);
        }
    }

    public class Effect_TakeThing : Effect
    {
        public ThingDef thingDef;
        public int count = 1;
        public override void Execute(Window dialog = null)
        {
            if (thingDef == null)
            {
                Log.Error("[SRA] Effect_GiveThing has a null thingDef.");
                return;
            }

            Map currentMap = Find.CurrentMap;
            if (currentMap == null)
            {
                Log.Error("[SRA] Effect_GiveThing cannot execute without a current map.");
                return;
            }
            int playerAmount = currentMap.resourceCounter.GetCount(thingDef);
            if (playerAmount >= count)
            {
                // 扣除资源
                TradeUtility.LaunchThingsOfType(thingDef, count, currentMap, null);
            }
        }
    }

    public class Effect_SpawnPawn : Effect
    {
        public PawnKindDef kindDef;
        public int count = 1;
        public bool joinPlayerFaction = true;
        public string letterLabel;
        public string letterText;
        public LetterDef letterDef;

        public override void Execute(Window dialog = null)
        {
            if (kindDef == null)
            {
                Log.Error("[SRA] Effect_SpawnPawn has a null kindDef.");
                return;
            }

            Map map = Find.CurrentMap;
            if (map == null)
            {
                Log.Error("[SRA] Effect_SpawnPawn cannot execute without a current map.");
                return;
            }

            for (int i = 0; i < count; i++)
            {
                Faction faction = joinPlayerFaction ? Faction.OfPlayer : null;
                PawnGenerationRequest request = new PawnGenerationRequest(
                    kindDef, faction, PawnGenerationContext.NonPlayer, -1, true, false, false, false,
                    true, 20f, false, true, false, true, true, false, false, false, false, 0f, 0f, null, 1f,
                    null, null, null, null, null, null, null, null, null, null, null, null, false
                );
                Pawn pawn = PawnGenerator.GeneratePawn(request);

                if (!CellFinder.TryFindRandomEdgeCellWith((IntVec3 c) => map.reachability.CanReachColony(c) && !c.Fogged(map), map, CellFinder.EdgeRoadChance_Neutral, out IntVec3 cell))
                {
                    cell = DropCellFinder.RandomDropSpot(map);
                }

                GenSpawn.Spawn(pawn, cell, map, WipeMode.Vanish);

                if (!string.IsNullOrEmpty(letterLabel) && !string.IsNullOrEmpty(letterText))
                {
                    TaggedString finalLabel = letterLabel.Formatted(pawn.Named("PAWN")).AdjustedFor(pawn);
                    TaggedString finalText = letterText.Formatted(pawn.Named("PAWN")).AdjustedFor(pawn);
                    PawnRelationUtility.TryAppendRelationsWithColonistsInfo(ref finalText, ref finalLabel, pawn);
                    Find.LetterStack.ReceiveLetter(finalLabel, finalText, letterDef ?? LetterDefOf.PositiveEvent, pawn);
                }
            }
        }
    }

    public class Effect_SpawnSkyfaller : Effect
    {
        public ThingDef skyfaller;
        public bool useTradeDropSpot;

        public override void Execute(Window dialog = null)
        {
            if (skyfaller == null)
            {
                Log.Error("[SRA] Effect_SpawnSkyfaller is not configured correctly (missing skyfaller).");
                return;
            }
            Map currentMap = Find.CurrentMap;
            if (currentMap == null)
            {
                Log.Error("[SRA] Effect_StoreColonyWealth cannot execute without a current map.");
                return;
            }
            IntVec3 result;
            if (useTradeDropSpot)
            {
                result = DropCellFinder.TradeDropSpot(currentMap);
            }
            else
            {
                IntVec3 intVec;
                if (CellFinderLoose.TryGetRandomCellWith((IntVec3 x) => x.Standable(currentMap) && !x.Roofed(currentMap) && !x.Fogged(currentMap) && currentMap.reachability.CanReachColony(x), currentMap, 1000, out intVec))
                {
                    result = intVec;
                }
                else
                {
                    result = DropCellFinder.RandomDropSpot(currentMap, true);
                }
            }
            SkyfallerMaker.SpawnSkyfaller(skyfaller, result, currentMap);
        }
    }
    public class Effect_SpawnOrbitTrader : Effect
    {
        public TraderKindDef traderKindDef;
        public override void Execute(Window dialog = null)
        {
            // 如果parms中没有指定，我们可以使用一个默认的，或者从其他方式获取
            if (traderKindDef == null)
            {
                // 如果没有指定，我们可以随机一个，或者返回false
                return;
            }
            Map map = Find.CurrentMap;
            TradeShip tradeShip = new TradeShip(traderKindDef);
            map.passingShipManager.AddShip(tradeShip);
            tradeShip.GenerateThings();
            Find.LetterStack.ReceiveLetter(tradeShip.def.LabelCap, "TraderArrival".Translate(tradeShip.name, tradeShip.def.label, (tradeShip.Faction == null) ? "TraderArrivalNoFaction".Translate() : "TraderArrivalFromFaction".Translate(tradeShip.Faction.Named("FACTION"))), LetterDefOf.PositiveEvent,
                lookTargets: new LookTargets(map.Center, map));

            return;
        }
    }
    public enum VariableOperation
    {
        Add,
        Subtract,
        Multiply,
        Divide
    }

    public class Effect_ModifyVariable : Effect
    {
        public string name;
        public string value;
        public string valueVariableName;
        public VariableOperation operation;

        public override void Execute(Window dialog = null)
        {
            if (string.IsNullOrEmpty(name))
            {
                Log.Error("[SRA] Effect_ModifyVariable has a null or empty name.");
                return;
            }

            var eventVarManager = Find.World.GetComponent<EventVariableManager>();

            // Determine the value to modify by
            string valueStr = value;
            if (!string.IsNullOrEmpty(valueVariableName))
            {
                valueStr = eventVarManager.GetVariable<object>(valueVariableName)?.ToString();
                if (valueStr == null)
                {
                    Log.Error($"[SRA] Effect_ModifyVariable: valueVariableName '{valueVariableName}' not found.");
                    return;
                }
            }

            // Get the target variable, or initialize it
            object variable = eventVarManager.GetVariable<object>(name);
            if (variable == null)
            {
                Log.Message($"[EventSystem] Effect_ModifyVariable: Variable '{name}' not found, initializing to 0.");
                variable = 0;
            }

            object originalValue = variable;
            object newValue = null;

            // Perform operation based on type
            try
            {
                if (variable is int || (variable is float && !valueStr.Contains("."))) // Allow int ops
                {
                    int currentVal = System.Convert.ToInt32(variable);
                    int modVal = int.Parse(valueStr);
                    newValue = (int)Modify((float)currentVal, (float)modVal, operation);
                }
                else // Default to float operation
                {
                    float currentVal = System.Convert.ToSingle(variable);
                    float modVal = float.Parse(valueStr);
                    newValue = Modify(currentVal, modVal, operation);
                }

                Log.Message($"[EventSystem] Modifying variable '{name}'. Operation: {operation}. Value: {valueStr}. From: {originalValue} To: {newValue}");
                eventVarManager.SetVariable(name, newValue);
            }
            catch (System.Exception e)
            {
                Log.Error($"[SRA] Effect_ModifyVariable: Could not parse or operate on value '{valueStr}' for variable '{name}'. Error: {e.Message}");
            }
        }

        private float Modify(float current, float modifier, VariableOperation op)
        {
            switch (op)
            {
                case VariableOperation.Add: return current + modifier;
                case VariableOperation.Subtract: return current - modifier;
                case VariableOperation.Multiply: return current * modifier;
                case VariableOperation.Divide:
                    if (modifier != 0) return current / modifier;
                    Log.Error($"[SRA] Effect_ModifyVariable tried to divide by zero.");
                    return current;
                default: return current;
            }
        }
    }

    public class Effect_ClearVariable : Effect
    {
        public string name;

        public override void Execute(Window dialog = null)
        {
            if (string.IsNullOrEmpty(name))
            {
                Log.Error("[SRA] Effect_ClearVariable has a null or empty name.");
                return;
            }
            Find.World.GetComponent<EventVariableManager>().ClearVariable(name);
        }
    }

    public class Effect_AddQuest : Effect
    {
        public QuestScriptDef quest;

        public override void Execute(Window dialog = null)
        {
            if (quest == null)
            {
                Log.Error("[SRA] Effect_AddQuest has a null quest Def.");
                return;
            }

            Quest newQuest = Quest.MakeRaw();
            newQuest.root = quest;
            newQuest.id = Find.UniqueIDsManager.GetNextQuestID();
            Find.QuestManager.Add(newQuest);
        }
    }

    public class Effect_FinishResearch : Effect
    {
        public ResearchProjectDef research;

        public override void Execute(Window dialog = null)
        {
            if (research == null)
            {
                Log.Error("[SRA] Effect_FinishResearch has a null research Def.");
                return;
            }

            Find.ResearchManager.FinishProject(research);
        }
    }
    public class Effect_TriggerRaid : Effect
    {
        public float points;
        public FactionDef faction;
        public RaidStrategyDef raidStrategy;
        public PawnsArrivalModeDef raidArrivalMode;
        public PawnGroupKindDef groupKind;
        public List<PawnGroupMaker> pawnGroupMakers;
        public string letterLabel;
        public string letterText;

        public override void Execute(Window dialog = null)
        {
            Map map = Find.CurrentMap;
            if (map == null)
            {
                Log.Error("[SRA] Effect_TriggerRaid cannot execute without a current map.");
                return;
            }

            Faction factionInst = Find.FactionManager.FirstFactionOfDef(this.faction);
            if (factionInst == null)
            {
                Log.Error($"[SRA] Effect_TriggerRaid could not find an active faction for FactionDef '{this.faction?.defName}'.");
                return;
            }

            IncidentParms parms = new IncidentParms
            {
                target = map,
                points = this.points,
                faction = factionInst,
                raidStrategy = this.raidStrategy,
                raidArrivalMode = this.raidArrivalMode,
                pawnGroupMakerSeed = Rand.Int,
                forced = true
            };

            if (!RCellFinder.TryFindRandomPawnEntryCell(out parms.spawnCenter, map, CellFinder.EdgeRoadChance_Hostile))
            {
                Log.Error("[SRA] Effect_TriggerRaid could not find a valid spawn center.");
                return;
            }

            PawnGroupMakerParms groupMakerParms = new PawnGroupMakerParms
            {
                groupKind = this.groupKind ?? PawnGroupKindDefOf.Combat,
                tile = map.Tile,
                points = this.points,
                faction = factionInst,
                raidStrategy = this.raidStrategy,
                seed = parms.pawnGroupMakerSeed
            };
            
            List<Pawn> pawns;
            if (!pawnGroupMakers.NullOrEmpty())
            {
                var groupMaker = pawnGroupMakers.RandomElementByWeight(x => x.commonality);
                pawns = groupMaker.GeneratePawns(groupMakerParms).ToList();
            }
            else
            {
                pawns = PawnGroupMakerUtility.GeneratePawns(groupMakerParms).ToList();
            }

            if (pawns.Any())
            {
                raidArrivalMode.Worker.Arrive(pawns, parms);
                // Assign Lord and LordJob to make the pawns actually perform the raid.
                raidStrategy.Worker.MakeLords(parms, pawns);
                
                if (!string.IsNullOrEmpty(letterLabel) && !string.IsNullOrEmpty(letterText))
                {
                    Find.LetterStack.ReceiveLetter(letterLabel, letterText, LetterDefOf.ThreatBig, pawns[0]);
                }
            }
        }
    }
    
    public class Effect_CheckFactionGoodwill : Effect
    {
        public FactionDef factionDef;
        public string variableName;

        public override void Execute(Window dialog = null)
        {
            if (factionDef == null || string.IsNullOrEmpty(variableName))
            {
                Log.Error("[SRA] Effect_CheckFactionGoodwill is not configured correctly.");
                return;
            }

            var eventVarManager = Find.World.GetComponent<EventVariableManager>();
            Faction faction = Find.FactionManager.FirstFactionOfDef(factionDef);
            
            if (faction != null)
            {
                int goodwill = faction.GoodwillWith(Faction.OfPlayer);
                Log.Message($"[EventSystem] Storing goodwill for faction '{faction.Name}' ({goodwill}) into variable '{variableName}'.");
                eventVarManager.SetVariable(variableName, goodwill);
            }
            else
            {
                Log.Warning($"[EventSystem] Effect_CheckFactionGoodwill: Faction '{factionDef.defName}' not found. Storing 0 in variable '{variableName}'.");
                eventVarManager.SetVariable(variableName, 0);
            }
        }
    }

    public class Effect_StoreRealPlayTime : Effect
    {
        public string variableName;

        public override void Execute(Window dialog = null)
        {
            if (string.IsNullOrEmpty(variableName))
            {
                Log.Error("[SRA] Effect_StoreRealPlayTime is not configured correctly (missing variableName).");
                return;
            }

            var eventVarManager = Find.World.GetComponent<EventVariableManager>();
            float realPlayTime = Find.GameInfo.RealPlayTimeInteracting;
            Log.Message($"[EventSystem] Storing real play time ({realPlayTime}s) into variable '{variableName}'.");
            eventVarManager.SetVariable(variableName, realPlayTime);
        }
    }

    public class Effect_StoreTicksPassed : Effect
    {
        public string variableName;

        public override void Execute(Window dialog = null)
        {
            if (string.IsNullOrEmpty(variableName))
            {
                Log.Error("[SRA] Effect_StoreTicksPassed is not configured correctly (missing variableName).");
                return;
            }

            var eventVarManager = Find.World.GetComponent<EventVariableManager>();
            int ticksPassed = Find.TickManager.TicksGame;
            Log.Message($"[EventSystem] Storing days passed ({ticksPassed}) into variable '{variableName}'.");
            eventVarManager.SetVariable(variableName, ticksPassed);
        }
    }

    
    public class Effect_StoreDaysPassed : Effect
    {
        public string variableName;

        public override void Execute(Window dialog = null)
        {
            if (string.IsNullOrEmpty(variableName))
            {
                Log.Error("[SRA] Effect_StoreDaysPassed is not configured correctly (missing variableName).");
                return;
            }

            var eventVarManager = Find.World.GetComponent<EventVariableManager>();
            int daysPassed = GenDate.DaysPassed;
            Log.Message($"[EventSystem] Storing days passed ({daysPassed}) into variable '{variableName}'.");
            eventVarManager.SetVariable(variableName, daysPassed);
        }
    }



    public class Effect_StoreColonyWealth : Effect
    {
        public string variableName;

        public override void Execute(Window dialog = null)
        {
            if (string.IsNullOrEmpty(variableName))
            {
                Log.Error("[SRA] Effect_StoreColonyWealth is not configured correctly (missing variableName).");
                return;
            }

            Map currentMap = Find.CurrentMap;
            if (currentMap == null)
            {
                Log.Error("[SRA] Effect_StoreColonyWealth cannot execute without a current map.");
                return;
            }

            var eventVarManager = Find.World.GetComponent<EventVariableManager>();
            float wealth = currentMap.wealthWatcher.WealthTotal;
            Log.Message($"[EventSystem] Storing colony wealth ({wealth}) into variable '{variableName}'.");
            eventVarManager.SetVariable(variableName, wealth);
        }
    }
}
