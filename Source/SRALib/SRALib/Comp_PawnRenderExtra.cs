using System;
using System.Reflection.Emit;
using System.Runtime.Remoting.Messaging;
using RimWorld;
using UnityEngine;
using Verse;

namespace SRA
{
    public class CompProperties_PawnRenderExtra : CompProperties
    {
        public CompProperties_PawnRenderExtra()
        {
            this.compClass = typeof(Comp_PawnRenderExtra);
        }

        public string path;

        public Vector3 size;

        public Color colorAlly;

        public Color colorEnemy;
        
        public ShaderTypeDef shader;

        public DrawData drawData;
    }
    [StaticConstructorOnStartup]
    public class Comp_PawnRenderExtra : ThingComp
    {
        public CompProperties_PawnRenderExtra Props
        {
            get
            {
                return this.props as CompProperties_PawnRenderExtra;
            }
        }

        private Pawn ParentPawn
        {
            get
            {
                return this.parent as Pawn;
            }
        }

        public override void PostDraw()
        {
            base.PostDraw();
            if (!this.ParentPawn.Dead && !this.ParentPawn.Downed && this.ParentPawn.CurJobDef != JobDefOf.MechCharge && this.ParentPawn.CurJobDef != JobDefOf.SelfShutdown)
            {
                this.DrawPawnRenderExtra();
            }
        }

        public void DrawPawnRenderExtra()
        {
            Vector3 pos = this.ParentPawn.DrawPos;
            if (this.ParentPawn.Faction == Faction.OfPlayer || !this.ParentPawn.Faction.HostileTo(Faction.OfPlayer))
            {
                this.color = this.Props.colorAlly;
            }
            else
            {
                this.color = this.Props.colorEnemy;
            }
            string graphic = this.GetPawnRenderExtra();
            Vector3 offset = GetOffsetByRot();
            float layer = GetLayerByRot();
            pos.y = AltitudeLayer.Pawn.AltitudeFor(layer);

            Matrix4x4 matrix = default(Matrix4x4);
            matrix.SetTRS(pos + offset, Quaternion.AngleAxis(0f, Vector3.up), this.Props.size);
            Material material = MaterialPool.MatFrom(graphic, this.Props.shader.Shader, this.color);
            Graphics.DrawMesh(MeshPool.plane10, matrix, material, (int)layer);
        }

        public Vector3 GetOffsetByRot()
        {
            Vector3 result;
            if (this.Props.drawData != null)
            {
                result = this.Props.drawData.OffsetForRot(this.ParentPawn.Rotation);
            }
            else
            {
                result = Vector3.zero;
            }
            return result;
        }
        public float GetLayerByRot()
        {
            float result;
            if (this.Props.drawData != null)
            {
                result = this.Props.drawData.LayerForRot(this.ParentPawn.Rotation, 0);
            }
            else
            {
                result = 0;
            }
            return result;
        }

        public string GetPawnRenderExtra()
        {
            if (this.ParentPawn.Rotation.AsInt == 0)
            {
                return this.Props.path + "_north";
            }
            if (this.ParentPawn.Rotation.AsInt == 1)
            {
                return this.Props.path + "_east";
            }
            if (this.ParentPawn.Rotation.AsInt == 2)
            {
                return this.Props.path + "_south";
            }
            if (this.ParentPawn.Rotation.AsInt == 3)
            {
                return this.Props.path + "_west";
            }
            return null;
        }
        public Color color;
    }
}
