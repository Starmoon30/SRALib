using RimWorld;
using Verse;
using System.Collections.Generic;
using RimWorld.Planet;

namespace SRA
{
    public class CompProperties_AircraftHangar : CompProperties
    {
        public ThingDef aircraftDef; // 对应的战机定义
        public int aircraftCount = 1; // 起飞后提供的战机数量
        public ThingDef skyfallerLeaving; // 起飞时的天空坠落者效果

        // 新增：自动发射配置
        public bool autoLaunchEnabled = false; // 是否启用自动发射
        public int autoLaunchDelayTicks = 600; // 自动发射延迟（ticks，默认10秒）
        public bool autoLaunchOnConstruction = true; // 建造完成后自动发射
        public bool autoLaunchOnPowerOn = false; // 通电时自动发射

        public CompProperties_AircraftHangar()
        {
            compClass = typeof(CompAircraftHangar);
        }
    }

    public class CompAircraftHangar : ThingComp
    {
        public CompProperties_AircraftHangar Props => (CompProperties_AircraftHangar)props;

        // 新增：自动发射状态
        private bool autoLaunchScheduled = false;
        private int autoLaunchTick = -1;
        private bool hasLaunched = false;

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);

            if (!respawningAfterLoad && Props.autoLaunchEnabled && Props.autoLaunchOnConstruction)
            {
                ScheduleAutoLaunch();
            }
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref autoLaunchScheduled, "autoLaunchScheduled", false);
            Scribe_Values.Look(ref autoLaunchTick, "autoLaunchTick", -1);
            Scribe_Values.Look(ref hasLaunched, "hasLaunched", false);
        }

        public override void CompTick()
        {
            base.CompTick();

            // 处理自动发射
            if (Props.autoLaunchEnabled && !hasLaunched)
            {
                HandleAutoLaunch();
            }
        }

        // 新增：自动发射处理
        private void HandleAutoLaunch()
        {
            // 检查预定发射
            if (autoLaunchScheduled && Find.TickManager.TicksGame >= autoLaunchTick)
            {
                LaunchAircraft();
                return;
            }

            // 检查通电自动发射
            if (Props.autoLaunchOnPowerOn && IsPoweredOn() && !autoLaunchScheduled)
            {
                ScheduleAutoLaunch();
                return;
            }
        }

        // 新增：检查电力状态
        private bool IsPoweredOn()
        {
            var powerComp = parent.GetComp<CompPowerTrader>();
            return powerComp != null && powerComp.PowerOn;
        }

        // 新增：预定自动发射
        private void ScheduleAutoLaunch()
        {
            if (hasLaunched || autoLaunchScheduled)
                return;

            autoLaunchScheduled = true;
            autoLaunchTick = Find.TickManager.TicksGame + Props.autoLaunchDelayTicks;

            Messages.Message("AircraftAutoLaunchScheduled".Translate(Props.aircraftDef.LabelCap, (Props.autoLaunchDelayTicks / 60f).ToString("F1")), parent, MessageTypeDefOf.NeutralEvent);
        }

        // 新增：强制立即发射（用于调试或其他系统调用）
        public void ForceLaunch()
        {
            if (!hasLaunched)
            {
                LaunchAircraft();
            }
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (Gizmo gizmo in base.CompGetGizmosExtra())
            {
                yield return gizmo;
            }

            // 如果已经发射，不显示任何按钮
            if (hasLaunched)
                yield break;

            // 手动发射命令
            Command_Action launchCommand = new Command_Action
            {
                defaultLabel = "LaunchAircraft".Translate(),
                defaultDesc = "LaunchAircraftDesc".Translate(),
                icon = TexCommand.Attack,
                action = LaunchAircraft
            };

            // 检查条件：建筑完好
            if (parent.HitPoints <= 0)
            {
                launchCommand.Disable("HangarDamaged".Translate());
            }

            yield return launchCommand;
        }

        // 新增：切换自动发射状态
        private void ToggleAutoLaunch()
        {
            if (autoLaunchScheduled)
            {
                // 取消预定发射
                autoLaunchScheduled = false;
                autoLaunchTick = -1;
                Messages.Message("AutoLaunchCancelled".Translate(), parent, MessageTypeDefOf.NeutralEvent);
            }
            else
            {
                // 预定发射
                ScheduleAutoLaunch();
            }
        }

        private void LaunchAircraft()
        {
            if (hasLaunched)
                return;

            // 获取全局战机管理器
            WorldComponent_AircraftManager aircraftManager = Find.World.GetComponent<WorldComponent_AircraftManager>();

            if (aircraftManager == null)
            {
                Log.Error("AircraftManagerNotFound".Translate());
                return;
            }

            // 立即向全局管理器注册战机
            aircraftManager.AddAircraft(Props.aircraftDef, Props.aircraftCount, parent.Faction);

            // 显示消息
            Messages.Message("AircraftLaunched".Translate(Props.aircraftCount, Props.aircraftDef.LabelCap), parent, MessageTypeDefOf.PositiveEvent);

            // 创建起飞效果（仅视觉效果）
            if (Props.skyfallerLeaving != null)
            {
                CreateTakeoffEffect();
            }
            else
            {
                // 如果没有定义 Skyfaller，直接销毁建筑
                parent.Destroy();
            }

            hasLaunched = true;
            autoLaunchScheduled = false;
        }

        private void CreateTakeoffEffect()
        {
            try
            {
                // 创建 1 单位 Chemfuel 作为 Skyfaller 的内容物
                Thing chemfuel = ThingMaker.MakeThing(ThingDefOf.Chemfuel);
                chemfuel.stackCount = 1;

                // 创建包含 Chemfuel 的 Skyfaller
                Skyfaller skyfaller = SkyfallerMaker.MakeSkyfaller(Props.skyfallerLeaving, chemfuel);

                // 设置起飞位置（建筑当前位置）
                IntVec3 takeoffPos = parent.Position;

                // 检查地图是否有效
                if (parent.Map == null)
                {
                    Log.Error("TakeoffEffectMapNull".Translate());
                    return;
                }

                // 生成 Skyfaller
                GenSpawn.Spawn(skyfaller, takeoffPos, parent.Map);

                Log.Message("TakeoffSkyfallerCreated".Translate(takeoffPos));

                // 销毁原建筑
                parent.Destroy(DestroyMode.Vanish);
            }
            catch (System.Exception ex)
            {
                Log.Error("TakeoffEffectError".Translate(ex.Message));
                // 如果Skyfaller创建失败，直接销毁建筑
                parent.Destroy(DestroyMode.Vanish);
            }
        }

        // 新增：检查是否已经发射
        public bool HasLaunched => hasLaunched;

        // 新增：获取自动发射状态信息（用于检查字符串）
        public override string CompInspectStringExtra()
        {
            if (hasLaunched)
                return "AircraftStatusLaunched".Translate();

            if (Props.autoLaunchEnabled)
            {
                if (autoLaunchScheduled)
                {
                    int ticksRemaining = autoLaunchTick - Find.TickManager.TicksGame;
                    float secondsRemaining = ticksRemaining / 60f;
                    return "AutoLaunchScheduled".Translate(secondsRemaining.ToString("F1"));
                }
                else
                {
                    return "AutoLaunchReady".Translate();
                }
            }

            return base.CompInspectStringExtra();
        }
    }
}
