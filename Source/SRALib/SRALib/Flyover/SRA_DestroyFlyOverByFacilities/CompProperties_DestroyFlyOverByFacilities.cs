using System.Collections.Generic;
using RimWorld;
using Verse;

namespace SRA
{
    // CompProperties 定义
    public class CompProperties_DestroyFlyOverByFacilities : CompProperties_AbilityEffect
    {
        public CompProperties_DestroyFlyOverByFacilities()
        {
            compClass = typeof(CompAbilityEffect_DestroyFlyOverByFacilities);
        }
    }

    // Comp 实现
    public class CompAbilityEffect_DestroyFlyOverByFacilities : CompAbilityEffect
    {
        public new CompProperties_DestroyFlyOverByFacilities Props => (CompProperties_DestroyFlyOverByFacilities)props;

        public override void Apply(LocalTargetInfo target, LocalTargetInfo dest)
        {
            base.Apply(target, dest);
            
            if (parent.pawn?.Map == null) return;

            // 只销毁带有 CompFlyOverFacilities 的 FlyOver
            DestroyFlyOversWithFacilities();
        }

        // 只销毁带有设施的 FlyOver
        private void DestroyFlyOversWithFacilities()
        {
            List<FlyOver> flyOversWithFacilities = new List<FlyOver>();
            
            // 使用 CompFlyOverFacilities 的静态方法来获取所有带有设施的 FlyOver
            var allFlyOvers = CompFlyOverFacilities.GetAllFlyOversWithFacilities(parent.pawn.Map);
            
            if (allFlyOvers.Count > 0)
            {
                foreach (var flyOver in allFlyOvers)
                {
                    if (flyOver != null && !flyOver.Destroyed)
                    {
                        flyOversWithFacilities.Add(flyOver);
                    }
                }
            }
            
            // 销毁找到的带有设施的 FlyOver
            foreach (FlyOver flyOver in flyOversWithFacilities)
            {
                flyOver.EmergencyDestroy();
                Log.Message($"[DestroyFlyOverByFacilities] Destroyed FlyOver with facilities at {flyOver.Position}");
            }
            
            if (flyOversWithFacilities.Count > 0)
            {
                Messages.Message($"WULA_DestroyFlyOver".Translate(flyOversWithFacilities.Count), parent.pawn, MessageTypeDefOf.PositiveEvent);
            }
            else
            {
                Messages.Message("WULA_NoFlyOverWithFacilities".Translate(), parent.pawn, MessageTypeDefOf.NeutralEvent);
            }
        }

        // 添加验证方法，确保只在有相关 FlyOver 时可用
        public override bool Valid(LocalTargetInfo target, bool throwMessages = false)
        {
            if (!base.Valid(target, throwMessages))
                return false;

            // 检查是否有带有设施的 FlyOver
            if (parent.pawn?.Map == null)
                return false;

            var flyOversWithFacilities = CompFlyOverFacilities.GetAllFlyOversWithFacilities(parent.pawn.Map);
            
            if (flyOversWithFacilities.Count == 0)
            {
                if (throwMessages)
                {
                    Messages.Message("WULA_NoFlyOverWithFacilities".Translate(), parent.pawn, MessageTypeDefOf.RejectInput);
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

            // 检查是否有带有设施的 FlyOver
            var flyOversWithFacilities = CompFlyOverFacilities.GetAllFlyOversWithFacilities(parent.pawn.Map);
            
            if (flyOversWithFacilities.Count == 0)
            {
                reason = "No FlyOver with facilities found";
                return true;
            }

            return base.GizmoDisabled(out reason);
        }

        public override string ExtraLabelMouseAttachment(LocalTargetInfo target)
        {
            try
            {
                if (parent.pawn?.Map == null)
                    return "Cannot use outside of map";

                var flyOversWithFacilities = CompFlyOverFacilities.GetAllFlyOversWithFacilities(parent.pawn.Map);
                
                if (flyOversWithFacilities.Count > 0)
                {
                    return $"Will destroy {flyOversWithFacilities.Count} FlyOver(s) with facilities";
                }

                return "No FlyOver with facilities found";
            }
            catch (System.Exception ex)
            {
                Log.Error($"[DestroyFlyOverByFacilities] Error in ExtraLabelMouseAttachment: {ex}");
                return "Error checking FlyOver status";
            }
        }
    }
}
