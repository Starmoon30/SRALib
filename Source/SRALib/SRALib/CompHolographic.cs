using System.Collections.Generic;
using UnityEngine;
using RimWorld;
using Verse;
//全息投影广告

namespace SRA
{
    public class CompProperties_Holographic : CompProperties
    {
        public CompProperties_Holographic()
        {
            this.compClass = typeof(CompHolographic);
        }
        public List<GraphicData> graphics;
        public float floatAmplitude = 0f;
        public float floatSpeed = 0f;
        public AltitudeLayer altitudeLayer = AltitudeLayer.BuildingOnTop;
        public float opacity = 1.0f;
        public int transitionDuration = 60;
        public int autoplayIntervalTicks = 600;
        public string uiIconPathNext = "UI/Commands/Next";
        public string uiIconPathAutoplay = "UI/Commands/Loop";
        public string labelNext = "Holographic.LabelNext";
        public string descNext = "Holographic.DescNext";
        public string labelAutoplay = "Holographic.LabelAutoplay";
        public string descAutoplay = "Holographic.DescAutoplay";
        public string disableReasonNoPower = "Holographic.DisableReasonNoPower";
        public string disableReasonChanging = "Holographic.DisableReasonChanging";
    }
    public class CompHolographic : ThingComp
    {
        private float randTimeOffset;
        private CompPowerTrader powerComp;
        private MaterialPropertyBlock matPropertyBlock;
        private int currentIndex = 0;
        private int transitionFromIndex = -1;
        private int transitionTicks = 0;
        private bool isAutoplaying = false;
        private int autoplayTimerTicks = 0;
        private List<Graphic> cachedGraphics;
        private CompProperties_Holographic Props
        {
            get
            {
                return (CompProperties_Holographic)this.props;
            }
        }
        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            this.powerComp = this.parent.GetComp<CompPowerTrader>();
            this.matPropertyBlock = new MaterialPropertyBlock();
            if (!respawningAfterLoad)
            {
                this.randTimeOffset = Rand.Range(0f, 600f);
                this.autoplayTimerTicks = this.Props.autoplayIntervalTicks;
            }
        }
        private void InitializeGraphicsCache()
        {
            this.cachedGraphics = new List<Graphic>();
            if (this.Props.graphics.NullOrEmpty())
            {
                return;
            }
            foreach (GraphicData gData in this.Props.graphics)
            {
                this.cachedGraphics.Add(gData.Graphic);
            }
        }
        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref this.randTimeOffset, "randTimeOffset", 0f);
            Scribe_Values.Look(ref this.currentIndex, "currentIndex", 0);
            Scribe_Values.Look(ref this.transitionFromIndex, "transitionFromIndex", -1);
            Scribe_Values.Look(ref this.transitionTicks, "transitionTicks", 0);
            Scribe_Values.Look(ref this.isAutoplaying, "isAutoplaying", false);
            Scribe_Values.Look(ref this.autoplayTimerTicks, "autoplayTimerTicks", 0);
        }
        public override void CompTick()
        {
            base.CompTick();
            if (!this.isAutoplaying
                || (this.powerComp != null && !this.powerComp.PowerOn)
                || this.transitionFromIndex != -1)
            {
                return;
            }
            this.autoplayTimerTicks--;
            if (this.autoplayTimerTicks <= 0)
            {
                this.StartTransition();
                this.autoplayTimerTicks = this.Props.autoplayIntervalTicks;
            }
        }
        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (Gizmo gizmo in base.CompGetGizmosExtra())
            {
                yield return gizmo;
            }
            if (this.cachedGraphics == null)
            {
                yield break;
            }
            if (this.Props.graphics.NullOrEmpty() || this.Props.graphics.Count <= 1)
            {
                yield break;
            }
            if (!this.isAutoplaying)
            {
                Command_Action nextAdGizmo = new Command_Action();
                nextAdGizmo.defaultLabel = this.Props.labelNext.Translate();
                nextAdGizmo.defaultDesc = this.Props.descNext.Translate();
                nextAdGizmo.icon = ContentFinder<Texture2D>.Get(this.Props.uiIconPathNext, true);
                nextAdGizmo.action = delegate
                {
                    this.StartTransition();
                };
                if (this.powerComp != null && !this.powerComp.PowerOn)
                {
                    nextAdGizmo.Disable(this.Props.disableReasonNoPower.Translate());
                }
                if (this.transitionFromIndex != -1)
                {
                    nextAdGizmo.Disable(this.Props.disableReasonChanging.Translate());
                }
                yield return nextAdGizmo;
            }
            Command_Toggle autoplayGizmo = new Command_Toggle();
            autoplayGizmo.defaultLabel = this.Props.labelAutoplay.Translate();
            autoplayGizmo.defaultDesc = this.Props.descAutoplay.Translate();
            autoplayGizmo.icon = ContentFinder<Texture2D>.Get(this.Props.uiIconPathAutoplay, true);
            autoplayGizmo.isActive = () => this.isAutoplaying;
            autoplayGizmo.toggleAction = delegate
            {
                this.isAutoplaying = !this.isAutoplaying;
                this.autoplayTimerTicks = this.Props.autoplayIntervalTicks;
            };
            if (this.powerComp != null && !this.powerComp.PowerOn)
            {
                autoplayGizmo.Disable(this.Props.disableReasonNoPower.Translate());
            }
            yield return autoplayGizmo;
        }
        private void StartTransition()
        {
            if (this.cachedGraphics == null) return;
            if (this.transitionFromIndex != -1 || this.Props.graphics.NullOrEmpty() || this.Props.graphics.Count <= 1)
            {
                return;
            }
            this.transitionFromIndex = this.currentIndex;
            this.currentIndex = (this.currentIndex + 1) % this.Props.graphics.Count;
            this.transitionTicks = this.Props.transitionDuration;
        }
        public override void PostDraw()
        {
            base.PostDraw();
            if (this.cachedGraphics == null)
            {
                this.InitializeGraphicsCache();
            }
            if (this.powerComp != null && !this.powerComp.PowerOn)
            {
                return;
            }
            if (this.Props.graphics.NullOrEmpty())
            {
                return;
            }
            float floatOffset = Mathf.Sin(((float)Find.TickManager.TicksGame + this.randTimeOffset) * this.Props.floatSpeed) * this.Props.floatAmplitude;
            bool isTransitioning = this.transitionTicks > 0 && this.transitionFromIndex != -1;
            if (isTransitioning)
            {
                this.transitionTicks--;
                float percent = 0f;
                if (this.Props.transitionDuration > 0)
                {
                    percent = (float)this.transitionTicks / (float)this.Props.transitionDuration;
                }
                float fadeOutAlpha = percent;
                float fadeInAlpha = 1.0f - percent;
                this.DrawHologram(this.transitionFromIndex, floatOffset, this.Props.opacity * fadeOutAlpha);
                this.DrawHologram(this.currentIndex, floatOffset, this.Props.opacity * fadeInAlpha);
                if (this.transitionTicks <= 0)
                {
                    this.transitionFromIndex = -1;
                }
            }
            else
            {
                this.DrawHologram(this.currentIndex, floatOffset, this.Props.opacity);
            }
        }
        private void DrawHologram(int index, float floatOffset, float finalOpacity)
        {
            if (index < 0 || this.Props.graphics.NullOrEmpty() || index >= this.Props.graphics.Count || this.cachedGraphics.NullOrEmpty() || index >= this.cachedGraphics.Count || finalOpacity <= 0f)
            {
                return;
            }
            GraphicData gData = this.Props.graphics[index];
            Graphic graphic = this.cachedGraphics[index];
            if (gData == null || graphic == null)
            {
                return;
            }
            Mesh mesh = graphic.MeshAt(this.parent.Rotation);
            Material mat = graphic.MatAt(this.parent.Rotation, null);
            if (mat == null || mesh == null)
            {
                Log.ErrorOnce($"CompHolographic on {this.parent.def.defName} has null material or mesh for index {index}.", this.parent.thingIDNumber % 1337 + index);
                return;
            }
            Vector3 drawPos = this.parent.DrawPos;
            drawPos.y = this.Props.altitudeLayer.AltitudeFor();
            drawPos.z += floatOffset;
            Vector3 finalPos = drawPos + gData.drawOffset.RotatedBy(this.parent.Rotation);
            Color color = gData.color;
            color.a *= finalOpacity;
            this.matPropertyBlock.SetColor(ShaderPropertyIDs.Color, color);
            Graphics.DrawMesh(mesh, finalPos, Quaternion.identity, mat, 0, null, 0, this.matPropertyBlock);
        }
    }
}