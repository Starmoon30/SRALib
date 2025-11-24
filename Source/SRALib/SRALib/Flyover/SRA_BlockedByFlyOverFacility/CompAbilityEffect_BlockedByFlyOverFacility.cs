using RimWorld;
using Verse;
using System.Collections.Generic;

namespace SRA
{
    public class CompAbilityEffect_BlockedByFlyOverFacility : CompAbilityEffect
    {
        public new CompProperties_BlockedByFlyOverFacility Props => (CompProperties_BlockedByFlyOverFacility)props;

        public override bool CanApplyOn(LocalTargetInfo target, LocalTargetInfo dest)
        {
            if (!base.CanApplyOn(target, dest))
                return false;

            // 检查是否有FlyOver存在
            if (HasFlyOverWithFacilities())
            {
                return false;
            }

            return true;
        }

        public override bool Valid(LocalTargetInfo target, bool throwMessages = false)
        {
            if (!base.Valid(target, throwMessages))
                return false;

            // 检查是否有FlyOver存在
            if (HasFlyOverWithFacilities())
            {
                if (throwMessages)
                {
                    Messages.Message(Props.blockedMessage, parent.pawn, MessageTypeDefOf.RejectInput);
                }
                return false;
            }

            return true;
        }

        public override bool GizmoDisabled(out string reason)
        {
            if (parent.pawn?.Map == null)
            {
                reason = "Cannot use outside of map";
                return true;
            }

            // 检查是否有FlyOver存在
            if (HasFlyOverWithFacilities())
            {
                reason = Props.blockedMessage;
                return true;
            }

            return base.GizmoDisabled(out reason);
        }

        public override string ExtraLabelMouseAttachment(LocalTargetInfo target)
        {
            try
            {
                var flyOvers = GetFlyOversWithFacilities();
                
                if (flyOvers.Count > 0)
                {
                    return $"航道堵塞: {flyOvers.Count}个飞行器在场上";
                }

                return "航道畅通";
            }
            catch (System.Exception ex)
            {
                Log.Error($"[BlockedByFlyOverFacility] Error in ExtraLabelMouseAttachment: {ex}");
                return "航道状态检查错误";
            }
        }

        // 检查是否有任何FlyOver携带设施组件
        private bool HasFlyOverWithFacilities()
        {
            return GetFlyOversWithFacilities().Count > 0;
        }

        // 获取所有携带设施组件的FlyOver
        private List<FlyOver> GetFlyOversWithFacilities()
        {
            var flyOversWithFacilities = new List<FlyOver>();
            
            if (parent.pawn?.Map == null)
                return flyOversWithFacilities;

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

                // 筛选携带设施组件的FlyOver
                foreach (var thing in allFlyOvers)
                {
                    if (thing is FlyOver flyOver)
                    {
                        var facilitiesComp = flyOver.GetComp<CompFlyOverFacilities>();
                        if (facilitiesComp != null)
                        {
                            flyOversWithFacilities.Add(flyOver);
                        }
                    }
                }
                
                Log.Message($"[BlockedByFlyOverFacility] Found {flyOversWithFacilities.Count} FlyOvers with facilities");
                
                return flyOversWithFacilities;
            }
            catch (System.Exception ex)
            {
                Log.Error($"[BlockedByFlyOverFacility] Error in GetFlyOversWithFacilities: {ex}");
                return new List<FlyOver>();
            }
        }

        // 重写：应用时也进行检查
        public override void Apply(LocalTargetInfo target, LocalTargetInfo dest)
        {
            // 在应用前再次检查，确保安全
            if (HasFlyOverWithFacilities())
            {
                Log.Warning($"[BlockedByFlyOverFacility] Attempted to use ability while FlyOvers are present");
                Messages.Message(Props.blockedMessage, parent.pawn, MessageTypeDefOf.RejectInput);
                return;
            }

            base.Apply(target, dest);
        }
    }

    public class CompProperties_BlockedByFlyOverFacility : CompProperties_AbilityEffect
    {
        // 堵塞时显示的消息
        public string blockedMessage = "航道堵塞：场上有飞行器，无法释放技能";
        
        // 可选：可以指定特定的FlyOver定义，如果为空则检查所有FlyOver
        public ThingDef specificFlyOverDef;
        
        // 可选：可以指定特定的设施名称，如果为空则检查任何设施
        public string requiredFacility;

        public CompProperties_BlockedByFlyOverFacility()
        {
            compClass = typeof(CompAbilityEffect_BlockedByFlyOverFacility);
        }
    }
}
