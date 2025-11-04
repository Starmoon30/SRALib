using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;
using UnityEngine;
using Verse.AI.Group;

namespace SRA
{
    // 扩展的空投仓配置属性 - 移除 dropPodDef
    public class CompProperties_FlyOverDropPods : CompProperties
    {
        public IntVec3 dropOffset = IntVec3.Zero;      // 投掷位置偏移
        
        // 投掷时机配置
        public float dropProgress = 0.5f;              // 投掷进度 (0-1)
        public bool useCyclicDrops = false;            // 是否使用循环投掷
        public float cyclicDropIntervalHours = 24f;    // 循环投掷间隔（小时）
        public bool waitForExternalSignal = false;     // 是否等待外部信号
        public string externalSignalTag;               // 外部信号标签
        
        public int dropCount = 1;                      // 投掷数量
        public float scatterRadius = 3f;               // 散布半径
        public bool useTradeDropSpot;                  // 是否使用贸易空投点
        public bool allowFogged;                       // 是否允许雾区
        public bool dropAllInSamePod;                  // 是否在同一空投仓中
        public bool leaveSlag;                         // 是否留下残骸
        
        // 内容物配置
        public List<ThingDefCountClass> thingDefs = new List<ThingDefCountClass>();
        public bool dropAllContents = false;           // 是否投掷所有内容物
        
        // Pawn 生成配置
        public List<PawnKindDefCountClass> pawnKinds = new List<PawnKindDefCountClass>();
        public FactionDef pawnFactionDef;              // Pawn 派系定义
        public bool generatePawnsOnDrop = true;        // 是否在投掷时生成 Pawn
        
        // 乘客配置
        public bool joinPlayer;
        public bool makePrisoners;
        
        // LordJob 配置 - 简化版本
        public bool assignAssaultLordJob = false;      // 是否分配袭击殖民地的 LordJob
        public bool canKidnap = true;                  // 是否可以绑架
        public bool canTimeoutOrFlee = true;           // 是否可以超时或逃跑
        public bool useSappers = false;                // 是否使用工兵
        public bool useAvoidGridSmart = false;         // 是否使用智能回避网格
        public bool canSteal = true;                   // 是否可以偷窃
        public bool useBreachers = false;              // 是否使用破墙者
        public bool canPickUpOpportunisticWeapons = false; // 是否可以捡起机会性武器
        
        // 信件通知
        public bool sendStandardLetter = true;
        public string customLetterText;
        public string customLetterLabel;
        public LetterDef customLetterDef;
        
        // 派系
        public Faction faction;

        public CompProperties_FlyOverDropPods()
        {
            this.compClass = typeof(CompFlyOverDropPods);
        }
    }

    // PawnKind 数量类
    public class PawnKindDefCountClass : IExposable
    {
        public PawnKindDef pawnKindDef;
        public int count;

        public PawnKindDefCountClass() { }

        public PawnKindDefCountClass(PawnKindDef pawnKindDef, int count)
        {
            this.pawnKindDef = pawnKindDef;
            this.count = count;
        }

        public void ExposeData()
        {
            Scribe_Defs.Look(ref pawnKindDef, "pawnKindDef");
            Scribe_Values.Look(ref count, "count", 1);
        }

        public override string ToString()
        {
            return $"{count}x {pawnKindDef?.label ?? "null"}";
        }
    }

    // 空投仓投掷 Comp - 使用原版空投仓
    public class CompFlyOverDropPods : ThingComp
    {
        public CompProperties_FlyOverDropPods Props => (CompProperties_FlyOverDropPods)props;

        // 状态变量
        private bool hasDropped = false;
        private int ticksUntilNextDrop = 0;
        private bool waitingForSignal = false;
        private List<Thing> items = new List<Thing>();
        private List<Pawn> pawns = new List<Pawn>();

        public override void Initialize(CompProperties props)
        {
            base.Initialize(props);

            // 预生成内容物
            if (Props.thingDefs != null)
            {
                foreach (ThingDefCountClass thingDefCount in Props.thingDefs)
                {
                    Thing thing = ThingMaker.MakeThing(thingDefCount.thingDef);
                    thing.stackCount = thingDefCount.count;
                    items.Add(thing);
                }
            }

            // 如果不在投掷时生成 Pawn，则预生成 Pawn
            if (!Props.generatePawnsOnDrop && Props.pawnKinds != null)
            {
                GeneratePawnsFromKinds();
            }

            // 初始化循环投掷计时器
            if (Props.useCyclicDrops)
            {
                ticksUntilNextDrop = (int)(Props.cyclicDropIntervalHours * 2500f); // 1小时 = 2500 ticks
                Log.Message($"Cyclic drops initialized: {Props.cyclicDropIntervalHours} hours interval");
            }

            // 初始化信号等待状态
            if (Props.waitForExternalSignal)
            {
                waitingForSignal = true;
                Log.Message($"Waiting for external signal: {Props.externalSignalTag}");
            }
        }

        // 从 PawnKind 定义生成 Pawn
        private void GeneratePawnsFromKinds()
        {
            if (Props.pawnKinds == null) return;

            foreach (PawnKindDefCountClass pawnKindCount in Props.pawnKinds)
            {
                for (int i = 0; i < pawnKindCount.count; i++)
                {
                    Pawn pawn = GeneratePawn(pawnKindCount.pawnKindDef);
                    if (pawn != null)
                    {
                        pawns.Add(pawn);
                        Log.Message($"Generated pawn: {pawn.Label} ({pawnKindCount.pawnKindDef.defName})");
                    }
                }
            }
        }

        // 生成单个 Pawn
        private Pawn GeneratePawn(PawnKindDef pawnKindDef)
        {
            if (pawnKindDef == null)
            {
                Log.Error("Attempted to generate pawn with null PawnKindDef");
                return null;
            }

            try
            {
                // 确定派系
                Faction faction = DetermineFactionForPawn();

                // 生成 Pawn
                PawnGenerationRequest request = new PawnGenerationRequest(
                    pawnKindDef,
                    faction,
                    PawnGenerationContext.NonPlayer,
                    -1,
                    forceGenerateNewPawn: true,
                    allowDead: false,
                    allowDowned: false,
                    canGeneratePawnRelations: true,
                    mustBeCapableOfViolence: false,
                    colonistRelationChanceFactor: 1f,
                    forceAddFreeWarmLayerIfNeeded: false,
                    allowGay: true,
                    allowFood: true,
                    allowAddictions: true,
                    inhabitant: false,
                    certainlyBeenInCryptosleep: false,
                    forceRedressWorldPawnIfFormerColonist: false,
                    worldPawnFactionDoesntMatter: false,
                    biocodeWeaponChance: 0f,
                    biocodeApparelChance: 0f,
                    extraPawnForExtraRelationChance: null,
                    relationWithExtraPawnChanceFactor: 1f,
                    validatorPreGear: null,
                    validatorPostGear: null,
                    forcedTraits: null,
                    prohibitedTraits: null,
                    minChanceToRedressWorldPawn: 0f,
                    fixedBiologicalAge: null,
                    fixedChronologicalAge: null,
                    fixedGender: null
                );

                Pawn pawn = PawnGenerator.GeneratePawn(request);

                // 设置 Pawn 的基本状态
                if (pawn.mindState != null)
                {
                    pawn.mindState.SetupLastHumanMeatTick();
                }

                Log.Message($"Successfully generated pawn: {pawn.LabelCap} from {pawnKindDef.defName}");
                return pawn;
            }
            catch (System.Exception ex)
            {
                Log.Error($"Failed to generate pawn from {pawnKindDef.defName}: {ex}");
                return null;
            }
        }

        // 确定 Pawn 的派系
        private Faction DetermineFactionForPawn()
        {
            // 优先使用指定的派系定义
            if (Props.pawnFactionDef != null)
            {
                Faction faction = Find.FactionManager.FirstFactionOfDef(Props.pawnFactionDef);
                if (faction != null) return faction;
            }

            // 使用 Comp 的派系
            if (Props.faction != null) return Props.faction;

            // 使用默认中立派系
            return Faction.OfAncients;
        }

        public override void CompTick()
        {
            base.CompTick();

            if (parent is FlyOver flyOver)
            {
                // 检查不同的投掷模式
                if (!hasDropped && !waitingForSignal)
                {
                    if (Props.useCyclicDrops)
                    {
                        CheckCyclicDrop(flyOver);
                    }
                    else
                    {
                        CheckProgressDrop(flyOver);
                    }
                }
            }
        }

        // 检查进度投掷
        private void CheckProgressDrop(FlyOver flyOver)
        {
            if (flyOver.currentProgress >= Props.dropProgress && !hasDropped)
            {
                DropPods(flyOver);
                hasDropped = true;
            }
        }

        // 检查循环投掷
        private void CheckCyclicDrop(FlyOver flyOver)
        {
            ticksUntilNextDrop--;

            if (ticksUntilNextDrop <= 0)
            {
                DropPods(flyOver);

                // 重置计时器
                ticksUntilNextDrop = (int)(Props.cyclicDropIntervalHours * 2500f);
                Log.Message($"Cyclic drop completed, next drop in {Props.cyclicDropIntervalHours} hours");
            }
        }

        // 外部信号触发投掷
        public void TriggerDropFromSignal()
        {
            if (parent is FlyOver flyOver && waitingForSignal)
            {
                Log.Message($"External signal received, triggering drop pods");
                DropPods(flyOver);
                waitingForSignal = false;
            }
        }

        // 接收信号的方法
        public override void ReceiveCompSignal(string signal)
        {
            base.ReceiveCompSignal(signal);

            if (Props.waitForExternalSignal && signal == Props.externalSignalTag)
            {
                TriggerDropFromSignal();
            }
        }

        private void DropPods(FlyOver flyOver)
        {
            Map map = flyOver.Map;
            if (map == null)
            {
                Log.Error("FlyOver DropPods: Map is null");
                return;
            }

            IntVec3 dropCenter = GetDropCenter(flyOver);
            Log.Message($"DropPods triggered at progress {flyOver.currentProgress}, center: {dropCenter}");

            // 如果在投掷时生成 Pawn，现在生成
            if (Props.generatePawnsOnDrop && Props.pawnKinds != null)
            {
                GeneratePawnsFromKinds();
            }

            // 准备要投掷的物品列表
            List<Thing> thingsToDrop = new List<Thing>();

            // 添加预生成的内容物（确保不在容器中）
            foreach (Thing item in items)
            {
                if (item.holdingOwner != null)
                {
                    item.holdingOwner.Remove(item);
                }
                thingsToDrop.Add(item);
            }

            // 添加生成的 Pawn（确保不在容器中）
            foreach (Pawn pawn in pawns)
            {
                if (pawn.holdingOwner != null)
                {
                    pawn.holdingOwner.Remove(pawn);
                }
                thingsToDrop.Add(pawn);
            }

            if (!thingsToDrop.Any())
            {
                Log.Warning("No items to drop from FlyOver drop pods");
                return;
            }

            // 移除已销毁的物品
            thingsToDrop.RemoveAll(x => x.Destroyed);

            // 设置乘客派系和行为
            SetupPawnsForDrop();

            // 执行投掷
            if (Props.dropCount > 1)
            {
                DropMultiplePods(thingsToDrop, dropCenter, map);
            }
            else
            {
                DropSinglePod(thingsToDrop, dropCenter, map);
            }

            // 发送信件通知
            if (Props.sendStandardLetter)
            {
                SendDropLetter(thingsToDrop, dropCenter, map);
            }

            Log.Message($"Drop pods completed: {thingsToDrop.Count} items dropped, including {pawns.Count} pawns");
            
            // 清空已投掷的物品列表，避免重复投掷
            items.Clear();
            pawns.Clear();
        }

        // 设置 Pawn 的派系和行为
        private void SetupPawnsForDrop()
        {
            foreach (Pawn pawn in pawns)
            {
                if (Props.joinPlayer)
                {
                    if (pawn.Faction != Faction.OfPlayer)
                    {
                        pawn.SetFaction(Faction.OfPlayer);
                    }
                }
                else if (Props.makePrisoners)
                {
                    if (pawn.RaceProps.Humanlike && !pawn.IsPrisonerOfColony)
                    {
                        pawn.guest.SetGuestStatus(Faction.OfPlayer, GuestStatus.Prisoner);
                        HealthUtility.TryAnesthetize(pawn);
                    }
                }

                // 设置初始状态
                pawn.needs.SetInitialLevels();
                pawn.mindState?.SetupLastHumanMeatTick();
            }

            // 分配 LordJob（如果启用）
            if (Props.assignAssaultLordJob && pawns.Count > 0)
            {
                AssignAssaultLordJob();
            }
        }

        // 分配袭击殖民地的 LordJob
        private void AssignAssaultLordJob()
        {
            // 按派系分组 Pawn
            var pawnsByFaction = new Dictionary<Faction, List<Pawn>>();
            
            foreach (Pawn pawn in pawns)
            {
                // 跳过玩家派系和囚犯
                if (pawn.Faction == Faction.OfPlayer || pawn.IsPrisonerOfColony)
                    continue;

                // 跳过无效派系
                if (pawn.Faction == null)
                    continue;

                if (!pawnsByFaction.ContainsKey(pawn.Faction))
                {
                    pawnsByFaction[pawn.Faction] = new List<Pawn>();
                }
                pawnsByFaction[pawn.Faction].Add(pawn);
            }

            // 为每个派系创建 LordJob
            foreach (var factionGroup in pawnsByFaction)
            {
                Faction faction = factionGroup.Key;
                List<Pawn> factionPawns = factionGroup.Value;

                if (factionPawns.Count == 0)
                    continue;

                // 创建 LordJob_AssaultColony
                LordJob_AssaultColony lordJob = new LordJob_AssaultColony(
                    faction,
                    Props.canKidnap,
                    Props.canTimeoutOrFlee,
                    Props.useSappers,
                    Props.useAvoidGridSmart,
                    Props.canSteal,
                    Props.useBreachers,
                    Props.canPickUpOpportunisticWeapons
                );

                // 创建 Lord
                Lord lord = LordMaker.MakeNewLord(faction, lordJob, Find.CurrentMap, factionPawns);
                
                Log.Message($"Assigned assault lord job to {factionPawns.Count} pawns of faction {faction.Name}");
            }
        }

        private void DropSinglePod(List<Thing> thingsToDrop, IntVec3 dropCenter, Map map)
        {
            // 使用原版空投仓系统
            if (Props.dropAllInSamePod)
            {
                // 所有物品在一个空投仓中
                DropPodUtility.DropThingGroupsNear(
                    dropCenter,
                    map,
                    new List<List<Thing>> { thingsToDrop },
                    openDelay: 110,
                    instaDrop: false,
                    leaveSlag: Props.leaveSlag,
                    canRoofPunch: !Props.useTradeDropSpot,
                    forbid: false,
                    allowFogged: Props.allowFogged,
                    canTransfer: false,
                    faction: Props.faction
                );
            }
            else
            {
                // 每个物品单独空投仓
                DropPodUtility.DropThingsNear(
                    dropCenter,
                    map,
                    thingsToDrop,
                    openDelay: 110,
                    canInstaDropDuringInit: false,
                    leaveSlag: Props.leaveSlag,
                    canRoofPunch: !Props.useTradeDropSpot,
                    forbid: false,
                    allowFogged: Props.allowFogged,
                    faction: Props.faction
                );
            }
        }

        private void DropMultiplePods(List<Thing> thingsToDrop, IntVec3 dropCenter, Map map)
        {
            List<List<Thing>> podGroups = new List<List<Thing>>();

            // 首先，确保所有物品都不在任何容器中
            foreach (Thing thing in thingsToDrop)
            {
                if (thing.holdingOwner != null)
                {
                    thing.holdingOwner.Remove(thing);
                }
            }

            if (Props.dropAllInSamePod)
            {
                // 所有物品在一个空投仓中，但生成多个相同的空投仓
                for (int i = 0; i < Props.dropCount; i++)
                {
                    List<Thing> podItems = CreatePodItemsCopy(thingsToDrop);
                    podGroups.Add(podItems);
                }
            }
            else
            {
                // 将原始物品分配到多个空投仓中
                List<Thing> remainingItems = new List<Thing>(thingsToDrop);
                
                for (int i = 0; i < Props.dropCount; i++)
                {
                    List<Thing> podItems = new List<Thing>();
                    int itemsPerPod = Mathf.CeilToInt((float)remainingItems.Count / (Props.dropCount - i));
                    
                    for (int j = 0; j < itemsPerPod && remainingItems.Count > 0; j++)
                    {
                        podItems.Add(remainingItems[0]);
                        remainingItems.RemoveAt(0);
                    }
                    
                    podGroups.Add(podItems);
                }
            }

            // 投掷多个空投仓组
            foreach (List<Thing> podGroup in podGroups)
            {
                if (podGroup.Count == 0) continue;
                
                IntVec3 scatterPos = GetScatteredDropPos(dropCenter, map);
                DropPodUtility.DropThingGroupsNear(
                    scatterPos,
                    map,
                    new List<List<Thing>> { podGroup },
                    openDelay: 110,
                    instaDrop: false,
                    leaveSlag: Props.leaveSlag,
                    canRoofPunch: !Props.useTradeDropSpot,
                    forbid: false,
                    allowFogged: Props.allowFogged,
                    canTransfer: false,
                    faction: Props.faction
                );
            }
        }

        // 创建物品的深拷贝
        private List<Thing> CreatePodItemsCopy(List<Thing> originalItems)
        {
            List<Thing> copies = new List<Thing>();
            
            foreach (Thing original in originalItems)
            {
                if (original is Pawn originalPawn)
                {
                    // 对于 Pawn，重新生成
                    Pawn newPawn = GeneratePawn(originalPawn.kindDef);
                    if (newPawn != null)
                    {
                        copies.Add(newPawn);
                    }
                }
                else
                {
                    // 对于物品，创建副本
                    Thing copy = ThingMaker.MakeThing(original.def, original.Stuff);
                    copy.stackCount = original.stackCount;
                    
                    // 复制其他重要属性
                    if (original.def.useHitPoints)
                    {
                        copy.HitPoints = original.HitPoints;
                    }
                    
                    copies.Add(copy);
                }
            }
            
            return copies;
        }

        private IntVec3 GetDropCenter(FlyOver flyOver)
        {
            // 计算投掷中心位置（基于当前飞行位置 + 偏移）
            Vector3 currentPos = Vector3.Lerp(
                flyOver.startPosition.ToVector3(),
                flyOver.endPosition.ToVector3(),
                flyOver.currentProgress
            );

            IntVec3 dropCenter = currentPos.ToIntVec3() + Props.dropOffset;

            // 如果使用贸易空投点，找到贸易空投点
            if (Props.useTradeDropSpot)
            {
                dropCenter = DropCellFinder.TradeDropSpot(flyOver.Map);
            }

            return dropCenter;
        }

        private IntVec3 GetScatteredDropPos(IntVec3 center, Map map)
        {
            if (Props.scatterRadius <= 0)
                return center;

            // 在散布半径内找到有效位置
            for (int i = 0; i < 10; i++)
            {
                IntVec3 scatterPos = center + new IntVec3(
                    Rand.RangeInclusive(-(int)Props.scatterRadius, (int)Props.scatterRadius),
                    0,
                    Rand.RangeInclusive(-(int)Props.scatterRadius, (int)Props.scatterRadius)
                );

                if (scatterPos.InBounds(map) &&
                    scatterPos.Standable(map) &&
                    !scatterPos.Roofed(map) &&
                    (Props.allowFogged || !scatterPos.Fogged(map)))
                {
                    return scatterPos;
                }
            }

            // 如果找不到有效位置，返回随机空投点
            return DropCellFinder.RandomDropSpot(map);
        }

        private void SendDropLetter(List<Thing> thingsToDrop, IntVec3 dropCenter, Map map)
        {
            TaggedString text = null;
            TaggedString label = null;

            // 生成信件内容（模仿原版逻辑）
            if (Props.joinPlayer && pawns.Count == 1 && pawns[0].RaceProps.Humanlike)
            {
                text = "LetterRefugeeJoins".Translate(pawns[0].Named("PAWN"));
                label = "LetterLabelRefugeeJoins".Translate(pawns[0].Named("PAWN"));
                PawnRelationUtility.TryAppendRelationsWithColonistsInfo(ref text, ref label, pawns[0]);
            }
            else
            {
                text = "LetterQuestDropPodsArrived".Translate(GenLabel.ThingsLabel(thingsToDrop));
                label = "LetterLabelQuestDropPodsArrived".Translate();
                PawnRelationUtility.Notify_PawnsSeenByPlayer_Letter(
                    pawns,
                    ref label,
                    ref text,
                    "LetterRelatedPawnsNeutralGroup".Translate(Faction.OfPlayer.def.pawnsPlural),
                    informEvenIfSeenBefore: true
                );
            }

            // 应用自定义文本
            label = (Props.customLetterLabel.NullOrEmpty() ? label : Props.customLetterLabel.Formatted(label.Named("BASELABEL")));
            text = (Props.customLetterText.NullOrEmpty() ? text : Props.customLetterText.Formatted(text.Named("BASETEXT")));

            // 发送信件
            Find.LetterStack.ReceiveLetter(
                label,
                text,
                Props.customLetterDef ?? LetterDefOf.PositiveEvent,
                new TargetInfo(dropCenter, map)
            );
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref hasDropped, "hasDropped", false);
            Scribe_Values.Look(ref ticksUntilNextDrop, "ticksUntilNextDrop", 0);
            Scribe_Values.Look(ref waitingForSignal, "waitingForSignal", false);
            Scribe_Collections.Look(ref items, "items", LookMode.Deep);
            Scribe_Collections.Look(ref pawns, "pawns", LookMode.Reference);
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            if (DebugSettings.ShowDevGizmos && parent is FlyOver)
            {
                yield return new Command_Action
                {
                    defaultLabel = "Dev: Trigger Drop Pods",
                    action = () => DropPods(parent as FlyOver)
                };

                yield return new Command_Action
                {
                    defaultLabel = "Dev: Generate Pawns Now",
                    action = () =>
                    {
                        GeneratePawnsFromKinds();
                        Messages.Message($"Generated {pawns.Count} pawns", MessageTypeDefOf.NeutralEvent);
                    }
                };

                if (Props.waitForExternalSignal)
                {
                    yield return new Command_Action
                    {
                        defaultLabel = "Dev: Send External Signal",
                        action = () => TriggerDropFromSignal()
                    };
                }
            }
        }

        // 公共方法：供其他 Comps 调用以触发投掷
        public void TriggerDropPods()
        {
            if (parent is FlyOver flyOver)
            {
                DropPods(flyOver);
            }
        }
    }
}
