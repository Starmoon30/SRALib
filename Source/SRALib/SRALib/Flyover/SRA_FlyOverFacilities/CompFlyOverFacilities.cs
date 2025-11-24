// CompFlyOverFacilities.cs
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace SRA
{
    public class CompFlyOverFacilities : ThingComp
    {
        // 在 CompFlyOverFacilities 类中添加以下方法
        public bool IsFacilityReady(string facilityName)
        {
            if (!HasFacility(facilityName))
                return false;

            var cooldownComp = parent.GetComp<CompFlyOverCooldown>();
            return cooldownComp == null || !cooldownComp.IsOnCooldown;
        }
        // 在 CompFlyOverFacilities 类中添加以下静态方法
        public static bool AnyFlyOverHasFacilities(Map map)
        {
            if (map == null)
                return false;
            try
            {
                var dynamicObjects = map.dynamicDrawManager.DrawThings;

                foreach (var thing in dynamicObjects)
                {
                    if (thing is FlyOver flyOver && !flyOver.Destroyed)
                    {
                        var facilitiesComp = flyOver.GetComp<CompFlyOverFacilities>();
                        if (facilitiesComp != null)
                        {
                            return true;
                        }
                    }
                }

                return false;
            }
            catch (System.Exception ex)
            {
                Log.Error($"[FlyOverFacilities] Error in AnyFlyOverHasFacilities: {ex}");
                return false;
            }
        }
        public static List<FlyOver> GetAllFlyOversWithFacilities(Map map)
        {
            var result = new List<FlyOver>();

            if (map == null)
                return result;
            try
            {
                var dynamicObjects = map.dynamicDrawManager.DrawThings;

                foreach (var thing in dynamicObjects)
                {
                    if (thing is FlyOver flyOver && !flyOver.Destroyed)
                    {
                        var facilitiesComp = flyOver.GetComp<CompFlyOverFacilities>();
                        if (facilitiesComp != null)
                        {
                            result.Add(flyOver);
                        }
                    }
                }

                return result;
            }
            catch (System.Exception ex)
            {
                Log.Error($"[FlyOverFacilities] Error in GetAllFlyOversWithFacilities: {ex}");
                return result;
            }
        }

        public string GetFacilityStatus(string facilityName)
        {
            if (!HasFacility(facilityName))
                return "Not Available";

            var cooldownComp = parent.GetComp<CompFlyOverCooldown>();
            if (cooldownComp == null)
                return "Ready (No Cooldown)";

            return cooldownComp.IsOnCooldown ? $"Cooldown: {cooldownComp.CooldownTicksRemaining.ToStringTicksToPeriod()}" : "Ready";
        }
        public CompProperties_FlyOverFacilities Props => (CompProperties_FlyOverFacilities)props;
        
        // 当前激活的设施列表
        public List<string> activeFacilities = new List<string>();
        
        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            
            if (!respawningAfterLoad)
            {
                // 只在初次生成时激活所有定义的设施
                activeFacilities.AddRange(Props.availableFacilities);
                Log.Message($"[FlyOverFacilities] Initialized with {activeFacilities.Count} facilities: {string.Join(", ", activeFacilities)}");
            }
        }

        // 检查是否拥有特定设施
        public bool HasFacility(string facilityName)
        {
            return activeFacilities?.Contains(facilityName) ?? false;
        }

        // 获取所有激活的设施
        public List<string> GetActiveFacilities()
        {
            return activeFacilities != null ? new List<string>(activeFacilities) : new List<string>();
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Collections.Look(ref activeFacilities, "activeFacilities", LookMode.Value);
            
            // 如果加载失败或列表为null，重新初始化
            if (Scribe.mode == LoadSaveMode.PostLoadInit && activeFacilities == null)
            {
                activeFacilities = new List<string>();
                // 在加载后重新添加默认设施
                activeFacilities.AddRange(Props.availableFacilities);
                Log.Message($"[FlyOverFacilities] Reinitialized after load with {activeFacilities.Count} facilities");
            }
        }

        public override void Initialize(CompProperties props)
        {
            base.Initialize(props);
            // 确保列表被初始化
            if (activeFacilities == null)
            {
                activeFacilities = new List<string>();
            }
        }
    }



    public class CompProperties_FlyOverFacilities : CompProperties
    {
        // 可用的设施列表（简单的字符串列表）
        public List<string> availableFacilities = new List<string>();
        
        public CompProperties_FlyOverFacilities()
        {
            compClass = typeof(CompFlyOverFacilities);
        }
    }
}
