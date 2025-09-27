using HarmonyLib;
using RimWorld;
using System;
using UnityEngine;
using Verse;

namespace SRA
{
    public class CompProperties_WeaponRenderDynamic : CompProperties
    {
        public CompProperties_WeaponRenderDynamic()
        {
            this.compClass = typeof(Comp_WeaponRenderDynamic);
        }
        public string TexturePath;
        public int totalFrames;
        public int ticksPerFrame;
        public Vector2 DrawSize = Vector2.one;
        public Vector3 Offset = Vector3.zero;
    }
    public class Comp_WeaponRenderDynamic : ThingComp
    {
        private CompProperties_WeaponRenderDynamic Props
        {
            get
            {
                return (CompProperties_WeaponRenderDynamic)this.props;
            }
        }
        public override void PostDraw()
        {
            Matrix4x4 matrix = default(Matrix4x4);
            Vector3 vector = this.parent.DrawPos + new Vector3(0f, 0.1f, 0f) + this.parent.Graphic.DrawOffset(this.parent.Rotation);
            Vector3 vector2 = new Vector3(this.parent.Graphic.drawSize.x, 1f, this.parent.Graphic.drawSize.y);
            matrix.SetTRS(vector, Quaternion.AngleAxis(this.AngleOnGround, Vector3.up), vector2);
            this.PostDrawExtraGlower(this.DefaultMesh, matrix);
        }
        private float AngleOnGround
        {
            get
            {
                return this.DrawAngle(this.parent.DrawPos, this.parent.def, this.parent);
            }
        }

        public float DrawAngle(Vector3 loc, ThingDef thingDef, Thing thing)
        {
            float result = 0f;
            float? rotInRack = this.GetRotInRack(thing, thingDef, loc.ToIntVec3());
            if (rotInRack != null)
            {
                result = rotInRack.Value;
            }
            else
            {
                if (thing != null)
                {
                    result = -this.parent.def.graphicData.onGroundRandomRotateAngle + (float)(thing.thingIDNumber * 542) % (this.parent.def.graphicData.onGroundRandomRotateAngle * 2f);
                }
            }
            return result;
        }
        private float? GetRotInRack(Thing thing, ThingDef thingDef, IntVec3 loc)
        {
            float? result;
            if (thing == null || !thingDef.IsWeapon || !thing.Spawned || !loc.InBounds(thing.Map) || loc.GetEdifice(thing.Map) == null || loc.GetItemCount(thing.Map) < 2)
            {
                result = null;
            }
            else
            {
                if (thingDef.rotateInShelves)
                {
                    result = new float?(-90f);
                }
                else
                {
                    result = new float?(0f);
                }
            }
            return result;
        }
        public void PostDrawExtraGlower(Mesh mesh, Matrix4x4 matrix)
        {
            int num = Find.TickManager.TicksGame / this.Props.ticksPerFrame % this.Props.totalFrames;
            Vector2 vector = new Vector2(1f / (float)this.Props.totalFrames, 1f);
            Vector2 mainTextureOffset = new Vector2((float)num * vector.x, 0f);
            Material getMaterial = this.GetMaterial;
            getMaterial.mainTextureOffset = mainTextureOffset;
            getMaterial.mainTextureScale = vector;
            getMaterial.shader = ShaderTypeDefOf.MoteGlow.Shader;
            Graphics.DrawMesh(mesh, matrix, getMaterial, 0);
        }
        private Material GetMaterial
        {
            get
            {
                Material materialS;
                if (this.MaterialS != null)
                {
                    materialS = this.MaterialS;
                }
                else
                {
                    this.MaterialS = MaterialPool.MatFrom(this.Props.TexturePath, ShaderTypeDefOf.MoteGlow.Shader);
                    materialS = this.MaterialS;
                }
                return materialS;
            }
        }
        public override void PostExposeData()
        {
            Scribe_Values.Look<Color>(ref this.Camocolor, "Camocolor", default(Color), false);
            base.PostExposeData();
        }
        private Material MaterialS;
        private readonly Mesh DefaultMesh = MeshPool.plane10;
        public Color Camocolor = Color.white;
    }
    [StaticConstructorOnStartup]
    public class DrawWeaponExtraEquipped
    {
        public static void DrawExtraMatStatic(Thing eq, Vector3 drawLoc, float aimAngle)
        {
            string texPath = eq.def.graphicData.texPath;
            try
            {
                float num = aimAngle - 90f;
                Mesh mesh;
                if (aimAngle > 20f && aimAngle < 160f)
                {
                    mesh = MeshPool.plane10;
                    num += eq.def.equippedAngleOffset;
                }
                else
                {
                    if (aimAngle > 200f && aimAngle < 340f)
                    {
                        mesh = MeshPool.plane10Flip;
                        num -= 180f;
                        num -= eq.def.equippedAngleOffset;
                    }
                    else
                    {
                        mesh = MeshPool.plane10;
                        num += eq.def.equippedAngleOffset;
                    }
                }
                num %= 360f;
                CompEquippable compEquippable = eq.TryGetComp<CompEquippable>();
                if (compEquippable != null)
                {
                    Vector3 vector;
                    float num2;
                    EquipmentUtility.Recoil(eq.def, EquipmentUtility.GetRecoilVerb(compEquippable.AllVerbs), out vector, out num2, aimAngle);
                    drawLoc += vector;
                    num += num2;
                }
                Graphic_StackCount graphic_StackCount = eq.Graphic as Graphic_StackCount;
                Material material;
                if (graphic_StackCount != null)
                {
                    material = graphic_StackCount.SubGraphicForStackCount(1, eq.def).MatSingleFor(eq);
                }
                else
                {
                    material = eq.Graphic.MatSingleFor(eq);
                }
                Vector3 vector2 = new Vector3(eq.Graphic.drawSize.x, 0f, eq.Graphic.drawSize.y);
                Matrix4x4 matrix4x = Matrix4x4.TRS(drawLoc, Quaternion.AngleAxis(num, Vector3.up), vector2);
                Graphics.DrawMesh(mesh, matrix4x, material, 0);
                Comp_WeaponRenderDynamic comp_WeaponRenderDynamic = eq.TryGetComp<Comp_WeaponRenderDynamic>();
                if (comp_WeaponRenderDynamic != null)
                {
                    comp_WeaponRenderDynamic.PostDrawExtraGlower(mesh, matrix4x);
                }
                //Comp_WeaponRenderStatic comp_WeaponRenderStatic = eq.TryGetComp<Comp_WeaponRenderStatic>();
                //bool flag6 = comp_WeaponRenderStatic != null;
                //if (flag6)
                //{
                //    comp_WeaponRenderStatic.PostDrawExtraGlower(mesh, matrix4x);
                //}
            }
            catch (Exception)
            {
            }
        }
        [HarmonyPatch(typeof(PawnRenderUtility), "DrawEquipmentAiming")]
        private class HarmonyPatch_PawnWeaponRenderer
        {
            // Token: 0x0600053B RID: 1339 RVA: 0x00026300 File Offset: 0x00024500
            public static bool Prefix(Thing eq, Vector3 drawLoc, float aimAngle)
            {
                bool flag = eq != null && eq.TryGetComp<Comp_WeaponRenderDynamic>() != null && eq.TryGetComp<CompEquippable>().ParentHolder != null;
                bool result;
                if (flag)
                {
                    DrawWeaponExtraEquipped.DrawExtraMatStatic(eq, drawLoc, aimAngle);
                    result = false;
                }
                else
                {
                    result = true;
                }
                return result;
            }
        }
    }
}