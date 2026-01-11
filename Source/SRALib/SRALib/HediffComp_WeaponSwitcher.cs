using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.AI;
using RimWorld;

namespace SRA
{
    // 属性定义类
    public class HediffCompProperties_WeaponSwitcher : HediffCompProperties
    {
        public List<ThingDef> linkedWeapons;
        public string gizmoIconPath = "SRA/UI/Commands/UI_SRA_WeaponSwitcher"; // XML中定义的图标路径
        public HediffCompProperties_WeaponSwitcher()
        {
            compClass = typeof(HediffComp_WeaponSwitcher);
        }
    }

    // 主组件类
    public class HediffComp_WeaponSwitcher : HediffComp
    {
        private List<ThingDef> cachedLinkedWeapons;

        public HediffCompProperties_WeaponSwitcher Props =>
            (HediffCompProperties_WeaponSwitcher)props;

        public List<ThingDef> LinkedWeapons
        {
            get
            {
                if (cachedLinkedWeapons == null)
                {
                    cachedLinkedWeapons = Props.linkedWeapons?.ToList() ?? new List<ThingDef>();
                }
                return cachedLinkedWeapons;
            }
        }

        public override IEnumerable<Gizmo> CompGetGizmos()
        {
            if (Pawn.Faction == Faction.OfPlayer && Pawn.equipment != null && LinkedWeapons.Count > 0)
            {
                yield return new Command_Action
                {
                    defaultLabel = "SRAWeaponSwitcherLabel".Translate(),
                    defaultDesc = "SRAWeaponSwitcherDesc".Translate(),
                    icon = ContentFinder<Texture2D>.Get(Props.gizmoIconPath),
                    action = OpenWeaponMenu
                };
            }
        }

        private void OpenWeaponMenu()
        {
            List<FloatMenuOption> options = new List<FloatMenuOption>();

            // 获取pawn当前持有的武器（包括装备和背包中的）
            HashSet<ThingDef> heldWeaponDefs = GetCurrentlyHeldWeaponDefs();

            // 筛选可用的武器
            foreach (var weaponDef in LinkedWeapons)
            {
                if (!heldWeaponDefs.Contains(weaponDef))
                {
                    options.Add(new FloatMenuOption(
                        weaponDef.LabelCap,
                        () => SwitchToWeapon(weaponDef),
                        shownItemForIcon:weaponDef
                    ));
                }
            }

            if (options.Count == 0)
            {
                //Find.WindowStack.Add(new Dialog_MessageBox(
                //    "No available weapons to switch to.",
                //    "OK"
                //));
                return;
            }

            Find.WindowStack.Add(new FloatMenu(options));
        }

        private HashSet<ThingDef> GetCurrentlyHeldWeaponDefs()
        {
            var heldDefs = new HashSet<ThingDef>();

            // 检查当前装备的武器
            if (Pawn.equipment.Primary != null &&
                LinkedWeapons.Contains(Pawn.equipment.Primary.def))
            {
                heldDefs.Add(Pawn.equipment.Primary.def);
            }

            // 检查背包中的武器
            if (Pawn.inventory != null && Pawn.inventory.innerContainer != null)
            {
                foreach (var thing in Pawn.inventory.innerContainer)
                {
                    if (thing != null &&
                        thing.def.IsWeapon &&
                        LinkedWeapons.Contains(thing.def))
                    {
                        heldDefs.Add(thing.def);
                    }
                }
            }

            return heldDefs;
        }

        private void SwitchToWeapon(ThingDef newWeaponDef)
        {
            // 先添加新武器（关键：先添加后删除）
            Thing newWeapon = CreateNewWeapon(newWeaponDef);

            // 然后删除旧武器
            RemoveOldWeapons();

            // 装备新武器
            EquipNewWeapon(newWeapon);
        }

        private Thing CreateNewWeapon(ThingDef weaponDef)
        {
            // 获取当前武器的品质（如果有）
            CompQuality currentQuality = GetCurrentWeaponQuality();
            QualityCategory quality = currentQuality?.Quality ?? QualityCategory.Normal;

            // 创建新武器
            Thing weapon = ThingMaker.MakeThing(weaponDef);

            // 设置品质
            if (weapon.TryGetComp<CompQuality>() != null)
            {
                weapon.TryGetComp<CompQuality>().SetQuality(quality, ArtGenerationContext.Colony);
            }

            // 设置其他属性（如颜色、破损度等）
            TransferWeaponProperties(weapon);

            return weapon;
        }

        private CompQuality GetCurrentWeaponQuality()
        {
            // 优先检查装备的武器
            if (Pawn.equipment.Primary != null &&
                LinkedWeapons.Contains(Pawn.equipment.Primary.def))
            {
                return Pawn.equipment.Primary.TryGetComp<CompQuality>();
            }

            // 检查背包中的武器
            if (Pawn.inventory != null)
            {
                foreach (var thing in Pawn.inventory.innerContainer)
                {
                    if (thing != null &&
                        thing.def.IsWeapon &&
                        LinkedWeapons.Contains(thing.def))
                    {
                        var qualityComp = thing.TryGetComp<CompQuality>();
                        if (qualityComp != null)
                            return qualityComp;
                    }
                }
            }

            return null;
        }

        private void TransferWeaponProperties(Thing newWeapon)
        {
            // 这里可以添加其他需要继承的属性
            // 例如：染色、特殊属性等
        }

        private void RemoveOldWeapons()
        {
            // 移除装备的武器
            if (Pawn.equipment.Primary != null &&
                LinkedWeapons.Contains(Pawn.equipment.Primary.def))
            {
                var oldWeapon = Pawn.equipment.Primary;
                Pawn.equipment.Remove(oldWeapon);

                // 处理装备移除后的逻辑
                HandleWeaponRemoval(oldWeapon);
            }

            // 移除背包中的武器
            if (Pawn.inventory != null && Pawn.inventory.innerContainer != null)
            {
                var weaponsToRemove = new List<Thing>();

                foreach (var thing in Pawn.inventory.innerContainer)
                {
                    if (thing != null &&
                        thing.def.IsWeapon &&
                        LinkedWeapons.Contains(thing.def))
                    {
                        weaponsToRemove.Add(thing);
                    }
                }

                foreach (var weapon in weaponsToRemove)
                {
                    Pawn.inventory.innerContainer.Remove(weapon);
                    HandleWeaponRemoval(weapon);
                }
            }
        }

        private void HandleWeaponRemoval(Thing weapon)
        {
            // 处理武器移除后的清理工作
            // 注意：这里先添加新武器再删除旧武器，所以hediff不会被意外移除

            // 如果武器有特殊效果，在这里处理
            if (weapon.def.IsApparel) // 有些武器同时也是服装
            {
                // 处理特殊逻辑
            }

            // 销毁武器或放入世界
            weapon.Destroy(DestroyMode.Vanish);
        }

        private void EquipNewWeapon(Thing newWeapon)
        {
            try
            {
                // 尝试直接装备
                if (Pawn.equipment.Primary == null)
                {
                    Pawn.equipment.AddEquipment((ThingWithComps)newWeapon);
                }
                else
                {
                    // 如果已有装备，先移除再添加
                    var currentEq = Pawn.equipment.Primary;
                    Pawn.equipment.Remove(currentEq);

                    // 将原装备放入背包
                    if (Pawn.inventory != null)
                    {
                        Pawn.inventory.innerContainer.TryAdd(currentEq);
                    }
                    else
                    {
                        currentEq.Destroy(DestroyMode.Vanish);
                    }

                    Pawn.equipment.AddEquipment((ThingWithComps)newWeapon);
                }

                // 发送消息通知
                //Messages.Message(
                //    $"{Pawn.LabelShort} switched to {newWeapon.LabelCap}",
                //    Pawn,
                //    MessageTypeDefOf.PositiveEvent
                //);
            }
            catch
            {
                // 如果装备失败，将武器放入背包
                if (Pawn.inventory != null)
                {
                    Pawn.inventory.innerContainer.TryAdd(newWeapon);
                }
                else
                {
                    newWeapon.Destroy(DestroyMode.Vanish);
                }
                throw;
            }
        }
    }
}