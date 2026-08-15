using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace SRA
{
    public class DefModExtension_GlobalAttackDeviceParams : DefModExtension
    {
        // 世界地图飞行速度。实际每 tick 进度会按球面距离归一化，值越大抵达越快。
        // 默认值与原版 TravellingTransporters 保持一致，避免近距离目标在首 tick 抵达。
        public float flightSpeed = 0.00025f;

        // 远程炮击的一轮本地 fake burst 发数。只影响远程炮击，不改变普通射击的 burstShotCount。
        public int remoteBurstShotCount = 1;

    }

    public class WorldObject_GlobalAttackDevice : WorldObject
    {
        public PlanetTile startTile = PlanetTile.Invalid;
        public PlanetTile destinationTile = PlanetTile.Invalid;
        public int destinationMapId = -1;
        public IntVec3 destinationCell = IntVec3.Invalid;
        public Thing instigator;
        public ThingDef payloadThingDef;

        private float traveledPct;
        private bool arrived;
        private float traveledPctStepPerTickCached = -1f;

        private DefModExtension_GlobalAttackDeviceParams ExtProps => def.GetModExtension<DefModExtension_GlobalAttackDeviceParams>();

        public override Vector3 DrawPos
        {
            get
            {
                if (!GlobalAttackMapLabelUtility.IsValidWorldTile(startTile) && !GlobalAttackMapLabelUtility.IsValidWorldTile(destinationTile))
                {
                    return Vector3.zero;
                }

                Vector3 startVec = GlobalAttackMapLabelUtility.IsValidWorldTile(startTile)
                    ? Find.WorldGrid.GetTileCenter(startTile)
                    : Find.WorldGrid.GetTileCenter(destinationTile);
                Vector3 endVec = GlobalAttackMapLabelUtility.IsValidWorldTile(destinationTile)
                    ? Find.WorldGrid.GetTileCenter(destinationTile)
                    : startVec;
                return Vector3.Slerp(startVec, endVec, traveledPct);
            }
        }

        private float TraveledPctStepPerTick
        {
            get
            {
                if (traveledPctStepPerTickCached >= 0f)
                {
                    return traveledPctStepPerTickCached;
                }

                if (!GlobalAttackMapLabelUtility.IsValidWorldTile(startTile) || !GlobalAttackMapLabelUtility.IsValidWorldTile(destinationTile))
                {
                    traveledPctStepPerTickCached = 1f;
                    return traveledPctStepPerTickCached;
                }

                Vector3 start = Find.WorldGrid.GetTileCenter(startTile);
                Vector3 end = Find.WorldGrid.GetTileCenter(destinationTile);
                if (start == end)
                {
                    traveledPctStepPerTickCached = 1f;
                    return traveledPctStepPerTickCached;
                }

                float sphericalDistance = GenMath.SphericalDistance(start.normalized, end.normalized);
                if (sphericalDistance <= 0f)
                {
                    traveledPctStepPerTickCached = 1f;
                    return traveledPctStepPerTickCached;
                }

                float speedFactor = ExtProps?.flightSpeed ?? 0.00025f;
                traveledPctStepPerTickCached = speedFactor / sphericalDistance;
                return traveledPctStepPerTickCached;
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref startTile, "startTile", PlanetTile.Invalid);
            Scribe_Values.Look(ref destinationTile, "destinationTile", PlanetTile.Invalid);
            Scribe_Values.Look(ref destinationMapId, "destinationMapId", -1);
            Scribe_Values.Look(ref destinationCell, "destinationCell");
            Scribe_References.Look(ref instigator, "instigator");
            Scribe_Defs.Look(ref payloadThingDef, "payloadThingDef");
            Scribe_Values.Look(ref traveledPct, "traveledPct", 0f);
            Scribe_Values.Look(ref arrived, "arrived", false);
        }

        protected override void Tick()
        {
            base.Tick();
            traveledPct += TraveledPctStepPerTick;
            if (traveledPct >= 1f)
            {
                traveledPct = 1f;
                Arrived();
            }
        }

        private void Arrived()
        {
            if (arrived)
            {
                return;
            }

            arrived = true;
            Map targetMap = ResolveTargetMap();
            if (targetMap == null || !destinationCell.IsValid || !destinationCell.InBounds(targetMap))
            {
                Find.WorldObjects.Remove(this);
                return;
            }

            if (payloadThingDef == null)
            {
                Log.Error($"SRALib: {def.defName} arrived but has no payload ThingDef.");
                Find.WorldObjects.Remove(this);
                return;
            }

            Thing spawned = GenSpawn.Spawn(payloadThingDef, destinationCell, targetMap, WipeMode.Vanish);
            if (spawned is OrbitalStrike orbitalStrike)
            {
                orbitalStrike.instigator = instigator;
            }

            Messages.Message("SRA_RemoteArtillery_GlobalAttackArrived".Translate(spawned.LabelCap, GlobalAttackMapLabelUtility.GetMapLabel(targetMap)), MessageTypeDefOf.PositiveEvent, true);
            Find.WorldObjects.Remove(this);
        }

        private Map ResolveTargetMap()
        {
            if (destinationMapId >= 0)
            {
                Map mapById = Find.Maps.Find(map => map.uniqueID == destinationMapId);
                if (mapById != null)
                {
                    return mapById;
                }
            }

            if (!GlobalAttackMapLabelUtility.IsValidWorldTile(destinationTile))
            {
                return null;
            }

            return Find.Maps.Find(map => map.Tile == destinationTile);
        }
    }
}
