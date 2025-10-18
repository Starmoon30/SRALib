using HarmonyLib;
using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using static HarmonyLib.Code;
using static UnityEngine.Scripting.GarbageCollector;

namespace SRA
{
    public class CompProperties_VehicleWeapon : CompProperties
    {
        // Token: 0x06000183 RID: 387 RVA: 0x00009D4D File Offset: 0x00007F4D
        public CompProperties_VehicleWeapon()
        {
            this.compClass = typeof(CompVehicleWeapon);
        }

        // Token: 0x040000C2 RID: 194
        public DrawData drawData;

        // Token: 0x040000C3 RID: 195
        public bool turretRotationFollowPawn = false;

        // Token: 0x040000C4 RID: 196
        public bool horizontalFlip = false;

        // Token: 0x040000C5 RID: 197
        public float rotationSmoothTime = 0.12f;

        // Token: 0x040000C6 RID: 198
        public ThingDef defaultWeapon;

        // Token: 0x040000C7 RID: 199
        public float drawSize = 0f;
    }
    // Token: 0x02000072 RID: 114
    public class CompVehicleWeapon : ThingComp
    {
        // Token: 0x17000048 RID: 72
        // (get) Token: 0x06000178 RID: 376 RVA: 0x0000994C File Offset: 0x00007B4C
        public float CurrentAngle
        {
            get
            {
                return this._currentAngle;
            }
        }

        // Token: 0x17000049 RID: 73
        // (get) Token: 0x06000179 RID: 377 RVA: 0x00009954 File Offset: 0x00007B54
        public float TargetAngle
        {
            get
            {
                Stance_Busy stance_Busy = this.pawn.stances.curStance as Stance_Busy;
                bool flag = stance_Busy != null && stance_Busy.focusTarg.IsValid;
                float result;
                if (flag)
                {
                    bool hasThing = stance_Busy.focusTarg.HasThing;
                    Vector3 vector;
                    if (hasThing)
                    {
                        vector = stance_Busy.focusTarg.Thing.DrawPos;
                    }
                    else
                    {
                        vector = stance_Busy.focusTarg.Cell.ToVector3Shifted();
                    }
                    result = (vector - this.pawn.DrawPos).AngleFlat();
                }
                else
                {
                    result = this._turretFollowingAngle;
                }
                return result;
            }
        }

        // Token: 0x1700004A RID: 74
        // (get) Token: 0x0600017A RID: 378 RVA: 0x000099F0 File Offset: 0x00007BF0
        public CompProperties_VehicleWeapon Props
        {
            get
            {
                return (CompProperties_VehicleWeapon)this.props;
            }
        }

        // Token: 0x0600017B RID: 379 RVA: 0x00009A10 File Offset: 0x00007C10
        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            this.pawn = (this.parent as Pawn);
            if (this.pawn == null)
            {
                Log.Error("The CompVehicleWeapon is set on a non-pawn object.");
            }
            else
            {
                if (this.pawn.equipment.Primary == null && this.Props.defaultWeapon != null)
                {
                    Thing thing = ThingMaker.MakeThing(this.Props.defaultWeapon, null);
                    this.pawn.equipment.AddEquipment((ThingWithComps)thing);
                }
                if (!CompVehicleWeapon.cachedVehicles.ContainsKey(((Pawn)this.parent).Drawer.renderer))
                    CompVehicleWeapon.cachedVehicles.Add(((Pawn)this.parent).Drawer.renderer, this);

                if (!CompVehicleWeapon.cachedPawns.ContainsKey(this))
                    CompVehicleWeapon.cachedPawns.Add(this, (Pawn)this.parent);

                if (!CompVehicleWeapon.cachedVehicldesPawns.ContainsKey((Pawn)this.parent))
                    CompVehicleWeapon.cachedVehicldesPawns.Add((Pawn)this.parent, this);
            }
        }

        // Token: 0x0600017C RID: 380 RVA: 0x00009AF8 File Offset: 0x00007CF8
        public override void PostDeSpawn(Map map, DestroyMode mode = DestroyMode.Vanish)
        {
            base.PostDeSpawn(map, mode);
            CompVehicleWeapon.cachedVehicles.Remove(((Pawn)this.parent).Drawer.renderer);
            CompVehicleWeapon.cachedPawns.Remove(this);
            CompVehicleWeapon.cachedVehicldesPawns.Remove((Pawn)this.parent);
        }

        // Token: 0x0600017D RID: 381 RVA: 0x00009B50 File Offset: 0x00007D50
        public override void PostDestroy(DestroyMode mode, Map previousMap)
        {
            base.PostDestroy(mode, previousMap);
            CompVehicleWeapon.cachedVehicles.Remove(((Pawn)this.parent).Drawer.renderer);
            CompVehicleWeapon.cachedPawns.Remove(this);
            CompVehicleWeapon.cachedVehicldesPawns.Remove((Pawn)this.parent);
        }

        // Token: 0x0600017E RID: 382 RVA: 0x00009BAC File Offset: 0x00007DAC
        public override void CompTick()
        {
            base.CompTick();
            bool flag = this.pawn == null;
            if (!flag)
            {
                bool turretRotationFollowPawn = this.Props.turretRotationFollowPawn;
                if (turretRotationFollowPawn)
                {
                    this._turretFollowingAngle = this.pawn.Rotation.AsAngle + this.Props.drawData.RotationOffsetForRot(this.pawn.Rotation);
                }
                else
                {
                    this._turretFollowingAngle += this._turretAnglePerFrame;
                }
                bool flag2 = this._lastRotation != this.pawn.Rotation;
                if (flag2)
                {
                    this._lastRotation = this.pawn.Rotation;
                    this._currentAngle = this._turretFollowingAngle;
                }
                this._currentAngle = Mathf.SmoothDampAngle(this._currentAngle, this.TargetAngle, ref this._rotationSpeed, this.Props.rotationSmoothTime);
            }
        }

        // Token: 0x0600017F RID: 383 RVA: 0x00009C91 File Offset: 0x00007E91
        public override void CompTickRare()
        {
            base.CompTickRare();
            this._turretAnglePerFrame = Rand.Range(-0.5f, 0.5f);
        }

        // Token: 0x06000180 RID: 384 RVA: 0x00009CB0 File Offset: 0x00007EB0
        public Vector3 GetOffsetByRot()
        {
            bool flag = this.Props.drawData != null;
            Vector3 result;
            if (flag)
            {
                result = this.Props.drawData.OffsetForRot(this.pawn.Rotation);
            }
            else
            {
                result = Vector3.zero;
            }
            return result;
        }

        // Token: 0x040000B9 RID: 185
        public Pawn pawn;

        // Token: 0x040000BA RID: 186
        private float _turretFollowingAngle = 0f;

        // Token: 0x040000BB RID: 187
        private float _turretAnglePerFrame = 0.1f;

        // Token: 0x040000BC RID: 188
        private float _currentAngle = 0f;

        // Token: 0x040000BD RID: 189
        private float _rotationSpeed = 0f;

        // Token: 0x040000BE RID: 190
        private Rot4 _lastRotation;

        // Token: 0x040000BF RID: 191
        public static readonly Dictionary<PawnRenderer, CompVehicleWeapon> cachedVehicles = new Dictionary<PawnRenderer, CompVehicleWeapon>();

        // Token: 0x040000C0 RID: 192
        public static readonly Dictionary<CompVehicleWeapon, Pawn> cachedPawns = new Dictionary<CompVehicleWeapon, Pawn>();

        // Token: 0x040000C1 RID: 193
        public static readonly Dictionary<Pawn, CompVehicleWeapon> cachedVehicldesPawns = new Dictionary<Pawn, CompVehicleWeapon>();
    }

    [HarmonyPatch(typeof(PawnRenderUtility), "DrawEquipmentAndApparelExtras")]
    internal static class Patch_DrawVehicleTurret
    {
        // Token: 0x060000BA RID: 186 RVA: 0x000051AC File Offset: 0x000033AC
        [HarmonyPriority(600)]
        public static bool Prefix(Pawn pawn, Vector3 drawPos, Rot4 facing, PawnRenderFlags flags)
        {
            CompVehicleWeapon compVehicleWeapon = CompVehicleWeapon.cachedVehicldesPawns.TryGetValue(pawn, null);
            bool flag = compVehicleWeapon != null;
            bool result;
            if (flag)
            {
                Pawn pawn2 = (Pawn)compVehicleWeapon.parent;
                bool flag2 = pawn2.equipment != null && pawn2.equipment.Primary != null;
                if (flag2)
                {
                    Patch_DrawVehicleTurret.DrawTuret(pawn2, compVehicleWeapon, pawn2.equipment.Primary);
                }
                result = false;
            }
            else
            {
                result = true;
            }
            return result;
        }

        // Token: 0x060000BB RID: 187 RVA: 0x0000521C File Offset: 0x0000341C
        public static void DrawTuret(Pawn pawn, CompVehicleWeapon compWeapon, Thing equipment)
        {
            float currentAngle = compWeapon.CurrentAngle;
            Vector3 vector = pawn.DrawPos + compWeapon.GetOffsetByRot();
            vector.y += 0.03846154f * compWeapon.Props.drawData.LayerForRot(pawn.Rotation, 1f);
            float num = currentAngle - 90f;
            num += equipment.def.equippedAngleOffset;
            Mesh plane = MeshPool.plane10;
            num %= 360f;
            Vector3 vector2;
            if (compWeapon.Props.drawSize != 0f)
            {
                vector2 = Vector3.one* compWeapon.Props.drawSize;
            }
            else
            {
                vector2 = equipment.Graphic.drawSize;
            }
            Matrix4x4 matrix4x = Matrix4x4.TRS(vector, Quaternion.AngleAxis(num, Vector3.up), new Vector3(vector2.x, 1f, vector2.y));
            Graphic_StackCount graphic_StackCount = equipment.Graphic as Graphic_StackCount;
            Material material = (graphic_StackCount == null) ? equipment.Graphic.MatSingle : graphic_StackCount.SubGraphicForStackCount(1, equipment.def).MatSingle;
            Graphics.DrawMesh(plane, matrix4x, material, 0);
        }
    }
}
