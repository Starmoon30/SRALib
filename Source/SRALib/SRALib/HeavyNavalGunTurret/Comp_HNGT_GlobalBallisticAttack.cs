using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace SRA
{
    public class CompProperties_HNGT_GlobalBallisticAttack : CompProperties
    {
        // 调度一次跨地图攻击后的冷却秒数。
        public int cooldownSeconds = 900;

        // RemoteMonitoring 分组按钮图标路径。
        public string iconPath;

        // 跨地图火炮类别 key。同 key 的火炮会合并成一个 RemoteMonitoring 调度按钮。
        public string categoryKey;

        // 类别显示名 keyed 文本；留空时使用建筑 label。
        public string categoryLabelKey;

        // 类别说明 keyed 文本；留空时使用通用说明。
        public string categoryDescKey;

        // 生成到世界地图上的飞行物 Def。留空时使用 SRALib 内置默认飞行物。
        public WorldObjectDef worldObjectDef;

        // 抵达目标地图后生成的 payload，通常是 SRA.HighOrbitAttack 类型的 OrbitalStrike ThingDef。
        public ThingDef payloadThingDef;

        public CompProperties_HNGT_GlobalBallisticAttack()
        {
            compClass = typeof(Comp_HNGT_GlobalBallisticAttack);
        }
    }

    public class Comp_HNGT_GlobalBallisticAttack : ThingComp
    {
        private const string DefaultWorldObjectDefName = "SRA_GlobalAttackDevice";
        private const float FakeTargetMaxDistance = 500f;
        private const int TicksPerSecond = 60;

        public const float RemoteFuelPerFakeShot = 1f;

        private int cooldownTicksLeft;
        private bool isFiringInterMap;
        private PlanetTile interMapTargetTile = PlanetTile.Invalid;
        private int interMapTargetMapId = -1;
        private IntVec3 interMapTargetCell = IntVec3.Invalid;
        private WorldObjectDef interMapWorldObjectDef;
        private ThingDef interMapPayloadThingDef;
        private bool interMapVisualBurstStarted;

        public CompProperties_HNGT_GlobalBallisticAttack Props => (CompProperties_HNGT_GlobalBallisticAttack)props;

        public bool IsFiringInterMap => isFiringInterMap;

        public int RemoteBurstShotCount
        {
            get
            {
                if (!isFiringInterMap)
                {
                    return 0;
                }

                return GetRemoteBurstShotCount(interMapWorldObjectDef);
            }
        }

        public int CooldownTicksLeft => Mathf.Max(0, cooldownTicksLeft);

        public float CooldownPercent
        {
            get
            {
                int cooldownTicks = Mathf.Max(1, Props.cooldownSeconds * TicksPerSecond);
                return Mathf.InverseLerp(cooldownTicks, 0f, CooldownTicksLeft);
            }
        }

        public WorldObjectDef ResolvedWorldObjectDef
        {
            get
            {
                if (Props.worldObjectDef != null)
                {
                    return Props.worldObjectDef;
                }

                return DefDatabase<WorldObjectDef>.GetNamed(DefaultWorldObjectDefName, false);
            }
        }

        public ThingDef ResolvedPayloadThingDef
        {
            get
            {
                if (Props.payloadThingDef != null)
                {
                    return Props.payloadThingDef;
                }

                return null;
            }
        }

        public string CategoryKey
        {
            get
            {
                if (!Props.categoryKey.NullOrEmpty())
                {
                    return Props.categoryKey;
                }

                if (parent?.def != null)
                {
                    return parent.def.defName;
                }

                ThingDef payloadDef = ResolvedPayloadThingDef;
                return payloadDef?.defName ?? "SRA_RemoteArtillery_Unknown";
            }
        }

        public string CategoryLabel
        {
            get
            {
                if (!Props.categoryLabelKey.NullOrEmpty())
                {
                    return Props.categoryLabelKey.Translate();
                }

                if (parent?.def != null)
                {
                    return parent.def.LabelCap;
                }

                ThingDef payloadDef = ResolvedPayloadThingDef;
                return payloadDef != null ? payloadDef.LabelCap : "SRA_RemoteArtillery_UnknownLabel".Translate();
            }
        }

        public string CategoryDesc
        {
            get
            {
                if (!Props.categoryDescKey.NullOrEmpty())
                {
                    return Props.categoryDescKey.Translate();
                }

                return "SRA_RemoteArtillery_CommandDesc".Translate(CategoryLabel);
            }
        }

        public Texture2D CommandIcon => RemoteMonitoringUtility.ResolveCommandIcon(Props.iconPath, "UI/Commands/Attack");

        public override void CompTick()
        {
            base.CompTick();
            if (cooldownTicksLeft > 0)
            {
                cooldownTicksLeft--;
            }
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (Gizmo gizmo in base.CompGetGizmosExtra())
            {
                yield return gizmo;
            }

            if (parent == null || parent.Faction != Faction.OfPlayer)
            {
                yield break;
            }

            Command_Action command = new Command_Action
            {
                defaultLabel = "SRA_RemoteArtillery_LocalCommandLabel".Translate(CategoryLabel),
                defaultDesc = "SRA_RemoteArtillery_LocalCommandDesc".Translate(CategoryLabel),
                icon = CommandIcon ?? BaseContent.BadTex,
                action = () => RemoteArtilleryUtility.BeginSingleArtilleryExistingMapTargeting(this)
            };

            if (!CanDispatchTo(null, out string disabledReason))
            {
                command.Disable(disabledReason);
            }
            else if (!RemoteArtilleryUtility.HasExistingTargetMap(this))
            {
                command.Disable("SRA_RemoteArtillery_NoExistingTargetMap".Translate());
            }

            yield return command;
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref cooldownTicksLeft, "HNGT_orbitalCooldown", 0);
            Scribe_Values.Look(ref isFiringInterMap, "isFiringInterMap", false);
            Scribe_Values.Look(ref interMapTargetTile, "interMapTargetTile", PlanetTile.Invalid);
            Scribe_Values.Look(ref interMapTargetMapId, "interMapTargetMapId", -1);
            Scribe_Values.Look(ref interMapTargetCell, "interMapTargetCell", IntVec3.Invalid);
            Scribe_Defs.Look(ref interMapWorldObjectDef, "interMapWorldObjectDef");
            Scribe_Defs.Look(ref interMapPayloadThingDef, "interMapPayloadThingDef");
            Scribe_Values.Look(ref interMapVisualBurstStarted, "interMapVisualBurstStarted", false);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                cooldownTicksLeft = Mathf.Max(0, cooldownTicksLeft);
            }
        }

        public bool CanEverDispatchTo(Map targetMap = null)
        {
            if (parent == null || parent.Destroyed || !parent.Spawned || parent.Faction != Faction.OfPlayer)
            {
                return false;
            }

            if (!(parent is Building_TurretGunHasSpeed) || parent.Map == null || parent.Map.IsPocketMap)
            {
                return false;
            }

            if (targetMap != null && targetMap == parent.Map)
            {
                return false;
            }

            return ResolvedWorldObjectDef != null && ResolvedPayloadThingDef != null;
        }

        public bool CanDispatchTo(Map targetMap = null)
        {
            return CanDispatchTo(targetMap, out _);
        }

        public bool CanDispatchTo(Map targetMap, out string disabledReason)
        {
            disabledReason = null;
            if (!CanEverDispatchTo(targetMap))
            {
                if (ResolvedWorldObjectDef == null || ResolvedPayloadThingDef == null)
                {
                    disabledReason = "SRA_RemoteArtillery_MissingDefinition".Translate(parent?.def?.LabelCap ?? "SRA_RemoteArtillery_UnknownLabel".Translate());
                }
                else
                {
                    disabledReason = "SRA_RemoteArtillery_NoAvailable".Translate(CategoryLabel);
                }

                return false;
            }

            if (cooldownTicksLeft > 0)
            {
                disabledReason = "SRA_RemoteArtillery_CooldownLeft".Translate(CooldownTicksLeft.ToStringTicksToPeriod());
                return false;
            }

            if (isFiringInterMap)
            {
                disabledReason = "SRA_RemoteArtillery_AlreadyFiring".Translate(parent.LabelCap);
                return false;
            }

            if (!HasRequiredAmmoForRemoteFire(out disabledReason))
            {
                return false;
            }

            Building_TurretGunHasSpeed turret = parent as Building_TurretGunHasSpeed;
            if (turret == null || !turret.SRA_CanAcceptRemoteArtilleryOrder)
            {
                disabledReason = "SRA_RemoteArtillery_LocalUnavailable".Translate(parent.LabelCap);
                return false;
            }

            return true;
        }

        public bool HasRequiredAmmoForRemoteFire(out string rejectReason)
        {
            return HasRequiredAmmo(GetRequiredRemoteShotCount(), out rejectReason);
        }

        public bool HasRequiredAmmoForNextFakeShot(out string rejectReason)
        {
            return HasRequiredAmmo(1, out rejectReason);
        }

        private bool HasRequiredAmmo(int requiredShotCount, out string rejectReason)
        {
            rejectReason = null;
            int required = Mathf.Max(1, requiredShotCount);

            CompChangeableProjectile changeableProjectile = GetChangeableProjectileComp();
            if (changeableProjectile != null && changeableProjectile.loadedCount < required)
            {
                rejectReason = changeableProjectile.loadedCount <= 0
                    ? "SRA_RemoteArtillery_NoAmmo".Translate(parent.LabelCap)
                    : "SRA_RemoteArtillery_NotEnoughAmmo".Translate(parent.LabelCap, changeableProjectile.loadedCount, required);
                return false;
            }

            CompRefuelable refuelable = GetRefuelableAmmoComp();
            if (refuelable != null)
            {
                float requiredFuel = required * RemoteFuelPerFakeShot;
                if (refuelable.Fuel < requiredFuel)
                {
                    rejectReason = refuelable.Fuel <= 0f
                        ? "SRA_RemoteArtillery_NoFuelAmmo".Translate(parent.LabelCap)
                        : "SRA_RemoteArtillery_NotEnoughFuelAmmo".Translate(parent.LabelCap, FormatFuel(refuelable.Fuel), FormatFuel(requiredFuel));
                    return false;
                }
            }

            return true;
        }

        private CompChangeableProjectile GetChangeableProjectileComp()
        {
            Building_TurretGunHasSpeed turret = parent as Building_TurretGunHasSpeed;
            return turret?.gun?.TryGetComp<CompChangeableProjectile>() ?? parent?.TryGetComp<CompChangeableProjectile>();
        }

        private CompRefuelable GetRefuelableAmmoComp()
        {
            Building_TurretGunHasSpeed turret = parent as Building_TurretGunHasSpeed;
            CompRefuelable refuelable = turret?.refuelableComp ?? parent?.TryGetComp<CompRefuelable>();
            if (refuelable == null || refuelable.Props == null || !refuelable.Props.consumeFuelOnlyWhenUsed)
            {
                return null;
            }

            return refuelable;
        }

        private int GetRequiredRemoteShotCount()
        {
            return GetRemoteBurstShotCount(ResolvedWorldObjectDef);
        }

        private static string FormatFuel(float fuel)
        {
            return fuel.ToString("0.#");
        }

        private static int GetRemoteBurstShotCount(WorldObjectDef worldObjectDef)
        {
            return Mathf.Max(1, worldObjectDef?.GetModExtension<DefModExtension_GlobalAttackDeviceParams>()?.remoteBurstShotCount ?? 1);
        }

        public bool TryStartRemoteFire(Map targetMap, IntVec3 targetCell, out string rejectReason)
        {
            rejectReason = null;
            if (targetMap == null || !targetCell.IsValid || !targetCell.InBounds(targetMap))
            {
                rejectReason = "SRA_RemoteArtillery_InvalidTarget".Translate();
                return false;
            }

            if (!CanDispatchTo(targetMap, out rejectReason))
            {
                return false;
            }

            WorldObjectDef worldObjectDef = ResolvedWorldObjectDef;
            ThingDef payloadDef = ResolvedPayloadThingDef;
            if (worldObjectDef == null || payloadDef == null)
            {
                rejectReason = "SRA_RemoteArtillery_MissingDefinition".Translate(parent.def.LabelCap);
                return false;
            }

            isFiringInterMap = true;
            interMapTargetTile = targetMap.Tile;
            interMapTargetMapId = targetMap.uniqueID;
            interMapTargetCell = targetCell;
            interMapWorldObjectDef = worldObjectDef;
            interMapPayloadThingDef = payloadDef;
            interMapVisualBurstStarted = false;
            cooldownTicksLeft = Mathf.Max(0, Props.cooldownSeconds * TicksPerSecond);

            if (parent is Building_TurretGunHasSpeed turret)
            {
                turret.SRA_ClearRemoteArtilleryTarget();
            }

            RemoteArtilleryUtility.InvalidateCache();
            return true;
        }

        public void TickInterMapFireForTurret(Building_TurretGunHasSpeed turret)
        {
            if (!isFiringInterMap || turret == null)
            {
                return;
            }

            if (!HasValidInterMapDestination())
            {
                Log.ErrorOnce($"SRALib: Turret {parent.def.defName} has an invalid inter-map destination.", parent.thingIDNumber);
                ResetInterMapState(turret);
                return;
            }

            float destinationRotation = GetInterMapDestinationRotation(turret);
            turret.SRA_RotateRemoteArtilleryTowards(destinationRotation);
            if (!IsAimedAt(turret, destinationRotation))
            {
                return;
            }

            if (interMapVisualBurstStarted)
            {
                if (turret.SRA_RemoteArtilleryVisualBurstFinished)
                {
                    TryLaunchInterMapAttack(turret);
                }

                return;
            }

            if (!HasRequiredAmmoForNextFakeShot(out string ammoRejectReason))
            {
                Messages.Message(ammoRejectReason, parent, MessageTypeDefOf.RejectInput, false);
                ResetInterMapState(turret);
                return;
            }

            if (!turret.SRA_CanBeginRemoteArtilleryBurst)
            {
                return;
            }

            LocalTargetInfo fakeTarget = GetFakeTarget(turret);
            if (!fakeTarget.IsValid || !turret.SRA_TryBeginRemoteArtilleryBurst(fakeTarget))
            {
                Log.WarningOnce($"SRALib: Turret {parent.def.defName} could not play an inter-map fake burst. Launching payload without additional local visual bursts.",
                    ("SRA_RemoteArtillery_FakeBurstFail_" + parent.thingIDNumber).GetHashCode());
                TryLaunchInterMapAttack(turret);
                return;
            }

            interMapVisualBurstStarted = true;
        }

        private void TryLaunchInterMapAttack(Building_TurretGunHasSpeed turret)
        {
            if (interMapWorldObjectDef == null || interMapPayloadThingDef == null)
            {
                Log.Error($"SRALib: {parent.def.defName} tried to launch a global attack with missing world object or payload def.");
                ResetInterMapState(turret);
                return;
            }

            if (!(WorldObjectMaker.MakeWorldObject(interMapWorldObjectDef) is WorldObject_GlobalAttackDevice shell))
            {
                Log.Error($"SRALib: WorldObjectDef '{interMapWorldObjectDef.defName}' must use SRA.WorldObject_GlobalAttackDevice or a subclass.");
                ResetInterMapState(turret);
                return;
            }

            shell.startTile = turret.Map.Tile;
            shell.destinationTile = interMapTargetTile;
            shell.destinationMapId = interMapTargetMapId;
            shell.destinationCell = interMapTargetCell;
            shell.payloadThingDef = interMapPayloadThingDef;
            shell.instigator = turret;
            shell.Tile = turret.Map.Tile;
            Find.WorldObjects.Add(shell);
            ResetInterMapState(turret);
        }

        private void ResetInterMapState(Building_TurretGunHasSpeed turret)
        {
            isFiringInterMap = false;
            interMapTargetTile = PlanetTile.Invalid;
            interMapTargetMapId = -1;
            interMapTargetCell = IntVec3.Invalid;
            interMapWorldObjectDef = null;
            interMapPayloadThingDef = null;
            interMapVisualBurstStarted = false;
            turret?.SRA_ClearRemoteArtilleryTarget();
            RemoteArtilleryUtility.InvalidateCache();
        }

        private bool HasValidInterMapDestination()
        {
            if (ResolveInterMapTargetMap() != null)
            {
                return true;
            }

            return GlobalAttackMapLabelUtility.IsValidWorldTile(interMapTargetTile);
        }

        private Map ResolveInterMapTargetMap()
        {
            if (interMapTargetMapId >= 0)
            {
                Map mapById = Find.Maps.Find(map => map.uniqueID == interMapTargetMapId);
                if (mapById != null)
                {
                    return mapById;
                }
            }

            if (!GlobalAttackMapLabelUtility.IsValidWorldTile(interMapTargetTile))
            {
                return null;
            }

            return Find.Maps.Find(map => map.Tile == interMapTargetTile);
        }

        private float GetInterMapDestinationRotation(Building_TurretGunHasSpeed turret)
        {
            PlanetTile targetTile = interMapTargetTile;
            Map targetMap = ResolveInterMapTargetMap();
            if (targetMap != null)
            {
                targetTile = targetMap.Tile;
            }

            PlanetTile startTile = turret.Map != null ? turret.Map.Tile : PlanetTile.Invalid;
            if (!GlobalAttackMapLabelUtility.IsValidWorldTile(startTile) ||
                !GlobalAttackMapLabelUtility.IsValidWorldTile(targetTile) ||
                startTile == targetTile)
            {
                return turret.curAngle;
            }

            return Find.WorldGrid.GetHeadingFromTo(startTile, targetTile);
        }

        private static bool IsAimedAt(Building_TurretGunHasSpeed turret, float targetAngle)
        {
            float angleDiff = Mathf.Abs(Mathf.DeltaAngle(turret.curAngle, targetAngle));
            return angleDiff <= Mathf.Max(0.1f, turret.rotateSpeed * 1.5f);
        }

        private static LocalTargetInfo GetFakeTarget(Building_TurretGunHasSpeed turret)
        {
            if (turret.Map == null || turret.AttackVerb == null)
            {
                return LocalTargetInfo.Invalid;
            }

            Vector3 direction = turret.turretOrientation.normalized;
            float minDistance = Mathf.Max(1f, turret.AttackVerb.verbProps.minRange + 1f);
            float fakeDistance = Mathf.Max(minDistance, Mathf.Min(FakeTargetMaxDistance, turret.AttackVerb.EffectiveRange));
            IntVec3 fakeCell = (turret.Position.ToVector3Shifted() + direction * fakeDistance).ToIntVec3();
            return fakeCell.IsValid ? new LocalTargetInfo(fakeCell) : LocalTargetInfo.Invalid;
        }
    }
}
