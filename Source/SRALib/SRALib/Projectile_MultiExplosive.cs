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
        protected override void Impact(Thing hitThing, bool blockedByShield = false)
        {
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
                Effecter effecter = properties.explosionEffect.Spawn();
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
                            thingsIgnoredByExplosion.Add(thing);
                        }
                    }
                }
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
                ignoredThings: thingsIgnoredByExplosion
            );
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