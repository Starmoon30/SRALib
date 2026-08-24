using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace SRA
{
    /// <summary>
    /// 为低温舱提供被收容 Pawn 的被动研究功能。Pawn 的收纳、弹出与状态冻结均由 Building_Casket 原版实现处理。
    /// </summary>
    public class CompCasketResearch : ThingComp
    {
        private CompPowerTrader powerComp;

        public CompProperties_CasketResearch Props => (CompProperties_CasketResearch)props;

        private Building_Casket Casket => parent as Building_Casket;

        private Building_CryptosleepCasket CryptosleepCasket => parent as Building_CryptosleepCasket;

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            powerComp = parent.GetComp<CompPowerTrader>();
        }

        /// <summary>
        /// 以配置间隔结算研究。使用原版的 ResearchPerformed，保留难度、科技成本系数、研究完成信件与统计记录的结算逻辑。
        /// </summary>
        public override void CompTickInterval(int delta)
        {
            base.CompTickInterval(delta);

            int interval = Math.Max(1, Props.researchIntervalTicks);
            if (!parent.IsHashIntervalTick(interval, delta) || !TryGetResearchContext(out Pawn researcher, out _))
            {
                return;
            }

            // 对 Normal ticker，delta 为 1，实际间隔即为 interval。对 Rare ticker，避免可能更长的调用间隔被少算。
            int workTicks = Math.Max(interval, Math.Max(1, delta));
            float researchWork = GetResearchWorkPerSecond(researcher) * workTicks / GenTicks.TicksPerRealSecond;
            if (researchWork <= 0f)
            {
                return;
            }

            Find.ResearchManager.ResearchPerformed(researchWork, researcher);
        }

        public override string CompInspectStringExtra()
        {
            string baseString = base.CompInspectStringExtra();
            string status = GetInspectStatus();
            if (status.NullOrEmpty())
            {
                return baseString;
            }

            return baseString.NullOrEmpty() ? status : baseString + "\n" + status;
        }

        /// <summary>
        /// 原版低温舱的右键菜单不允许装入己方囚犯。此按钮直接使用原版进入或搬运到低温舱工作，使任何被收纳的 Pawn 均能成为研究单元。
        /// </summary>
        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (Gizmo gizmo in base.CompGetGizmosExtra())
            {
                yield return gizmo;
            }

            if (parent.Faction != Faction.OfPlayer || CryptosleepCasket == null)
            {
                yield break;
            }

            Command_Action loadSubjectCommand = new Command_Action
            {
                defaultLabel = Props.Localization.loadSubjectLabelKey.Translate(),
                defaultDesc = Props.Localization.loadSubjectDescKey.Translate(),
                icon = ContentFinder<Texture2D>.Get("UI/Commands/PodEject", true),
                action = BeginLoadSubjectTargeting
            };

            if (Casket.HasAnyContents)
            {
                loadSubjectCommand.Disable(Props.Localization.loadOccupiedKey.Translate());
            }

            yield return loadSubjectCommand;
        }

        /// <summary>
        /// 按每秒口径计算研究工作量：有研究能力的 Pawn 使用自身 ResearchSpeed 转换后的每秒值；其他 Pawn 使用 XML 指定的每秒低速度。两者均与建筑研究速度系数和额外乘数相乘。
        /// </summary>
        private float GetResearchWorkPerSecond(Pawn researcher)
        {
            float pawnResearchSpeed = HasResearchAbility(researcher)
                ? researcher.GetStatValue(StatDefOf.ResearchSpeed) * GenTicks.TicksPerRealSecond
                : Mathf.Max(0f, Props.incapableResearchSpeed);
            float buildingResearchFactor = parent.GetStatValue(StatDefOf.ResearchSpeedFactor);
            return Mathf.Max(0f, pawnResearchSpeed * buildingResearchFactor * Mathf.Max(0f, Props.researchSpeedFactor));
        }

        /// <summary>
        /// 集中检查所有生效条件，使结算和信息面板使用完全一致的判定。
        /// </summary>
        private bool TryGetResearchContext(out Pawn researcher, out ResearchProjectDef project)
        {
            researcher = GetResearchSubject();
            project = null;
            if (!HasRequiredPower() || !IsResearchSubject(researcher))
            {
                return false;
            }

            ResearchManager manager = Find.ResearchManager;
            project = manager?.GetProject();
            return CanResearchProject(project);
        }

        private Pawn GetResearchSubject()
        {
            return Casket?.ContainedThing as Pawn;
        }

        /// <summary>
        /// CanStartNow 包含原版的前置科技、科技图纸、研究设施、机械师和已分析物品判定。低温研究舱只代替研究者，不突破这些科技门槛。
        /// </summary>
        private static bool CanResearchProject(ResearchProjectDef project)
        {
            return project != null && project.knowledgeCategory == null && project.CanStartNow;
        }

        /// <summary>
        /// 进入点选模式以选择待装入的 Pawn。不生成全地图列表，避免动物数量较多时造成菜单膨胀。
        /// </summary>
        private void BeginLoadSubjectTargeting()
        {
            if (CryptosleepCasket == null || Casket.HasAnyContents || parent.Map == null)
            {
                return;
            }

            Find.Targeter.BeginTargeting(new TargetingParameters
            {
                canTargetPawns = true,
                canTargetBuildings = false,
                canTargetItems = false,
                canTargetLocations = false,
                mapObjectTargetsMustBeAutoAttackable = false,
                validator = target => target.Thing is Pawn subject && CanTargetForLoad(subject)
            }, target => QueueLoad(target.Pawn));
        }

        /// <summary>
        /// 此判定会在鼠标移动时反复执行，因此只检查不需寻路或遍历植民者的快速条件。搬运者和路径会在实际点击后再确认。
        /// </summary>
        private bool CanTargetForLoad(Pawn subject)
        {
            return CryptosleepCasket != null && !Casket.HasAnyContents && subject != null && subject.Spawned && subject.MapHeld == parent.Map &&
                   IsResearchSubject(subject);
        }

        /// <summary>
        /// 属于玩家派系且不是囚犯的清醒 Pawn 使用原版主动进入工作；殖民地囚犯和所有倒地对象使用原版的搬运到低温舱工作。
        /// 这与原版裂解扫描仪的囚犯装入方式一致，无需先将囚犯击倒。其他清醒 Pawn 不会被强制搬运。
        /// </summary>
        private void QueueLoad(Pawn subject)
        {
            if (!CanQueueLoad(subject, out string disabledReason))
            {
                if (!disabledReason.NullOrEmpty())
                {
                    Messages.Message(disabledReason, parent, MessageTypeDefOf.RejectInput, false);
                }

                return;
            }

            if (RequiresCarrier(subject))
            {
                Pawn carrier = FindCarrier(subject);
                if (carrier == null)
                {
                    return;
                }

                Job carryJob = JobMaker.MakeJob(JobDefOf.CarryToCryptosleepCasket, subject, CryptosleepCasket);
                carryJob.count = 1;
                carrier.jobs.TryTakeOrderedJob(carryJob, JobTag.Misc, false);
                return;
            }

            Job enterJob = JobMaker.MakeJob(JobDefOf.EnterCryptosleepCasket, CryptosleepCasket);
            subject.jobs.TryTakeOrderedJob(enterJob, JobTag.Misc, false);
        }

        private bool CanQueueLoad(Pawn subject, out string disabledReason)
        {
            disabledReason = null;
            if (CryptosleepCasket == null || Casket.HasAnyContents)
            {
                disabledReason = Props.Localization.loadOccupiedKey.Translate();
                return false;
            }

            if (!IsResearchSubject(subject) || !subject.Spawned || subject.MapHeld != parent.Map)
            {
                disabledReason = Props.Localization.invalidSubjectKey.Translate(subject?.LabelShortCap ?? "?");
                return false;
            }

            if (RequiresCarrier(subject))
            {
                if (FindCarrier(subject) != null)
                {
                    return true;
                }

                disabledReason = Props.Localization.loadNoCarrierKey.Translate();
                return false;
            }

            if (!CanEnterCasketByOrder(subject))
            {
                disabledReason = Props.Localization.loadSubjectMustBeDownedKey.Translate();
                return false;
            }

            if (!subject.CanReserveAndReach(CryptosleepCasket, PathEndMode.InteractionCell, Danger.Deadly))
            {
                disabledReason = Props.Localization.loadUnreachableKey.Translate();
                return false;
            }

            return true;
        }

        /// <summary>
        /// 为倒地对象寻找一个能够依次到达对象和低温舱的己方植民者。只在打开装入菜单或点击选项时执行。
        /// </summary>
        private Pawn FindCarrier(Pawn subject)
        {
            if (subject == null || parent.Map == null)
            {
                return null;
            }

            Pawn bestCarrier = null;
            float bestDistanceSquared = float.MaxValue;
            IReadOnlyList<Pawn> colonists = parent.Map.mapPawns.FreeColonistsSpawned;
            for (int i = 0; i < colonists.Count; i++)
            {
                Pawn candidate = colonists[i];
                if (candidate == null || candidate.Dead || candidate.Downed || !candidate.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation) ||
                    !candidate.CanReserveAndReach(subject, PathEndMode.OnCell, Danger.Deadly) ||
                    !candidate.CanReserveAndReach(CryptosleepCasket, PathEndMode.InteractionCell, Danger.Deadly))
                {
                    continue;
                }

                float distanceSquared = (candidate.Position - subject.Position).LengthHorizontalSquared;
                if (distanceSquared < bestDistanceSquared)
                {
                    bestCarrier = candidate;
                    bestDistanceSquared = distanceSquared;
                }
            }

            return bestCarrier;
        }

        /// <summary>
        /// 研究舱不对 Pawn 的种族、派系或客户状态施加限制。只要是存活 Pawn 并已被收纳，包括动物，便能提供研究。
        /// </summary>
        private static bool IsResearchSubject(Pawn pawn)
        {
            return pawn != null && !pawn.Dead;
        }

        /// <summary>
        /// 只有拥有 Intellectual 技能且未被禁止研究的 Pawn 使用自身 ResearchSpeed。其他 Pawn 依然可研究，但使用 incapableResearchSpeed 作为每秒低速度。
        /// </summary>
        private static bool HasResearchAbility(Pawn pawn)
        {
            return pawn.skills != null && pawn.skills.GetSkill(SkillDefOf.Intellectual) != null && !pawn.WorkTypeIsDisabled(WorkTypeDefOf.Research) &&
                   pawn.GetStatValue(StatDefOf.ResearchSpeed) > 0f;
        }

        private static bool CanEnterCasketByOrder(Pawn pawn)
        {
            return pawn.Faction == Faction.OfPlayer && !pawn.IsPrisonerOfColony;
        }

        /// <summary>
        /// 原版 CarryToCryptosleepCasket 可以直接搬运殖民地囚犯。将其视为需要搬运的目标，可避免清醒囚犯被错误要求先倒地。
        /// </summary>
        private static bool RequiresCarrier(Pawn pawn)
        {
            return pawn.Downed || pawn.IsPrisonerOfColony;
        }

        private bool HasRequiredPower()
        {
            if (!Props.requirePower)
            {
                return true;
            }

            powerComp ??= parent.GetComp<CompPowerTrader>();
            return powerComp != null && powerComp.PowerOn;
        }

        private string GetInspectStatus()
        {
            if (Casket == null)
            {
                return Props.Localization.invalidHostKey.Translate();
            }

            Pawn researcher = GetResearchSubject();
            if (researcher == null)
            {
                return Props.Localization.noSubjectKey.Translate();
            }

            if (!IsResearchSubject(researcher))
            {
                return Props.Localization.invalidSubjectKey.Translate(researcher.LabelShortCap);
            }

            if (!HasRequiredPower())
            {
                return Props.Localization.noPowerKey.Translate();
            }

            ResearchProjectDef project = Find.ResearchManager?.GetProject();
            if (!CanResearchProject(project))
            {
                return Props.Localization.noProjectKey.Translate();
            }

            string result = Props.Localization.workingKey.Translate(researcher.LabelShortCap, project.LabelCap) + "\n" +
                            Props.Localization.speedKey.Translate(GetResearchWorkPerSecond(researcher).ToString("0.##"));
            if (!HasResearchAbility(researcher))
            {
                result += "\n" + Props.Localization.incapableSpeedKey.Translate((Props.incapableResearchSpeed / GenTicks.TicksPerRealSecond).ToStringPercent());
            }

            return result;
        }
    }

    /// <summary>
    /// 低温研究舱的 XML 属性。必须挂载在 Building_CryptosleepCasket 或其子类上，以便调用原版的主动进入、搬运与弹出工作。
    /// </summary>
    public class CompProperties_CasketResearch : CompProperties
    {
        // 计算研究进度的间隔，单位为 tick。更短的间隔会更快反映捕获对象、供电和科技切换。
        public int researchIntervalTicks = 250;

        // 额外研究乘数。它与 Pawn 的 ResearchSpeed 和建筑的 ResearchSpeedFactor 相乘。
        public float researchSpeedFactor = 1f;

        // 当收容 Pawn 没有研究能力时使用的每秒研究工作量。6 等同于基础研究速度的 10%。
        public float incapableResearchSpeed = 6f;

        // 是否要求建筑存在且当前已通电的 CompPowerTrader。设为 false 时可用于无需供电的低温研究舱。
        public bool requirePower = true;

        // 研究舱使用的全部 Keyed 本地化键。可由 XML 覆盖为所属模组自己的本地化键。
        public CasketResearchLocalization localization = new CasketResearchLocalization();

        public CasketResearchLocalization Localization
        {
            get
            {
                localization ??= new CasketResearchLocalization();
                return localization;
            }
        }

        public CompProperties_CasketResearch()
        {
            compClass = typeof(CompCasketResearch);
        }

        public override IEnumerable<string> ConfigErrors(ThingDef parentDef)
        {
            foreach (string error in base.ConfigErrors(parentDef))
            {
                yield return error;
            }

            if (parentDef?.thingClass == null || !typeof(Building_CryptosleepCasket).IsAssignableFrom(parentDef.thingClass))
            {
                yield return Localization.configRequiresCryptosleepCasketKey.Translate(parentDef?.defName ?? "<null>");
            }

            if (researchIntervalTicks < 1)
            {
                yield return Localization.configInvalidIntervalKey.Translate(parentDef?.defName ?? "<null>");
            }

            if (researchSpeedFactor < 0f)
            {
                yield return Localization.configInvalidSpeedFactorKey.Translate(parentDef?.defName ?? "<null>");
            }

            if (incapableResearchSpeed < 0f)
            {
                yield return Localization.configInvalidIncapableSpeedKey.Translate(parentDef?.defName ?? "<null>");
            }
        }
    }

    /// <summary>
    /// 低温研究舱所用的 Keyed 本地化键。字段只接受键名，不支持在 Def 中直接写死显示文本。
    /// </summary>
    public class CasketResearchLocalization
    {
        // 装入研究单元按钮的标题与说明。
        public string loadSubjectLabelKey = "SRA_CasketResearch_LoadSubject";
        public string loadSubjectDescKey = "SRA_CasketResearch_LoadSubjectDesc";

        // 装入流程中显示的占用、目标、搬运和路径错误。
        public string loadOccupiedKey = "SRA_CasketResearch_LoadOccupied";
        public string invalidSubjectKey = "SRA_CasketResearch_InvalidSubject";
        public string loadSubjectMustBeDownedKey = "SRA_CasketResearch_LoadSubjectMustBeDowned";
        public string loadNoCarrierKey = "SRA_CasketResearch_LoadNoCarrier";
        public string loadUnreachableKey = "SRA_CasketResearch_LoadUnreachable";

        // 检查面板中的状态文本与速度说明。
        public string invalidHostKey = "SRA_CasketResearch_InvalidHost";
        public string noSubjectKey = "SRA_CasketResearch_NoSubject";
        public string noPowerKey = "SRA_CasketResearch_NoPower";
        public string noProjectKey = "SRA_CasketResearch_NoProject";
        public string workingKey = "SRA_CasketResearch_Working";
        public string speedKey = "SRA_CasketResearch_Speed";
        public string incapableSpeedKey = "SRA_CasketResearch_IncapableSpeed";

        // Def 加载阶段的配置错误文本。
        public string configRequiresCryptosleepCasketKey = "SRA_CasketResearch_ConfigRequiresCryptosleepCasket";
        public string configInvalidIntervalKey = "SRA_CasketResearch_ConfigInvalidInterval";
        public string configInvalidSpeedFactorKey = "SRA_CasketResearch_ConfigInvalidSpeedFactor";
        public string configInvalidIncapableSpeedKey = "SRA_CasketResearch_ConfigInvalidIncapableSpeed";
    }
}
