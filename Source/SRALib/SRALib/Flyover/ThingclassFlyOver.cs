using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace SRA
{
    [StaticConstructorOnStartup]
    public class FlyOver : ThingWithComps, IThingHolder
    {
        // 核心字段
        public ThingOwner innerContainer;           // 内部物品容器
        public IntVec3 startPosition;              // 起始位置
        public IntVec3 endPosition;                // 结束位置
        public float flightSpeed = 1f;             // 飞行速度
        public float currentProgress = 0f;         // 当前进度 (0-1)
        public float altitude = 10f;               // 飞行高度
        public Faction faction;                     // 派系引用

        // 淡入效果相关
        public float fadeInDuration = 1.5f;        // 淡入持续时间（秒）
        public float currentFadeInTime = 0f;       // 当前淡入时间
        public bool fadeInCompleted = false;       // 淡入是否完成

        // 淡出效果相关
        public float fadeOutDuration = 0f;         // 动态计算的淡出持续时间
        public float currentFadeOutTime = 0f;      // 当前淡出时间
        public bool fadeOutStarted = false;        // 淡出是否开始
        public bool fadeOutCompleted = false;      // 淡出是否完成
        public float fadeOutStartProgress = 0.7f;  // 开始淡出的进度阈值（0-1）
        public float defaultFadeOutDuration = 1.5f; // 默认淡出持续时间（仅用于销毁）

        // 进场动画相关 - 新增
        public float approachDuration = 1.0f;      // 进场动画持续时间（秒）
        public float currentApproachTime = 0f;     // 当前进场动画时间
        public bool approachCompleted = false;     // 进场动画是否完成
        public float approachOffsetDistance = 3f;  // 进场偏移距离（格）
        public bool useApproachAnimation = true;   // 是否使用进场动画

        // 伴飞相关
        public float escortScale = 1f;             // 伴飞缩放比例
        public bool isEscort = false;              // 是否是伴飞

        // 状态标志
        public bool hasStarted = false;
        public bool hasCompleted = false;

        // 音效系统
        private Sustainer flightSoundPlaying;

        // 视觉效果
        private Material cachedShadowMaterial;
        private static MaterialPropertyBlock shadowPropertyBlock = new MaterialPropertyBlock();
        private static MaterialPropertyBlock fadePropertyBlock = new MaterialPropertyBlock();

        // 配置字段
        public bool spawnContentsOnImpact = false; // 是否在结束时生成内容物
        public bool playFlyOverSound = true;       // 是否播放飞越音效
        public bool createShadow = true;           // 是否创建阴影

        public Pawn caster;                       // 施法者引用

        // 属性 - 修改后的 DrawPos，包含进场动画
        public override Vector3 DrawPos
        {
            get
            {
                // 线性插值计算基础位置
                Vector3 start = startPosition.ToVector3();
                Vector3 end = endPosition.ToVector3();
                Vector3 basePos = Vector3.Lerp(start, end, currentProgress);

                // 添加高度偏移
                basePos.y = altitude;

                // 应用进场动画偏移
                if (useApproachAnimation && !approachCompleted)
                {
                    basePos = ApplyApproachAnimation(basePos);
                }

                return basePos;
            }
        }

        // 进场动画位置计算
        private Vector3 ApplyApproachAnimation(Vector3 basePos)
        {
            float approachProgress = currentApproachTime / approachDuration;
            
            // 使用缓动函数让移动更自然
            float easedProgress = EasingFunction(approachProgress, EasingType.OutCubic);
            
            // 计算偏移方向（飞行方向的反方向）
            Vector3 approachDirection = -MovementDirection.normalized;
            
            // 计算偏移量：从最大偏移逐渐减少到0
            float currentOffset = approachOffsetDistance * (1f - easedProgress);
            
            // 应用偏移
            Vector3 offsetPos = basePos + approachDirection * currentOffset;
            
            return offsetPos;
        }

        // 缓动函数 - 让动画更自然
        private float EasingFunction(float t, EasingType type)
        {
            switch (type)
            {
                case EasingType.OutCubic:
                    return 1f - Mathf.Pow(1f - t, 3f);
                case EasingType.OutQuad:
                    return 1f - (1f - t) * (1f - t);
                case EasingType.OutSine:
                    return Mathf.Sin(t * Mathf.PI * 0.5f);
                default:
                    return t;
            }
        }

        // 缓动类型枚举
        private enum EasingType
        {
            Linear,
            OutQuad,
            OutCubic,
            OutSine
        }

        // 新增：进场动画进度（0-1）
        public float ApproachProgress
        {
            get
            {
                if (approachCompleted) return 1f;
                return Mathf.Clamp01(currentApproachTime / approachDuration);
            }
        }

        public override Graphic Graphic
        {
            get
            {
                Thing thingForGraphic = GetThingForGraphic();
                if (thingForGraphic == this)
                {
                    return base.Graphic;
                }
                return thingForGraphic.Graphic.ExtractInnerGraphicFor(thingForGraphic);
            }
        }

        protected Material ShadowMaterial
        {
            get
            {
                if (cachedShadowMaterial == null && createShadow)
                {
                    cachedShadowMaterial = MaterialPool.MatFrom("Things/Skyfaller/SkyfallerShadowCircle", ShaderDatabase.Transparent);
                }
                return cachedShadowMaterial;
            }
        }

        // 精确旋转 - 模仿原版 Projectile
        public virtual Quaternion ExactRotation
        {
            get
            {
                Vector3 direction = (endPosition.ToVector3() - startPosition.ToVector3()).normalized;
                return Quaternion.LookRotation(direction.Yto0());
            }
        }

        // 简化的方向计算方法
        public Vector3 MovementDirection
        {
            get
            {
                return (endPosition.ToVector3() - startPosition.ToVector3()).normalized;
            }
        }

        // 淡入透明度（0-1）
        public float FadeInAlpha
        {
            get
            {
                if (fadeInCompleted) return 1f;
                return Mathf.Clamp01(currentFadeInTime / fadeInDuration);
            }
        }

        // 淡出透明度（0-1）
        public float FadeOutAlpha
        {
            get
            {
                if (!fadeOutStarted) return 1f;
                if (fadeOutCompleted) return 0f;
                return Mathf.Clamp01(1f - (currentFadeOutTime / fadeOutDuration));
            }
        }

        // 总体透明度（淡入 * 淡出）
        public float OverallAlpha
        {
            get
            {
                return FadeInAlpha * FadeOutAlpha;
            }
        }

        // 新增：计算剩余飞行时间（秒）
        public float RemainingFlightTime
        {
            get
            {
                float remainingProgress = 1f - currentProgress;
                return remainingProgress / (flightSpeed * 0.001f) * (1f / 60f);
            }
        }

        // 新增：计算基于剩余距离的淡出持续时间
        private float CalculateDynamicFadeOutDuration()
        {
            // 获取 ModExtension 配置
            var shadowExtension = def.GetModExtension<FlyOverShadowExtension>();
            float minFadeOutDuration = shadowExtension?.minFadeOutDuration ?? 0.5f;
            float maxFadeOutDuration = shadowExtension?.maxFadeOutDuration ?? 3f;
            float fadeOutDistanceFactor = shadowExtension?.fadeOutDistanceFactor ?? 0.3f;

            // 计算剩余飞行时间
            float remainingTime = RemainingFlightTime;

            // 使用剩余时间的一部分作为淡出持续时间
            float dynamicDuration = remainingTime * fadeOutDistanceFactor;

            // 限制在最小和最大范围内
            return Mathf.Clamp(dynamicDuration, minFadeOutDuration, maxFadeOutDuration);
        }

        public FlyOver()
        {
            innerContainer = new ThingOwner<Thing>(this);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Deep.Look(ref innerContainer, "innerContainer", this);
            Scribe_Values.Look(ref startPosition, "startPosition");
            Scribe_Values.Look(ref endPosition, "endPosition");
            Scribe_Values.Look(ref flightSpeed, "flightSpeed", 1f);
            Scribe_Values.Look(ref currentProgress, "currentProgress", 0f);
            Scribe_Values.Look(ref altitude, "altitude", 10f);
            Scribe_Values.Look(ref hasStarted, "hasStarted", false);
            Scribe_Values.Look(ref hasCompleted, "hasCompleted", false);
            Scribe_Values.Look(ref spawnContentsOnImpact, "spawnContentsOnImpact", false);
            Scribe_Values.Look(ref fadeInDuration, "fadeInDuration", 1.5f);
            Scribe_Values.Look(ref currentFadeInTime, "currentFadeInTime", 0f);
            Scribe_Values.Look(ref fadeInCompleted, "fadeInCompleted", false);

            // 淡出效果数据保存
            Scribe_Values.Look(ref fadeOutDuration, "fadeOutDuration", 0f);
            Scribe_Values.Look(ref currentFadeOutTime, "currentFadeOutTime", 0f);
            Scribe_Values.Look(ref fadeOutStarted, "fadeOutStarted", false);
            Scribe_Values.Look(ref fadeOutCompleted, "fadeOutCompleted", false);
            Scribe_Values.Look(ref fadeOutStartProgress, "fadeOutStartProgress", 0.7f);
            Scribe_Values.Look(ref defaultFadeOutDuration, "defaultFadeOutDuration", 1.5f);

            // 进场动画数据保存 - 新增
            Scribe_Values.Look(ref approachDuration, "approachDuration", 1.0f);
            Scribe_Values.Look(ref currentApproachTime, "currentApproachTime", 0f);
            Scribe_Values.Look(ref approachCompleted, "approachCompleted", false);
            Scribe_Values.Look(ref approachOffsetDistance, "approachOffsetDistance", 3f);
            Scribe_Values.Look(ref useApproachAnimation, "useApproachAnimation", true);

            Scribe_References.Look(ref caster, "caster");
            Scribe_References.Look(ref faction, "faction");
        }

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);

            SRALog.Debug($"FlyOver Spawned - Start: {startPosition}, End: {endPosition}, Speed: {flightSpeed}, Altitude: {altitude}");
            if (!respawningAfterLoad)
            {
                SRALog.Debug($"FlyOver Direction - Vector: {MovementDirection}, Rotation: {ExactRotation.eulerAngles}");

                // 设置初始位置
                base.Position = startPosition;
                hasStarted = true;

                // 重置淡入状态
                currentFadeInTime = 0f;
                fadeInCompleted = false;

                // 重置淡出状态
                currentFadeOutTime = 0f;
                fadeOutStarted = false;
                fadeOutCompleted = false;
                fadeOutDuration = 0f;

                // 重置进场动画状态 - 新增
                currentApproachTime = 0f;
                approachCompleted = false;

                // 从 ModExtension 加载进场动画配置
                var extension = def.GetModExtension<FlyOverShadowExtension>();
                if (extension != null)
                {
                    useApproachAnimation = extension.useApproachAnimation;
                    approachDuration = extension.approachDuration;
                    approachOffsetDistance = extension.approachOffsetDistance;
                }

                SRALog.Debug($"FlyOver approach animation: {useApproachAnimation}, duration: {approachDuration}s, offset: {approachOffsetDistance}");

                // 开始飞行音效
                if (playFlyOverSound && def.skyfaller?.floatingSound != null)
                {
                    flightSoundPlaying = def.skyfaller.floatingSound.TrySpawnSustainer(
                        SoundInfo.InMap(new TargetInfo(startPosition, map), MaintenanceType.PerTick));
                    SRALog.Debug("FlyOver sound started");
                }
            }
        }

        protected override void Tick()
        {
            base.Tick();

            if (!hasStarted || hasCompleted)
                return;

            // 更新进场动画 - 新增
            if (useApproachAnimation && !approachCompleted)
            {
                currentApproachTime += 1f / 60f;
                if (currentApproachTime >= approachDuration)
                {
                    approachCompleted = true;
                    currentApproachTime = approachDuration;
                    SRALog.Debug("FlyOver approach animation completed");
                }
            }

            // 更新淡入效果
            if (!fadeInCompleted)
            {
                currentFadeInTime += 1f / 60f;
                if (currentFadeInTime >= fadeInDuration)
                {
                    fadeInCompleted = true;
                    currentFadeInTime = fadeInDuration;
                }
            }

            // 更新飞行进度
            currentProgress += flightSpeed * 0.001f;

            // 检查是否应该开始淡出（基于剩余距离动态计算）
            if (!fadeOutStarted && currentProgress >= fadeOutStartProgress)
            {
                StartFadeOut();
            }

            // 更新淡出效果
            if (fadeOutStarted && !fadeOutCompleted)
            {
                currentFadeOutTime += 1f / 60f;
                if (currentFadeOutTime >= fadeOutDuration)
                {
                    fadeOutCompleted = true;
                    currentFadeOutTime = fadeOutDuration;
                    SRALog.Debug("FlyOver fade out completed");
                }
            }

            // 更新当前位置
            UpdatePosition();

            // 维持飞行音效（在淡出时逐渐降低音量）
            UpdateFlightSound();

            // 检查是否到达终点
            if (currentProgress >= 1f)
            {
                CompleteFlyOver();
            }

            // 生成飞行轨迹特效（在淡出时减少特效）
            CreateFlightEffects();
        }

        // 新增：开始淡出效果
        private void StartFadeOut()
        {
            fadeOutStarted = true;

            // 基于剩余距离动态计算淡出持续时间
            fadeOutDuration = CalculateDynamicFadeOutDuration();

            SRALog.Debug($"FlyOver started fade out at progress {currentProgress:F2}, duration: {fadeOutDuration:F2}s, remaining time: {RemainingFlightTime:F2}s");
        }

        private void UpdatePosition()
        {
            Vector3 currentWorldPos = Vector3.Lerp(startPosition.ToVector3(), endPosition.ToVector3(), currentProgress);
            IntVec3 newPos = currentWorldPos.ToIntVec3();

            if (newPos != base.Position && newPos.InBounds(base.Map))
            {
                base.Position = newPos;
            }
        }

        private void UpdateFlightSound()
        {
            if (flightSoundPlaying != null)
            {
                if (fadeOutStarted)
                {
                    // 淡出时逐渐降低音效音量
                    flightSoundPlaying.externalParams["VolumeFactor"] = FadeOutAlpha;
                }
                flightSoundPlaying?.Maintain();
            }
        }

        private void CompleteFlyOver()
        {
            hasCompleted = true;
            currentProgress = 1f;

            // 生成内容物（如果需要）
            if (spawnContentsOnImpact && innerContainer.Any)
            {
                SpawnContents();
            }

            // 播放完成音效
            if (def.skyfaller?.impactSound != null)
            {
                def.skyfaller.impactSound.PlayOneShot(
                    SoundInfo.InMap(new TargetInfo(endPosition, base.Map)));
            }

            SRALog.Debug($"FlyOver completed at {endPosition}");

            // 销毁自身
            Destroy();
        }

        // 新增：紧急销毁方法（使用默认淡出时间）
        public void EmergencyDestroy()
        {
            if (!fadeOutStarted)
            {
                // 如果还没有开始淡出，使用默认淡出时间
                fadeOutStarted = true;
                fadeOutDuration = defaultFadeOutDuration;
                SRALog.Debug($"FlyOver emergency destroy with default fade out: {defaultFadeOutDuration}s");
            }

            // 设置标记，下一帧会处理淡出
            hasCompleted = true;
        }

        private void SpawnContents()
        {
            foreach (Thing thing in innerContainer)
            {
                if (thing != null && !thing.Destroyed)
                {
                    GenPlace.TryPlaceThing(thing, endPosition, base.Map, ThingPlaceMode.Near);
                }
            }
            innerContainer.Clear();
        }

        private void CreateFlightEffects()
        {
            // 在飞行轨迹上生成粒子效果
            if (Rand.MTBEventOccurs(0.5f, 1f, 1f) && def.skyfaller?.motesPerCell > 0 && !fadeOutCompleted)
            {
                Vector3 effectPos = DrawPos;
                effectPos.y = AltitudeLayer.MoteOverhead.AltitudeFor();

                // 淡出时减少粒子效果强度
                float effectIntensity = fadeOutStarted ? FadeOutAlpha : 1f;
                FleckMaker.ThrowSmoke(effectPos, base.Map, 1f * effectIntensity);

                // 可选：根据速度生成更多效果
                if (flightSpeed > 2f && !fadeOutStarted)
                {
                    FleckMaker.ThrowAirPuffUp(effectPos, base.Map);
                }
            }
        }

        // 关键修复：重写 DrawAt 方法，绕过探索状态检查
        protected override void DrawAt(Vector3 drawLoc, bool flip = false)
        {
            // 直接绘制，不检查探索状态
            Vector3 finalDrawPos = drawLoc;

            if (createShadow)
            {
                DrawFlightShadow();
            }

            DrawFlyOverWithFade(finalDrawPos);
        }

        protected virtual void DrawFlyOverWithFade(Vector3 drawPos)
        {
            Thing thingForGraphic = GetThingForGraphic();
            Graphic graphic = thingForGraphic.Graphic;
            if (graphic == null)
                return;
            Material material = graphic.MatSingle;
            if (material == null)
                return;
            float alpha = OverallAlpha;
            if (alpha <= 0.001f)
                return;
            if (fadeInCompleted && !fadeOutStarted && alpha >= 0.999f)
            {
                Vector3 highAltitudePos = drawPos;
                highAltitudePos.y = AltitudeLayer.MetaOverlays.AltitudeFor();

                // 应用伴飞缩放
                Vector3 finalScale = Vector3.one;
                if (def.graphicData != null)
                {
                    finalScale = new Vector3(def.graphicData.drawSize.x * escortScale, 1f, def.graphicData.drawSize.y * escortScale);
                }
                else
                {
                    finalScale = new Vector3(escortScale, 1f, escortScale);
                }

                Matrix4x4 matrix = Matrix4x4.TRS(highAltitudePos, ExactRotation, finalScale);
                Graphics.DrawMesh(MeshPool.plane10, matrix, material, 0);
                return;
            }
            fadePropertyBlock.SetColor(ShaderPropertyIDs.Color,
                new Color(graphic.Color.r, graphic.Color.g, graphic.Color.b, graphic.Color.a * alpha));
            // 应用伴飞缩放
            Vector3 scale = Vector3.one;
            if (def.graphicData != null)
            {
                scale = new Vector3(def.graphicData.drawSize.x * escortScale, 1f, def.graphicData.drawSize.y * escortScale);
            }
            else
            {
                scale = new Vector3(escortScale, 1f, escortScale);
            }
            Vector3 highPos = drawPos;
            highPos.y = AltitudeLayer.MetaOverlays.AltitudeFor();
            Matrix4x4 matrix2 = Matrix4x4.TRS(highPos, ExactRotation, scale);
            Graphics.DrawMesh(MeshPool.plane10, matrix2, material, 0, null, 0, fadePropertyBlock);
        }

        protected virtual void DrawFlightShadow()
        {
            var shadowExtension = def.GetModExtension<FlyOverShadowExtension>();

            Material shadowMaterial;
            if (shadowExtension?.useCustomShadow == true && !shadowExtension.customShadowPath.NullOrEmpty())
            {
                shadowMaterial = MaterialPool.MatFrom(shadowExtension.customShadowPath, ShaderDatabase.Transparent);
            }
            else
            {
                shadowMaterial = ShadowMaterial;
            }

            if (shadowMaterial == null)
                return;

            Vector3 shadowPos = DrawPos;
            shadowPos.y = AltitudeLayer.Shadows.AltitudeFor();

            float shadowIntensity = shadowExtension?.shadowIntensity ?? 1f;
            float minAlpha = shadowExtension?.minShadowAlpha ?? 0.3f;
            float maxAlpha = shadowExtension?.maxShadowAlpha ?? 1f;
            float minScale = shadowExtension?.minShadowScale ?? 0.5f;
            float maxScale = shadowExtension?.maxShadowScale ?? 1.5f;

            float shadowAlpha = Mathf.Lerp(minAlpha, maxAlpha, currentProgress) * shadowIntensity;
            float shadowScale = Mathf.Lerp(minScale, maxScale, currentProgress);

            shadowAlpha *= OverallAlpha;

            if (shadowAlpha <= 0.001f)
                return;

            Vector3 s = new Vector3(shadowScale, 1f, shadowScale);
            Vector3 vector = new Vector3(0f, -0.01f, 0f);
            Matrix4x4 matrix = Matrix4x4.TRS(shadowPos + vector, Quaternion.identity, s);

            Graphics.DrawMesh(MeshPool.plane10, matrix, shadowMaterial, 0);
        }

        // IThingHolder 接口实现
        public ThingOwner GetDirectlyHeldThings()
        {
            return innerContainer;
        }

        public void GetChildHolders(List<IThingHolder> outChildren)
        {
            ThingOwnerUtility.AppendThingHoldersFromThings(outChildren, GetDirectlyHeldThings());
        }

        private Thing GetThingForGraphic()
        {
            if (def.graphicData != null || !innerContainer.Any)
            {
                return this;
            }
            return innerContainer[0];
        }

        // 工具方法：创建飞越物体
        public static FlyOver MakeFlyOver(ThingDef flyOverDef, IntVec3 start, IntVec3 end, Map map,
            float speed = 1f, float height = 10f, ThingOwner contents = null,
            float fadeInDuration = 1.5f, float defaultFadeOutDuration = 1.5f, Pawn casterPawn = null,
            bool useApproachAnimation = true, float approachDuration = 1.0f, float approachOffsetDistance = 3f) // 新增参数
        {
            FlyOver flyOver = (FlyOver)ThingMaker.MakeThing(flyOverDef);
            flyOver.startPosition = start;
            flyOver.endPosition = end;
            flyOver.flightSpeed = speed;
            flyOver.altitude = height;
            flyOver.fadeInDuration = fadeInDuration;
            flyOver.defaultFadeOutDuration = defaultFadeOutDuration;
            flyOver.caster = casterPawn;
            
            // 进场动画参数 - 新增
            flyOver.useApproachAnimation = useApproachAnimation;
            flyOver.approachDuration = approachDuration;
            flyOver.approachOffsetDistance = approachOffsetDistance;

            // 简化派系设置 - 直接设置 faction 字段
            if (casterPawn != null && casterPawn.Faction != null)
            {
                flyOver.faction = casterPawn.Faction;
                SRALog.Debug($"FlyOver faction set to: {casterPawn.Faction.Name}");
            }
            else
            {
                SRALog.Debug($"FlyOver: Cannot set faction - casterPawn: {casterPawn?.Label ?? "NULL"}, casterFaction: {casterPawn?.Faction?.Name ?? "NULL"}");
            }

            if (contents != null)
            {
                flyOver.innerContainer.TryAddRangeOrTransfer(contents);
            }

            GenSpawn.Spawn(flyOver, start, map);

            SRALog.Debug($"FlyOver created: {flyOver} from {start} to {end} at altitude {height}, Faction: {flyOver.faction?.Name ?? "NULL"}");
            return flyOver;
        }
    }

    // 扩展的 ModExtension 配置 - 新增进场动画参数
    public class FlyOverShadowExtension : DefModExtension
    {
        public string customShadowPath;
        public float shadowIntensity = 0.6f;
        public bool useCustomShadow = false;
        public float minShadowAlpha = 0.05f;
        public float maxShadowAlpha = 0.2f;
        public float minShadowScale = 0.5f;
        public float maxShadowScale = 1.0f;
        public float defaultFadeInDuration = 1.5f;
        public float defaultFadeOutDuration = 0.5f;
        public float fadeOutStartProgress = 0.98f;
        
        // 动态淡出配置
        public float minFadeOutDuration = 0.5f;
        public float maxFadeOutDuration = 0.5f;
        public float fadeOutDistanceFactor = 0.01f;
        
        public float ActuallyHeight = 150f;

        // 进场动画配置 - 新增
        public bool useApproachAnimation = true;
        public float approachDuration = 1.0f;
        public float approachOffsetDistance = 3f;
    }
}
