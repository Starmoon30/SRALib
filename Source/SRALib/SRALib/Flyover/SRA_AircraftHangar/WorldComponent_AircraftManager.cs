using RimWorld;
using Verse;
using System.Collections.Generic;
using RimWorld.Planet;
using System.Linq;

namespace SRA
{
    public class WorldComponent_AircraftManager : WorldComponent
    {
        // 使用列表而不是嵌套字典，更容易序列化
        private List<FactionAircraftData> allFactionAircraftData = new List<FactionAircraftData>();
        private List<AircraftCooldownEvent> cooldownEvents = new List<AircraftCooldownEvent>();

        public WorldComponent_AircraftManager(World world) : base(world) { }

        // 派系战机数据
        private class FactionAircraftData : IExposable
        {
            public Faction faction;
            public ThingDef aircraftDef;
            public int totalCount;
            public int availableCount;

            public void ExposeData()
            {
                Scribe_References.Look(ref faction, "faction");
                Scribe_Defs.Look(ref aircraftDef, "aircraftDef");
                Scribe_Values.Look(ref totalCount, "totalCount", 0);
                Scribe_Values.Look(ref availableCount, "availableCount", 0);
            }
        }

        // 冷却事件
        private class AircraftCooldownEvent : IExposable
        {
            public Faction faction;
            public ThingDef aircraftDef;
            public int endTick;
            public int aircraftCount;

            public void ExposeData()
            {
                Scribe_References.Look(ref faction, "faction");
                Scribe_Defs.Look(ref aircraftDef, "aircraftDef");
                Scribe_Values.Look(ref endTick, "endTick", 0);
                Scribe_Values.Look(ref aircraftCount, "aircraftCount", 0);
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            
            // 使用简单的列表序列化
            Scribe_Collections.Look(ref allFactionAircraftData, "allFactionAircraftData", LookMode.Deep);
            Scribe_Collections.Look(ref cooldownEvents, "cooldownEvents", LookMode.Deep);
            
            // 确保列表不为null
            if (allFactionAircraftData == null)
                allFactionAircraftData = new List<FactionAircraftData>();
            if (cooldownEvents == null)
                cooldownEvents = new List<AircraftCooldownEvent>();
            
            // 调试日志
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                SRALog.Debug($"Saving aircraft data: {allFactionAircraftData.Count} faction entries, {cooldownEvents.Count} cooldown events");
            }
            else if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                SRALog.Debug($"Loaded aircraft data: {allFactionAircraftData.Count} faction entries, {cooldownEvents.Count} cooldown events");
            }
        }

        public override void WorldComponentTick()
        {
            base.WorldComponentTick();
            
            // 处理冷却事件
            int currentTick = Find.TickManager.TicksAbs;
            for (int i = cooldownEvents.Count - 1; i >= 0; i--)
            {
                AircraftCooldownEvent cooldownEvent = cooldownEvents[i];
                
                if (currentTick >= cooldownEvent.endTick)
                {
                    RestoreAircraftAfterCooldown(cooldownEvent);
                    cooldownEvents.RemoveAt(i);
                }
            }
        }

        // 获取或创建派系战机数据
        private FactionAircraftData GetOrCreateFactionAircraftData(Faction faction, ThingDef aircraftDef)
        {
            var data = allFactionAircraftData.FirstOrDefault(x => x.faction == faction && x.aircraftDef == aircraftDef);
            if (data == null)
            {
                data = new FactionAircraftData
                {
                    faction = faction,
                    aircraftDef = aircraftDef,
                    totalCount = 0,
                    availableCount = 0
                };
                allFactionAircraftData.Add(data);
            }
            return data;
        }

        // 获取派系战机数据（可能为null）
        private FactionAircraftData GetFactionAircraftData(Faction faction, ThingDef aircraftDef)
        {
            return allFactionAircraftData.FirstOrDefault(x => x.faction == faction && x.aircraftDef == aircraftDef);
        }

        // 添加战机到派系
        public void AddAircraft(ThingDef aircraftDef, int count, Faction faction)
        {
            if (faction == null)
            {
                SRALog.Debug("AddAircraftNullFaction".Translate());
                return;
            }

            var data = GetOrCreateFactionAircraftData(faction, aircraftDef);
            data.totalCount += count;
            data.availableCount += count;
            
            SRALog.Debug($"Added {count} {aircraftDef.LabelCap} to {faction.Name}. Total: {data.totalCount}, Available: {data.availableCount}");
        }

        // 尝试使用战机
        public bool TryUseAircraft(ThingDef aircraftDef, int count, Faction faction, int cooldownTicks)
        {
            if (!HasAvailableAircraft(aircraftDef, count, faction))
                return false;

            var data = GetFactionAircraftData(faction, aircraftDef);
            data.availableCount -= count;

            AircraftCooldownEvent cooldownEvent = new AircraftCooldownEvent
            {
                faction = faction,
                aircraftDef = aircraftDef,
                endTick = Find.TickManager.TicksAbs + cooldownTicks,
                aircraftCount = count
            };
            
            cooldownEvents.Add(cooldownEvent);
            
            SRALog.Debug($"Used {count} {aircraftDef.LabelCap} from {faction.Name}. Available now: {data.availableCount}, Cooldown until: {cooldownEvent.endTick}");
            
            return true;
        }

        // 检查是否有可用战机
        public bool HasAvailableAircraft(ThingDef aircraftDef, int count, Faction faction)
        {
            var data = GetFactionAircraftData(faction, aircraftDef);
            return data != null && data.availableCount >= count;
        }

        // 获取可用战机数量
        public int GetAvailableAircraftCount(ThingDef aircraftDef, Faction faction)
        {
            var data = GetFactionAircraftData(faction, aircraftDef);
            return data?.availableCount ?? 0;
        }

        // 获取总战机数量
        public int GetTotalAircraftCount(ThingDef aircraftDef, Faction faction)
        {
            var data = GetFactionAircraftData(faction, aircraftDef);
            return data?.totalCount ?? 0;
        }

        // 冷却结束后恢复战机
        private void RestoreAircraftAfterCooldown(AircraftCooldownEvent cooldownEvent)
        {
            var data = GetFactionAircraftData(cooldownEvent.faction, cooldownEvent.aircraftDef);
            if (data != null)
            {
                data.availableCount += cooldownEvent.aircraftCount;
                
                if (cooldownEvent.aircraftDef != null)
                {
                    Messages.Message("AircraftCooldownEnded".Translate(cooldownEvent.aircraftDef.LabelCap), MessageTypeDefOf.PositiveEvent);
                    SRALog.Debug($"Cooldown ended for {cooldownEvent.aircraftCount} {cooldownEvent.aircraftDef.LabelCap}. Available now: {data.availableCount}");
                }
            }
        }

        // 获取冷却中的战机数量
        public int GetCooldownAircraftCount(ThingDef aircraftDef, Faction faction)
        {
            return cooldownEvents
                .Where(e => e.faction == faction && e.aircraftDef == aircraftDef)
                .Sum(e => e.aircraftCount);
        }
        
        // 调试方法：显示当前状态
        public void DebugLogStatus()
        {
            SRALog.Debug("=== Aircraft Manager Status ===");
            SRALog.Debug($"Total faction entries: {allFactionAircraftData.Count}");
            
            var factions = allFactionAircraftData.Select(x => x.faction).Distinct();
            foreach (var faction in factions)
            {
                SRALog.Debug($"Faction: {faction?.Name ?? "Unknown"}");
                var factionData = allFactionAircraftData.Where(x => x.faction == faction);
                foreach (var data in factionData)
                {
                    SRALog.Debug($"  {data.aircraftDef.LabelCap}: {data.availableCount}/{data.totalCount} available");
                }
            }
            
            SRALog.Debug($"Active cooldown events: {cooldownEvents.Count}");
            SRALog.Debug("===============================");
        }
    }
}
