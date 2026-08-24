using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace SRA
{
    /// <summary>
    /// 单个 Hediff 探测条目。可指定 HediffDef，也可选择匹配所有使用原版隐形组件的 Hediff，并在目标头顶绘制 iconPath 对应的图标。
    /// 缺失的 HediffDef 会被静默跳过，以兼容未启用的 DLC 或可选模组。
    /// </summary>
    public class HediffDetectorTarget
    {
        // 要探测的 HediffDef.defName。
        public string hediffDef;

        // 是否匹配所有带有原版 HediffComp_Invisibility 的 Hediff。启用时 hediffDef 可以留空；若两者均填写，指定 Hediff 与所有原版隐形 Hediff 都会匹配。
        public bool matchAllInvisibilityHediffs = false;

        // 目标头顶标记的贴图路径。使用 MetaOverlay Shader 渲染。
        public string iconPath;
    }

    /// <summary>
    /// Hediff 探测器的 XML 配置。探测半径、图标标记、迷雾揭露和反隐均可独立配置。
    /// </summary>
    public class CompProperties_HediffDetector : CompProperties
    {
        private const string DefaultGizmoLabelKey = "SRA_HediffDetector_ToggleLabel";
        private const string DefaultGizmoDescKey = "SRA_HediffDetector_ToggleDesc";
        private const string DefaultUiIconPath = "UI/Commands/Attack";

        // 探测半径。大于 0 时扫描圆形半径；小于等于 0 时扫描整张地图，并在 revealFog 启用时清除全图迷雾。
        public float detectRadius = 0f;

        // 扫描间隔，单位为 tick。小于 1 的配置会按 1 tick 处理。
        public int scanIntervalTicks = 250;

        // 是否揭露迷雾。detectRadius 大于 0 时揭露圆形范围；小于等于 0 时清除全图迷雾。
        public bool revealFog = false;

        // 要探测并在头顶标记的 Hediff 列表。列表靠后的同 Hediff 条目拥有更高显示优先级。
        public List<HediffDetectorTarget> detectionList = new List<HediffDetectorTarget>();

        // 是否压制范围内敌对 Pawn 的原版隐形。隐形组件在运行时无法解析时会自然不生效。
        public bool disruptEnemyInvisibility = true;

        // 强制可见持续时间，单位为 tick。小于等于 0 时自动按扫描间隔加缓冲计算，避免两次扫描间出现空档。
        public int disruptionDurationTicks = 0;

        // 是否要求建筑通电。仅当建筑存在 CompPowerTrader 时，断电才会使探测器停用。
        public bool requirePower = true;

        // 建筑生成时是否默认开启探测器。默认关闭，可通过 gizmo 手动开启。
        public bool startEnabled = false;

        // gizmo 标题的 Keyed 本地化键。
        public string gizmoLabelKey = DefaultGizmoLabelKey;

        // gizmo 说明的 Keyed 本地化键。
        public string gizmoDescKey = DefaultGizmoDescKey;

        // 探测器开启时的 gizmo 图标路径。
        public string uiIconPathEnabled = DefaultUiIconPath;

        // 探测器关闭时的 gizmo 图标路径。
        public string uiIconPathDisabled = DefaultUiIconPath;

        // 目标头顶标记的平面缩放。
        public float markScale = 2.5f;

        // 目标头顶标记沿地图 Z 轴的高度偏移。
        public float markHeightOffset = 1.5f;

        // 标记上下浮动的频率。小于等于 0 时不浮动。
        public float markBobbingFrequency = 0.3f;

        // 标记上下浮动的幅度。小于等于 0 时不浮动。
        public float markBobbingAmplitude = 0.3f;

        // 已解析的 Def 与材质缓存，避免每次扫描重复查 Def 或创建材质。
        [Unsaved(false)]
        private HediffDetectorEntry[] cachedEntries;

        // 非主线程不会解析 Unity 材质，返回空结果以便后续主线程调用安全地创建缓存。
        private static readonly HediffDetectorEntry[] EmptyEntries = new HediffDetectorEntry[0];

        // 按距离排序的迷雾扫描格偏移缓存，供同一 Def 的所有建筑实例复用。
        [Unsaved(false)]
        private IntVec3[] cachedFogOffsets;

        [Unsaved(false)]
        private float cachedFogOffsetRadius = -1f;

        public CompProperties_HediffDetector()
        {
            compClass = typeof(CompHediffDetector);
        }

        /// <summary>
        /// 将 XML 字符串转换为运行时 Def/材质引用。无效条目不会阻止其他有效条目工作；隐形通配条目不需要解析 HediffDef。
        /// </summary>
        public HediffDetectorEntry[] GetDetectionEntries()
        {
            // MaterialPool 和 ContentFinder 会访问 Unity 对象，必须严格在主线程创建。
            if (!UnityData.IsInMainThread)
            {
                return EmptyEntries;
            }

            if (cachedEntries != null)
            {
                return cachedEntries;
            }

            List<HediffDetectorEntry> entries = new List<HediffDetectorEntry>();
            if (!detectionList.NullOrEmpty())
            {
                for (int i = 0; i < detectionList.Count; i++)
                {
                    HediffDetectorTarget target = detectionList[i];
                    if (target == null || target.iconPath.NullOrEmpty() || (!target.matchAllInvisibilityHediffs && target.hediffDef.NullOrEmpty()))
                    {
                        continue;
                    }

                    HediffDef hediffDef = target.hediffDef.NullOrEmpty()
                        ? null
                        : DefDatabase<HediffDef>.GetNamedSilentFail(target.hediffDef);
                    if (hediffDef == null && !target.matchAllInvisibilityHediffs)
                    {
                        continue;
                    }

                    Material material = MaterialPool.MatFrom(target.iconPath, ShaderDatabase.MetaOverlay);
                    if (material != null)
                    {
                        entries.Add(new HediffDetectorEntry
                        {
                            def = hediffDef,
                            material = material,
                            priority = i,
                            matchAllInvisibilityHediffs = target.matchAllInvisibilityHediffs
                        });
                    }
                }
            }

            cachedEntries = entries.ToArray();
            return cachedEntries;
        }

        /// <summary>
        /// 为迷雾揭露生成半径内格子并按距离排序。半径无效或全图扫描模式不需要格偏移。
        /// </summary>
        public IntVec3[] GetFogScanOffsets()
        {
            if (cachedFogOffsets != null && Mathf.Approximately(cachedFogOffsetRadius, detectRadius))
            {
                return cachedFogOffsets;
            }

            List<IntVec3> offsets = new List<IntVec3>();
            if (detectRadius > 0f)
            {
                int radiusCeil = Mathf.CeilToInt(detectRadius);
                float radiusSquared = detectRadius * detectRadius;
                for (int z = -radiusCeil; z <= radiusCeil; z++)
                {
                    float zSquared = z * z;
                    if (zSquared > radiusSquared)
                    {
                        continue;
                    }

                    for (int x = -radiusCeil; x <= radiusCeil; x++)
                    {
                        if (x * x + zSquared <= radiusSquared)
                        {
                            offsets.Add(new IntVec3(x, 0, z));
                        }
                    }
                }

                offsets.Sort((a, b) => OffsetDistanceSquared(a).CompareTo(OffsetDistanceSquared(b)));
            }

            cachedFogOffsets = offsets.ToArray();
            cachedFogOffsetRadius = detectRadius;
            return cachedFogOffsets;
        }

        private static int OffsetDistanceSquared(IntVec3 offset)
        {
            return offset.x * offset.x + offset.z * offset.z;
        }
    }

    /// <summary>
    /// 已解析的 Hediff 探测条目。priority 保留 XML 原有顺序，用于决定同目标的图标覆盖关系。
    /// </summary>
    public struct HediffDetectorEntry
    {
        public HediffDef def;
        public Material material;
        public int priority;
        public bool matchAllInvisibilityHediffs;
    }

    /// <summary>
    /// 当前扫描锁定的 Pawn 与其头顶标记材质。
    /// </summary>
    public struct HediffDetectorLockedTarget
    {
        public Pawn pawn;
        public Material material;
    }

    /// <summary>
    /// Hediff 探测器的共享渲染资源。
    /// StaticConstructorOnStartup 保证 Unity 材质在游戏主线程和 Def 初始化阶段创建，避免运行时懒加载触发线程警告。
    /// </summary>
    [StaticConstructorOnStartup]
    internal static class HediffDetectorRenderResources
    {
        internal static readonly Material RadiusRingMaterial = MaterialPool.MatFrom(BaseContent.WhiteTex, ShaderDatabase.MetaOverlay, Color.red);
    }

    /// <summary>
    /// 建筑 Hediff 探测器。自身只提交请求和保存局部状态，实际扫描、迷雾任务和标记绘制由地图管理器统一调度。
    /// </summary>
    public class CompHediffDetector : ThingComp
    {
        private const int MinimumDisruptionDurationTicks = 60;
        private const int DisruptionGraceTicks = 10;
        private const int GizmoGroupKey = 510025;

        private static int lastSelectionToggleFrame = -1;
        private static bool lastSelectionToggleTargetState;

        private CompPowerTrader powerComp;
        private bool detectorEnabled;
        private float cachedRadiusSquared = -1f;
        private Texture2D cachedEnabledIcon;
        private Texture2D cachedDisabledIcon;

        // 仅保存本组件最近一次扫描的标记目标，地图管理器负责统一绘制并进行跨探测器去重。
        internal readonly List<HediffDetectorLockedTarget> lockedTargets = new List<HediffDetectorLockedTarget>();

        public CompProperties_HediffDetector Props => (CompProperties_HediffDetector)props;

        public bool DetectorEnabled => detectorEnabled;

        private float RadiusSquared
        {
            get
            {
                if (cachedRadiusSquared < 0f)
                {
                    cachedRadiusSquared = Props.detectRadius * Props.detectRadius;
                }

                return cachedRadiusSquared;
            }
        }

        private Texture2D EnabledIcon => ResolveIcon(ref cachedEnabledIcon, Props.uiIconPathEnabled);

        private Texture2D DisabledIcon => ResolveIcon(ref cachedDisabledIcon, Props.uiIconPathDisabled);

        public override void Initialize(CompProperties properties)
        {
            base.Initialize(properties);
            detectorEnabled = Props.startEnabled;
        }

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            powerComp = parent.GetComp<CompPowerTrader>();

            MapComponent_HediffDetectorManager manager = MapComponent_HediffDetectorManager.Get(parent.Map);
            manager?.Register(this);
            if (CanScan())
            {
                RequestScans();
            }
        }

        public override void PostDeSpawn(Map map, DestroyMode mode = DestroyMode.Vanish)
        {
            base.PostDeSpawn(map, mode);
            map?.GetComponent<MapComponent_HediffDetectorManager>()?.Deregister(this);
            lockedTargets.Clear();
        }

        public override void CompTick()
        {
            base.CompTick();
            if (!CanScan())
            {
                lockedTargets.Clear();
                return;
            }

            if (parent.IsHashIntervalTick(Math.Max(1, Props.scanIntervalTicks)))
            {
                RequestScans();
            }
        }

        /// <summary>
        /// 判断当前建筑是否处于可工作状态。断电和手动关闭都会停止扫描与头顶标记。
        /// </summary>
        internal bool CanScan()
        {
            if (!detectorEnabled || !parent.Spawned || parent.Map == null)
            {
                return false;
            }

            if (!Props.requirePower)
            {
                return true;
            }

            powerComp ??= parent.GetComp<CompPowerTrader>();
            return powerComp == null || powerComp.PowerOn;
        }

        private void RequestScans()
        {
            Map map = parent.Map;
            MapComponent_HediffDetectorManager manager = map != null ? MapComponent_HediffDetectorManager.Get(map) : null;
            if (manager == null)
            {
                return;
            }

            // 迷雾扫描与 Pawn 扫描分队列执行，避免高半径迷雾任务延迟反隐。
            if (Props.revealFog)
            {
                manager.RequestFogScan(this);
            }

            HediffDetectorEntry[] entries = Props.GetDetectionEntries();
            if (entries.Length > 0 || Props.disruptEnemyInvisibility)
            {
                manager.RequestPawnScan(this);
            }
            else
            {
                lockedTargets.Clear();
            }
        }

        /// <summary>
        /// 在地图管理器的预算内执行一次 Pawn 扫描。先按范围过滤，再仅遍历候选 Pawn 的 Hediff 列表。
        /// </summary>
        internal void PerformPawnScan(MapComponent_HediffDetectorManager manager)
        {
            Map map = parent.Map;
            if (map == null)
            {
                return;
            }

            HediffDetectorEntry[] entries = Props.GetDetectionEntries();
            bool disruptInvisibility = Props.disruptEnemyInvisibility;
            bool hasInvisibilityMarker = HasInvisibilityMarker(entries);
            lockedTargets.Clear();

            IReadOnlyList<Pawn> pawns = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn pawn = pawns[i];
                if (pawn == null || pawn.Dead || !IsWithinDetectRange(pawn))
                {
                    continue;
                }

                CheckAndProcessPawn(pawn, entries, disruptInvisibility, hasInvisibilityMarker, manager);
            }
        }

        private static bool HasInvisibilityMarker(HediffDetectorEntry[] entries)
        {
            for (int i = 0; i < entries.Length; i++)
            {
                if (entries[i].matchAllInvisibilityHediffs)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 探测条目和反隐在同一轮 Hediff 遍历内完成，避免分别扫描同一 Pawn 的 Hediff 列表。
        /// </summary>
        private void CheckAndProcessPawn(Pawn pawn, HediffDetectorEntry[] entries, bool disruptInvisibility, bool hasInvisibilityMarker, MapComponent_HediffDetectorManager manager)
        {
            List<Hediff> hediffs = pawn.health?.hediffSet?.hediffs;
            if (hediffs == null || hediffs.Count == 0)
            {
                return;
            }

            bool shouldDisrupt = disruptInvisibility && IsEnemyOfDetector(pawn);
            bool invisibilityRecorded = false;
            Material bestMaterial = null;
            int bestPriority = -1;

            for (int i = 0; i < hediffs.Count; i++)
            {
                Hediff hediff = hediffs[i];
                if (hediff == null)
                {
                    continue;
                }

                // 一个 Hediff 在本轮只做一次隐形组件判断，同时供反隐和隐形通配标记使用。
                bool needsInvisibilityCheck = hasInvisibilityMarker || (shouldDisrupt && !invisibilityRecorded);
                bool isInvisibilityHediff = needsInvisibilityCheck && HediffDetectorInvisibilityCompatibility.IsInvisibilityHediff(hediff);
                if (shouldDisrupt && !invisibilityRecorded && isInvisibilityHediff)
                {
                    manager.MarkPawnInvisibilityDisrupted(pawn, EffectiveDisruptionDurationTicks);
                    invisibilityRecorded = true;
                }

                for (int j = 0; j < entries.Length; j++)
                {
                    HediffDetectorEntry entry = entries[j];
                    if ((hediff.def == entry.def || (entry.matchAllInvisibilityHediffs && isInvisibilityHediff)) && entry.priority > bestPriority)
                    {
                        bestPriority = entry.priority;
                        bestMaterial = entry.material;
                    }
                }
            }

            if (bestMaterial != null)
            {
                lockedTargets.Add(new HediffDetectorLockedTarget
                {
                    pawn = pawn,
                    material = bestMaterial
                });
            }
        }

        /// <summary>
        /// 小于等于零的 detectRadius 表示整图模式；否则使用 AABB 加平方距离避免开方。
        /// </summary>
        private bool IsWithinDetectRange(Pawn pawn)
        {
            float radius = Props.detectRadius;
            if (radius <= 0f)
            {
                return true;
            }

            float dx = pawn.Position.x - parent.Position.x;
            if (dx > radius || dx < -radius)
            {
                return false;
            }

            float dz = pawn.Position.z - parent.Position.z;
            return dz <= radius && dz >= -radius && dx * dx + dz * dz <= RadiusSquared;
        }

        /// <summary>
        /// 使用 Thing-Faction 敌对判定，确保原版隐形不会让目标在反隐前被误判为非敌对。
        /// </summary>
        private bool IsEnemyOfDetector(Pawn pawn)
        {
            return parent.Faction != null && pawn.HostileTo(parent.Faction);
        }

        private int EffectiveDisruptionDurationTicks
        {
            get
            {
                if (Props.disruptionDurationTicks > 0)
                {
                    return Props.disruptionDurationTicks;
                }

                return Math.Max(MinimumDisruptionDurationTicks, Math.Max(1, Props.scanIntervalTicks) + DisruptionGraceTicks);
            }
        }

        /// <summary>
        /// 在探测半径内寻找最近的仍被迷雾遮蔽的格子，并将连通揭雾任务交给地图管理器分帧完成。整图模式则单独提交全图揭雾任务。
        /// </summary>
        internal void PerformFogScan(MapComponent_HediffDetectorManager manager)
        {
            Map map = parent.Map;
            if (map == null || map.fogGrid == null)
            {
                return;
            }

            if (Props.detectRadius <= 0f)
            {
                manager.QueueFullMapFogReveal();
                return;
            }

            IntVec3[] offsets = Props.GetFogScanOffsets();
            IntVec3 center = parent.Position;
            int directBlockersRevealed = 0;
            const int maxDirectBlockersPerScan = 128;

            for (int i = 0; i < offsets.Length; i++)
            {
                IntVec3 cell = new IntVec3(center.x + offsets[i].x, 0, center.z + offsets[i].z);
                if (!cell.InBounds(map) || !map.fogGrid.IsFogged(cell))
                {
                    continue;
                }

                if (IsFogBlocker(map, cell))
                {
                    map.fogGrid.Unfog(cell);
                    directBlockersRevealed++;
                    if (directBlockersRevealed >= maxDirectBlockersPerScan)
                    {
                        return;
                    }

                    continue;
                }

                manager.QueueConnectedFogReveal(cell);
                return;
            }
        }

        internal static bool IsFogBlocker(Map map, IntVec3 cell)
        {
            Building edifice = cell.GetEdifice(map);
            return edifice != null && edifice.def.MakeFog;
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref detectorEnabled, "hediffDetectorEnabled", Props.startEnabled);
        }

        private void SetDetectorEnabled(bool enabled)
        {
            if (detectorEnabled == enabled)
            {
                return;
            }

            detectorEnabled = enabled;
            if (detectorEnabled)
            {
                SoundDefOf.Tick_High.PlayOneShotOnCamera();
                if (CanScan())
                {
                    RequestScans();
                }
            }
            else
            {
                SoundDefOf.Tick_Low.PlayOneShotOnCamera();
                lockedTargets.Clear();
            }
        }

        private void ToggleDetectorForSelection()
        {
            int frame = Time.frameCount;
            if (lastSelectionToggleFrame != frame)
            {
                lastSelectionToggleFrame = frame;
                lastSelectionToggleTargetState = ShouldEnableSelection();
            }

            SetDetectorEnabled(lastSelectionToggleTargetState);
        }

        /// <summary>
        /// 多选时只要存在关闭的同类玩家建筑，就将整组选中建筑开启；否则整组关闭。
        /// </summary>
        private bool ShouldEnableSelection()
        {
            bool foundDetector = false;
            List<object> selected = Find.Selector.SelectedObjectsListForReading;
            for (int i = 0; i < selected.Count; i++)
            {
                ThingWithComps thing = selected[i] as ThingWithComps;
                CompHediffDetector detector = thing?.GetComp<CompHediffDetector>();
                if (detector == null || detector.parent.Faction != Faction.OfPlayer)
                {
                    continue;
                }

                foundDetector = true;
                if (!detector.DetectorEnabled)
                {
                    return true;
                }
            }

            return !foundDetector || !detectorEnabled;
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (Gizmo gizmo in base.CompGetGizmosExtra())
            {
                yield return gizmo;
            }

            if (parent.Faction != Faction.OfPlayer)
            {
                yield break;
            }

            Command_Toggle command = new Command_Toggle
            {
                defaultLabel = Props.gizmoLabelKey.Translate(),
                defaultDesc = Props.gizmoDescKey.Translate(),
                icon = detectorEnabled ? EnabledIcon : DisabledIcon,
                isActive = () => detectorEnabled,
                toggleAction = ToggleDetectorForSelection,
                groupKey = GizmoGroupKey
            };
            yield return command;
        }

        public override void PostDrawExtraSelectionOverlays()
        {
            base.PostDrawExtraSelectionOverlays();
            if (detectorEnabled && Props.detectRadius > 0.1f)
            {
                DrawRadiusRing(parent.DrawPos, Props.detectRadius);
            }
        }

        private static Texture2D ResolveIcon(ref Texture2D cachedIcon, string path)
        {
            if (cachedIcon == null)
            {
                cachedIcon = path.NullOrEmpty() ? BaseContent.BadTex : ContentFinder<Texture2D>.Get(path, false) ?? BaseContent.BadTex;
            }

            return cachedIcon;
        }

        /// <summary>
        /// 使用平滑红线绘制探测半径，避免原版半径环在大半径时出现明显折线。
        /// </summary>
        private static void DrawRadiusRing(Vector3 center, float radius)
        {
            center.y = AltitudeLayer.MetaOverlays.AltitudeFor() + 5f;

            int segments = Mathf.Clamp(Mathf.RoundToInt(radius * 4f), 32, 128);
            float angleStep = 360f / segments;
            Vector3 previous = center + new Vector3(radius, 0f, 0f);
            for (int i = 1; i <= segments; i++)
            {
                float radians = i * angleStep * Mathf.Deg2Rad;
                Vector3 next = center + new Vector3(Mathf.Cos(radians) * radius, 0f, Mathf.Sin(radians) * radius);
                GenDraw.DrawLineBetween(previous, next, HediffDetectorRenderResources.RadiusRingMaterial, 0.05f);
                previous = next;
            }
        }
    }

    /// <summary>
    /// 同地图探测器的统一调度器：限制全图 Pawn 扫描数量、分帧揭露迷雾、维护反隐缓存并集中绘制图标。
    /// </summary>
    public class MapComponent_HediffDetectorManager : MapComponent
    {
        private const int MaxFogScansPerTick = 1;
        private const int MaxPawnScansPerTick = 1;
        private const int FogRevealCellsPerTick = 384;
        private const int CacheCleanupIntervalTicks = 60;
        private const int PendingQueueCleanupThreshold = 64;

        private readonly List<CompHediffDetector> detectors = new List<CompHediffDetector>();
        private readonly HashSet<Pawn> drawnPawns = new HashSet<Pawn>();
        private Queue<CompHediffDetector> pendingFogScans = new Queue<CompHediffDetector>();
        private readonly HashSet<CompHediffDetector> pendingFogScanSet = new HashSet<CompHediffDetector>();
        private Queue<CompHediffDetector> pendingPawnScans = new Queue<CompHediffDetector>();
        private readonly HashSet<CompHediffDetector> pendingPawnScanSet = new HashSet<CompHediffDetector>();
        private readonly List<ConnectedFogRevealJob> fogRevealJobs = new List<ConnectedFogRevealJob>();
        private readonly HashSet<int> queuedFogRevealRoots = new HashSet<int>();
        private FullMapFogRevealJob fullMapFogRevealJob;
        private bool fullMapFogRevealed;
        private readonly Dictionary<Pawn, int> disruptedPawns = new Dictionary<Pawn, int>();
        private readonly List<Pawn> expiredPawns = new List<Pawn>();

        private float markerAnimationTime;
        private int nextCacheCleanupTick = -1;

        public MapComponent_HediffDetectorManager(Map map) : base(map)
        {
        }

        public static MapComponent_HediffDetectorManager Get(Map map)
        {
            return map?.GetComponent<MapComponent_HediffDetectorManager>();
        }

        public void Register(CompHediffDetector detector)
        {
            if (detector != null && !detectors.Contains(detector))
            {
                detectors.Add(detector);
            }
        }

        public void Deregister(CompHediffDetector detector)
        {
            detectors.Remove(detector);
            pendingFogScanSet.Remove(detector);
            pendingPawnScanSet.Remove(detector);
            CleanupPendingQueuesIfNeeded(true);
        }

        public void RequestFogScan(CompHediffDetector detector)
        {
            if (detector != null && pendingFogScanSet.Add(detector))
            {
                pendingFogScans.Enqueue(detector);
                CleanupPendingQueuesIfNeeded(false);
            }
        }

        public void RequestPawnScan(CompHediffDetector detector)
        {
            if (detector != null && pendingPawnScanSet.Add(detector))
            {
                pendingPawnScans.Enqueue(detector);
                CleanupPendingQueuesIfNeeded(false);
            }
        }

        /// <summary>
        /// 同一迷雾根格只保留一个 Flood job，防止多个探测器反复提交相同的连通区域。
        /// </summary>
        public void QueueConnectedFogReveal(IntVec3 root)
        {
            if (map == null || map.fogGrid == null || !root.InBounds(map) || !map.fogGrid.IsFogged(root))
            {
                return;
            }

            int rootIndex = map.cellIndices.CellToIndex(root);
            if (!queuedFogRevealRoots.Add(rootIndex))
            {
                return;
            }

            if (CompHediffDetector.IsFogBlocker(map, root))
            {
                map.fogGrid.Unfog(root);
                queuedFogRevealRoots.Remove(rootIndex);
                return;
            }

            fogRevealJobs.Add(new ConnectedFogRevealJob(map, root, rootIndex));
        }

        /// <summary>
        /// 提交整张地图的揭雾任务。同一地图只会保留一个任务，完成后不再重复遍历整张地图。
        /// </summary>
        public void QueueFullMapFogReveal()
        {
            if (map == null || map.fogGrid == null || fullMapFogRevealed || fullMapFogRevealJob != null)
            {
                return;
            }

            fullMapFogRevealJob = new FullMapFogRevealJob(map);
        }

        public void MarkPawnInvisibilityDisrupted(Pawn pawn, int durationTicks)
        {
            if (pawn == null || pawn.Destroyed || pawn.Dead || !pawn.Spawned || pawn.MapHeld != map)
            {
                return;
            }

            int untilTick = Find.TickManager.TicksGame + Math.Max(1, durationTicks);
            if (!disruptedPawns.TryGetValue(pawn, out int oldUntilTick) || oldUntilTick < untilTick)
            {
                disruptedPawns[pawn] = untilTick;
            }

            if (nextCacheCleanupTick < 0)
            {
                nextCacheCleanupTick = Find.TickManager.TicksGame + CacheCleanupIntervalTicks;
            }
        }

        public bool IsPawnInvisibilityDisrupted(Pawn pawn)
        {
            return pawn != null && !pawn.Destroyed && !pawn.Dead && pawn.Spawned && pawn.MapHeld == map &&
                   disruptedPawns.TryGetValue(pawn, out int untilTick) && Find.TickManager.TicksGame <= untilTick;
        }

        public override void MapComponentTick()
        {
            base.MapComponentTick();
            ProcessPendingFogScans();
            ProcessPendingPawnScans();
            ProcessFogRevealJobs();

            if (nextCacheCleanupTick >= 0 && Find.TickManager.TicksGame >= nextCacheCleanupTick)
            {
                CleanupExpiredDisruptions();
            }
        }

        public override void MapComponentDraw()
        {
            base.MapComponentDraw();
            if (Find.CurrentMap != map || !HasActiveMarkers())
            {
                return;
            }

            if (!Find.TickManager.Paused)
            {
                markerAnimationTime += Time.deltaTime;
            }

            drawnPawns.Clear();
            for (int i = detectors.Count - 1; i >= 0; i--)
            {
                CompHediffDetector detector = detectors[i];
                if (!IsRegisteredDetectorValid(detector))
                {
                    detectors.RemoveAt(i);
                    continue;
                }

                if (!detector.CanScan())
                {
                    continue;
                }

                for (int j = 0; j < detector.lockedTargets.Count; j++)
                {
                    HediffDetectorLockedTarget target = detector.lockedTargets[j];
                    Pawn pawn = target.pawn;
                    if (pawn == null || pawn.Destroyed || pawn.Dead || !pawn.Spawned || pawn.MapHeld != map || target.material == null || !drawnPawns.Add(pawn))
                    {
                        continue;
                    }

                    float bobbing = 0f;
                    if (detector.Props.markBobbingFrequency > 0f && detector.Props.markBobbingAmplitude > 0f)
                    {
                        bobbing = Mathf.Sin(markerAnimationTime * detector.Props.markBobbingFrequency) * detector.Props.markBobbingAmplitude;
                    }

                    Vector3 drawPosition = pawn.DrawPos;
                    drawPosition.z += detector.Props.markHeightOffset + bobbing;
                    drawPosition.y = AltitudeLayer.MetaOverlays.AltitudeFor();
                    float scale = Math.Max(0.01f, detector.Props.markScale);
                    Matrix4x4 matrix = Matrix4x4.TRS(drawPosition, Quaternion.identity, new Vector3(scale, 1f, scale));
                    Graphics.DrawMesh(MeshPool.plane10, matrix, target.material, 0);
                }
            }
        }

        private void ProcessPendingFogScans()
        {
            ProcessPendingQueue(pendingFogScans, pendingFogScanSet, MaxFogScansPerTick, detector => detector.PerformFogScan(this));
        }

        private void ProcessPendingPawnScans()
        {
            ProcessPendingQueue(pendingPawnScans, pendingPawnScanSet, MaxPawnScansPerTick, detector => detector.PerformPawnScan(this));
        }

        /// <summary>
        /// 先跳过失效队列项；每 tick 只真正执行预算数量的扫描，避免多个大半径建筑造成扫描尖峰。
        /// </summary>
        private void ProcessPendingQueue(Queue<CompHediffDetector> queue, HashSet<CompHediffDetector> queueSet, int budget, Action<CompHediffDetector> action)
        {
            int processed = 0;
            while (processed < budget && queue.Count > 0)
            {
                CompHediffDetector detector = queue.Dequeue();
                queueSet.Remove(detector);
                if (!IsRegisteredDetectorValid(detector) || !detector.CanScan())
                {
                    continue;
                }

                action(detector);
                processed++;
            }
        }

        private void ProcessFogRevealJobs()
        {
            int budget = FogRevealCellsPerTick;

            if (fullMapFogRevealJob != null && budget > 0)
            {
                budget -= fullMapFogRevealJob.Process(map, budget);
                if (fullMapFogRevealJob.Complete)
                {
                    fullMapFogRevealJob = null;
                    fullMapFogRevealed = true;
                }
            }

            for (int i = 0; i < fogRevealJobs.Count && budget > 0;)
            {
                ConnectedFogRevealJob job = fogRevealJobs[i];
                budget -= job.Process(map, budget);
                if (job.Complete)
                {
                    queuedFogRevealRoots.Remove(job.rootCellIndex);
                    fogRevealJobs.RemoveAt(i);
                }
                else
                {
                    i++;
                }
            }
        }

        private void CleanupPendingQueuesIfNeeded(bool force)
        {
            if (!force && pendingFogScans.Count < PendingQueueCleanupThreshold && pendingPawnScans.Count < PendingQueueCleanupThreshold)
            {
                return;
            }

            CleanupPendingQueue(ref pendingFogScans, pendingFogScanSet);
            CleanupPendingQueue(ref pendingPawnScans, pendingPawnScanSet);
        }

        private void CleanupPendingQueue(ref Queue<CompHediffDetector> queue, HashSet<CompHediffDetector> queueSet)
        {
            Queue<CompHediffDetector> cleaned = new Queue<CompHediffDetector>(queue.Count);
            queueSet.Clear();
            while (queue.Count > 0)
            {
                CompHediffDetector detector = queue.Dequeue();
                if (IsRegisteredDetectorValid(detector) && detector.CanScan() && queueSet.Add(detector))
                {
                    cleaned.Enqueue(detector);
                }
            }

            queue = cleaned;
        }

        private void CleanupExpiredDisruptions()
        {
            int ticks = Find.TickManager.TicksGame;
            expiredPawns.Clear();
            foreach (KeyValuePair<Pawn, int> entry in disruptedPawns)
            {
                Pawn pawn = entry.Key;
                if (pawn == null || pawn.Destroyed || pawn.Dead || !pawn.Spawned || pawn.MapHeld != map || ticks > entry.Value)
                {
                    expiredPawns.Add(pawn);
                }
            }

            for (int i = 0; i < expiredPawns.Count; i++)
            {
                disruptedPawns.Remove(expiredPawns[i]);
            }

            expiredPawns.Clear();
            nextCacheCleanupTick = disruptedPawns.Count > 0 ? ticks + CacheCleanupIntervalTicks : -1;
        }

        private bool HasActiveMarkers()
        {
            for (int i = detectors.Count - 1; i >= 0; i--)
            {
                CompHediffDetector detector = detectors[i];
                if (!IsRegisteredDetectorValid(detector))
                {
                    detectors.RemoveAt(i);
                    continue;
                }

                if (detector.CanScan() && detector.lockedTargets.Count > 0)
                {
                    return true;
                }
            }

            return false;
        }

        private bool IsRegisteredDetectorValid(CompHediffDetector detector)
        {
            return detector != null && detector.parent != null && !detector.parent.Destroyed && detector.parent.Spawned && detector.parent.Map == map;
        }

        /// <summary>
        /// 分帧揭露一个连通迷雾区域。visited 使用 cell index，避免在大面积迷雾中重复入队。
        /// </summary>
        private sealed class ConnectedFogRevealJob
        {
            private readonly Queue<IntVec3> cells = new Queue<IntVec3>(128);
            private readonly HashSet<int> visited = new HashSet<int>();

            public readonly int rootCellIndex;

            public bool Complete => cells.Count == 0;

            public ConnectedFogRevealJob(Map map, IntVec3 root, int rootCellIndex)
            {
                this.rootCellIndex = rootCellIndex;
                TryEnqueue(map, root);
            }

            public int Process(Map map, int budget)
            {
                if (map == null || map.fogGrid == null || budget <= 0)
                {
                    return 0;
                }

                int revealed = 0;
                while (revealed < budget && cells.Count > 0)
                {
                    IntVec3 cell = cells.Dequeue();
                    if (!cell.InBounds(map) || !map.fogGrid.IsFogged(cell))
                    {
                        continue;
                    }

                    bool blocker = CompHediffDetector.IsFogBlocker(map, cell);
                    map.fogGrid.Unfog(cell);
                    revealed++;
                    if (blocker)
                    {
                        continue;
                    }

                    for (int i = 0; i < GenAdj.CardinalDirections.Length; i++)
                    {
                        TryEnqueue(map, cell + GenAdj.CardinalDirections[i]);
                    }

                    for (int i = 0; i < GenAdj.AdjacentCells.Length; i++)
                    {
                        IntVec3 adjacent = cell + GenAdj.AdjacentCells[i];
                        if (adjacent.InBounds(map) && map.fogGrid.IsFogged(adjacent) && CompHediffDetector.IsFogBlocker(map, adjacent))
                        {
                            TryEnqueue(map, adjacent);
                        }
                    }
                }

                return revealed;
            }

            private void TryEnqueue(Map map, IntVec3 cell)
            {
                if (map == null || map.fogGrid == null || !cell.InBounds(map) || !map.fogGrid.IsFogged(cell))
                {
                    return;
                }

                if (visited.Add(map.cellIndices.CellToIndex(cell)))
                {
                    cells.Enqueue(cell);
                }
            }
        }

        /// <summary>
        /// 整图揭雾任务。以固定顺序遍历每个格子，不依赖可见路径，因此能确保清除所有迷雾。
        /// </summary>
        private sealed class FullMapFogRevealJob
        {
            private readonly int width;
            private readonly int height;
            private int currentX;
            private int currentZ;

            public bool Complete => currentZ >= height;

            public FullMapFogRevealJob(Map map)
            {
                width = map.Size.x;
                height = map.Size.z;
            }

            /// <summary>
            /// 返回本 tick 已检查的格子数，而非仅返回已揭露的格子数，以保证无迷雾的地图也不会突破公共预算。
            /// </summary>
            public int Process(Map map, int budget)
            {
                if (map == null || map.fogGrid == null || budget <= 0 || Complete)
                {
                    return 0;
                }

                int inspected = 0;
                while (inspected < budget && !Complete)
                {
                    IntVec3 cell = new IntVec3(currentX, 0, currentZ);
                    if (map.fogGrid.IsFogged(cell))
                    {
                        map.fogGrid.Unfog(cell);
                    }

                    inspected++;
                    currentX++;
                    if (currentX >= width)
                    {
                        currentX = 0;
                        currentZ++;
                    }
                }

                return inspected;
            }
        }

        public static bool IsPawnDisruptedByDetector(Pawn pawn)
        {
            Map map = pawn?.MapHeld;
            MapComponent_HediffDetectorManager manager = map?.GetComponent<MapComponent_HediffDetectorManager>();
            return manager != null && manager.IsPawnInvisibilityDisrupted(pawn);
        }
    }

    /// <summary>
    /// 原版隐形组件的可选兼容层。使用字符串解析类型，避免在相关内容未加载或类型不存在的环境中产生装载依赖。
    /// </summary>
    internal static class HediffDetectorInvisibilityCompatibility
    {
        private const string InvisibilityCompTypeName = "Verse.HediffComp_Invisibility";

        private static bool attemptedResolution;
        private static Type invisibilityCompType;
        private static MethodBase forcedVisibleGetter;

        // 只有隐形组件类型和 ForcedVisible getter 都存在时，反隐扫描才会入队。
        // 这样即使未来原版保留类型但改动私有属性名，也不会留下无效的全图 Hediff 扫描。
        public static bool IsAvailable => ForcedVisibleGetter != null;

        public static MethodBase ForcedVisibleGetter
        {
            get
            {
                _ = InvisibilityCompType;
                return forcedVisibleGetter;
            }
        }

        private static Type InvisibilityCompType
        {
            get
            {
                if (!attemptedResolution)
                {
                    attemptedResolution = true;
                    invisibilityCompType = AccessTools.TypeByName(InvisibilityCompTypeName);
                    forcedVisibleGetter = invisibilityCompType != null
                        ? AccessTools.PropertyGetter(invisibilityCompType, "ForcedVisible")
                        : null;
                }

                return invisibilityCompType;
            }
        }

        /// <summary>
        /// 在 HediffWithComps 的组件列表内以运行时 Type 匹配原版隐形组件，不直接引用 Anomaly 类型。
        /// </summary>
        public static bool IsInvisibilityHediff(Hediff hediff)
        {
            Type invisibilityType = InvisibilityCompType;
            if (invisibilityType == null || !(hediff is HediffWithComps hediffWithComps))
            {
                return false;
            }

            List<HediffComp> comps = hediffWithComps.comps;
            for (int i = 0; i < comps.Count; i++)
            {
                if (invisibilityType.IsInstanceOfType(comps[i]))
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// 将地图管理器的短时反隐记录追加到原版私有 ForcedVisible 结果。当前游戏未加载该组件时，Prepare 返回 false，补丁不会安装。
    /// </summary>
    [HarmonyPatch]
    public static class Patch_HediffComp_Invisibility_ForcedVisible_HediffDetector
    {
        public static bool Prepare()
        {
            return HediffDetectorInvisibilityCompatibility.ForcedVisibleGetter != null;
        }

        public static MethodBase TargetMethod()
        {
            return HediffDetectorInvisibilityCompatibility.ForcedVisibleGetter;
        }

        public static void Postfix(HediffComp __instance, ref bool __result)
        {
            if (!__result && MapComponent_HediffDetectorManager.IsPawnDisruptedByDetector(__instance?.parent?.pawn))
            {
                __result = true;
            }
        }
    }
}
