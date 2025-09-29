using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using Verse;

namespace SRA
{
    public class CompProperties_RepairTower : CompProperties
    {
        public float repairRadius = 0f; // 0 = 无限范围
        public RepairArea repairArea = RepairArea.HomeArea; // 修复区域类型
        public float repairRatePerSecond = 0.02f; // 每秒修复百分比 (2%)
        public float maxRepairMultiplier = 2f; // 修复上限系数
        public int maxRepairOffset = 800; // 修复上限偏移量

        public CompProperties_RepairTower()
        {
            this.compClass = typeof(Comp_RepairTower);
        }
    }

    public enum RepairArea
    {
        HomeArea,      // 仅居住区
        EntireArea,    // 指定范围内全部
    }

    public class Comp_RepairTower : ThingComp
    {
        private int SRArepairTick;
        private CompProperties_RepairTower Props => (CompProperties_RepairTower)this.props;

        public override void Initialize(CompProperties props)
        {
            base.Initialize(props);
            this.SRArepairTick = 300; // 5秒间隔
        }

        public override void CompTick()
        {
            base.CompTick();
            if (this.SRArepairTick <= 0)
            {
                this.SRArepairTick = 300; // 5秒间隔
                this.SRARepairAllThings();
                this.SRARepairAllApparelsInPawn();
            }
            else
            {
                this.SRArepairTick--;
            }
        }

        private bool SRACanRepair(Thing thing)
        {
            return thing.def.useHitPoints && (float)thing.HitPoints < (float)thing.MaxHitPoints * Props.maxRepairMultiplier + Props.maxRepairOffset;
        }
        private float GetMaxRepairLimit(Thing thing)
        {
            return (float)thing.MaxHitPoints * Props.maxRepairMultiplier + Props.maxRepairOffset;
        }

        private IEnumerable<Thing> GetThingsInRepairRange()
        {
            Map map = this.parent.MapHeld;
            if (map == null) yield break;

            HashSet<Thing> processedThings = new HashSet<Thing>(); // 用于跟踪已处理的物体
            // 根据配置获取修复区域
            switch (Props.repairArea)
            {
                case RepairArea.HomeArea:
                    // 居住区内的所有东西
                    if (Props.repairRadius <= 0)
                    {
                        // 无限范围 - 整个地图
                        foreach (IntVec3 cell in map.areaManager.Home.ActiveCells)
                        {
                            foreach (Thing thing in map.thingGrid.ThingsAt(cell))
                            {
                                if (!processedThings.Contains(thing) && this.SRACanRepair(thing))
                                {
                                    processedThings.Add(thing);
                                    yield return thing;
                                }
                            }
                        }
                    }
                    else
                    {
                        // 有限范围
                        foreach (IntVec3 cell in GenRadial.RadialCellsAround(this.parent.Position, Props.repairRadius, true))
                        {
                            if (cell.InBounds(map) && map.areaManager.Home[cell])
                            {
                                foreach (Thing thing in map.thingGrid.ThingsAt(cell))
                                {
                                    if (!processedThings.Contains(thing) && this.SRACanRepair(thing))
                                    {
                                        processedThings.Add(thing);
                                        yield return thing;
                                    }
                                }
                            }
                        }
                    }
                    break;

                case RepairArea.EntireArea:
                    // 指定范围内的所有东西
                    if (Props.repairRadius <= 0)
                    {
                        // 无限范围 - 整个地图
                        foreach (Thing thing in map.listerThings.AllThings)
                        {
                            if (thing.Spawned && !processedThings.Contains(thing) && this.SRACanRepair(thing))
                            {
                                processedThings.Add(thing);
                                yield return thing;
                            }
                        }
                    }
                    else
                    {
                        // 有限范围
                        foreach (IntVec3 cell in GenRadial.RadialCellsAround(this.parent.Position, Props.repairRadius, true))
                        {
                            if (cell.InBounds(map))
                            {
                                foreach (Thing thing in map.thingGrid.ThingsAt(cell))
                                {
                                    if (!processedThings.Contains(thing) && this.SRACanRepair(thing))
                                    {
                                        processedThings.Add(thing);
                                        yield return thing;
                                    }
                                }
                            }
                        }
                    }
                    break;
            }
        }

        public void SRARepairAllThings()
        {
            try
            {
                foreach (Thing thing in GetThingsInRepairRange())
                {
                    SRARepairThing(thing);
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Error in SRARepairAllThings: {ex}");
            }
        }

        public void SRARepairAllApparelsInOnePawn(Pawn p)
        {
            if (p == null || p.Destroyed) return;
            // 修复穿戴的装备
            if (p.apparel != null)
            {
                foreach (Apparel apparel in p.apparel.WornApparel)
                {
                    if (apparel.def.useHitPoints && (float)apparel.HitPoints < (float)apparel.MaxHitPoints)
                    {
                        apparel.HitPoints = apparel.MaxHitPoints;
                    }
                }
            }

            // 修复装备的武器
            if (p.equipment != null)
            {
                foreach (ThingWithComps thingWithComps in p.equipment.AllEquipmentListForReading)
                {
                    if (thingWithComps.def.useHitPoints && (float)thingWithComps.HitPoints < (float)thingWithComps.MaxHitPoints)
                    {
                        thingWithComps.HitPoints = thingWithComps.MaxHitPoints;
                    }
                }
            }

            // 修复物品栏中的物品
            if (p.inventory != null)
            {
                foreach (Thing thing in p.inventory.GetDirectlyHeldThings())
                {
                    if (thing.def.useHitPoints && (float)thing.HitPoints < (float)thing.MaxHitPoints)
                    {
                        thing.HitPoints = thing.MaxHitPoints;
                    }
                }
            }
        }
        public void SRARepairAllApparelsInPawn()
        {
            Map map = this.parent.MapHeld;
            if (map == null) return;

            HashSet<Pawn> processedPawns = new HashSet<Pawn>(); // 避免重复处理同一Pawn

            foreach (Pawn p in map.mapPawns.PawnsInFaction(Faction.OfPlayer))
            {
                if (processedPawns.Contains(p)) continue;

                // 检查Pawn是否在修复范围内
                if (Props.repairRadius <= 0 || (p.Position - this.parent.Position).LengthHorizontal <= Props.repairRadius)
                {
                    this.SRARepairAllApparelsInOnePawn(p);
                    processedPawns.Add(p);
                }
            }
        }

        private void SRARepairThing(Thing thing)
        {
            if (thing == null || thing.Destroyed) return;

            int maxRepairLimit = Mathf.FloorToInt(GetMaxRepairLimit(thing));
            int currentHP = thing.HitPoints;

            // 计算修复量 (5秒的修复量)
            int repairAmount = Mathf.CeilToInt((float)thing.MaxHitPoints * Props.repairRatePerSecond * 5f);

            // 确保不超过修复上限
            int newHP = Mathf.Min(currentHP + repairAmount, maxRepairLimit);

            if (newHP > currentHP)
            {
                thing.HitPoints = newHP;
            }
        }
        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref SRArepairTick, "SRArepairTick", 300);
        }
        public override void PostDrawExtraSelectionOverlays()
        {
            base.PostDrawExtraSelectionOverlays();

            // 绘制修复范围（如果有限范围）
            if (Props.repairRadius > 0)
            {
                GenDraw.DrawRadiusRing(this.parent.Position, Props.repairRadius);
            }
        }
    }
}