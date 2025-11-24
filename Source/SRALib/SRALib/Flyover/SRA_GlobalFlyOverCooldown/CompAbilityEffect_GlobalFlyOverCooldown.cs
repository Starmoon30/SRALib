using RimWorld;
using Verse;
using System.Collections.Generic;
using System.Linq;

namespace SRA
{
    public class CompAbilityEffect_GlobalFlyOverCooldown : CompAbilityEffect
    {
        public new CompProperties_GlobalFlyOverCooldown Props => (CompProperties_GlobalFlyOverCooldown)props;

        public override void Apply(LocalTargetInfo target, LocalTargetInfo dest)
        {
            base.Apply(target, dest);
            
            if (parent.pawn == null || parent.pawn.Map == null)
                return;

            // 获取所有可用的轰炸设施FlyOver
            var availableFlyOvers = GetAvailableFlyOvers();
            
            if (availableFlyOvers.Count == 0)
            {
                Log.Error($"[GlobalFlyOverCooldown] No available FlyOver with BombardmentFacility found");
                return;
            }

            // 随机选择一个FlyOver来执行任务
            var selectedFlyOver = availableFlyOvers.RandomElement();
            var facilitiesComp = selectedFlyOver.GetComp<CompFlyOverFacilities>();
            
            if (facilitiesComp == null)
            {
                Log.Error($"[GlobalFlyOverCooldown] Selected FlyOver has no CompFlyOverFacilities");
                return;
            }

            // 设置冷却时间
            SetCooldown(selectedFlyOver, Props.globalCooldownTicks);
            
            Log.Message($"[GlobalFlyOverCooldown] Set cooldown on FlyOver at {selectedFlyOver.Position} for {Props.globalCooldownTicks} ticks");
        }

        public override bool GizmoDisabled(out string reason)
        {
            if (parent.pawn?.Map == null)
            {
                reason = "WULA_GlobalFlyOverCooldown.CannotUseOutsideMap".Translate();
                return true;
            }

            var availableFlyOvers = GetAvailableFlyOvers();
            var totalFlyOvers = GetTotalFlyOvers();
            
            if (availableFlyOvers.Count == 0)
            {
                reason = "WULA_GlobalFlyOverCooldown.NoAvailableFacilities".Translate(totalFlyOvers.Count);
                return true;
            }

            return base.GizmoDisabled(out reason);
        }

        public override string ExtraLabelMouseAttachment(LocalTargetInfo target)
        {
            try
            {
                var availableFlyOvers = GetAvailableFlyOvers();
                var totalFlyOvers = GetTotalFlyOvers();
                var cooldownFlyOvers = totalFlyOvers.Count - availableFlyOvers.Count;
                
                return "WULA_GlobalFlyOverCooldown.FacilityStatus".Translate(availableFlyOvers.Count, cooldownFlyOvers);
            }
            catch (System.Exception ex)
            {
                Log.Error($"[GlobalFlyOverCooldown] Error in ExtraLabelMouseAttachment: {ex}");
                return "WULA_GlobalFlyOverCooldown.FacilityStatusError".Translate();
            }
        }

        // 重写：添加额外的工具提示信息
        public override string ExtraTooltipPart()
        {
            var availableFlyOvers = GetAvailableFlyOvers();
            var totalFlyOvers = GetTotalFlyOvers();
            var cooldownFlyOvers = totalFlyOvers.Count - availableFlyOvers.Count;
            
            var baseTooltip = base.ExtraTooltipPart() ?? "";
            var facilityDesc = "\n" + "WULA_GlobalFlyOverCooldown.FacilityStatusDetailed".Translate(availableFlyOvers.Count, cooldownFlyOvers);
            
            if (availableFlyOvers.Count > 0)
            {
                var cooldownComp = availableFlyOvers[0].GetComp<CompFlyOverCooldown>();
                if (cooldownComp != null)
                {
                    facilityDesc += "\n" + "WULA_GlobalFlyOverCooldown.CooldownTime".Translate(Props.globalCooldownTicks.ToStringTicksToPeriod());
                }
            }
            
            return baseTooltip + facilityDesc;
        }

        // 重写：验证目标时检查设施可用性
        public override bool Valid(LocalTargetInfo target, bool throwMessages = false)
        {
            if (!base.Valid(target, throwMessages))
                return false;

            var availableFlyOvers = GetAvailableFlyOvers();
            
            if (availableFlyOvers.Count == 0)
            {
                if (throwMessages)
                {
                    Messages.Message("WULA_GlobalFlyOverCooldown.NoAvailableFacilitiesSimple".Translate(), parent.pawn, MessageTypeDefOf.RejectInput);
                }
                return false;
            }

            return true;
        }

        // 获取可用的FlyOver（不在冷却中的）
        private List<FlyOver> GetAvailableFlyOvers()
        {
            var availableFlyOvers = new List<FlyOver>();
            
            if (parent.pawn?.Map == null)
                return availableFlyOvers;

            try
            {
                var allFlyOvers = GetTotalFlyOvers();
                
                foreach (var flyOver in allFlyOvers)
                {
                    if (!IsOnCooldown(flyOver))
                    {
                        availableFlyOvers.Add(flyOver);
                    }
                }
                
                return availableFlyOvers;
            }
            catch (System.Exception ex)
            {
                Log.Error($"[GlobalFlyOverCooldown] Error in GetAvailableFlyOvers: {ex}");
                return new List<FlyOver>();
            }
        }

        // 获取所有具有轰炸设施的FlyOver
        private List<FlyOver> GetTotalFlyOvers()
        {
            var totalFlyOvers = new List<FlyOver>();
            
            if (parent.pawn?.Map == null)
                return totalFlyOvers;

            try
            {
                // 获取地图上所有FlyOver
                var allFlyOvers = new List<Thing>();
                var dynamicObjects = parent.pawn.Map.dynamicDrawManager.DrawThings;
                
                foreach (var thing in dynamicObjects)
                {
                    if (thing is FlyOver flyOver && !flyOver.Destroyed)
                    {
                        allFlyOvers.Add(thing);
                    }
                }

                // 筛选具有轰炸设施的FlyOver
                foreach (var thing in allFlyOvers)
                {
                    if (thing is FlyOver flyOver)
                    {
                        var facilitiesComp = flyOver.GetComp<CompFlyOverFacilities>();
                        if (facilitiesComp != null && facilitiesComp.HasFacility("BombardmentFacility"))
                        {
                            totalFlyOvers.Add(flyOver);
                        }
                    }
                }
                
                return totalFlyOvers;
            }
            catch (System.Exception ex)
            {
                Log.Error($"[GlobalFlyOverCooldown] Error in GetTotalFlyOvers: {ex}");
                return new List<FlyOver>();
            }
        }

        // 检查FlyOver是否在冷却中
        private bool IsOnCooldown(FlyOver flyOver)
        {
            if (flyOver == null)
                return true;

            // 从FlyOver的comps中查找冷却组件
            var cooldownComp = flyOver.GetComp<CompFlyOverCooldown>();
            return cooldownComp?.IsOnCooldown ?? false;
        }

        // 设置FlyOver的冷却时间
        private void SetCooldown(FlyOver flyOver, int cooldownTicks)
        {
            if (flyOver == null)
                return;

            // 获取或添加冷却组件
            var cooldownComp = flyOver.GetComp<CompFlyOverCooldown>();
            if (cooldownComp == null)
            {
                Log.Error($"[GlobalFlyOverCooldown] FlyOver at {flyOver.Position} has no CompFlyOverCooldown");
                return;
            }

            cooldownComp.StartCooldown(cooldownTicks);
        }
    }

    public class CompProperties_GlobalFlyOverCooldown : CompProperties_AbilityEffect
    {
        // 全局冷却时间（ticks）
        public int globalCooldownTicks = 60000; // 默认1天
        
        // 必需的设施名称
        public string requiredFacility = "BombardmentFacility";
        
        public CompProperties_GlobalFlyOverCooldown()
        {
            compClass = typeof(CompAbilityEffect_GlobalFlyOverCooldown);
        }
    }
}
