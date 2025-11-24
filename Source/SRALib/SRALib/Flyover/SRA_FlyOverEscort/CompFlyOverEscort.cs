using RimWorld;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace SRA
{
    public class CompFlyOverEscort : ThingComp
    {
        public CompProperties_FlyOverEscort Props => (CompProperties_FlyOverEscort)props;
        
        // 状态变量
        private float ticksUntilNextSpawn = 0f;
        private List<FlyOver> activeEscorts = new List<FlyOver>();
        private bool hasInitialized = false;
        
        // 存储每个伴飞的缩放和遮罩数据
        private Dictionary<FlyOver, EscortVisualData> escortVisualData = new Dictionary<FlyOver, EscortVisualData>();
        
        // 新增：伴飞ID映射，用于解决存档加载时的引用问题
        private Dictionary<int, EscortVisualData> escortDataByID = new Dictionary<int, EscortVisualData>();
        private List<int> escortIDs = new List<int>();
        private List<EscortVisualData> escortDataList = new List<EscortVisualData>();
        
        public override void Initialize(CompProperties props)
        {
            base.Initialize(props);
            
            if (Props.spawnOnStart)
            {
                ticksUntilNextSpawn = 0f;
            }
            else
            {
                ticksUntilNextSpawn = Props.spawnIntervalTicks;
            }
        }

        public override void CompTick()
        {
            base.CompTick();

            if (parent is not FlyOver mainFlyOver || !mainFlyOver.Spawned || mainFlyOver.Map == null)
                return;

            // 初始化检查
            if (!hasInitialized && mainFlyOver.hasStarted)
            {
                hasInitialized = true;
            }

            // 清理已销毁的伴飞
            CleanupDestroyedEscorts();

            // 检查是否应该生成新伴飞
            if (ShouldSpawnEscort(mainFlyOver))
            {
                ticksUntilNextSpawn -= 1f;
                
                if (ticksUntilNextSpawn <= 0f)
                {
                    SpawnEscorts(mainFlyOver);
                    ticksUntilNextSpawn = Props.spawnIntervalTicks;
                }
            }

            // 更新现有伴飞的位置（如果需要）
            UpdateEscortPositions(mainFlyOver);
        }

        private void CleanupDestroyedEscorts()
        {
            // 清理已销毁的伴飞
            for (int i = activeEscorts.Count - 1; i >= 0; i--)
            {
                if (activeEscorts[i] == null || activeEscorts[i].Destroyed || !activeEscorts[i].Spawned)
                {
                    FlyOver removedEscort = activeEscorts[i];
                    activeEscorts.RemoveAt(i);
                    
                    // 从两个字典中都移除
                    if (escortVisualData.ContainsKey(removedEscort))
                    {
                        escortVisualData.Remove(removedEscort);
                    }
                    
                    int escortID = GetEscortID(removedEscort);
                    if (escortDataByID.ContainsKey(escortID))
                    {
                        escortDataByID.Remove(escortID);
                    }
                }
            }
        }

        private bool ShouldSpawnEscort(FlyOver mainFlyOver)
        {
            if (!mainFlyOver.hasStarted || mainFlyOver.hasCompleted)
                return false;

            if (!Props.continuousSpawning && activeEscorts.Count >= Props.spawnCount)
                return false;

            if (activeEscorts.Count >= Props.maxEscorts)
                return false;

            return true;
        }

        private void SpawnEscorts(FlyOver mainFlyOver)
        {
            int escortsToSpawn = Mathf.Min(Props.spawnCount, Props.maxEscorts - activeEscorts.Count);
            int successfulSpawns = 0;
            int maxAttempts = escortsToSpawn * 5; // 最多尝试5倍的数量
            
            for (int attempt = 0; attempt < maxAttempts && successfulSpawns < escortsToSpawn; attempt++)
            {
                // 先生成视觉数据
                EscortVisualData visualData = GenerateEscortVisualData();
                
                FlyOver escort = CreateEscort(mainFlyOver, visualData);
                if (escort != null)
                {
                    // 检查安全距离
                    if (IsSafeDistance(escort, mainFlyOver))
                    {
                        activeEscorts.Add(escort);
                        escortVisualData[escort] = visualData;
                        
                        // 添加到ID映射
                        int escortID = GetEscortID(escort);
                        escortDataByID[escortID] = visualData;
                        
                        successfulSpawns++;
                    }
                    else
                    {
                        // 不安全，销毁这个伴飞
                        escort.Destroy();
                    }
                }
                
                // 如果已经生成足够数量，提前退出
                if (successfulSpawns >= escortsToSpawn)
                    break;
            }
            
            if (successfulSpawns < escortsToSpawn)
            {
                Log.Message($"Spawned {successfulSpawns}/{escortsToSpawn} escorts (some positions were too close to existing escorts)");
            }
        }

        // 修改：分别检查与主飞行物和伴飞物的安全距离
        private bool IsSafeDistance(FlyOver newEscort, FlyOver mainFlyOver)
        {
            Vector3 newPos = newEscort.DrawPos;
            
            // 检查与主FlyOver的距离
            if (Props.minSafeDistanceFromMain > 0)
            {
                float distToMain = Vector3.Distance(newPos, mainFlyOver.DrawPos);
                if (distToMain < Props.minSafeDistanceFromMain)
                {
                    return false;
                }
            }
            
            // 检查与其他伴飞的距离
            if (Props.minSafeDistanceBetweenEscorts > 0)
            {
                foreach (FlyOver existingEscort in activeEscorts)
                {
                    if (existingEscort == null || existingEscort.Destroyed)
                        continue;
                        
                    float distToEscort = Vector3.Distance(newPos, existingEscort.DrawPos);
                    if (distToEscort < Props.minSafeDistanceBetweenEscorts)
                    {
                        return false;
                    }
                }
            }
            
            return true;
        }

        private EscortVisualData GenerateEscortVisualData()
        {
            EscortVisualData data = new EscortVisualData();
            
            // 随机生成缩放比例
            data.scale = Props.escortScaleRange.RandomInRange;
            
            // 根据缩放计算遮罩透明度（小的飞得更高，更透明）
            float scaleFactor = Mathf.InverseLerp(Props.escortScaleRange.min, Props.escortScaleRange.max, data.scale);
            data.heightMaskAlpha = Mathf.Lerp(Props.heightMaskAlphaRange.max, Props.heightMaskAlphaRange.min, scaleFactor);
            
            // 计算遮罩缩放
            data.heightMaskScale = data.scale * Props.heightMaskScaleMultiplier;
            
            return data;
        }

        private FlyOver CreateEscort(FlyOver mainFlyOver, EscortVisualData visualData)
        {
            try
            {
                // 选择伴飞定义
                ThingDef escortDef = SelectEscortDef();
                if (escortDef == null)
                {
                    Log.Error("FlyOver Escort: No valid escort def found");
                    return null;
                }

                // 计算伴飞的起点和终点
                IntVec3 escortStart = CalculateEscortStart(mainFlyOver);
                IntVec3 escortEnd = CalculateEscortEnd(mainFlyOver, escortStart);
                
                if (!escortStart.InBounds(mainFlyOver.Map) || !escortEnd.InBounds(mainFlyOver.Map))
                {
                    Log.Warning("FlyOver Escort: Escort start or end position out of bounds");
                    return null;
                }

                // 计算伴飞参数
                float escortSpeed = mainFlyOver.flightSpeed * Props.escortSpeedMultiplier;
                float escortAltitude = mainFlyOver.altitude + Props.escortAltitudeOffset;

                // 创建伴飞
                FlyOver escort = FlyOver.MakeFlyOver(
                    escortDef,
                    escortStart,
                    escortEnd,
                    mainFlyOver.Map,
                    escortSpeed,
                    escortAltitude,
                    null, // 没有内容物
                    mainFlyOver.fadeInDuration
                );

                // 设置伴飞属性 - 现在传入 visualData
                SetupEscortProperties(escort, mainFlyOver, visualData);

                return escort;
            }
            catch (System.Exception ex)
            {
                Log.Error($"Error creating FlyOver escort: {ex}");
                return null;
            }
        }

        private ThingDef SelectEscortDef()
        {
            if (Props.escortFlyOverDefs != null && Props.escortFlyOverDefs.Count > 0)
            {
                return Props.escortFlyOverDefs.RandomElement();
            }
            
            return Props.escortFlyOverDef;
        }

        private IntVec3 CalculateEscortStart(FlyOver mainFlyOver)
        {
            Vector3 mainDirection = mainFlyOver.MovementDirection;
            Vector3 mainPosition = mainFlyOver.DrawPos;
            
            // 计算横向偏移方向（垂直于飞行方向）
            Vector3 lateralDirection = GetLateralOffsetDirection(mainDirection);
            
            // 计算偏移量
            float lateralOffset = Props.useRandomOffset ? 
                Rand.Range(-Props.lateralOffset, Props.lateralOffset) : 
                Props.lateralOffset;
                
            float spawnDistance = Props.useRandomOffset ? 
                Rand.Range(Props.spawnDistance * 0.5f, Props.spawnDistance * 1.5f) : 
                Props.spawnDistance;

            // 计算起点位置（从主FlyOver后方偏移）
            Vector3 offset = (-mainDirection * spawnDistance) + (lateralDirection * lateralOffset);
            Vector3 escortStartPos = mainPosition + offset;

            // 确保位置在地图边界内
            IntVec3 escortStart = escortStartPos.ToIntVec3();
            if (!escortStart.InBounds(mainFlyOver.Map))
            {
                // 如果超出边界，调整到边界内
                escortStart = ClampToMap(escortStart, mainFlyOver.Map);
            }

            return escortStart;
        }

        private IntVec3 CalculateEscortEnd(FlyOver mainFlyOver, IntVec3 escortStart)
        {
            Vector3 mainDirection = mainFlyOver.MovementDirection;
            Vector3 mainEndPos = mainFlyOver.endPosition.ToVector3();
            
            // 如果镜像移动，使用相反方向
            if (Props.mirrorMovement)
            {
                mainDirection = -mainDirection;
            }

            // 计算从起点沿飞行方向延伸的终点
            float flightDistance = mainFlyOver.startPosition.DistanceTo(mainFlyOver.endPosition);
            Vector3 escortEndPos = escortStart.ToVector3() + (mainDirection * flightDistance);

            // 确保终点在地图边界内
            IntVec3 escortEnd = escortEndPos.ToIntVec3();
            if (!escortEnd.InBounds(mainFlyOver.Map))
            {
                escortEnd = ClampToMap(escortEnd, mainFlyOver.Map);
            }

            return escortEnd;
        }

        private Vector3 GetLateralOffsetDirection(Vector3 mainDirection)
        {
            // 获取垂直于飞行方向的向量（随机选择左侧或右侧）
            Vector3 lateral = new Vector3(-mainDirection.z, 0f, mainDirection.x);
            
            // 随机选择方向
            if (Rand.Value > 0.5f)
            {
                lateral = -lateral;
            }
            
            return lateral.normalized;
        }

        private IntVec3 ClampToMap(IntVec3 position, Map map)
        {
            CellRect mapRect = CellRect.WholeMap(map);
            return new IntVec3(
                Mathf.Clamp(position.x, mapRect.minX, mapRect.maxX),
                0,
                Mathf.Clamp(position.z, mapRect.minZ, mapRect.maxZ)
            );
        }

        private void SetupEscortProperties(FlyOver escort, FlyOver mainFlyOver, EscortVisualData visualData)
        {
            // 设置伴飞缩放 - 现在直接从参数获取
            escort.escortScale = visualData.scale;
            escort.isEscort = true;

            // 禁用阴影（如果需要）
            if (!mainFlyOver.createShadow)
            {
                escort.createShadow = false;
            }

            // 禁用音效（如果需要）
            if (!mainFlyOver.playFlyOverSound)
            {
                escort.playFlyOverSound = false;
            }
        }

        private void UpdateEscortPositions(FlyOver mainFlyOver)
        {
            // 如果需要实时更新伴飞位置，可以在这里实现
            // 目前伴飞会按照自己的路径飞行
        }

        // 新增：在绘制时调用
        public override void PostDraw()
        {
            base.PostDraw();
            DrawEscortHeightMasks();
        }

        // 新增：绘制伴飞的高度遮罩
        public void DrawEscortHeightMasks()
        {
            if (!Props.useHeightMask || escortVisualData.Count == 0)
                return;

            foreach (var kvp in escortVisualData)
            {
                FlyOver escort = kvp.Key;
                EscortVisualData visualData = kvp.Value;

                if (escort == null || escort.Destroyed || !escort.Spawned)
                    continue;

                DrawHeightMaskForEscort(escort, visualData);
            }
        }

        private void DrawHeightMaskForEscort(FlyOver escort, EscortVisualData visualData)
        {
            if (visualData.heightMaskAlpha <= 0.01f)
                return;

            // 获取伴飞的绘制位置
            Vector3 drawPos = escort.DrawPos;
            drawPos.y = AltitudeLayer.MetaOverlays.AltitudeFor() + 0.01f; // 稍微高于伴飞本身

            // 计算遮罩矩阵
            Matrix4x4 matrix = Matrix4x4.TRS(
                drawPos,
                escort.ExactRotation,
                new Vector3(visualData.heightMaskScale, 1f, visualData.heightMaskScale)
            );

            // 设置遮罩材质属性
            Material heightMaskMat = GetHeightMaskMaterial();
            if (heightMaskMat != null)
            {
                // 计算最终颜色和透明度
                Color finalColor = Props.heightMaskColor;
                finalColor.a *= visualData.heightMaskAlpha * escort.OverallAlpha;

                var propertyBlock = new MaterialPropertyBlock();
                propertyBlock.SetColor(ShaderPropertyIDs.Color, finalColor);

                // 绘制遮罩
                Graphics.DrawMesh(
                    MeshPool.plane10,
                    matrix,
                    heightMaskMat,
                    0, // layer
                    null, // camera
                    0, // submeshIndex
                    propertyBlock
                );
            }
        }

        private Material heightMaskMaterial;
        private Material GetHeightMaskMaterial()
        {
            if (heightMaskMaterial == null)
            {
                // 创建一个简单的圆形遮罩材质
                heightMaskMaterial = MaterialPool.MatFrom("UI/Overlays/SoftShadowCircle", ShaderDatabase.Transparent);
            }
            return heightMaskMaterial;
        }

        public override void PostDestroy(DestroyMode mode, Map previousMap)
        {
            base.PostDestroy(mode, previousMap);
            
            // 销毁所有伴飞
            if (Props.destroyWithParent)
            {
                foreach (FlyOver escort in activeEscorts)
                {
                    if (escort != null && escort.Spawned)
                    {
                        escort.Destroy();
                    }
                }
                activeEscorts.Clear();
                escortVisualData.Clear();
                escortDataByID.Clear();
            }
        }

        // 新增：获取伴飞的唯一ID
        private int GetEscortID(FlyOver escort)
        {
            return escort?.thingIDNumber ?? 0;
        }

        // 修复的序列化方法
        public override void PostExposeData()
        {
            base.PostExposeData();
            
            Scribe_Values.Look(ref ticksUntilNextSpawn, "ticksUntilNextSpawn", 0f);
            Scribe_Collections.Look(ref activeEscorts, "activeEscorts", LookMode.Reference);
            Scribe_Values.Look(ref hasInitialized, "hasInitialized", false);
            
            // 修复：使用ID映射来保存和恢复视觉数据
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                // 保存时，将视觉数据转换为ID映射
                escortIDs.Clear();
                escortDataList.Clear();
                escortDataByID.Clear();
                
                foreach (var kvp in escortVisualData)
                {
                    if (kvp.Key != null && !kvp.Key.Destroyed)
                    {
                        int escortID = GetEscortID(kvp.Key);
                        escortIDs.Add(escortID);
                        escortDataList.Add(kvp.Value);
                        escortDataByID[escortID] = kvp.Value;
                    }
                }
                
                Scribe_Collections.Look(ref escortIDs, "escortIDs", LookMode.Value);
                Scribe_Collections.Look(ref escortDataList, "escortDataList", LookMode.Deep);
            }
            else if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                // 加载时，先清空现有数据
                escortVisualData.Clear();
                escortDataByID.Clear();
                
                // 加载ID和视觉数据列表
                Scribe_Collections.Look(ref escortIDs, "escortIDs", LookMode.Value);
                Scribe_Collections.Look(ref escortDataList, "escortDataList", LookMode.Deep);
                
                // 重建ID映射
                if (escortIDs != null && escortDataList != null && escortIDs.Count == escortDataList.Count)
                {
                    for (int i = 0; i < escortIDs.Count; i++)
                    {
                        escortDataByID[escortIDs[i]] = escortDataList[i];
                    }
                }
            }
            else if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                // 在PostLoadInit阶段重建视觉数据字典
                RebuildEscortVisualData();
            }
        }

        // 新增：重建伴飞视觉数据字典
        private void RebuildEscortVisualData()
        {
            escortVisualData.Clear();
            
            foreach (var escort in activeEscorts)
            {
                if (escort != null && !escort.Destroyed)
                {
                    int escortID = GetEscortID(escort);
                    
                    if (escortDataByID.TryGetValue(escortID, out var visualData))
                    {
                        // 恢复保存的视觉数据
                        escortVisualData[escort] = visualData;
                        
                        // 重新设置伴飞的缩放
                        escort.escortScale = visualData.scale;
                    }
                    else
                    {
                        // 如果没有保存的数据，重新生成
                        var newVisualData = GenerateEscortVisualData();
                        escortVisualData[escort] = newVisualData;
                        escort.escortScale = newVisualData.scale;
                    }
                }
            }
        }

        // 公共方法：强制生成伴飞
        public void SpawnEscortNow()
        {
            if (parent is FlyOver flyOver)
            {
                SpawnEscorts(flyOver);
            }
        }

        // 公共方法：获取活跃伴飞数量
        public int GetActiveEscortCount()
        {
            return activeEscorts.Count;
        }

        // 新增：获取伴飞的视觉数据
        public EscortVisualData GetEscortVisualData(FlyOver escort)
        {
            if (escortVisualData.TryGetValue(escort, out var data))
            {
                return data;
            }
            return new EscortVisualData { scale = 1f, heightMaskAlpha = 1f, heightMaskScale = 1f };
        }
    }

    // 伴飞视觉数据类
    public class EscortVisualData : IExposable
    {
        public float scale = 1f;
        public float heightMaskAlpha = 1f;
        public float heightMaskScale = 1f;

        public void ExposeData()
        {
            Scribe_Values.Look(ref scale, "scale", 1f);
            Scribe_Values.Look(ref heightMaskAlpha, "heightMaskAlpha", 1f);
            Scribe_Values.Look(ref heightMaskScale, "heightMaskScale", 1f);
        }
    }
}
