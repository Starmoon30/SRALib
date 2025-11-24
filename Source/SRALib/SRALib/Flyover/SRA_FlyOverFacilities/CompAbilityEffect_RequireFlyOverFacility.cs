// CompAbilityEffect_RequireFlyOverFacility.cs
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace SRA
{
    public class CompAbilityEffect_RequireFlyOverFacility : CompAbilityEffect
    {
        new public CompProperties_RequireFlyOverFacility Props => (CompProperties_RequireFlyOverFacility)props;

        public override void Apply(LocalTargetInfo target, LocalTargetInfo dest)
        {
            base.Apply(target, dest);
            
            if (parent.pawn == null || parent.pawn.Map == null)
                return;

            // 查找可用的 FlyOver
            var availableFlyOvers = GetValidFlyOvers();
            
            if (availableFlyOvers.Count == 0)
            {
                Log.Error($"[RequireFlyOverFacility] No valid FlyOver found with required facility: {Props.requiredFacility}");
                return;
            }

            // 执行技能效果
            ExecuteSkillEffect(availableFlyOvers, target, dest);
        }

        public override bool Valid(LocalTargetInfo target, bool throwMessages = false)
        {
            if (!base.Valid(target, throwMessages))
                return false;

            // 检查是否有可用的 FlyOver
            var validFlyOvers = GetValidFlyOvers();
            
            if (validFlyOvers.Count == 0)
            {
                if (throwMessages)
                {
                    Messages.Message(Props.facilityNotFoundMessage, parent.pawn, MessageTypeDefOf.RejectInput);
                }
                return false;
            }

            return true;
        }

        public override string ExtraLabelMouseAttachment(LocalTargetInfo target)
        {
            try
            {
                var validFlyOvers = GetValidFlyOvers();
                
                if (validFlyOvers.Count == 0)
                {
                    return $"需要拥有 {Props.requiredFacility} 设施的飞行器";
                }

                return $"可用飞行器: {validFlyOvers.Count}";
            }
            catch (System.Exception ex)
            {
                // 捕获异常，避免UI崩溃
                Log.Error($"[RequireFlyOverFacility] Error in ExtraLabelMouseAttachment: {ex}");
                return "设施检查错误";
            }
        }

        // 获取有效的 FlyOver 列表
        private List<FlyOver> GetValidFlyOvers()
        {
            var validFlyOvers = new List<FlyOver>();
            
            if (parent.pawn?.Map == null)
                return validFlyOvers;

            try
            {
                List<Thing> allFlyOvers;
                
                if (Props.flyOverDef != null)
                {
                    // 如果指定了特定的 FlyOver 定义，只检查该定义的物体
                    allFlyOvers = parent.pawn.Map.listerThings.ThingsOfDef(Props.flyOverDef);
                    Log.Message($"[RequireFlyOverFacility] Checking specific FlyOverDef: {Props.flyOverDef.defName}, found: {allFlyOvers.Count}");
                }
                else
                {
                    // 如果没有指定 FlyOver 定义，检查所有类型为 FlyOver 的物体
                    // 使用更高效的方式：只检查动态物体列表，避免遍历所有物体
                    allFlyOvers = new List<Thing>();
                    var dynamicObjects = parent.pawn.Map.dynamicDrawManager.DrawThings;
                    foreach (var thing in dynamicObjects)
                    {
                        if (thing is FlyOver)
                        {
                            allFlyOvers.Add(thing);
                        }
                    }
                    Log.Message($"[RequireFlyOverFacility] Checking all FlyOver types, found: {allFlyOvers.Count}");
                }
                
                foreach (var thing in allFlyOvers)
                {
                    if (thing is FlyOver flyOver && !flyOver.Destroyed)
                    {
                        // 检查设施
                        var facilitiesComp = flyOver.GetComp<CompFlyOverFacilities>();
                        if (facilitiesComp == null)
                        {
                            Log.Warning($"[RequireFlyOverFacility] FlyOver at {flyOver.Position} has no CompFlyOverFacilities");
                            continue;
                        }
                        
                        if (!facilitiesComp.HasFacility(Props.requiredFacility))
                        {
                            Log.Message($"[RequireFlyOverFacility] FlyOver at {flyOver.Position} missing facility: {Props.requiredFacility}. Has: {string.Join(", ", facilitiesComp.GetActiveFacilities())}");
                            continue;
                        }
                        
                        validFlyOvers.Add(flyOver);
                        Log.Message($"[RequireFlyOverFacility] Found valid FlyOver at {flyOver.Position} with facility: {Props.requiredFacility}");
                    }
                }
                
                return validFlyOvers;
            }
            catch (System.Exception ex)
            {
                Log.Error($"[RequireFlyOverFacility] Error in GetValidFlyOvers: {ex}");
                return new List<FlyOver>();
            }
        }

        // 执行技能效果（由子类重写）
        protected virtual void ExecuteSkillEffect(List<FlyOver> availableFlyOvers, LocalTargetInfo target, LocalTargetInfo dest)
        {
            // 基础实现：选择第一个可用的 FlyOver
            var selectedFlyOver = availableFlyOvers.FirstOrDefault();
            if (selectedFlyOver != null)
            {
                Log.Message($"[RequireFlyOverFacility] Skill executed using FlyOver at {selectedFlyOver.Position} with facility: {Props.requiredFacility}");
            }
        }

        // 重写 Gizmo 方法，确保不会在绘制时崩溃
        public override bool GizmoDisabled(out string reason)
        {
            if (parent.pawn?.Map == null)
            {
                reason = "无法在地图外使用";
                return true;
            }

            var validFlyOvers = GetValidFlyOvers();
            if (validFlyOvers.Count == 0)
            {
                reason = Props.facilityNotFoundMessage;
                return true;
            }

            return base.GizmoDisabled(out reason);
        }
    }

    public class CompProperties_RequireFlyOverFacility : CompProperties_AbilityEffect
    {
        // 必需的 FlyOver 定义（可以为 null，表示检查所有 FlyOver 类型）
        public ThingDef flyOverDef;
        
        // 必需的设施名称
        public string requiredFacility;
        
        // 消息文本
        public string facilityNotFoundMessage = "需要拥有特定设施的飞行器";
        
        public CompProperties_RequireFlyOverFacility()
        {
            compClass = typeof(CompAbilityEffect_RequireFlyOverFacility);
        }
    }
}
