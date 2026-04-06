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
        
        public CompProperties_AircraftHangar()
        {
            compClass = typeof(CompAircraftHangar);
        }
    }

    public class CompAircraftHangar : ThingComp
    {
        public CompProperties_AircraftHangar Props => (CompProperties_AircraftHangar)props;
        
        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (Gizmo gizmo in base.CompGetGizmosExtra())
            {
                yield return gizmo;
            }

            // 起飞命令
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

        private void LaunchAircraft()
        {
            // 获取全局战机管理器
            WorldComponent_AircraftManager aircraftManager = Find.World.GetComponent<WorldComponent_AircraftManager>();
            
            if (aircraftManager == null)
            {
                SRALog.Debug("AircraftManagerNotFound".Translate());
                return;
            }

            // 立即向全局管理器注册战机
            aircraftManager.AddAircraft(Props.aircraftDef, Props.aircraftCount, parent.Faction);
            
            // 显示消息
            Messages.Message("AircraftLaunched".Translate(Props.aircraftCount, Props.aircraftDef.LabelCap), MessageTypeDefOf.PositiveEvent);
            
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
                    SRALog.Debug("TakeoffEffectMapNull".Translate());
                    return;
                }
                
                // 生成 Skyfaller
                GenSpawn.Spawn(skyfaller, takeoffPos, parent.Map);
                
                SRALog.Debug("TakeoffSkyfallerCreated".Translate(takeoffPos));
                
                // 销毁原建筑
                parent.Destroy(DestroyMode.Vanish);
            }
            catch (System.Exception ex)
            {
                SRALog.Debug("TakeoffEffectError".Translate(ex.Message));
                // 如果Skyfaller创建失败，直接销毁建筑
                parent.Destroy(DestroyMode.Vanish);
            }
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            // 不需要保存状态，因为建筑起飞后就销毁了
        }
    }
}
