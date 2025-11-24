using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace SRA
{
    public class CompSectorSurveillance : ThingComp
    {
        public CompProperties_SectorSurveillance Props => (CompProperties_SectorSurveillance)props;
        
        // 监视状态
        private HashSet<Pawn> attackedPawns = new HashSet<Pawn>();
        private Dictionary<Pawn, int> activeTargets = new Dictionary<Pawn, int>();
        private Dictionary<Pawn, int> shotCooldowns = new Dictionary<Pawn, int>();
        
        // 性能优化
        private int checkInterval = 10;
        private int lastCheckTick = 0;

        // 调试状态
        private int totalFramesProcessed = 0;
        private int totalTargetsFound = 0;
        private int totalShotsFired = 0;

        // 派系缓存
        private Faction cachedFaction = null;
        private bool factionInitialized = false;

        // 射弹数量跟踪
        private int remainingProjectiles = -1;  // -1 表示无限
        private bool ammoExhausted = false;

        // 新增：横纵轴偏移状态
        private float currentLateralOffsetAngle = 0f;      // 当前横向偏移角度
        private float currentLongitudinalOffset = 0f;      // 当前纵向偏移距离
        private bool isForwardPhase = true;                // 是否处于向前偏移阶段
        private int shotsFired = 0;                        // 总发射次数（用于偏移计算）

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            
            // 初始化射弹数量
            if (!respawningAfterLoad)
            {
                remainingProjectiles = Props.maxProjectiles;
                ammoExhausted = false;
                
                // 初始化偏移
                currentLateralOffsetAngle = Props.lateralInitialOffsetAngle;
                currentLongitudinalOffset = Props.longitudinalInitialOffset;
            }
            
            Log.Message($"SectorSurveillance: Initialized - Angle: {Props.sectorAngle}°, Range: {Props.sectorRange}, Shots: {Props.shotCount}, Interval: {Props.shotInterval}s");
            Log.Message($"SectorSurveillance: ProjectileDef = {Props.projectileDef?.defName ?? "NULL"}");
            Log.Message($"SectorSurveillance: Parent = {parent?.def?.defName ?? "NULL"} at {parent?.Position.ToString() ?? "NULL"}");
            Log.Message($"SectorSurveillance: Max Projectiles = {Props.maxProjectiles}, Remaining = {remainingProjectiles}");
            Log.Message($"SectorSurveillance: Lateral Mode: {Props.lateralOffsetMode}, Longitudinal Mode: {Props.longitudinalOffsetMode}");
            
            InitializeFactionCache();
        }

        private void InitializeFactionCache()
        {
            Log.Message($"SectorSurveillance: Initializing faction cache...");
            
            if (parent.Faction != null)
            {
                cachedFaction = parent.Faction;
                Log.Message($"SectorSurveillance: Using parent.Faction: {cachedFaction?.Name ?? "NULL"}");
            }
            else
            {
                FlyOver flyOver = parent as FlyOver;
                if (flyOver?.caster != null && flyOver.caster.Faction != null)
                {
                    cachedFaction = flyOver.caster.Faction;
                    Log.Message($"SectorSurveillance: Using caster.Faction: {cachedFaction?.Name ?? "NULL"}");
                }
                else if (flyOver?.faction != null)
                {
                    cachedFaction = flyOver.faction;
                    Log.Message($"SectorSurveillance: Using flyOver.faction: {cachedFaction?.Name ?? "NULL"}");
                }
                else
                {
                    Log.Error($"SectorSurveillance: CRITICAL - No faction found!");
                }
            }
            
            factionInitialized = true;
            Log.Message($"SectorSurveillance: Faction cache initialized: {cachedFaction?.Name ?? "NULL"}");
        }

        private Faction GetEffectiveFaction()
        {
            if (!factionInitialized)
            {
                InitializeFactionCache();
            }
            
            if (cachedFaction == null)
            {
                Log.Warning("SectorSurveillance: Cached faction is null, reinitializing...");
                InitializeFactionCache();
            }
            
            return cachedFaction;
        }
        
        public override void CompTick()
        {
            base.CompTick();
            totalFramesProcessed++;

            // 每60帧输出一次状态摘要
            if (Find.TickManager.TicksGame % 60 == 0)
            {
                Faction currentFaction = GetEffectiveFaction();
                Log.Message($"SectorSurveillance Status: Frames={totalFramesProcessed}, TargetsFound={totalTargetsFound}, ShotsFired={totalShotsFired}, ActiveTargets={activeTargets.Count}, Cooldowns={shotCooldowns.Count}, Faction={currentFaction?.Name ?? "NULL"}, RemainingProjectiles={remainingProjectiles}, AmmoExhausted={ammoExhausted}");
                Log.Message($"SectorSurveillance Offsets: Lateral={currentLateralOffsetAngle:F1}°, Longitudinal={currentLongitudinalOffset:F1}, TotalShots={shotsFired}");
            }
            
            UpdateShotCooldowns();
            
            if (Find.TickManager.TicksGame - lastCheckTick >= checkInterval)
            {
                CheckSectorForTargets();
                lastCheckTick = Find.TickManager.TicksGame;
            }
            
            ExecuteAttacks();
        }
        
        private void UpdateShotCooldowns()
        {
            List<Pawn> toRemove = new List<Pawn>();
            
            // 关键修复：创建键的副本
            List<Pawn> cooldownKeys = new List<Pawn>(shotCooldowns.Keys);
            
            foreach (Pawn pawn in cooldownKeys)
            {
                // 检查pawn是否仍然有效（可能已被爆炸杀死）
                if (pawn == null || pawn.Destroyed || pawn.Dead || !pawn.Spawned)
                {
                    toRemove.Add(pawn);
                    continue;
                }
                
                // 检查键是否仍在字典中
                if (!shotCooldowns.ContainsKey(pawn))
                {
                    continue;
                }
                
                shotCooldowns[pawn]--;
                if (shotCooldowns[pawn] <= 0)
                {
                    toRemove.Add(pawn);
                }
            }
            
            foreach (Pawn pawn in toRemove)
            {
                shotCooldowns.Remove(pawn);
                Log.Message($"SectorSurveillance: Cooldown finished for {pawn?.Label ?? "NULL"}");
            }
        }
        
        private void CheckSectorForTargets()
        {
            // 如果弹药耗尽，不再检查新目标
            if (ammoExhausted)
            {
                return;
            }
            
            List<Pawn> enemiesInSector = GetEnemiesInSector();
            Log.Message($"SectorSurveillance: Found {enemiesInSector.Count} enemies in sector");

            if (enemiesInSector.Count > 0)
            {
                Log.Message($"SectorSurveillance: Enemies in sector: {string.Join(", ", enemiesInSector.ConvertAll(p => p.Label))}");
            }
            
            foreach (Pawn enemy in enemiesInSector)
            {
                totalTargetsFound++;
                
                if (!attackedPawns.Contains(enemy) && 
                    !activeTargets.ContainsKey(enemy) && 
                    !shotCooldowns.ContainsKey(enemy))
                {
                    activeTargets[enemy] = Props.shotCount;
                    Log.Message($"SectorSurveillance: Starting attack sequence on {enemy.Label} at {enemy.Position} - {Props.shotCount} shots");
                }
            }
        }
        
        private void ExecuteAttacks()
        {
            // 如果弹药耗尽，不再执行攻击
            if (ammoExhausted)
            {
                return;
            }
            
            List<Pawn> completedTargets = new List<Pawn>();
            
            // 关键修复：在枚举之前创建键的副本
            List<Pawn> targetsToProcess = new List<Pawn>(activeTargets.Keys);
            
            foreach (Pawn enemy in targetsToProcess)
            {
                // 检查目标是否仍然有效（可能已被爆炸杀死）
                if (enemy == null || enemy.Destroyed || enemy.Dead || !enemy.Spawned)
                {
                    completedTargets.Add(enemy);
                    continue;
                }
                
                // 检查目标是否仍在字典中
                if (!activeTargets.ContainsKey(enemy))
                {
                    continue;
                }
                
                int remainingShots = activeTargets[enemy];
                
                if (!IsInSector(enemy.Position))
                {
                    Log.Message($"SectorSurveillance: Target {enemy.Label} left sector, cancelling attack");
                    completedTargets.Add(enemy);
                    continue;
                }
                
                if (shotCooldowns.ContainsKey(enemy))
                {
                    Log.Message($"SectorSurveillance: Target {enemy.Label} in cooldown, skipping this frame");
                    continue;
                }
                
                // 检查剩余射弹数量
                if (remainingProjectiles == 0)
                {
                    Log.Message($"SectorSurveillance: Ammo exhausted, cannot fire at {enemy.Label}");
                    ammoExhausted = true;
                    break; // 跳出循环，不再发射任何射弹
                }
                
                Log.Message($"SectorSurveillance: Attempting to fire at {enemy.Label}, remaining shots: {remainingShots}, remaining projectiles: {remainingProjectiles}");
                if (LaunchProjectileAt(enemy))
                {
                    totalShotsFired++;
                    remainingShots--;
                    activeTargets[enemy] = remainingShots;
                    
                    // 减少剩余射弹数量（如果不是无限）
                    if (remainingProjectiles > 0)
                    {
                        remainingProjectiles--;
                        Log.Message($"SectorSurveillance: Remaining projectiles: {remainingProjectiles}");
                        
                        // 检查是否耗尽弹药
                        if (remainingProjectiles == 0)
                        {
                            ammoExhausted = true;
                            Log.Message($"SectorSurveillance: AMMO EXHAUSTED - No more projectiles available");
                        }
                    }
                    
                    // 新增：更新偏移状态
                    UpdateOffsets();
                    
                    int cooldownTicks = Mathf.RoundToInt(Props.shotInterval * 60f);
                    shotCooldowns[enemy] = cooldownTicks;
                    
                    Log.Message($"SectorSurveillance: Successfully fired at {enemy.Label}, {remainingShots} shots remaining, cooldown: {cooldownTicks} ticks");
                    
                    if (remainingShots <= 0)
                    {
                        attackedPawns.Add(enemy);
                        completedTargets.Add(enemy);
                        Log.Message($"SectorSurveillance: Completed attack sequence on {enemy.Label}");
                    }
                }
                else
                {
                    Log.Error($"SectorSurveillance: Failed to fire projectile at {enemy.Label}");
                }
            }
            
            // 清理已完成的目标
            foreach (Pawn enemy in completedTargets)
            {
                // 再次检查目标是否有效
                if (enemy != null)
                {
                    activeTargets.Remove(enemy);
                    Log.Message($"SectorSurveillance: Removed {enemy.Label} from active targets");
                }
                else
                {
                    // 如果目标已不存在，直接从字典中移除对应的键
                    activeTargets.Remove(enemy);
                    Log.Message($"SectorSurveillance: Removed null target from active targets");
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
        
        // 新增：计算包含横向和纵向偏移的发射位置
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
        
        private List<Pawn> GetEnemiesInSector()
        {
            List<Pawn> enemies = new List<Pawn>();
            Map map = parent.Map;
            
            if (map == null)
            {
                Log.Error("SectorSurveillance: Map is null!");
                return enemies;
            }
            
            FlyOver flyOver = parent as FlyOver;
            if (flyOver == null)
            {
                Log.Error("SectorSurveillance: Parent is not a FlyOver!");
                return enemies;
            }
            
            Vector3 center = parent.DrawPos;
            Vector3 flightDirection = flyOver.MovementDirection;
            float range = Props.sectorRange;
            float halfAngle = Props.sectorAngle * 0.5f;

            Log.Message($"SectorSurveillance: Checking sector - Center: {center}, Direction: {flightDirection}, Range: {range}, HalfAngle: {halfAngle}");
            
            int totalEnemiesChecked = 0;
            
            // 关键修复：创建pawn列表的副本，避免在枚举时集合被修改
            List<Pawn> allPawns = new List<Pawn>(map.mapPawns.AllPawnsSpawned);
            
            foreach (Pawn pawn in allPawns)
            {
                totalEnemiesChecked++;
                if (IsValidTarget(pawn))
                {
                    bool inSector = IsInSector(pawn.Position);
                    if (inSector)
                    {
                        enemies.Add(pawn);
                        Log.Message($"SectorSurveillance: Valid target found - {pawn.Label} at {pawn.Position}, in sector: {inSector}");
                    }
                }
            }

            Log.Message($"SectorSurveillance: Checked {totalEnemiesChecked} pawns, found {enemies.Count} valid targets in sector");
            return enemies;
        }
        
        private bool IsValidTarget(Pawn pawn)
        {
            if (pawn == null)
            {
                Log.Message("SectorSurveillance: IsValidTarget - pawn is null");
                return false;
            }

            // 关键修复：检查pawn是否已被销毁或死亡
            if (pawn.Destroyed || pawn.Dead || !pawn.Spawned)
            {
                Log.Message($"SectorSurveillance: IsValidTarget - {pawn.Label} is destroyed/dead/unspawned");
                return false;
            }

            if (pawn.Downed)
            {
                Log.Message($"SectorSurveillance: IsValidTarget - {pawn.Label} is downed");
                return false;
            }

            Faction effectiveFaction = GetEffectiveFaction();
            if (effectiveFaction == null)
            {
                Log.Error($"SectorSurveillance: IsValidTarget - No effective faction found for {pawn.Label}");
                return false;
            }

            bool hostile = pawn.HostileTo(effectiveFaction);
            Log.Message($"SectorSurveillance: IsValidTarget - {pawn.Label} from {pawn.Faction?.Name ?? "NULL"} is hostile to {effectiveFaction.Name}: {hostile}");

            return hostile;
        }
        
        private bool IsInSector(IntVec3 targetPos)
        {
            FlyOver flyOver = parent as FlyOver;
            if (flyOver == null)
            {
                Log.Error("SectorSurveillance: IsInSector - Parent is not a FlyOver!");
                return false;
            }
            
            Vector3 flyOverPos = parent.DrawPos;
            Vector3 targetVector = targetPos.ToVector3() - flyOverPos;
            targetVector.y = 0;
            
            float distance = targetVector.magnitude;
            if (distance > Props.sectorRange)
            {
                Log.Message($"SectorSurveillance: IsInSector - Target at {targetPos} is out of range: {distance:F1} > {Props.sectorRange}");
                return false;
            }
            
            Vector3 flightDirection = flyOver.MovementDirection;
            float angle = Vector3.Angle(flightDirection, targetVector);
            
            bool inAngle = angle <= Props.sectorAngle * 0.5f;
            
            Log.Message($"SectorSurveillance: IsInSector - Target at {targetPos}, distance: {distance:F1}, angle: {angle:F1}°, inAngle: {inAngle}");
            
            return inAngle;
        }
        
        private bool LaunchProjectileAt(Pawn target)
        {
            if (Props.projectileDef == null)
            {
                Log.Error("SectorSurveillance: No projectile defined for sector surveillance");
                return false;
            }

            Log.Message($"SectorSurveillance: LaunchProjectileAt - Starting launch for target {target?.Label ?? "NULL"}");
            
            try
            {
                Vector3 spawnPos = parent.DrawPos;
                Vector3 targetPos = target.Position.ToVector3();
                Vector3 directionToTarget = (targetPos - spawnPos).normalized;
                
                // 计算偏移后的发射位置
                Vector3 offsetSpawnPos = CalculateOffsetPosition(spawnPos, directionToTarget);
                
                IntVec3 spawnCell = offsetSpawnPos.ToIntVec3();
                
                Log.Message($"SectorSurveillance: Spawn position - World: {offsetSpawnPos}, Cell: {spawnCell}, Lateral Offset: {currentLateralOffsetAngle:F1}°, Longitudinal Offset: {currentLongitudinalOffset:F1}");

                if (parent.Map == null)
                {
                    Log.Error("SectorSurveillance: Map is null during projectile launch");
                    return false;
                }

                if (!spawnCell.InBounds(parent.Map))
                {
                    Log.Error($"SectorSurveillance: Spawn cell {spawnCell} is out of bounds");
                    return false;
                }

                Log.Message($"SectorSurveillance: Attempting to spawn projectile: {Props.projectileDef.defName}");
                Projectile projectile = (Projectile)GenSpawn.Spawn(Props.projectileDef, spawnCell, parent.Map);
                
                if (projectile != null)
                {
                    Log.Message($"SectorSurveillance: Projectile spawned successfully: {projectile}");
                    
                    Thing launcher = GetLauncher();
                    Vector3 launchPos = offsetSpawnPos;
                    
                    LocalTargetInfo targetInfo = new LocalTargetInfo(target);
                    
                    Log.Message($"SectorSurveillance: Launching projectile - Launcher: {launcher?.def?.defName ?? "NULL"}, LaunchPos: {launchPos}, Target: {targetInfo.Cell}");
                    
                    projectile.Launch(
                        launcher,
                        launchPos,
                        targetInfo,
                        targetInfo,
                        ProjectileHitFlags.IntendedTarget,
                        false
                    );
                    
                    // 播放偏移特效
                    if (Props.spawnOffsetEffect)
                    {
                        CreateOffsetEffect(offsetSpawnPos, directionToTarget);
                    }
                    
                    Log.Message($"SectorSurveillance: Projectile launched successfully");
                    return true;
                }
                else
                {
                    Log.Error("SectorSurveillance: Failed to spawn projectile - GenSpawn.Spawn returned null");
                    return false;
                }
            }
            catch (System.Exception ex)
            {
                Log.Error($"SectorSurveillance: Exception launching projectile: {ex}");
                Log.Error($"SectorSurveillance: Stack trace: {ex.StackTrace}");
                return false;
            }
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
            //if (flyOver != null && flyOver.caster != null)
            //{
            //    Log.Message($"SectorSurveillance: Using caster as launcher: {flyOver.caster.Label}");
            //    return flyOver.caster;
            //}
            
            Log.Message($"SectorSurveillance: Using parent as launcher: {parent.Label}");
            return parent;
        }
        
        // 获取剩余射弹数量的方法（用于UI显示等）
        public int GetRemainingProjectiles()
        {
            return remainingProjectiles;
        }
        
        // 检查是否还有弹药
        public bool HasAmmo()
        {
            return !ammoExhausted;
        }
        
        public override void PostExposeData()
        {
            base.PostExposeData();
            
            Scribe_Collections.Look(ref attackedPawns, "attackedPawns", LookMode.Reference);
            Scribe_Collections.Look(ref activeTargets, "activeTargets", LookMode.Reference, LookMode.Value);
            Scribe_Collections.Look(ref shotCooldowns, "shotCooldowns", LookMode.Reference, LookMode.Value);
            Scribe_Values.Look(ref lastCheckTick, "lastCheckTick", 0);
            Scribe_Values.Look(ref totalFramesProcessed, "totalFramesProcessed", 0);
            Scribe_Values.Look(ref totalTargetsFound, "totalTargetsFound", 0);
            Scribe_Values.Look(ref totalShotsFired, "totalShotsFired", 0);
            Scribe_References.Look(ref cachedFaction, "cachedFaction");
            Scribe_Values.Look(ref factionInitialized, "factionInitialized", false);
            
            // 保存和加载射弹数量状态
            Scribe_Values.Look(ref remainingProjectiles, "remainingProjectiles", -1);
            Scribe_Values.Look(ref ammoExhausted, "ammoExhausted", false);
            
            // 新增：保存和加载偏移状态
            Scribe_Values.Look(ref currentLateralOffsetAngle, "currentLateralOffsetAngle", Props.lateralInitialOffsetAngle);
            Scribe_Values.Look(ref currentLongitudinalOffset, "currentLongitudinalOffset", Props.longitudinalInitialOffset);
            Scribe_Values.Look(ref shotsFired, "shotsFired", 0);
            Scribe_Values.Look(ref isForwardPhase, "isForwardPhase", true);
        }
        
        public override string CompInspectStringExtra()
        {
            string baseString = base.CompInspectStringExtra();
            string ammoString = "";
            
            if (Props.maxProjectiles == -1)
            {
                ammoString = "Ammo: Unlimited";
            }
            else
            {
                ammoString = $"Ammo: {remainingProjectiles}/{Props.maxProjectiles}";
                if (ammoExhausted)
                {
                    ammoString += " (EXHAUSTED)";
                }
            }
            
            // 新增：显示偏移状态
            string offsetString = $"Offsets: Lateral {currentLateralOffsetAngle:F1}°, Longitudinal {currentLongitudinalOffset:F1}";
            
            string result = ammoString + "\n" + offsetString;
            
            if (!string.IsNullOrEmpty(baseString))
            {
                result = baseString + "\n" + result;
            }
            
            return result;
        }
        
        // 新增：调试方法
        public void DebugOffsetStatus()
        {
            Log.Message($"SectorSurveillance Offset Status:");
            Log.Message($"  Lateral - Angle: {currentLateralOffsetAngle:F1}°, Mode: {Props.lateralOffsetMode}");
            Log.Message($"  Longitudinal - Offset: {currentLongitudinalOffset:F1}, Mode: {Props.longitudinalOffsetMode}");
            Log.Message($"  Shots Fired: {shotsFired}, Forward Phase: {isForwardPhase}");
        }
    }
    
    public class CompProperties_SectorSurveillance : CompProperties
    {
        public ThingDef projectileDef;
        public float sectorAngle = 90f;
        public float sectorRange = 25f;
        public int shotCount = 3;
        public float shotInterval = 0.3f;
        
        // 最大射弹数量限制
        public int maxProjectiles = -1;  // -1 表示无限开火
        
        // 新增：横纵轴偏移配置
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
        
        public CompProperties_SectorSurveillance()
        {
            compClass = typeof(CompSectorSurveillance);
        }
    }
}
