using RimWorld;
using Verse;
using RimWorld.Planet;
using System.Collections.Generic;
using System.Text;

namespace SRA
{
    public class CompProperties_AircraftStrike : CompProperties_AbilityEffect
    {
        public ThingDef requiredAircraftType; // 需要的战机类型
        public int aircraftCooldownTicks = 60000; // 战机冷却时间（默认1天）
        public int aircraftsPerUse = 1; // 每次使用消耗的战机数量
        
        public CompProperties_AircraftStrike()
        {
            compClass = typeof(CompAbilityEffect_AircraftStrike);
        }
    }

    public class CompAbilityEffect_AircraftStrike : CompAbilityEffect
    {
        public new CompProperties_AircraftStrike Props => (CompProperties_AircraftStrike)props;
        
        public override void Apply(LocalTargetInfo target, LocalTargetInfo dest)
        {
            base.Apply(target, dest);
            
            // 获取全局战机管理器
            WorldComponent_AircraftManager aircraftManager = Find.World.GetComponent<WorldComponent_AircraftManager>();
            
            if (aircraftManager == null)
            {
                SRALog.Debug("AircraftManagerNotFound".Translate());
                return;
            }

            // 检查并消耗战机
            if (aircraftManager.TryUseAircraft(Props.requiredAircraftType, Props.aircraftsPerUse, parent.pawn.Faction, Props.aircraftCooldownTicks))
            {
                // 成功消耗战机，发送消息
                Messages.Message("AircraftStrikeInitiated".Translate(Props.requiredAircraftType.LabelCap), MessageTypeDefOf.PositiveEvent);
                SRALog.Debug("AircraftStrikeSuccess".Translate(Props.aircraftsPerUse, Props.requiredAircraftType.LabelCap));
            }
            else
            {
                Messages.Message("NoAvailableAircraft".Translate(Props.requiredAircraftType.LabelCap), MessageTypeDefOf.NegativeEvent);
                SRALog.Debug("AircraftStrikeFailed".Translate(Props.requiredAircraftType.LabelCap, parent.pawn.Faction?.Name ?? "UnknownFaction".Translate()));
            }
        }

        public override bool CanApplyOn(LocalTargetInfo target, LocalTargetInfo dest)
        {
            // 检查是否有可用的战机
            WorldComponent_AircraftManager aircraftManager = Find.World.GetComponent<WorldComponent_AircraftManager>();
            
            return base.CanApplyOn(target, dest) && 
                   aircraftManager != null && 
                   aircraftManager.HasAvailableAircraft(Props.requiredAircraftType, Props.aircraftsPerUse, parent.pawn.Faction);
        }

        public override string ExtraLabelMouseAttachment(LocalTargetInfo target)
        {
            WorldComponent_AircraftManager aircraftManager = Find.World.GetComponent<WorldComponent_AircraftManager>();
            
            if (aircraftManager != null)
            {
                int available = aircraftManager.GetAvailableAircraftCount(Props.requiredAircraftType, parent.pawn.Faction);
                int onCooldown = aircraftManager.GetCooldownAircraftCount(Props.requiredAircraftType, parent.pawn.Faction);
                
                // 使用符号显示飞机状态
                string availableSymbols = GetAircraftSymbols(available, "◆");
                string cooldownSymbols = GetAircraftSymbols(onCooldown, "◇");
                
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("AvailableAircraft".Translate(Props.requiredAircraftType.LabelCap, availableSymbols));
                sb.AppendLine("CooldownAircraft".Translate(cooldownSymbols));
                sb.Append("CostPerUse".Translate(Props.aircraftsPerUse));
                
                return sb.ToString();
            }
            
            return base.ExtraLabelMouseAttachment(target);
        }

        // 生成飞机符号表示
        private string GetAircraftSymbols(int count, string symbol)
        {
            if (count <= 0) return "—"; // 无飞机时显示破折号
            
            StringBuilder sb = new StringBuilder();
            int displayCount = count;
            
            // 如果数量过多，用数字+符号表示
            if (count > 10)
            {
                return $"{count}{symbol}";
            }
            
            // 直接显示符号
            for (int i = 0; i < displayCount; i++)
            {
                sb.Append(symbol);
            }
            
            return sb.ToString();
        }

        public override bool Valid(LocalTargetInfo target, bool throwMessages = false)
        {
            if (!base.Valid(target, throwMessages))
                return false;

            // 检查战机可用性
            WorldComponent_AircraftManager aircraftManager = Find.World.GetComponent<WorldComponent_AircraftManager>();
            if (aircraftManager == null || !aircraftManager.HasAvailableAircraft(Props.requiredAircraftType, Props.aircraftsPerUse, parent.pawn.Faction))
            {
                if (throwMessages)
                {
                    Messages.Message("NoAircraftForStrike".Translate(Props.requiredAircraftType.LabelCap), MessageTypeDefOf.RejectInput);
                }
                return false;
            }

            return true;
        }

        // 鼠标悬停时的工具提示
        public override string ExtraTooltipPart()
        {
            WorldComponent_AircraftManager aircraftManager = Find.World.GetComponent<WorldComponent_AircraftManager>();
            
            if (aircraftManager != null)
            {
                int available = aircraftManager.GetAvailableAircraftCount(Props.requiredAircraftType, parent.pawn.Faction);
                int onCooldown = aircraftManager.GetCooldownAircraftCount(Props.requiredAircraftType, parent.pawn.Faction);
                int total = available + onCooldown;
                
                // 将冷却时间从 tick 转换为小时
                float cooldownHours = TicksToHours(Props.aircraftCooldownTicks);
                
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("AircraftStatusTooltip".Translate());
                sb.AppendLine("• " + "TotalAircraft".Translate(total));
                sb.AppendLine("• " + "ReadyAircraft".Translate(available));
                sb.AppendLine("• " + "CooldownAircraft".Translate(onCooldown));
                sb.AppendLine("AircraftAbilityDescription".Translate(Props.requiredAircraftType.LabelCap, Props.aircraftsPerUse, cooldownHours.ToString("F1")));

                return sb.ToString();
            }
            
            return base.ExtraTooltipPart();
        }

        // 将 tick 转换为小时
        private float TicksToHours(int ticks)
        {
            // RimWorld 中 1 小时 = 2500 tick
            return ticks / 2500f;
        }
    }
}
