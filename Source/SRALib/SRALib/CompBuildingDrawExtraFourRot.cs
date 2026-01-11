using System;
using RimWorld;
using UnityEngine;
using Verse;

namespace SRA
{
    public class CompProperties_BuildingDrawExtraFourRot : CompProperties
    {
        public CompProperties_BuildingDrawExtraFourRot()
        {
            this.compClass = typeof(CompBuildingDrawExtraFourRot);
        }
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
                if (this._powerComp == null)
                {
                    this._powerComp = this.parent.GetComp<CompPowerTrader>();
                }
                return this._powerComp;
            }
        }
        public override void PostDraw()
        {
            base.PostDraw();
            if (this.PowerComp == null || this.PowerComp.PowerOn)
            {
                Mesh mesh = this.Properties.graphicDataExtra.Graphic.MeshAt(this.parent.Rotation);
                Graphics.DrawMesh(mesh, this.parent.DrawPos + new Vector3(0f, 1f, 0f) + this.Properties.graphicDataExtra.DrawOffsetForRot(this.parent.Rotation), Quaternion.AngleAxis(0f, Vector3.up), this.Properties.graphicDataExtra.Graphic.MatAt(this.parent.Rotation, null), 0);
            }
        }
        private CompPowerTrader _powerComp;
    }
}
