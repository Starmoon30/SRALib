using RimWorld;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using Verse.Noise;

namespace SRA
{

    // 爆炸属性定义类
    public class MultiExplosionProperties
    {
        public float radius;
        public DamageDef damageDef;
        public int damageAmount = 1;
        public float armorPenetration = 1f;
        public SoundDef explosionSound;
        public bool explosionDamageFalloff = true;
        public EffecterDef explosionEffect;
        public int explosionEffectLifetimeTicks;
        public bool onlyAntiHostile = false;
        // 穿透障碍：爆炸影响范围不再被墙体或其它满填充建筑阻挡。
        public bool penetrateObstacles = false;
        // 采矿：爆炸伤害会被记为采矿伤害，掉落量按原版采矿收益累计结算。
        public bool mining = false;

        // 爆炸影响每个格子前尝试生成的物体。
        public ThingDef preExplosionSpawnThingDef = null;
        // 爆炸影响每个格子前生成物体的概率。
        public float preExplosionSpawnChance = 0f;
        // 爆炸影响每个格子前生成物体的数量。
        public int preExplosionSpawnThingCount = 1;
        // 爆炸开始时在爆炸中心尝试生成的单个物体。
        public ThingDef preExplosionSpawnSingleThingDef = null;
        public ThingDef postExplosionSpawnThingDef = null;
        public float postExplosionSpawnChance = 0f;
        public int postExplosionSpawnThingCount = 1;
        // 爆炸结束时在爆炸中心尝试生成的单个物体。
        public ThingDef postExplosionSpawnSingleThingDef = null;
        public GasType ? postExplosionGasType = null;
        public float ? postExplosionGasRadiusOverride = null;
        public int postExplosionGasAmount = 255;
    }

    // 子弹头发射属性定义类
    public class BulletLaunchProperties
    {
        public ThingDef projectileDef;  // 子弹头的Def
        public int bulletCount = 1;     // 发射数量
        public float angleRange = 60f;  // 角度范围（基于母弹头朝向的左右各多少度）
        public FloatRange distanceRange = new FloatRange(3f, 10f); // 目标距离范围
    }
    public class MultiExplosiveExtension : DefModExtension
    {
        public List<MultiExplosionProperties> multiexplosions = new List<MultiExplosionProperties>();
        public List<BulletLaunchProperties> bulletLaunches = new List<BulletLaunchProperties>();
    }


    public class Projectile_MultiExplosive : Projectile
    {
        private const int MiningYieldApplications = 2;

        protected virtual bool isNorthArcTrail => false;
        private TailBulletDef tailBulletDefInt;
        protected List<int> tailFleckTicks = new List<int>();
        private Vector3 lastTickPosition;

        public TailBulletDef TailDef
        {
            get
            {
                if (tailBulletDefInt == null)
                {
                    tailBulletDefInt = def.GetModExtension<TailBulletDef>();
                    if (tailBulletDefInt == null)
                    {
                        this.tailBulletDefInt = new TailBulletDef();
                    }
                }
                return tailBulletDefInt;
            }
        }
        private int ticksToDetonation = 1;
        private bool projIsLanded = false;
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look<int>(ref this.ticksToDetonation, "ticksToDetonation", 1, false);
            Scribe_Values.Look<bool>(ref this.projIsLanded, "projIsLanded", false, false);
            Scribe_Collections.Look(ref this.tailFleckTicks, "tailFleckTicks", LookMode.Value);
            if (this.tailFleckTicks == null)
            {
                this.tailFleckTicks = new List<int>();
            }
        }

        protected override void Tick()
        {
            base.Tick();
            if (!isNorthArcTrail)
            {
                TickTailFlecks(base.ExactPosition, lastTickPosition);
            }
            if (projIsLanded)
            {
                --ticksToDetonation;
            }
            lastTickPosition = base.ExactPosition;
        }

        protected void TickTailFlecks(Vector3 currentPosition, Vector3 previousPosition)
        {
            TailBulletDef tailDef = TailDef;
            if (tailDef == null || tailDef.tailFlecks == null || tailDef.tailFlecks.Count == 0)
            {
                return;
            }

            EnsureTailFleckTickState(tailDef.tailFlecks.Count);

            for (int i = 0; i < tailDef.tailFlecks.Count; i++)
            {
                tailFleckTicks[i]++;
            }

            Map map = base.Map;
            Vector3 movement = currentPosition - previousPosition;
            if (map == null || movement.MagnitudeHorizontalSquared() <= 0.0001f)
            {
                return;
            }

            float moveAngle = movement.AngleFlat();
            for (int i = 0; i < tailDef.tailFlecks.Count; i++)
            {
                TailFleckEntry tailFleck = tailDef.tailFlecks[i];
                if (tailFleck == null || tailFleck.tailFleckDef == null)
                {
                    continue;
                }

                if (!ShouldSpawnTailFleck(tailFleck, tailFleckTicks[i]))
                {
                    continue;
                }

                SpawnTailFleck(map, currentPosition, moveAngle, tailFleck);
            }
        }

        private void EnsureTailFleckTickState(int count)
        {
            while (tailFleckTicks.Count < count)
            {
                tailFleckTicks.Add(0);
            }

            if (tailFleckTicks.Count > count)
            {
                tailFleckTicks.RemoveRange(count, tailFleckTicks.Count - count);
            }
        }

        private bool ShouldSpawnTailFleck(TailFleckEntry tailFleck, int tick)
        {
            int delay = Mathf.Max(0, tailFleck.fleckDelayTicks);
            int interval = Mathf.Max(1, tailFleck.fleckMakeFleckTickMax);
            int firstSpawnTick = delay == 0 ? 1 : delay;

            if (tick < firstSpawnTick)
            {
                return false;
            }

            if (tick == firstSpawnTick)
            {
                return true;
            }

            return (tick - firstSpawnTick) % interval == 0;
        }

        private void SpawnTailFleck(Map map, Vector3 currentPosition, float moveAngle, TailFleckEntry tailFleck)
        {
            int count = tailFleck.fleckMakeFleckNum.RandomInRange;
            for (int i = 0; i < count; i++)
            {
                FleckCreationData dataStatic = FleckMaker.GetDataStatic(currentPosition, map, tailFleck.tailFleckDef, tailFleck.fleckScale.RandomInRange);
                dataStatic.rotation = moveAngle;
                dataStatic.rotationRate = tailFleck.fleckRotation.RandomInRange;
                dataStatic.velocityAngle = tailFleck.fleckAngle.RandomInRange + moveAngle;
                dataStatic.velocitySpeed = tailFleck.fleckSpeed.RandomInRange;
                map.flecks.CreateFleck(dataStatic);
            }
        }
        protected override void Impact(Thing hitThing, bool blockedByShield = false)
        {
            if (this.def.projectile.explosionDelay > 0 && ticksToDetonation > 0)
            {
                if (!projIsLanded)
                {
                    projIsLanded = true;
                    ticksToDetonation = this.def.projectile.explosionDelay;
                }
                return;
            }
            var extension = this.def.GetModExtension<MultiExplosiveExtension>();
            if (extension != null)
            {
                // 执行多重爆炸
                if (extension.multiexplosions != null && extension.multiexplosions.Count > 0)
                {
                    foreach (var explosion in extension.multiexplosions)
                    {
                        ExecuteExplosion(explosion);
                    }
                }

                // 发射额外子弹头
                if (extension.bulletLaunches != null && extension.bulletLaunches.Count > 0)
                {
                    foreach (var bulletLaunch in extension.bulletLaunches)
                    {
                        LaunchAdditionalBullets(bulletLaunch);
                    }
                }
            }
            base.Impact(hitThing);
        }
        private void ExecuteExplosion(MultiExplosionProperties properties)
        {

            if (properties.explosionEffect != null)
            {
                Effecter effecter = properties.explosionEffect.Spawn().Trigger(new TargetInfo(Position, launcher.Map, false), this.launcher, -1);
                if (properties.explosionEffectLifetimeTicks != 0)
                {
                    Map.effecterMaintainer.AddEffecterToMaintain(effecter, Position.ToVector3().ToIntVec3(), properties.explosionEffectLifetimeTicks);
                }
                else
                {
                    effecter.Trigger(new TargetInfo(Position, Map, false), new TargetInfo(Position, Map, false), -1);
                    effecter.Cleanup();
                }
            }
            List<Thing> thingsIgnoredByExplosion = new List<Thing>();
            if (properties.onlyAntiHostile)
            {
                foreach (IntVec3 cell in GenRadial.RadialCellsAround(Position, properties.radius, true))
                {
                    if (!cell.InBounds(Map)) continue;
                    foreach (Thing thing in Map.thingGrid.ThingsListAt(cell))
                    {
                        // 敌我识别
                        if (!GenHostility.HostileTo(thing, launcher))
                        {
                            AddIgnoredThing(thingsIgnoredByExplosion, thing);
                        }
                    }
                }
            }
            List<IntVec3> affectedCellsOverride = null;
            if (properties.penetrateObstacles)
            {
                affectedCellsOverride = GetPenetratingExplosionCells(Position, Map, properties.radius);
            }
            if (properties.mining)
            {
                List<IntVec3> miningCells = affectedCellsOverride ?? GetStandardExplosionCells(properties);
                ApplyMiningExplosionToMineables(properties, miningCells, thingsIgnoredByExplosion);
            }
            GenExplosion.DoExplosion(
                center: Position,
                map: Map, 
                radius: properties.radius,
                damType: properties.damageDef,
                instigator: launcher,
                damAmount: properties.damageAmount,
                armorPenetration: properties.armorPenetration,
                explosionSound: properties.explosionSound,
                weapon: equipmentDef,
                projectile: def,
                intendedTarget: intendedTarget.Thing,
                damageFalloff: properties.explosionDamageFalloff,
                preExplosionSpawnThingDef: properties.preExplosionSpawnThingDef,
                preExplosionSpawnChance: properties.preExplosionSpawnChance,
                preExplosionSpawnThingCount: properties.preExplosionSpawnThingCount,
                postExplosionSpawnThingDef: properties.postExplosionSpawnThingDef,
                postExplosionSpawnChance: properties.postExplosionSpawnChance,
                postExplosionSpawnThingCount: properties.postExplosionSpawnThingCount,
                postExplosionGasType: properties.postExplosionGasType,
                postExplosionGasRadiusOverride: properties.postExplosionGasRadiusOverride,
                postExplosionGasAmount: properties.postExplosionGasAmount,
                ignoredThings: thingsIgnoredByExplosion,
                overrideCells: affectedCellsOverride,
                preExplosionSpawnSingleThingDef: properties.preExplosionSpawnSingleThingDef,
                postExplosionSpawnSingleThingDef: properties.postExplosionSpawnSingleThingDef
            );
        }

        private static List<IntVec3> GetPenetratingExplosionCells(IntVec3 center, Map map, float radius)
        {
            List<IntVec3> cells = new List<IntVec3>();
            foreach (IntVec3 cell in GenRadial.RadialCellsAround(center, radius, true))
            {
                if (cell.InBounds(map))
                {
                    cells.Add(cell);
                }
            }

            return cells;
        }

        private List<IntVec3> GetStandardExplosionCells(MultiExplosionProperties properties)
        {
            List<IntVec3> cells = new List<IntVec3>();
            if (properties.damageDef == null)
            {
                return cells;
            }

            foreach (IntVec3 cell in properties.damageDef.Worker.ExplosionCellsToHit(Position, Map, properties.radius))
            {
                cells.Add(cell);
            }

            return cells;
        }

        private void ApplyMiningExplosionToMineables(MultiExplosionProperties properties, List<IntVec3> cells, List<Thing> ignoredThings)
        {
            if (cells == null || cells.Count == 0 || Map == null)
            {
                return;
            }

            HashSet<Thing> processedMineables = new HashSet<Thing>();
            for (int i = 0; i < cells.Count; i++)
            {
                IntVec3 cell = cells[i];
                if (!cell.InBounds(Map))
                {
                    continue;
                }

                List<Thing> thingList = Map.thingGrid.ThingsListAt(cell);
                for (int j = thingList.Count - 1; j >= 0; j--)
                {
                    Mineable mineable = thingList[j] as Mineable;
                    if (mineable == null || mineable.Destroyed || !processedMineables.Add(mineable))
                    {
                        continue;
                    }

                    AddIgnoredThing(ignoredThings, mineable);
                    ApplyMiningExplosionToMineable(properties, mineable, cell, ignoredThings);
                }
            }
        }

        private void ApplyMiningExplosionToMineable(MultiExplosionProperties properties, Mineable mineable, IntVec3 cell, List<Thing> ignoredThings)
        {
            if (!mineable.def.useHitPoints || properties.damageDef != null && !properties.damageDef.harmsHealth)
            {
                return;
            }

            float adjustedDamage = GetAdjustedBuildingDamage(properties.damageDef, GetExplosionDamageAmountAt(properties, cell), mineable);
            int damage = Mathf.Min(mineable.HitPoints, GenMath.RoundRandom(adjustedDamage));
            if (damage <= 0)
            {
                return;
            }

            if (damage >= mineable.HitPoints)
            {
                DestroyMineableFromMiningDamage(mineable, damage, ignoredThings);
                return;
            }

            ApplyMiningYield(mineable, damage);
            mineable.HitPoints -= damage;
        }

        private void ApplyMiningYield(Mineable mineable, int damage)
        {
            Pawn miner = launcher as Pawn;
            for (int i = 0; i < MiningYieldApplications; i++)
            {
                mineable.Notify_TookMiningDamage(damage, miner);
            }
        }

        private int GetExplosionDamageAmountAt(MultiExplosionProperties properties, IntVec3 cell)
        {
            int damageAmount = ResolveDamageAmount(properties);
            if (!properties.explosionDamageFalloff || properties.radius <= 0f)
            {
                return damageAmount;
            }

            float t = cell.DistanceTo(Position) / properties.radius;
            return Mathf.Max(GenMath.RoundRandom(Mathf.Lerp(damageAmount, damageAmount * 0.2f, t)), 1);
        }

        private static int ResolveDamageAmount(MultiExplosionProperties properties)
        {
            if (properties.damageAmount >= 0)
            {
                return properties.damageAmount;
            }

            if (properties.damageDef != null && properties.damageDef.defaultDamage >= 0)
            {
                return properties.damageDef.defaultDamage;
            }

            return 1;
        }

        private float GetAdjustedBuildingDamage(DamageDef damageDef, float damageAmount, Building building)
        {
            if (damageDef == null)
            {
                return damageAmount;
            }

            float adjustedDamage = damageAmount * damageDef.buildingDamageFactor;
            adjustedDamage *= building.def.passability == Traversability.Impassable ? damageDef.buildingDamageFactorImpassable : damageDef.buildingDamageFactorPassable;
            if (damageDef.scaleDamageToBuildingsBasedOnFlammability)
            {
                adjustedDamage *= Mathf.Max(0.05f, building.GetStatValue(StatDefOf.Flammability, true, -1));
            }

            Pawn pawn = launcher as Pawn;
            if (pawn != null && pawn.IsShambler)
            {
                adjustedDamage *= 1.5f;
            }

            return adjustedDamage;
        }

        private void DestroyMineableFromMiningDamage(Mineable mineable, int damage, List<Thing> ignoredThings)
        {
            if (mineable.Destroyed)
            {
                return;
            }

            Map map = mineable.Map;
            IntVec3 position = mineable.Position;
            ThingDef yieldThingDef = mineable.def.building?.mineableThing;
            Dictionary<Thing, int> yieldStacksBefore = GetNearbyThingStacks(map, position, yieldThingDef);
            ApplyMiningYield(mineable, damage);
            mineable.DestroyMined(launcher as Pawn);
            AddNewOrChangedYieldThingsToIgnored(map, position, yieldThingDef, yieldStacksBefore, ignoredThings);
        }

        private static void AddIgnoredThing(List<Thing> ignoredThings, Thing thing)
        {
            if (thing != null && !ignoredThings.Contains(thing))
            {
                ignoredThings.Add(thing);
            }
        }

        private static Dictionary<Thing, int> GetNearbyThingStacks(Map map, IntVec3 center, ThingDef thingDef)
        {
            Dictionary<Thing, int> stacks = new Dictionary<Thing, int>();
            if (map == null || thingDef == null)
            {
                return stacks;
            }

            foreach (IntVec3 cell in GenRadial.RadialCellsAround(center, 5f, true))
            {
                if (!cell.InBounds(map))
                {
                    continue;
                }

                List<Thing> thingList = map.thingGrid.ThingsListAt(cell);
                for (int i = 0; i < thingList.Count; i++)
                {
                    Thing thing = thingList[i];
                    if (thing.def == thingDef && !stacks.ContainsKey(thing))
                    {
                        stacks.Add(thing, thing.stackCount);
                    }
                }
            }

            return stacks;
        }

        private static void AddNewOrChangedYieldThingsToIgnored(Map map, IntVec3 center, ThingDef thingDef, Dictionary<Thing, int> stacksBefore, List<Thing> ignoredThings)
        {
            if (map == null || thingDef == null)
            {
                return;
            }

            foreach (IntVec3 cell in GenRadial.RadialCellsAround(center, 5f, true))
            {
                if (!cell.InBounds(map))
                {
                    continue;
                }

                List<Thing> thingList = map.thingGrid.ThingsListAt(cell);
                for (int i = 0; i < thingList.Count; i++)
                {
                    Thing thing = thingList[i];
                    if (thing.def != thingDef)
                    {
                        continue;
                    }

                    int stackCountBefore;
                    if (stacksBefore == null || !stacksBefore.TryGetValue(thing, out stackCountBefore) || thing.stackCount > stackCountBefore)
                    {
                        AddIgnoredThing(ignoredThings, thing);
                    }
                }
            }
        }

        private void LaunchAdditionalBullets(BulletLaunchProperties properties)
        {
            if (properties.projectileDef == null || properties.bulletCount <= 0)
                return;

            // 获取母弹头的朝向角度
            float baseAngle = ExactRotation.eulerAngles.y;

            for (int i = 0; i < properties.bulletCount; i++)
            {
                // 计算随机角度（基于母弹头朝向的角度范围内）
                float randomAngle = baseAngle + Random.Range(-properties.angleRange, properties.angleRange);
                randomAngle = (randomAngle + 360f) % 360f; // 确保角度在0-360度范围内

                // 计算随机距离
                float randomDistance = Random.Range(properties.distanceRange.min, properties.distanceRange.max);

                // 计算目标位置
                Vector3 direction = Quaternion.Euler(0f, randomAngle, 0f) * Vector3.forward;
                IntVec3 targetCell = Position + new IntVec3((int)(direction.x * randomDistance), 0, (int)(direction.z * randomDistance));

                // 确保目标单元格在地图范围内
                if (!targetCell.InBounds(Map))
                {
                    // 如果目标超出地图范围，尝试在半径范围内重新选择
                    targetCell = GetRandomValidTargetCell(randomDistance);
                    if (!targetCell.IsValid) continue;
                }

                // 创建并发射子弹头
                Projectile projectile = (Projectile)ThingMaker.MakeThing(properties.projectileDef);
                if (projectile != null)
                {
                    // 设置子弹头的位置
                    GenSpawn.Spawn(projectile, Position, Map);

                    // 使用正确的Launch方法参数
                    projectile.Launch(
                        launcher: launcher,
                        usedTarget: new LocalTargetInfo(targetCell),
                        intendedTarget: new LocalTargetInfo(targetCell),
                        hitFlags: projectile.HitFlags,
                        equipment: equipment
                    );
                }
            }
        }

        private IntVec3 GetRandomValidTargetCell(float radius)
        {
            // 在指定半径范围内随机选择有效的地面格子
            for (int i = 0; i < 10; i++) // 尝试10次
            {
                IntVec3 randomCell = this.Position + new IntVec3(
                    Random.Range(-(int)radius, (int)radius),
                    0,
                    Random.Range(-(int)radius, (int)radius)
                );

                if (randomCell.InBounds(Map) && randomCell.Walkable(Map))
                {
                    return randomCell;
                }
            }

            // 如果找不到有效格子，返回无效位置
            return IntVec3.Invalid;
        }
    }
}
