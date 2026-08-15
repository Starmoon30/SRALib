using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace SRA
{
    public class ModExtension_HighOrbitAttack : DefModExtension
    {
        // 每次轨道打击实际落下的 projectile。优先使用这个直接 Def 引用。
        public ThingDef projectileDef;

        // 打击散布半径。
        public float impactAreaRadius = 15f;

        // 总落弹次数。
        public int explosionCount = 30;

        // 两次落弹之间的 tick 间隔。
        public int bombIntervalTicks = 18;

        // 生成后到开始打击前的预热 tick。
        public int warmupTicks = 60;

        // 空中飞行物视觉贴图路径。
        public string projectileTexturePath;

        // 空中飞行物视觉材质使用的 ShaderTypeDef。留空时沿用原版轰炸视觉的 Transparent；能量/发光弹头可写 MoteGlow。
        public ShaderTypeDef shaderType;

        // 空中飞行物视觉绘制尺寸。默认 (2.5, 2.5)，与原版 BombardmentProjectile 的绘制尺寸一致。
        public Vector2 drawSize = new Vector2(2.5f, 2.5f);

        // 空中飞行物从警告到命中的飞行 tick。
        public int projectileFlyTimeTicks = 60;

        // 原版预命中音效的音量倍率。
        public float preImpactSoundVolume = 1f;

        // 优先选择非厚岩顶格。
        public bool avoidThickRoof = true;

        // 若命中厚岩顶，是否先移除屋顶再结算。
        public bool punchThroughThickRoofIfBlocked = true;

        public ThingDef ResolvedProjectileDef
        {
            get
            {
                if (projectileDef != null)
                {
                    return projectileDef;
                }

                return null;
            }
        }

        public Shader ResolvedShader => shaderType != null ? shaderType.Shader : ShaderDatabase.Transparent;

        public Vector2 ResolvedDrawSize
        {
            get
            {
                if (drawSize.x > 0f && drawSize.y > 0f)
                {
                    return drawSize;
                }

                return new Vector2(2.5f, 2.5f);
            }
        }
    }

    [StaticConstructorOnStartup]
    public class HighOrbitAttack : OrbitalStrike
    {
        private ModExtension_HighOrbitAttack ExtProps => def.GetModExtension<ModExtension_HighOrbitAttack>();

        public float impactAreaRadius = 15f;
        public FloatRange explosionRadiusRange = new FloatRange(6f, 8f);
        public int bombIntervalTicks = 18;
        public int explosionCount = 30;
        public int warmupTicks = 60;

        private int ticksToNextEffect;
        private IntVec3 nextExplosionCell = IntVec3.Invalid;
        private List<Bombardment.BombardmentProjectile> projectiles = new List<Bombardment.BombardmentProjectile>();
        private int projectileFlyTimeTicks = 60;
        private Vector2 projectileDrawSize = new Vector2(2.5f, 2.5f);
        private Material cachedProjectileMaterial;

        private static readonly List<IntVec3> TmpCells = new List<IntVec3>();

        public static readonly SimpleCurve DistanceChanceFactor = new SimpleCurve
        {
            { new CurvePoint(0f, 1f), true },
            { new CurvePoint(1f, 0.1f), true }
        };

        public override void SpawnSetup(Map map, bool respawningAfterReload)
        {
            ApplyExtensionSettings();
            base.SpawnSetup(map, respawningAfterReload);

            if (!respawningAfterReload)
            {
                GetNextExplosionCell();
            }

            string texturePath = ExtProps != null && !ExtProps.projectileTexturePath.NullOrEmpty()
                ? ExtProps.projectileTexturePath
                : "Things/Projectile/Bullet_Big";
            LongEventHandler.ExecuteWhenFinished(delegate
            {
                cachedProjectileMaterial = MaterialPool.MatFrom(texturePath, ExtProps?.ResolvedShader ?? ShaderDatabase.Transparent, Color.white);
            });
        }

        public override void StartStrike()
        {
            duration = bombIntervalTicks * explosionCount;
            base.StartStrike();
        }

        protected override void Tick()
        {
            if (Destroyed)
            {
                return;
            }

            if (warmupTicks > 0)
            {
                warmupTicks--;
                if (warmupTicks <= 0)
                {
                    StartStrike();
                }
            }
            else
            {
                base.Tick();
            }

            EffectTick();
        }

        protected override void DrawAt(Vector3 drawLoc, bool flip = false)
        {
            base.DrawAt(drawLoc, flip);
            if (cachedProjectileMaterial == null || projectiles.NullOrEmpty())
            {
                return;
            }

            for (int i = 0; i < projectiles.Count; i++)
            {
                DrawProjectileVisual(projectiles[i]);
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref impactAreaRadius, "impactAreaRadius", 15f);
            Scribe_Values.Look(ref explosionRadiusRange, "explosionRadiusRange", new FloatRange(6f, 8f));
            Scribe_Values.Look(ref bombIntervalTicks, "bombIntervalTicks", 18);
            Scribe_Values.Look(ref explosionCount, "explosionCount", 30);
            Scribe_Values.Look(ref warmupTicks, "warmupTicks", 0);
            Scribe_Values.Look(ref projectileFlyTimeTicks, "projectileFlyTimeTicks", 60);
            Scribe_Values.Look(ref projectileDrawSize, "projectileDrawSize", new Vector2(2.5f, 2.5f));
            Scribe_Values.Look(ref ticksToNextEffect, "ticksToNextEffect", 0);
            Scribe_Values.Look(ref nextExplosionCell, "nextExplosionCell", IntVec3.Invalid);
            Scribe_Collections.Look(ref projectiles, "projectiles", LookMode.Deep, Array.Empty<object>());
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (projectiles == null)
                {
                    projectiles = new List<Bombardment.BombardmentProjectile>();
                }

                if (!nextExplosionCell.IsValid)
                {
                    GetNextExplosionCell();
                }
            }
        }

        private void ApplyExtensionSettings()
        {
            ModExtension_HighOrbitAttack ext = ExtProps;
            if (ext == null)
            {
                Log.Error($"SRALib: {def.defName} lacks ModExtension_HighOrbitAttack.");
                return;
            }

            if (ext.ResolvedProjectileDef == null)
            {
                Log.Error($"SRALib: {def.defName} ModExtension_HighOrbitAttack has no projectileDef.");
            }

            impactAreaRadius = ext.impactAreaRadius;
            explosionCount = ext.explosionCount;
            bombIntervalTicks = ext.bombIntervalTicks;
            warmupTicks = ext.warmupTicks;
            projectileFlyTimeTicks = ext.projectileFlyTimeTicks;
            projectileDrawSize = ext.ResolvedDrawSize;
        }

        private void DrawProjectileVisual(Bombardment.BombardmentProjectile projectileVisual)
        {
            if (projectileVisual == null || projectileVisual.LifeTime <= 0)
            {
                return;
            }

            // Keep vanilla bombardment's north-to-target descent path, but draw it ourselves so XML can control shader and size.
            int maxLifeTime = Mathf.Max(1, projectileFlyTimeTicks);
            float progress = 1f - Mathf.Clamp01((float)projectileVisual.LifeTime / maxLifeTime);
            Vector3 drawPos = projectileVisual.targetCell.ToVector3() + Vector3.forward * Mathf.Lerp(60f, 0f, progress);
            drawPos.y = AltitudeLayer.MoteOverhead.AltitudeFor();

            Vector2 size = projectileDrawSize.x > 0f && projectileDrawSize.y > 0f
                ? projectileDrawSize
                : new Vector2(2.5f, 2.5f);
            Matrix4x4 matrix = Matrix4x4.TRS(
                drawPos,
                Quaternion.Euler(0f, 180f, 0f),
                new Vector3(size.x, 1f, size.y));
            Graphics.DrawMesh(MeshPool.plane10, matrix, cachedProjectileMaterial, 0);
        }

        private void EffectTick()
        {
            if (!nextExplosionCell.IsValid)
            {
                ticksToNextEffect = warmupTicks;
                GetNextExplosionCell();
            }

            if (warmupTicks <= 0)
            {
                ticksToNextEffect--;
            }

            if (ticksToNextEffect <= 0 && TicksLeft >= bombIntervalTicks)
            {
                float volume = ExtProps?.preImpactSoundVolume ?? 1f;
                SoundInfo info = SoundInfo.InMap(new TargetInfo(nextExplosionCell, Map, false));
                info.volumeFactor = volume;
                SoundDefOf.Bombardment_PreImpact.PlayOneShot(info);
                projectiles.Add(new Bombardment.BombardmentProjectile(projectileFlyTimeTicks, nextExplosionCell));
                ticksToNextEffect = bombIntervalTicks;
                GetNextExplosionCell();
            }

            for (int i = projectiles.Count - 1; i >= 0; i--)
            {
                projectiles[i].Tick();
                if (projectiles[i].LifeTime <= 0)
                {
                    TryDoCustomExplosion(projectiles[i]);
                    projectiles.RemoveAt(i);
                }
            }
        }

        private void TryDoCustomExplosion(Bombardment.BombardmentProjectile projectileVisual)
        {
            ThingDef projectileDef = ExtProps?.ResolvedProjectileDef;
            if (projectileDef?.projectile == null)
            {
                return;
            }

            IntVec3 targetCell = projectileVisual.targetCell;
            Map map = Map;
            PrepareCellForOrbitalImpact(targetCell, map);

            bool isCompoundExplosion = projectileDef.thingClass != null && typeof(Projectile_CompoundExplosion).IsAssignableFrom(projectileDef.thingClass);
            if (!isCompoundExplosion && TryLaunchRealProjectile(projectileDef, targetCell, map))
            {
                return;
            }

            Projectile_CompoundExplosion.DoExplosionFromProjectileProperties(
                map,
                targetCell,
                projectileDef.projectile,
                instigator,
                weaponDef,
                projectileDef,
                null,
                null,
                projectileDef.projectile.doExplosionVFX);

            if (projectileDef.projectile is ProjectileProperties_CompoundExplosion compoundProps && !compoundProps.additionalExplosions.NullOrEmpty())
            {
                for (int i = 0; i < compoundProps.additionalExplosions.Count; i++)
                {
                    ExplosionParams explosion = compoundProps.additionalExplosions[i];
                    if (explosion?.damageDef == null || explosion.radius <= 0f)
                    {
                        continue;
                    }

                    GenExplosion.DoExplosion(
                        center: targetCell,
                        map: map,
                        radius: explosion.radius,
                        damType: explosion.damageDef,
                        instigator: instigator,
                        damAmount: explosion.damageAmount,
                        armorPenetration: explosion.armorPenetration,
                        explosionSound: explosion.soundExplode,
                        weapon: weaponDef,
                        projectile: projectileDef,
                        chanceToStartFire: explosion.damageDef.defName.ToLower() == "flame" ? 0.5f : 0f,
                        damageFalloff: compoundProps.explosionDamageFalloff,
                        doVisualEffects: compoundProps.doExplosionVFX,
                        propagationSpeed: explosion.damageDef.expolosionPropagationSpeed,
                        applyDamageToExplosionCellsNeighbors: compoundProps.applyDamageToExplosionCellsNeighbors,
                        screenShakeFactor: compoundProps.screenShakeFactor);
                }
            }
        }

        private bool TryLaunchRealProjectile(ThingDef projectileDef, IntVec3 targetCell, Map map)
        {
            if (projectileDef.thingClass == null || !typeof(Projectile).IsAssignableFrom(projectileDef.thingClass))
            {
                Log.ErrorOnce($"SRALib: HighOrbitAttack projectileDef '{projectileDef.defName}' thingClass is not a Projectile. Falling back to projectile properties explosion.",
                    ("SRA_HighOrbitAttack_NotProjectile_" + projectileDef.defName).GetHashCode());
                return false;
            }

            Thing spawnedThing = GenSpawn.Spawn(projectileDef, targetCell, map, WipeMode.Vanish);
            if (!(spawnedThing is Projectile projectile))
            {
                spawnedThing.Destroy(DestroyMode.Vanish);
                Log.ErrorOnce($"SRALib: HighOrbitAttack spawned '{projectileDef.defName}', but it is not a Projectile. Falling back to projectile properties explosion.",
                    ("SRA_HighOrbitAttack_SpawnedNotProjectile_" + projectileDef.defName).GetHashCode());
                return false;
            }

            LocalTargetInfo target = new LocalTargetInfo(targetCell);
            Vector3 origin = targetCell.ToVector3Shifted();
            projectile.Launch(instigator, origin, target, target, ProjectileHitFlags.All, false, null, null);
            return true;
        }

        private void GetNextExplosionCell()
        {
            Map map = Map;
            TmpCells.Clear();
            foreach (IntVec3 cell in GenRadial.RadialCellsAround(Position, impactAreaRadius, true))
            {
                if (cell.InBounds(map))
                {
                    TmpCells.Add(cell);
                }
            }

            if (TmpCells.Count <= 0)
            {
                nextExplosionCell = Position;
                return;
            }

            if (ShouldAvoidThickRoof())
            {
                IntVec3 openCell = RandomWeightedImpactCell(TmpCells, requireOpenRoof: true);
                if (openCell.IsValid)
                {
                    nextExplosionCell = openCell;
                    return;
                }
            }

            nextExplosionCell = RandomWeightedImpactCell(TmpCells, requireOpenRoof: false);
        }

        private IntVec3 RandomWeightedImpactCell(List<IntVec3> cells, bool requireOpenRoof)
        {
            float totalWeight = 0f;
            for (int i = 0; i < cells.Count; i++)
            {
                totalWeight += GetImpactCellWeight(cells[i], requireOpenRoof);
            }

            if (totalWeight <= 0f)
            {
                return IntVec3.Invalid;
            }

            float chosenWeight = Rand.Value * totalWeight;
            for (int i = 0; i < cells.Count; i++)
            {
                chosenWeight -= GetImpactCellWeight(cells[i], requireOpenRoof);
                if (chosenWeight <= 0f)
                {
                    return cells[i];
                }
            }

            return cells[cells.Count - 1];
        }

        private float GetImpactCellWeight(IntVec3 cell, bool requireOpenRoof)
        {
                if (requireOpenRoof && HasThickRoof(cell, Map))
                {
                    return 0f;
                }

                float normalizedDistance = impactAreaRadius > 0f ? cell.DistanceTo(Position) / impactAreaRadius : 0f;
                return DistanceChanceFactor.Evaluate(normalizedDistance);
        }

        private bool ShouldAvoidThickRoof()
        {
            return ExtProps == null || ExtProps.avoidThickRoof;
        }

        private bool ShouldPunchThroughThickRoofIfBlocked()
        {
            return ExtProps == null || ExtProps.punchThroughThickRoofIfBlocked;
        }

        private bool HasThickRoof(IntVec3 cell, Map map)
        {
            if (map == null || !cell.InBounds(map))
            {
                return false;
            }

            RoofDef roof = map.roofGrid.RoofAt(cell);
            return roof != null && roof.isThickRoof;
        }

        private void PrepareCellForOrbitalImpact(IntVec3 cell, Map map)
        {
            if (!ShouldPunchThroughThickRoofIfBlocked() || map == null || !cell.InBounds(map))
            {
                return;
            }

            if (HasThickRoof(cell, map))
            {
                map.roofGrid.SetRoof(cell, null);
            }
        }
    }
}
