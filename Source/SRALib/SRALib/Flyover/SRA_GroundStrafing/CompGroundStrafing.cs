using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Noise;

namespace SRA
{
    public class CompGroundStrafing : ThingComp
    {
        public CompProperties_GroundStrafing Props => (CompProperties_GroundStrafing)props;
        
        // 简化的扫射状态
        private List<IntVec3> confirmedTargetCells = new List<IntVec3>();
        private HashSet<IntVec3> firedCells = new HashSet<IntVec3>();
        
        // 横向偏移状态（左右）
        private float currentLateralOffsetAngle = 0f;
        private int shotsFired = 0;
        private Vector3 lastProjectileDirection = Vector3.zero;
        
        // 新增：纵向偏移状态（前后）
        private float currentLongitudinalOffset = 0f;      // 当前纵向偏移距离
        private bool isForwardPhase = true;                // 是否处于向前偏移阶段
        
        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            
            // 初始化偏移
            if (!respawningAfterLoad)
            {
                currentLateralOffsetAngle = Props.lateralInitialOffsetAngle;
                currentLongitudinalOffset = Props.longitudinalInitialOffset;
            }
            
            SRALog.Debug($"GroundStrafing: Initialized with {confirmedTargetCells.Count} targets, " +
                       $"Lateral Offset: {currentLateralOffsetAngle:F1}°, " +
                       $"Longitudinal Offset: {currentLongitudinalOffset:F1}");
        }
        
        public override void CompTick()
        {
            base.CompTick();
            
            if (confirmedTargetCells.Count == 0)
            {
                return;
            }
            
            CheckAndFireAtTargets();
            
            // 定期状态输出
            if (Find.TickManager.TicksGame % 120 == 0 && confirmedTargetCells.Count > 0)
            {
                SRALog.Debug($"GroundStrafing: {firedCells.Count}/{confirmedTargetCells.Count + firedCells.Count} targets fired, " +
                           $"Lateral: {currentLateralOffsetAngle:F1}°, Longitudinal: {currentLongitudinalOffset:F1}");
            }
        }
        
        private void CheckAndFireAtTargets()
        {
            Vector3 currentPos = parent.DrawPos;

            for (int i = confirmedTargetCells.Count - 1; i >= 0; i--)
            {
                IntVec3 targetCell = confirmedTargetCells[i];
                
                if (firedCells.Contains(targetCell))
                {
                    confirmedTargetCells.RemoveAt(i);
                    continue;
                }
                
                float horizontalDistance = GetHorizontalDistance(currentPos, targetCell);
                if (horizontalDistance <= Props.range)
                {
                    if (LaunchProjectileAt(targetCell))
                    {
                        firedCells.Add(targetCell);
                        confirmedTargetCells.RemoveAt(i);
                        
                        // 更新所有偏移参数
                        UpdateOffsets();
                        
                        if (firedCells.Count == 1)
                        {
                            SRALog.Debug($"First strafing shot at {targetCell}, " +
                                       $"Lateral offset: {currentLateralOffsetAngle:F1}°, " +
                                       $"Longitudinal offset: {currentLongitudinalOffset:F1}");
                        }
                    }
                }
            }
        }
        
        // 新增：更新所有偏移参数
        private void UpdateOffsets()
        {
            shotsFired++;
            
            // 更新横向偏移
            UpdateLateralOffset();
            
            // 更新纵向偏移
            UpdateLongitudinalOffset();
        }
        
        // 横向偏移逻辑（左右）
        private void UpdateLateralOffset()
        {
            switch (Props.lateralOffsetMode)
            {
                case OffsetMode.Alternating:
                    currentLateralOffsetAngle = (shotsFired % 2 == 0) ? Props.lateralOffsetDistance : -Props.lateralOffsetDistance;
                    break;
                    
                case OffsetMode.Progressive:
                    currentLateralOffsetAngle += Props.lateralAngleIncrement;
                    if (Mathf.Abs(currentLateralOffsetAngle) > Props.lateralMaxOffsetAngle)
                    {
                        currentLateralOffsetAngle = Props.lateralInitialOffsetAngle;
                    }
                    break;
                    
                case OffsetMode.Random:
                    currentLateralOffsetAngle = Random.Range(-Props.lateralMaxOffsetAngle, Props.lateralMaxOffsetAngle);
                    break;
                    
                case OffsetMode.Fixed:
                default:
                    break;
            }
            
            if (Props.lateralMaxOffsetAngle > 0)
            {
                currentLateralOffsetAngle = Mathf.Clamp(currentLateralOffsetAngle, -Props.lateralMaxOffsetAngle, Props.lateralMaxOffsetAngle);
            }
        }
        
        // 新增：纵向偏移逻辑（前后）
        private void UpdateLongitudinalOffset()
        {
            switch (Props.longitudinalOffsetMode)
            {
                case LongitudinalOffsetMode.Alternating:
                    // 交替模式：前后交替
                    currentLongitudinalOffset = (shotsFired % 2 == 0) ? Props.longitudinalAlternationAmplitude : -Props.longitudinalAlternationAmplitude;
                    break;
                    
                case LongitudinalOffsetMode.Progressive:
                    // 渐进模式：逐渐向前然后向后
                    if (isForwardPhase)
                    {
                        currentLongitudinalOffset += Props.longitudinalProgressionStep;
                        if (currentLongitudinalOffset >= Props.longitudinalMaxOffset)
                        {
                            isForwardPhase = false;
                        }
                    }
                    else
                    {
                        currentLongitudinalOffset -= Props.longitudinalProgressionStep;
                        if (currentLongitudinalOffset <= Props.longitudinalMinOffset)
                        {
                            isForwardPhase = true;
                        }
                    }
                    break;
                    
                case LongitudinalOffsetMode.Random:
                    // 随机模式
                    currentLongitudinalOffset = Random.Range(Props.longitudinalMinOffset, Props.longitudinalMaxOffset);
                    break;
                    
                case LongitudinalOffsetMode.Sinusoidal:
                    // 正弦波模式：平滑的前后波动
                    float time = shotsFired * Props.longitudinalOscillationSpeed;
                    currentLongitudinalOffset = Mathf.Sin(time) * Props.longitudinalOscillationAmplitude;
                    break;
                    
                case LongitudinalOffsetMode.Fixed:
                default:
                    // 固定模式：保持不变
                    break;
            }
            
            // 应用限制
            currentLongitudinalOffset = Mathf.Clamp(currentLongitudinalOffset, Props.longitudinalMinOffset, Props.longitudinalMaxOffset);
        }
        
        // 修改：计算包含横向和纵向偏移的发射位置
        private Vector3 CalculateOffsetPosition(Vector3 basePosition, Vector3 directionToTarget)
        {
            Vector3 finalPosition = basePosition;
            
            // 应用横向偏移（左右）
            if (Mathf.Abs(currentLateralOffsetAngle) > 0.01f)
            {
                Vector3 flyDirection = GetFlyOverDirection();
                Vector3 perpendicular = Vector3.Cross(flyDirection, Vector3.up).normalized;
                float lateralOffsetDistance = Props.lateralOffsetDistance;
                Vector3 lateralOffset = perpendicular * lateralOffsetDistance * Mathf.Sin(currentLateralOffsetAngle * Mathf.Deg2Rad);
                finalPosition += lateralOffset;
            }
            
            // 应用纵向偏移（前后）
            if (Mathf.Abs(currentLongitudinalOffset) > 0.01f)
            {
                Vector3 flyDirection = GetFlyOverDirection();
                Vector3 longitudinalOffset = flyDirection * currentLongitudinalOffset;
                finalPosition += longitudinalOffset;
            }
            
            return finalPosition;
        }
        
        private Vector3 GetFlyOverDirection()
        {
            FlyOver flyOver = parent as FlyOver;
            if (flyOver != null)
            {
                return flyOver.MovementDirection;
            }
            return Vector3.forward;
        }
        
        private float GetHorizontalDistance(Vector3 fromPos, IntVec3 toCell)
        {
            Vector2 fromPos2D = new Vector2(fromPos.x, fromPos.z);
            Vector2 toPos2D = new Vector2(toCell.x, toCell.z);
            return Vector2.Distance(fromPos2D, toPos2D);
        }
        
        private bool LaunchProjectileAt(IntVec3 targetCell)
        {
            if (Props.projectileDef == null)
            {
                SRALog.Debug("No projectile defined for ground strafing");
                return false;
            }
            
            try
            {
                Vector3 spawnPos = parent.DrawPos;
                Vector3 targetPos = targetCell.ToVector3();
                Vector3 directionToTarget = (targetPos - spawnPos).normalized;
                
                // 计算偏移后的发射位置
                Vector3 offsetSpawnPos = CalculateOffsetPosition(spawnPos, directionToTarget);
                offsetSpawnPos = new Vector3(Mathf.Clamp(offsetSpawnPos.x, 0.5f, parent.Map.Size.x - 0.5f), offsetSpawnPos.y, Mathf.Clamp(offsetSpawnPos.z, 0.5f, parent.Map.Size.z - 0.5f));
                IntVec3 spawnCell = offsetSpawnPos.ToIntVec3();
                // 创建抛射体
                Projectile projectile = (Projectile)GenSpawn.Spawn(Props.projectileDef, spawnCell, parent.Map);
                
                if (projectile != null)
                {
                    Thing launcher = GetLauncher();
                    lastProjectileDirection = directionToTarget;
                    
                    // 发射抛射体
                    projectile.Launch(
                        launcher,
                        offsetSpawnPos,
                        new LocalTargetInfo(targetCell),
                        new LocalTargetInfo(targetCell),
                        ProjectileHitFlags.IntendedTarget,
                        false
                    );
                    
                    // 播放偏移特效
                    if (Props.spawnOffsetEffect)
                    {
                        CreateOffsetEffect(offsetSpawnPos, directionToTarget);
                    }
                    
                    return true;
                }
            }
            catch (System.Exception ex)
            {
                SRALog.Debug($"Error launching ground strafing projectile: {ex}");
            }
            
            return false;
        }
        
        // 偏移特效
        private void CreateOffsetEffect(Vector3 spawnPos, Vector3 direction)
        {
            if (Props.offsetEffectDef != null)
            {
                MoteMaker.MakeStaticMote(
                    spawnPos, 
                    parent.Map, 
                    Props.offsetEffectDef, 
                    1f
                );
            }
        }
        
        private Thing GetLauncher()
        {
            FlyOver flyOver = parent as FlyOver;
            if (flyOver != null && flyOver.caster != null)
            {
                return flyOver.caster;
            }
            return parent;
        }
        
        public void SetConfirmedTargets(List<IntVec3> targets)
        {
            confirmedTargetCells.Clear();
            firedCells.Clear();
            shotsFired = 0;
            currentLateralOffsetAngle = Props.lateralInitialOffsetAngle;
            currentLongitudinalOffset = Props.longitudinalInitialOffset;
            isForwardPhase = true;
            
            confirmedTargetCells.AddRange(targets);
            
            SRALog.Debug($"GroundStrafing: Set {confirmedTargetCells.Count} targets, " +
                       $"Lateral Mode: {Props.lateralOffsetMode}, " +
                       $"Longitudinal Mode: {Props.longitudinalOffsetMode}");
            
            if (confirmedTargetCells.Count > 0)
            {
                SRALog.Debug($"First target: {confirmedTargetCells[0]}, Last target: {confirmedTargetCells[confirmedTargetCells.Count - 1]}");
            }
        }
        
        public override void PostExposeData()
        {
            base.PostExposeData();
            
            Scribe_Collections.Look(ref confirmedTargetCells, "confirmedTargetCells", LookMode.Value);
            Scribe_Collections.Look(ref firedCells, "firedCells", LookMode.Value);
            Scribe_Values.Look(ref currentLateralOffsetAngle, "currentLateralOffsetAngle", Props.lateralInitialOffsetAngle);
            Scribe_Values.Look(ref currentLongitudinalOffset, "currentLongitudinalOffset", Props.longitudinalInitialOffset);
            Scribe_Values.Look(ref shotsFired, "shotsFired", 0);
            Scribe_Values.Look(ref isForwardPhase, "isForwardPhase", true);
        }
        
        // 修改：调试方法
        public void DebugOffsetStatus()
        {
            SRALog.Debug($"GroundStrafing Offset Status:");
            SRALog.Debug($"  Lateral - Angle: {currentLateralOffsetAngle:F1}°, Mode: {Props.lateralOffsetMode}");
            SRALog.Debug($"  Longitudinal - Offset: {currentLongitudinalOffset:F1}, Mode: {Props.longitudinalOffsetMode}");
            SRALog.Debug($"  Shots Fired: {shotsFired}, Forward Phase: {isForwardPhase}");
        }
    }
    
    public class CompProperties_GroundStrafing : CompProperties
    {
        public ThingDef projectileDef;          // 抛射体定义
        public float range = 15f;               // 射程
        
        // 横向偏移配置（左右）
        public float lateralOffsetDistance = 2f;
        public float lateralInitialOffsetAngle = 0f;
        public float lateralMaxOffsetAngle = 45f;
        public float lateralAngleIncrement = 5f;
        public OffsetMode lateralOffsetMode = OffsetMode.Alternating;
        
        // 纵向偏移配置（前后）
        public float longitudinalInitialOffset = 0f;                    // 初始纵向偏移
        public float longitudinalMinOffset = -2f;                       // 最小纵向偏移
        public float longitudinalMaxOffset = 2f;                        // 最大纵向偏移
        public LongitudinalOffsetMode longitudinalOffsetMode = LongitudinalOffsetMode.Alternating; // 纵向偏移模式
        
        // 正弦波模式参数
        public float longitudinalOscillationSpeed = 0.5f;               // 振荡速度
        public float longitudinalOscillationAmplitude = 1f;             // 振荡幅度
        
        // 交替模式参数
        public float longitudinalAlternationAmplitude = 1f;             // 交替幅度
        
        // 渐进模式参数
        public float longitudinalProgressionStep = 0.1f;                // 渐进步长
        
        // 视觉效果
        public bool spawnOffsetEffect = false;
        public ThingDef offsetEffectDef;
        
        public CompProperties_GroundStrafing()
        {
            compClass = typeof(CompGroundStrafing);
        }
    }
    
    // 横向偏移模式枚举
    public enum OffsetMode
    {
        Fixed,
        Alternating,
        Progressive,
        Random
    }
    
    // 新增：纵向偏移模式枚举
    public enum LongitudinalOffsetMode
    {
        Fixed,          // 固定
        Alternating,    // 交替（前后交替）
        Progressive,    // 渐进（逐渐变化）
        Random,         // 随机
        Sinusoidal      // 正弦波（平滑波动）
    }
}