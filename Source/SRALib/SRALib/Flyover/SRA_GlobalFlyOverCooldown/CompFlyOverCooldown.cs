using RimWorld;
using Verse;

namespace SRA
{
    public class CompFlyOverCooldown : ThingComp
    {
        public CompProperties_FlyOverCooldown Props => (CompProperties_FlyOverCooldown)props;
        
        // 冷却结束的tick
        private int cooldownEndTick = -1;
        
        // 是否在冷却中
        public bool IsOnCooldown => Find.TickManager.TicksGame < cooldownEndTick;
        
        // 剩余冷却时间（ticks）
        public int CooldownTicksRemaining => IsOnCooldown ? cooldownEndTick - Find.TickManager.TicksGame : 0;
        
        // 冷却进度（0-1）
        public float CooldownProgress 
        { 
            get 
            {
                if (!IsOnCooldown) return 0f;
                int totalCooldown = cooldownEndTick - (cooldownEndTick - Props.baseCooldownTicks);
                return 1f - ((float)CooldownTicksRemaining / Props.baseCooldownTicks);
            } 
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref cooldownEndTick, "cooldownEndTick", -1);
        }

        // 开始冷却
        public void StartCooldown(int cooldownTicks = -1)
        {
            int actualCooldown = cooldownTicks > 0 ? cooldownTicks : Props.baseCooldownTicks;
            cooldownEndTick = Find.TickManager.TicksGame + actualCooldown;
            
            Log.Message($"[FlyOverCooldown] Cooldown started for {actualCooldown} ticks, ends at tick {cooldownEndTick}");
        }

        // 强制结束冷却
        public void EndCooldown()
        {
            cooldownEndTick = -1;
        }

        // 获取冷却状态描述
        public string GetCooldownStatus()
        {
            if (!IsOnCooldown)
                return "WULA_FlyOverCooldown.Ready".Translate();
            
            return "WULA_FlyOverCooldown.CooldownRemaining".Translate(CooldownTicksRemaining.ToStringTicksToPeriod());
        }

        public override void CompTick()
        {
            base.CompTick();
            
            // 可以在这里添加冷却期间的视觉效果或逻辑
            if (IsOnCooldown && Find.TickManager.TicksGame % 60 == 0) // 每60ticks检查一次
            {
                // 可选：在冷却期间添加一些视觉效果
                if (Rand.MTBEventOccurs(10f, 1f, 1f))
                {
                    FleckMaker.ThrowMetaIcon(parent.Position, parent.Map, FleckDefOf.SleepZ);
                }
            }
        }

        // 在检视面板中显示冷却信息
        public override string CompInspectStringExtra()
        {
            if (IsOnCooldown)
            {
                return "WULA_FlyOverCooldown.BombardmentFacilityStatus".Translate(GetCooldownStatus());
            }
            return "WULA_FlyOverCooldown.BombardmentFacilityReady".Translate();
        }
    }

    public class CompProperties_FlyOverCooldown : CompProperties
    {
        // 基础冷却时间（ticks）
        public int baseCooldownTicks = 60000; // 默认1天
        
        public CompProperties_FlyOverCooldown()
        {
            compClass = typeof(CompFlyOverCooldown);
        }
    }
}
