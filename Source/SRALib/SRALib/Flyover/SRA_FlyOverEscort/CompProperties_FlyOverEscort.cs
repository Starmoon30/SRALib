using RimWorld;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace SRA
{
    public class CompProperties_FlyOverEscort : CompProperties
    {
        // 伴飞配置
        public ThingDef escortFlyOverDef; // 伴飞FlyOver定义
        public List<ThingDef> escortFlyOverDefs; // 多个伴飞定义（随机选择）
        
        // 生成配置
        public float spawnIntervalTicks = 600f; // 生成间隔（tick）
        public int maxEscorts = 3; // 最大伴飞数量
        public int spawnCount = 1; // 每次生成的伴飞数量
        
        // 位置配置
        public float spawnDistance = 10f; // 生成距离（从主FlyOver）
        public float lateralOffset = 5f; // 横向偏移量
        public float verticalOffset = 2f; // 垂直偏移量（高度差）
        public bool useRandomOffset = true; // 是否使用随机偏移
        
        // 修改：独立的安全距离配置
        public float minSafeDistanceFromMain = 8f; // 与主飞行物的最小安全距离（单元格）
        public float minSafeDistanceBetweenEscorts = 3f; // 伴飞物之间的最小安全距离（单元格）
        
        // 飞行配置
        public float escortSpeedMultiplier = 1f; // 速度乘数（相对于主FlyOver）
        public float escortAltitudeOffset = 0f; // 高度偏移
        public bool mirrorMovement = false; // 是否镜像移动（相反方向）
        
        // 行为配置
        public bool spawnOnStart = true; // 开始时立即生成
        public bool continuousSpawning = true; // 是否持续生成
        public bool destroyWithParent = true; // 是否随父级销毁
        
        // 外观配置
        public float escortScale = 1f; // 缩放比例（向后兼容）
        public FloatRange escortScaleRange = new FloatRange(0.5f, 1.5f); // 缩放比例区间
        public bool useParentRotation = true; // 使用父级旋转
        
        // 新增：高度遮罩配置
        public bool useHeightMask = true; // 是否使用高度遮罩
        public FloatRange heightMaskAlphaRange = new FloatRange(0.3f, 0.8f); // 遮罩透明度区间
        public Color heightMaskColor = new Color(0.8f, 0.9f, 1.0f, 1f); // 遮罩颜色（淡蓝色）
        public float heightMaskScaleMultiplier = 1.2f; // 遮罩缩放倍数
        
        public CompProperties_FlyOverEscort()
        {
            compClass = typeof(CompFlyOverEscort);
        }
    }
}
