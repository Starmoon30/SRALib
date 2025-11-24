using RimWorld;
using System.Collections.Generic;
using Verse;
using UnityEngine;

namespace SRA
{
    public class CompProperties_AbilitySpawnFlyOver : CompProperties_AbilityEffect
    {
        public ThingDef flyOverDef;                    // 飞越物体的 ThingDef
        public FlyOverType flyOverType = FlyOverType.Standard; // 飞越类型
        public ApproachType approachType = ApproachType.Standard; // 进场类型
        public float flightSpeed = 1f;                 // 飞行速度
        public float altitude = 15f;                   // 飞行高度
        public bool spawnContents = false;             // 是否生成内容物
        public List<ThingDefCount> contents;           // 内容物列表
        public bool dropContentsOnImpact = true;       // 是否在终点投放内容物
        public SoundDef customSound;                   // 自定义音效
        public bool playFlyOverSound = true;           // 是否播放飞越音效

        // 起始位置选项（当approachType为Standard时使用）
        public StartPosition startPosition = StartPosition.Caster;
        public IntVec3 customStartOffset = IntVec3.Zero;

        // 终点位置选项（当approachType为Standard时使用）  
        public EndPosition endPosition = EndPosition.TargetCell;
        public IntVec3 customEndOffset = IntVec3.Zero;
        public int flyOverDistance = 30;               // 飞越距离（当终点为自定义时）

        // 地面扫射配置
        public bool enableGroundStrafing = false;      // 是否启用地面扫射
        public int strafeWidth = 3;                    // 扫射宽度（用于预览）
        public int strafeLength = 15;                  // 扫射长度
        public float strafeFireChance = 0.7f;          // 扫射发射概率
        public int minStrafeProjectiles = -1;          // 新增：最小射弹数
        public int maxStrafeProjectiles = -1;          // 新增：最大射弹数
        public ThingDef strafeProjectile;              // 抛射体定义

        // 地面扫射可视化
        public bool showStrafePreview = true;          // 是否显示扫射预览
        public Color strafePreviewColor = new Color(1f, 0.3f, 0.3f, 0.3f);

        // 扇形监视配置 - 只传递信号，不传递具体参数
        public bool enableSectorSurveillance = false;  // 是否启用扇形区域监视

        // 扇形监视可视化 - 使用strafeWidth来近似预览区域宽度
        public bool showSectorPreview = true;          // 是否显示扇形预览
        public Color sectorPreviewColor = new Color(0.3f, 0.7f, 1f, 0.3f);

        public CompProperties_AbilitySpawnFlyOver()
        {
            this.compClass = typeof(CompAbilityEffect_SpawnFlyOver);
        }
    }

    // 飞越类型枚举
    public enum FlyOverType
    {
        Standard,           // 标准飞越
        HighAltitude,       // 高空飞越
        CargoDrop,          // 货运飞越
        BombingRun,         // 轰炸飞越
        Reconnaissance,     // 侦察飞越
        GroundStrafing,     // 地面扫射
        SectorSurveillance, // 扇形区域监视
    }

    // 进场类型枚举
    public enum ApproachType
    {
        Standard,           // 标准进场（使用原有的位置计算）
        Perpendicular       // 垂直线进场（垂直于施法者-目标连线）
    }

    // 起始位置枚举
    public enum StartPosition
    {
        Caster,             // 施法者位置
        MapEdge,            // 地图边缘
        CustomOffset,       // 自定义偏移
        RandomMapEdge       // 随机地图边缘
    }

    // 终点位置枚举
    public enum EndPosition
    {
        TargetCell,         // 目标单元格
        OppositeMapEdge,    // 对面地图边缘
        CustomOffset,       // 自定义偏移
        FixedDistance,      // 固定距离
        RandomMapEdge
    }
}
