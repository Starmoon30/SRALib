using HarmonyLib;
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
        private const float LayerStep = 0.03846154f;

        public CompProperties_PawnRenderExtra Props
        {
            get
            {
                return this.props as CompProperties_PawnRenderExtra;
            }
        }

        public bool ShouldDrawFor(Pawn pawn)
        {
            if (pawn == null || pawn.Dead || pawn.Downed)
            {
                return false;
            }

            JobDef curJobDef = pawn.CurJobDef;
            return curJobDef != JobDefOf.MechCharge && curJobDef != JobDefOf.SelfShutdown;
        }

        public void DrawFor(Pawn pawn, Vector3 drawPos, Rot4 facing)
        {
            if (!this.ShouldDrawFor(pawn))
            {
                return;
            }

            if (!this.TryResolveGraphic(facing, out string graphicPath, out Mesh mesh))
            {
                return;
            }

            Vector3 pos = drawPos + this.GetOffsetByRot(facing);
            pos.y += LayerStep * this.GetLayerByRot(facing);

            Matrix4x4 matrix = default(Matrix4x4);
            matrix.SetTRS(pos, Quaternion.identity, this.GetScale());
            Material material = MaterialPool.MatFrom(graphicPath, this.GetShader(), this.GetColorFor(pawn));
            Graphics.DrawMesh(mesh, matrix, material, 0);
        }

        private Color GetColorFor(Pawn pawn)
        {
            Faction faction = pawn.Faction;
            if (faction == null || faction == Faction.OfPlayer || !faction.HostileTo(Faction.OfPlayer))
            {
                return this.Props.colorAlly;
            }

            return this.Props.colorEnemy;
        }

        private Shader GetShader()
        {
            return this.Props.shader != null ? this.Props.shader.Shader : ShaderDatabase.Cutout;
        }

        private Vector3 GetScale()
        {
            float x = this.Props.size.x != 0f ? this.Props.size.x : 1f;
            float z = this.Props.size.z != 0f ? this.Props.size.z : (this.Props.size.y != 0f ? this.Props.size.y : x);
            return new Vector3(x, 1f, z);
        }

        public Vector3 GetOffsetByRot(Rot4 facing)
        {
            if (this.Props.drawData != null)
            {
                return this.Props.drawData.OffsetForRot(facing);
            }

            return Vector3.zero;
        }

        public float GetLayerByRot(Rot4 facing)
        {
            if (this.Props.drawData != null)
            {
                return this.Props.drawData.LayerForRot(facing, 0f);
            }

            return 0f;
        }

        private bool TryResolveGraphic(Rot4 facing, out string graphicPath, out Mesh mesh)
        {
            mesh = MeshPool.plane10;
            foreach ((string candidatePath, bool candidateFlip) in this.GetGraphicCandidates(facing))
            {
                if (string.IsNullOrEmpty(candidatePath) || ContentFinder<Texture2D>.Get(candidatePath, false) == null)
                {
                    continue;
                }

                graphicPath = candidatePath;
                mesh = candidateFlip ? MeshPool.plane10Flip : MeshPool.plane10;
                return true;
            }

            graphicPath = null;
            return false;
        }

        private (string path, bool flip)[] GetGraphicCandidates(Rot4 facing)
        {
            switch (facing.AsInt)
            {
                case 0:
                    return new (string, bool)[]
                    {
                        (this.Props.path + "_north", false),
                        (this.Props.path, false)
                    };
                case 1:
                    return new (string, bool)[]
                    {
                        (this.Props.path + "_east", false),
                        (this.Props.path, false)
                    };
                case 2:
                    return new (string, bool)[]
                    {
                        (this.Props.path + "_south", false),
                        (this.Props.path, false)
                    };
                case 3:
                    return new (string, bool)[]
                    {
                        (this.Props.path + "_west", false),
                        (this.Props.path + "_east", true),
                        (this.Props.path, false)
                    };
                default:
                    return new (string, bool)[]
                    {
                        (this.Props.path, false)
                    };
            }
        }
    }

    [HarmonyPatch(typeof(PawnRenderUtility), "DrawEquipmentAndApparelExtras")]
    internal static class Patch_DrawPawnRenderExtra
    {
        [HarmonyPriority(Priority.Low)]
        public static void Postfix(Pawn pawn, Vector3 drawPos, Rot4 facing, PawnRenderFlags flags)
        {
            if (pawn == null || flags.FlagSet(PawnRenderFlags.Invisible))
            {
                return;
            }

            Comp_PawnRenderExtra pawnComp = pawn.GetComp<Comp_PawnRenderExtra>();
            pawnComp?.DrawFor(pawn, drawPos, facing);

            if (pawn.apparel == null || !flags.FlagSet(PawnRenderFlags.Clothes))
            {
                return;
            }

            for (int i = 0; i < pawn.apparel.WornApparelCount; i++)
            {
                Comp_PawnRenderExtra apparelComp = pawn.apparel.WornApparel[i].GetComp<Comp_PawnRenderExtra>();
                apparelComp?.DrawFor(pawn, drawPos, facing);
            }
        }
    }
}
