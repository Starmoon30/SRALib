using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace SRA
{
    /// <summary>
    /// 强制温控器。常规模式仅将所在的封闭房间维持在目标温度；超频模式可覆写整张地图的室外环境温度。
    /// </summary>
    public class Building_TempControler : Building_TempControl
    {
        // 超频状态独立保存。旧存档没有该字段时按 false 读取，保持默认关闭。
        private bool overclockEnabled;

        public bool OverclockEnabled => overclockEnabled;

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            if (overclockEnabled)
            {
                MapComponent_TempControlerOverclock.Get(map)?.Register(this);
            }
        }

        public override void DeSpawn(DestroyMode mode = DestroyMode.Vanish)
        {
            if (overclockEnabled)
            {
                MapComponent_TempControlerOverclock.Get(Map)?.Deregister(this);
            }

            base.DeSpawn(mode);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref overclockEnabled, "tempControlerOverclockEnabled", false);
        }

        /// <summary>
        /// 原有建筑使用 Rare ticker，此处维持同一结算频率。室外温度由地图组件和 MapTemperature 补丁持续处理，避免被原版温度均衡重置。
        /// </summary>
        public override void TickRare()
        {
            base.TickRare();
            ApplyIndoorTemperatureControl();
        }

        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (Gizmo gizmo in base.GetGizmos())
            {
                yield return gizmo;
            }

            yield return new Command_Toggle
            {
                defaultLabel = "SRA_TempControler_OverclockLabel".Translate(),
                defaultDesc = "SRA_TempControler_OverclockDesc".Translate(),
                icon = TexCommand.DesirePower,
                isActive = () => overclockEnabled,
                toggleAction = ToggleOverclock
            };
        }

        /// <summary>
        /// 超频在同一张地图内互斥。开启当前设备时，地图管理器会关闭其他设备的超频状态。
        /// </summary>
        private void ToggleOverclock()
        {
            SetOverclockEnabled(!overclockEnabled);
        }

        private void SetOverclockEnabled(bool enabled)
        {
            if (overclockEnabled == enabled)
            {
                return;
            }

            overclockEnabled = enabled;
            if (!Spawned)
            {
                return;
            }

            MapComponent_TempControlerOverclock manager = MapComponent_TempControlerOverclock.Get(Map);
            if (enabled)
            {
                manager?.Register(this);
            }
            else
            {
                manager?.Deregister(this);
            }
        }

        /// <summary>
        /// 常规模式只修改封闭房间。室外房间由原版每 120 tick 回写为 OutdoorTemp，故不能在此处直接写入。
        /// </summary>
        private void ApplyIndoorTemperatureControl()
        {
            if (!TryGetOperationalTargetTemperature(out float targetTemperature))
            {
                return;
            }

            Room room = this.GetRoom(RegionType.Set_Passable);
            if (room == null || room.UsesOutdoorTemperature)
            {
                return;
            }

            room.Temperature = targetTemperature;
        }

        /// <summary>
        /// 返回当前设备可提供的超频目标。地图管理器每 tick 调用此方法缓存结果，高频的 OutdoorTemp getter 不会重复查询建筑组件。
        /// </summary>
        internal bool TryGetOverclockTemperature(out float targetTemperature)
        {
            targetTemperature = 0f;
            return overclockEnabled && Spawned && TryGetOperationalTargetTemperature(out targetTemperature);
        }

        private bool TryGetOperationalTargetTemperature(out float targetTemperature)
        {
            targetTemperature = 0f;
            if (compTempControl == null || (compPowerTrader != null && !compPowerTrader.PowerOn))
            {
                return false;
            }

            targetTemperature = compTempControl.targetTemperature;
            return true;
        }

        /// <summary>
        /// 由地图管理器调用以维持超频互斥。此处只修改状态；管理器会在同一调用链内移除注册并刷新室外温度。
        /// </summary>
        internal void DisableOverclockFromManager()
        {
            overclockEnabled = false;
        }
    }

    /// <summary>
    /// 同地图超频温控器的轻量调度器。超频设备互斥，并在设备状态变化时立即刷新室外房间温度。
    /// </summary>
    public class MapComponent_TempControlerOverclock : MapComponent
    {
        private readonly List<Building_TempControler> controllers = new List<Building_TempControler>();
        private bool hasOutdoorTemperatureOverride;
        private float outdoorTemperatureOverride;

        public MapComponent_TempControlerOverclock(Map map) : base(map)
        {
            TempControlerOverclockRegistry.Register(map.mapTemperature, this);
        }

        public static MapComponent_TempControlerOverclock Get(Map map)
        {
            MapComponent_TempControlerOverclock manager = map?.GetComponent<MapComponent_TempControlerOverclock>();
            if (manager != null)
            {
                // 保险起见在首次实际使用时再次登记，兼容地图组件先于 mapTemperature 构造的特殊加载顺序。
                TempControlerOverclockRegistry.Register(map.mapTemperature, manager);
            }

            return manager;
        }

        public override void MapComponentTick()
        {
            if (controllers.Count > 0)
            {
                UpdateOutdoorTemperatureOverride();
            }
        }

        public void Register(Building_TempControler controller)
        {
            if (controller == null)
            {
                return;
            }

            // 同图超频只能由一台设备持有。清除其他设备的保存状态，避免断电、读档后重新出现多个超频设备。
            for (int i = controllers.Count - 1; i >= 0; i--)
            {
                Building_TempControler existing = controllers[i];
                controllers.RemoveAt(i);
                if (existing != null && existing != controller && existing.OverclockEnabled)
                {
                    existing.DisableOverclockFromManager();
                }
            }

            controllers.Add(controller);
            UpdateOutdoorTemperatureOverride();
        }

        public void Deregister(Building_TempControler controller)
        {
            if (controller != null && controllers.Remove(controller))
            {
                UpdateOutdoorTemperatureOverride();
            }
        }

        internal bool TryGetOutdoorTemperatureOverride(out float targetTemperature)
        {
            targetTemperature = outdoorTemperatureOverride;
            return hasOutdoorTemperatureOverride;
        }

        /// <summary>
        /// 选择唯一登记且可用的设备。失效、拆除或关闭的条目会在此处清理，不会长期保留无效引用。
        /// </summary>
        private void UpdateOutdoorTemperatureOverride()
        {
            bool previousHasOverride = hasOutdoorTemperatureOverride;
            float previousTemperature = outdoorTemperatureOverride;

            hasOutdoorTemperatureOverride = false;
            outdoorTemperatureOverride = 0f;
            for (int i = controllers.Count - 1; i >= 0; i--)
            {
                Building_TempControler controller = controllers[i];
                if (controller == null || controller.Destroyed || !controller.Spawned || controller.Map != map || !controller.OverclockEnabled)
                {
                    controllers.RemoveAt(i);
                    continue;
                }

                if (controller.TryGetOverclockTemperature(out float targetTemperature))
                {
                    hasOutdoorTemperatureOverride = true;
                    outdoorTemperatureOverride = targetTemperature;
                    break;
                }
            }

            if (previousHasOverride != hasOutdoorTemperatureOverride ||
                (hasOutdoorTemperatureOverride && !Mathf.Approximately(previousTemperature, outdoorTemperatureOverride)))
            {
                RefreshOutdoorRoomTemperatures();
            }
        }

        /// <summary>
        /// 覆写状态切换时立即同步所有使用室外温度的房间；之后原版每 120 tick 的均衡会从被覆写的 OutdoorTemp 读取相同温度。
        /// </summary>
        private void RefreshOutdoorRoomTemperatures()
        {
            float targetTemperature = map.mapTemperature.OutdoorTemp;
            IReadOnlyList<Room> rooms = map.regionGrid.AllRooms;
            for (int i = 0; i < rooms.Count; i++)
            {
                Room room = rooms[i];
                if (room != null && !room.Dereferenced && room.UsesOutdoorTemperature)
                {
                    room.Temperature = targetTemperature;
                }
            }
        }
    }

    /// <summary>
    /// 将已有地图组件与 MapTemperature 关联。ConditionalWeakTable 不会阻止地图及其温度对象在地图关闭后被回收。
    /// </summary>
    internal static class TempControlerOverclockRegistry
    {
        private static readonly ConditionalWeakTable<MapTemperature, MapComponent_TempControlerOverclock> Managers =
            new ConditionalWeakTable<MapTemperature, MapComponent_TempControlerOverclock>();

        internal static void Register(MapTemperature mapTemperature, MapComponent_TempControlerOverclock manager)
        {
            if (mapTemperature == null || manager == null)
            {
                return;
            }

            // 读档时原版会先创建地图组件，再反序列化替换组件列表。必须替换旧映射，不能在键已存在时保留已脱离地图的构造期实例。
            Managers.Remove(mapTemperature);
            Managers.Add(mapTemperature, manager);
        }

        internal static bool TryGetOutdoorTemperatureOverride(MapTemperature mapTemperature, out float targetTemperature)
        {
            targetTemperature = 0f;
            return mapTemperature != null && Managers.TryGetValue(mapTemperature, out MapComponent_TempControlerOverclock manager) &&
                   manager.TryGetOutdoorTemperatureOverride(out targetTemperature);
        }
    }

    /// <summary>
    /// 仅对登记有超频温控器的地图覆写 OutdoorTemp；其他地图只进行一次弱表查询后直接保留原版结果。
    /// </summary>
    [HarmonyPatch(typeof(MapTemperature), nameof(MapTemperature.OutdoorTemp), MethodType.Getter)]
    [HarmonyPriority(Priority.Last)]
    public static class Patch_MapTemperature_OutdoorTemp_TempControler
    {
        [HarmonyPostfix]
        public static void Postfix(MapTemperature __instance, ref float __result)
        {
            if (TempControlerOverclockRegistry.TryGetOutdoorTemperatureOverride(__instance, out float targetTemperature))
            {
                __result = targetTemperature;
            }
        }
    }

    /// <summary>
    /// 原版 CompTempControl 在无 CompPowerTrader 时会以 AppendLine 结束检查文本，触发 ThingWithComps 的尾随空白报错。
    /// 仅清理本温控器的返回值，不改变其他温控建筑的原版文本。
    /// </summary>
    [HarmonyPatch(typeof(CompTempControl), nameof(CompTempControl.CompInspectStringExtra))]
    public static class Patch_CompTempControl_InspectString_TempControler
    {
        [HarmonyPostfix]
        public static void Postfix(CompTempControl __instance, ref string __result)
        {
            if (__instance?.parent is Building_TempControler && !__result.NullOrEmpty())
            {
                __result = __result.TrimEnd();
            }
        }
    }
}
