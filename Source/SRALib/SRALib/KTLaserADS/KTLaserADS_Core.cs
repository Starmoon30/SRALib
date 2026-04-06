using System.Collections.Generic;
using UnityEngine;
using RimWorld;
using Verse;

//近防激光核心逻辑
namespace SRA
{
    //配置数据库
    public class CompProperties_LaserADS : CompProperties
    {
        public string label = "LaserADS";
        public string uiIconPath = "";
        public string uiIconPath_ModeOff = "";
        public string uiIconPath_ModeAir = "";
        public string uiIconPath_ModeGround = "";
        public string uiIconPath_ManualAim = "";
        public string uiIconPath_CancelAim = "";
        public float baseRestAngle = 0f;
        public float turnSpeed = 15f;
        public Vector2 turretOffset = Vector2.zero;
        public string turretTexPath;
        public float turretDrawSize = 2f;
        public int cooldownTicks = 60;
        public SoundDef interceptSound;
        public float interceptAngleTolerance = 3f;
        public int defaultMinDamage = 0;
        public int minDamageStep = 10;
        public IntRange meltingSparkCountRange = new IntRange(1, 2);
        public float meltingSparkAngleSpread = 50f;
        public Vector2 meltingSparkSpeedRange = new Vector2(2f, 5f);
        public Vector2 meltingSparkScaleRange = new Vector2(0.6f, 1.5f);
        public int interceptSparkCount = 40;
        public float interceptSparkAngleSpread = 45f;
        public Vector2 interceptSparkSpeedRange = new Vector2(4f, 12f);
        public Vector2 interceptSparkScaleRange = new Vector2(1.5f, 3.5f);
        public Vector2 laserStartOffset = Vector2.zero;
        public string laserTexPath = "Things/Projectile/ChargeLanceShot";
        public float laserWidth = 0.5f;
        public int laserDurationTicks = 15;
        public float groundRange = 40f;
        public int groundDamageIntervalTicks = 30;
        public float groundDamageAmount = 15f;
        public float groundArmorPenetration = -1f;
        public SoundDef groundShootSound;
        public float groundIgniteSize = 0.5f;
        public float groundFleeDistance = 15f;
        public int groundMaxIrradiationTicks = 300;
        public float groundShatterGearChance = 0f;
        public CompProperties_LaserADS() { this.compClass = typeof(CompLaserADS); }
    }
    //火控工作模式
    public enum ADSMode : byte { Off = 0, AntiAir = 1, AntiGround = 2 }
    [StaticConstructorOnStartup]
    //工作组件
    public partial class CompLaserADS : ThingComp
    {
        public CompProperties_LaserADS Props => (CompProperties_LaserADS)props;
        public ADSMode currentMode = ADSMode.AntiAir;
        public int minInterceptDamage = 0;
        public float curTurretAngle;
        public Thing currentTarget = null;
        public LocalTargetInfo forcedTarget = LocalTargetInfo.Invalid;
        private int cooldownTicksLeft = 0;
        private int searchTickLeft = 0;
        private int lastIrradiationTick = -999;
        private int groundDamageTicksLeft = 0;
        private int currentTargetIrradiationTicks = 0;
        private bool initialized = false;
        private Material turretMat;
        private int lastInterceptTick = -999;
        private Vector3 lastInterceptPos;
        private static readonly Dictionary<string, Material> CachedLaserMats = new Dictionary<string, Material>();
        private static readonly MaterialPropertyBlock laserPropertyBlock = new MaterialPropertyBlock();
        private static readonly int ID_Color = Shader.PropertyToID("_Color");
        private static readonly Color[] LaserSparkColors = new Color[] { new Color(1f, 1f, 1f, 1f), new Color(0.4f, 0.8f, 1f, 1f), new Color(1f, 0.6f, 0.2f, 1f) };
        private CompPowerTrader powerComp;
        private CompBreakdownable breakdownComp;
        //加载初始化
        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            powerComp = this.parent.GetComp<CompPowerTrader>();
            breakdownComp = this.parent.GetComp<CompBreakdownable>();
            LongEventHandler.ExecuteWhenFinished(() =>
            {
                if (!string.IsNullOrEmpty(Props.turretTexPath)) turretMat = MaterialPool.MatFrom(Props.turretTexPath);
                if (!string.IsNullOrEmpty(Props.laserTexPath))
                {
                    if (!CachedLaserMats.TryGetValue(Props.laserTexPath, out Material mat))
                    {
                        Material baseMat = MaterialPool.MatFrom(Props.laserTexPath, ShaderDatabase.MoteGlow);
                        mat = new Material(baseMat) { renderQueue = 3500 };
                        CachedLaserMats[Props.laserTexPath] = mat;
                    }
                }
            });
            if (!initialized)
            {
                curTurretAngle = BaseAngle;
                minInterceptDamage = Props.defaultMinDamage;
                currentMode = ADSMode.AntiAir;
                initialized = true;
            }
            this.parent.Map.GetComponent<MapComponent_LaserADSManager>()?.Register(this);
        }
        //存档读写器
        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref initialized, "initialized", false);
            Scribe_Values.Look(ref currentMode, "currentMode", ADSMode.AntiAir);
            Scribe_Values.Look(ref curTurretAngle, "curTurretAngle", 0f);
            Scribe_Values.Look(ref cooldownTicksLeft, "cooldownTicksLeft", 0);
            Scribe_Values.Look(ref minInterceptDamage, "minInterceptDamage", 0);
            Scribe_Values.Look(ref groundDamageTicksLeft, "groundDamageTicksLeft", 0);
            Scribe_Values.Look(ref currentTargetIrradiationTicks, "currentTargetIrradiationTicks", 0);
            Scribe_TargetInfo.Look(ref forcedTarget, "forcedTarget", LocalTargetInfo.Invalid);
        }
        //工作前提检查
        private bool IsPoweredAndFunctional => (powerComp == null || powerComp.PowerOn) && (breakdownComp == null || !breakdownComp.BrokenDown) && !this.parent.Map.roofGrid.Roofed(this.parent.Position);
        public float BaseAngle => this.parent.Rotation.AsAngle + Props.baseRestAngle;
        public Vector3 GetAbsolutePosition() => this.parent.DrawPos + new Vector3(Props.turretOffset.x, 0, Props.turretOffset.y);
        //手动瞄准控制
        public void ResetTarget()
        {
            currentTarget = null;
            forcedTarget = LocalTargetInfo.Invalid;
            groundDamageTicksLeft = 0;
            currentTargetIrradiationTicks = 0;
        }
        public void SetForcedTarget(LocalTargetInfo targ)
        {
            forcedTarget = targ;
            currentTarget = targ.Thing;
            groundDamageTicksLeft = Props.groundDamageIntervalTicks / 2;
            currentTargetIrradiationTicks = 0;
        }
        //主逻辑帧
        public override void CompTick()
        {
            base.CompTick();
            if (!this.parent.Spawned) return;
            if (currentMode == ADSMode.Off || !IsPoweredAndFunctional)
            {
                ResetTarget();
                RotateTowards(BaseAngle);
                return;
            }
            if (cooldownTicksLeft > 0)
            {
                cooldownTicksLeft--;
                return;
            }
            if (currentTarget == null)
            {
                if (currentMode == ADSMode.AntiGround && forcedTarget.IsValid && forcedTarget.HasThing && !forcedTarget.Thing.Destroyed)
                {
                    currentTarget = forcedTarget.Thing;
                    currentTargetIrradiationTicks = 0;
                }
                else if (--searchTickLeft <= 0)
                {
                    searchTickLeft = 5;
                    if (currentMode == ADSMode.AntiAir) TryFindTarget_AntiAir();
                    else if (currentMode == ADSMode.AntiGround) TryFindTarget_AntiGround();
                }
            }
            if (currentTarget != null)
            {
                bool targetInvalid = currentTarget.Destroyed || !currentTarget.Spawned;
                if (!targetInvalid && currentMode == ADSMode.AntiGround)
                {
                    bool outOfRange = currentTarget.Position.DistanceToSquared(GetAbsolutePosition().ToIntVec3()) > Props.groundRange * Props.groundRange;
                    bool isRoofed = currentTarget.Position.Roofed(this.parent.Map);
                    if (outOfRange || isRoofed) targetInvalid = true;
                }
                if (targetInvalid)
                {
                    if (currentMode == ADSMode.AntiAir && currentTarget.Destroyed && Find.TickManager.TicksGame - lastIrradiationTick <= 2) cooldownTicksLeft = Props.cooldownTicks;
                    else if (currentMode == ADSMode.AntiGround && currentTargetIrradiationTicks > 0) cooldownTicksLeft = Props.cooldownTicks;
                    ResetTarget();
                }
                else if (currentMode == ADSMode.AntiGround)
                {
                    Pawn p = currentTarget as Pawn;
                    if (p != null && p.Dead)
                    {
                        cooldownTicksLeft = Props.cooldownTicks;
                        ResetTarget();
                    }
                    else if (currentTargetIrradiationTicks >= Props.groundMaxIrradiationTicks)
                    {
                        lastInterceptTick = Find.TickManager.TicksGame;
                        lastInterceptPos = currentTarget.DrawPos;
                        cooldownTicksLeft = Props.cooldownTicks;
                        ResetTarget();
                    }
                }
            }
            if (currentTarget != null)
            {
                float targetAngle = (currentTarget.DrawPos - GetAbsolutePosition()).Yto0().AngleFlat();
                RotateTowards(targetAngle);
                if (Mathf.Abs(Mathf.DeltaAngle(curTurretAngle, targetAngle)) <= Props.interceptAngleTolerance)
                {
                    if (currentMode == ADSMode.AntiAir) ProcessAntiAirTick();
                    else if (currentMode == ADSMode.AntiGround) ProcessAntiGroundTick(targetAngle);
                }
            }
            else if (cooldownTicksLeft <= 0) RotateTowards(BaseAngle);
        }
        //伺服转向机
        private void RotateTowards(float targetAngle)
        {
            float delta = Mathf.DeltaAngle(curTurretAngle, targetAngle);
            curTurretAngle = Mathf.Abs(delta) > Props.turnSpeed ? curTurretAngle + Mathf.Sign(delta) * Props.turnSpeed : curTurretAngle + delta;
            while (curTurretAngle > 360f) curTurretAngle -= 360f;
            while (curTurretAngle < 0f) curTurretAngle += 360f;
        }
        //渲染逻辑
        public void DrawLaserOffscreen()
        {
            if (!this.parent.Spawned || !IsPoweredAndFunctional) return;
            Vector3 absoluteCenter = GetAbsolutePosition();
            if (Find.TickManager.TicksGame - lastIrradiationTick <= 1 && currentTarget != null && !currentTarget.Destroyed) DrawLaserBeam(absoluteCenter, currentTarget.DrawPos, 1f);
            else if (Find.TickManager.TicksGame - lastInterceptTick >= 0 && Find.TickManager.TicksGame - lastInterceptTick < 10) DrawLaserBeam(absoluteCenter, lastInterceptPos, 1f - ((float)(Find.TickManager.TicksGame - lastInterceptTick) / 10f));
        }
        public override void PostDraw()
        {
            base.PostDraw();
            if (!this.parent.Spawned) return;
            if (turretMat != null)
            {
                Vector3 topPos = GetAbsolutePosition();
                topPos.y = this.parent.DrawPos.y + 0.041f;
                Graphics.DrawMesh(MeshPool.plane10, Matrix4x4.TRS(topPos, Quaternion.AngleAxis(curTurretAngle - 90f, Vector3.up), new Vector3(Props.turretDrawSize, 1f, Props.turretDrawSize)), turretMat, 0);
            }
        }
        private void DrawLaserBeam(Vector3 startPos, Vector3 endPos, float alpha)
        {
            if (!CachedLaserMats.TryGetValue(Props.laserTexPath, out Material mat) || mat == null) return;
            laserPropertyBlock.Clear();
            laserPropertyBlock.SetColor(ID_Color, new Color(1f, 1f, 1f, alpha));
            if (Props.laserStartOffset != Vector2.zero) startPos += Quaternion.AngleAxis(curTurretAngle, Vector3.up) * new Vector3(Props.laserStartOffset.x, 0, Props.laserStartOffset.y);
            startPos.y = AltitudeLayer.MetaOverlays.AltitudeFor() + 1f;
            endPos.y = startPos.y;
            Vector3 dir = endPos - startPos;
            Graphics.DrawMesh(MeshPool.plane10, Matrix4x4.TRS(startPos + dir / 2f, Quaternion.AngleAxis(dir.AngleFlat(), Vector3.up), new Vector3(Props.laserWidth, 1f, dir.MagnitudeHorizontal())), mat, 0, null, 0, laserPropertyBlock);
        }
        private void EmitLaserSparks(Vector3 hitPos, float baseAngle, bool isBurst)
        {
            FleckDef fleckDef = DefDatabase<FleckDef>.GetNamedSilentFail("FleckKT_MeltMetal") ?? FleckDefOf.MicroSparks;
            int count = isBurst ? Props.interceptSparkCount : Props.meltingSparkCountRange.RandomInRange;
            float posOffset = isBurst ? 0.4f : 0.2f;
            for (int i = 0; i < count; i++)
            {
                FleckCreationData data = default(FleckCreationData);
                data.def = fleckDef;
                data.spawnPosition = hitPos + Gen.RandomHorizontalVector(posOffset);
                data.spawnPosition.y = AltitudeLayer.MoteOverhead.AltitudeFor();
                data.scale = isBurst ? Rand.Range(Props.interceptSparkScaleRange.x, Props.interceptSparkScaleRange.y) : Rand.Range(Props.meltingSparkScaleRange.x, Props.meltingSparkScaleRange.y);
                data.instanceColor = LaserSparkColors.RandomElement();
                data.rotation = Rand.Range(0f, 360f);
                float spread = isBurst ? Props.interceptSparkAngleSpread : Props.meltingSparkAngleSpread;
                data.velocityAngle = baseAngle + Rand.Range(-spread / 2f, spread / 2f);
                Vector2 speedRange = isBurst ? Props.interceptSparkSpeedRange : Props.meltingSparkSpeedRange;
                data.velocitySpeed = Rand.Range(speedRange.x, speedRange.y);
                data.ageTicksOverride = -1;
                data.solidTimeOverride = -1f;
                this.parent.Map.flecks.CreateFleck(data);
            }
            Color dustColor = new Color(0.4f, 0.7f, 1f, 0.8f);
            if (isBurst)
            {
                float rad = baseAngle * Mathf.Deg2Rad;
                Vector3 dirVec = new Vector3(Mathf.Sin(rad), 0, Mathf.Cos(rad));
                for (int i = 0; i < 3; i++) FleckMaker.ThrowDustPuffThick(hitPos + dirVec * (i * 0.5f), this.parent.Map, 1.2f, dustColor);
                FleckMaker.ThrowLightningGlow(hitPos, this.parent.Map, 2.0f);
            }
            else if (Find.TickManager.TicksGame % 3 == 0) FleckMaker.ThrowDustPuffThick(hitPos, this.parent.Map, 0.8f, dustColor);
        }
        //交互界面接口
        public string GetModeLabel()
        {
            switch (currentMode)
            {
                case ADSMode.Off: return "KTLaserADS_ModeOff".Translate();
                case ADSMode.AntiAir: return "KTLaserADS_ModeAntiAir".Translate();
                case ADSMode.AntiGround: return "KTLaserADS_ModeAntiGround".Translate();
                default: return "Unknown";
            }
        }
        public Texture2D GetModeIcon()
        {
            string path = "";
            switch (currentMode)
            {
                case ADSMode.Off: path = Props.uiIconPath_ModeOff; break;
                case ADSMode.AntiAir: path = Props.uiIconPath_ModeAir; break;
                case ADSMode.AntiGround: path = Props.uiIconPath_ModeGround; break;
            }
            if (string.IsNullOrEmpty(path)) path = Props.uiIconPath;
            if (string.IsNullOrEmpty(path)) path = Props.turretTexPath;
            if (!string.IsNullOrEmpty(path)) return ContentFinder<Texture2D>.Get(path, false);
            return BaseContent.BadTex;
        }
        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (Gizmo gizmo in base.CompGetGizmosExtra()) yield return gizmo;
            if (this.parent.Faction == Faction.OfPlayer) yield return new Gizmo_LaserController(this);
        }
    }
}