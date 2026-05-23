using RimWorld;
using System.Text;
using UnityEngine;
using Verse;

namespace SRA
{
    [StaticConstructorOnStartup]
    public class SRABarrierGizmo : Gizmo
    {
        private readonly HediffComp_SRABarrier barrier;
        private int groupedCount = 1;

        private const float FullBarrierTolerance = 0.1f;
        private static readonly Texture2D FullBarrierBarTex = SolidColorMaterials.NewSolidColorTexture(Color.gray);

        public SRABarrierGizmo(HediffComp_SRABarrier barrier)
        {
            this.barrier = barrier;
        }

        public override float Order => -100f;

        public override float GetWidth(float maxWidth) => 180f;

        public override bool GroupsWith(Gizmo other)
        {
            if (other is not SRABarrierGizmo otherBarrierGizmo)
            {
                return false;
            }

            return barrier.parent.def == otherBarrierGizmo.barrier.parent.def &&
                   IsBarrierFull(barrier) &&
                   IsBarrierFull(otherBarrierGizmo.barrier);
        }

        public override void MergeWith(Gizmo other)
        {
            base.MergeWith(other);

            if (other is SRABarrierGizmo otherBarrierGizmo)
            {
                groupedCount += otherBarrierGizmo.groupedCount;
            }
        }

        public override GizmoResult GizmoOnGUI(Vector2 topLeft, float maxWidth, GizmoRenderParms parms)
        {
            Rect rect = new Rect(topLeft.x, topLeft.y, GetWidth(maxWidth), 75f);
            Widgets.DrawWindowBackground(rect);

            Rect titleRect = new Rect(rect.x, rect.y + 5f, rect.width, 24f);
            Text.Anchor = TextAnchor.UpperCenter;
            string hediffName = barrier.parent.LabelCap;
            if (groupedCount > 1)
            {
                hediffName += $" (x{groupedCount})";
            }
            Widgets.Label(titleRect, hediffName + "SRABarrierTitle".Translate());
            Text.Anchor = TextAnchor.UpperLeft;

            Rect barRect = new Rect(rect.x + 10f, rect.y + 30f, rect.width - 20f, 20f);
            float fillPercent = barrier.Props.maxBarrier > 0f ? barrier.CurrentBarrier / barrier.Props.maxBarrier : 0f;
            if (fillPercent > 1f)
            {
                fillPercent = 1f;
            }

            Widgets.FillableBar(
                barRect,
                fillPercent,
                FullBarrierBarTex,
                BaseContent.BlackTex,
                false
            );

            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(barRect, $"{barrier.CurrentBarrier:F0}/{barrier.Props.maxBarrier:F0}");
            Text.Anchor = TextAnchor.UpperLeft;

            Rect statusRect = new Rect(rect.x + 5f, rect.y + 55f, rect.width, 20f);
            if (barrier.InCooldown)
            {
                string cooldownText = "SRA_BarrierRecharging".Translate(
                    barrier.GetCooldownSeconds().ToString("N" + 2)
                );
                Widgets.Label(statusRect, cooldownText);
            }
            else
            {
                string regenText = "SRA_BarrierRegen".Translate(
                    barrier.Props.regenRate.ToString()
                );
                Widgets.Label(statusRect, regenText);
            }

            StringBuilder tooltipText = new StringBuilder("SRABarrierTooltip".Translate(
                barrier.Props.regenDelay.ToString(),
                barrier.Props.rechargeCooldown.ToString(),
                barrier.Props.DamageTakenMult.ToString(),
                barrier.Props.DamageTakenMax.ToString(),
                barrier.Props.DamageTakenReduce.ToString()
            ));
            if (barrier.Props.HardenedBarrier)
            {
                tooltipText.Append("SRA_BarrierHardenedExtra".Translate());
            }
            if (barrier.Props.DeflectiveBarrier)
            {
                tooltipText.Append("SRA_BarrierDeflectiveExtra".Translate());
            }
            TooltipHandler.TipRegion(rect, tooltipText.ToString());

            return new GizmoResult(GizmoState.Clear);
        }

        private static bool IsBarrierFull(HediffComp_SRABarrier barrier)
        {
            return barrier.Props.maxBarrier > 0f &&
                   barrier.CurrentBarrier >= barrier.Props.maxBarrier - FullBarrierTolerance;
        }
    }
}
