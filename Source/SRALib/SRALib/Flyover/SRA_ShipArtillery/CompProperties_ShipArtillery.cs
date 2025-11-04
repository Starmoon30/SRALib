using RimWorld;
using System.Collections.Generic;
using Verse;

namespace SRA
{
    public class CompProperties_ShipArtillery : CompProperties
    {
        // 攻击配置
        public int ticksBetweenAttacks = 600; // 攻击间隔（tick）
        public int attackDurationTicks = 1800; // 攻击持续时间（tick）
        public int warmupTicks = 120; // 预热时间（tick）
        public bool continuousAttack = false; // 是否持续攻击直到飞越结束
        
        // 目标区域配置
        public float attackRadius = 15f; // 攻击半径
        public IntVec3 targetOffset = IntVec3.Zero; // 目标偏移
        public bool useRandomTargets = true; // 是否使用随机目标
        public bool avoidPlayerAssets = true; // 是否避开玩家资产
        public float playerAssetAvoidanceRadius = 5f; // 避开玩家资产的半径
        
        // 新增：无视保护机制的概率
        public float ignoreProtectionChance = 0f; // 0-1之间的值，0表示从不无视，1表示总是无视
        
        // Skyfaller 配置
        public ThingDef skyfallerDef; // 使用的 Skyfaller 定义
        public List<ThingDef> skyfallerDefs; // 多个 Skyfaller 定义（随机选择）
        public int shellsPerVolley = 1; // 每轮齐射的炮弹数量
        public bool useDifferentShells = false; // 是否使用不同类型的炮弹
        
        // 音效配置
        public SoundDef attackSound; // 攻击音效
        public SoundDef impactSound; // 撞击音效
        
        // 视觉效果
        public EffecterDef warmupEffect; // 预热效果
        public EffecterDef attackEffect; // 攻击效果
        public FleckDef warmupFleck; // 预热粒子
        public FleckDef attackFleck; // 攻击粒子
        
        // 避免击中飞越物体本身
        public bool avoidHittingFlyOver = true;
        
        // 信件通知
        public bool sendAttackLetter = true; // 是否发送攻击信件
        public string customLetterLabel; // 自定义信件标题
        public string customLetterText; // 自定义信件内容
        public LetterDef letterDef = LetterDefOf.ThreatBig; // 信件类型

        public CompProperties_ShipArtillery()
        {
            compClass = typeof(CompShipArtillery);
        }
    }
}
