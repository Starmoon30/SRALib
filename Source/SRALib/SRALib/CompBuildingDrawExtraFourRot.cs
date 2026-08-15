using System;
using RimWorld;
using UnityEngine;
using Verse;

namespace SRA
{
    /// <summary>
    /// Draws an XML-defined extra graphic over a building, matching the building rotation.
    /// Useful for lights, overlays, and other visual layers that should not replace the base graphic.
    /// </summary>
    public class CompProperties_BuildingDrawExtraFourRot : CompProperties
    {
        public CompProperties_BuildingDrawExtraFourRot()
        {
            this.compClass = typeof(CompBuildingDrawExtraFourRot);
        }

        public override void PostLoadSpecial(ThingDef parent)
        {
            base.PostLoadSpecial(parent);

            // ThingComp.PostDraw only runs reliably for real-time drawers. MapMeshOnly buildings
            // bake their base graphic into the map mesh and can skip this comp entirely.
            if (parent.drawerType == DrawerType.None || parent.drawerType == DrawerType.MapMeshOnly)
            {
                parent.drawerType = DrawerType.MapMeshAndRealTime;
            }
        }

        public override void ResolveReferences(ThingDef parentDef)
        {
            base.ResolveReferences(parentDef);

            // This GraphicData is nested inside a comp, not ThingDef.graphicData, so vanilla does
            // not initialize it through the normal ThingDef graphic resolution path.
            this.graphicDataExtra?.ResolveReferencesSpecial();
        }

        /// <summary>
        /// Extra overlay graphic. Uses the same XML shape as ThingDef.graphicData.
        /// </summary>
        public GraphicData graphicDataExtra;
    }

    public class CompBuildingDrawExtraFourRot : ThingComp
    {
        public CompProperties_BuildingDrawExtraFourRot Properties
        {
            get
            {
                return (CompProperties_BuildingDrawExtraFourRot)this.props;
            }
        }
        private CompPowerTrader PowerComp
        {
            get
            {
                // Cache a missing power comp too; powerless buildings should not call GetComp every draw.
                if (!this.powerCompResolved)
                {
                    this._powerComp = this.parent.GetComp<CompPowerTrader>();
                    this.powerCompResolved = true;
                }
                return this._powerComp;
            }
        }
        public override void PostDraw()
        {
            base.PostDraw();
            GraphicData graphicDataExtra = this.Properties.graphicDataExtra;

            // Missing graphicDataExtra disables the visual layer instead of throwing during drawing.
            if (graphicDataExtra != null && this.ShouldDrawExtra())
            {
                Graphic graphic = graphicDataExtra.Graphic;
                Mesh mesh = graphic.MeshAt(this.parent.Rotation);
                Graphics.DrawMesh(mesh, this.parent.DrawPos + new Vector3(0f, 1f, 0f) + graphicDataExtra.DrawOffsetForRot(this.parent.Rotation), Quaternion.AngleAxis(0f, Vector3.up), graphic.MatAt(this.parent.Rotation, null), 0);
            }
        }

        private bool ShouldDrawExtra()
        {
            CompPowerTrader powerComp = this.PowerComp;

            // Only actual power consumers should hide the overlay when unpowered. Transmitters,
            // producers, and zero-consumption comps are treated as not requiring power.
            return powerComp == null || !RequiresPower(powerComp) || powerComp.PowerOn;
        }

        private static bool RequiresPower(CompPowerTrader powerComp)
        {
            // PowerConsumption is the public accessor for the private basePowerConsumption field.
            return powerComp.Props != null && powerComp.Props.PowerConsumption > 0f;
        }

        private CompPowerTrader _powerComp;
        private bool powerCompResolved;
    }
}
