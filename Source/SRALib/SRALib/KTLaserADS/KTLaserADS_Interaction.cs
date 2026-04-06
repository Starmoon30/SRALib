using UnityEngine;
using Verse.Sound;
using RimWorld;
using Verse;

//交互界面
namespace SRA
{
    //交互界面控制器
    public class Gizmo_LaserController : Gizmo
    {
        public CompLaserADS comp;
        private Texture2D mainTurretIcon;
        private Texture2D manualAimTex;
        private Texture2D cancelAimTex;
        public Gizmo_LaserController(CompLaserADS comp)
        {
            this.comp = comp;
            this.Order = -90f;
            string texPath = !string.IsNullOrEmpty(comp.Props.uiIconPath) ? comp.Props.uiIconPath : comp.Props.turretTexPath;
            if (!string.IsNullOrEmpty(texPath)) mainTurretIcon = ContentFinder<Texture2D>.Get(texPath, false);
            else mainTurretIcon = BaseContent.BadTex;
            if (!string.IsNullOrEmpty(comp.Props.uiIconPath_ManualAim)) manualAimTex = ContentFinder<Texture2D>.Get(comp.Props.uiIconPath_ManualAim, false);
            if (!string.IsNullOrEmpty(comp.Props.uiIconPath_CancelAim)) cancelAimTex = ContentFinder<Texture2D>.Get(comp.Props.uiIconPath_CancelAim, false);
        }
        //构造交互界面
        public override float GetWidth(float maxWidth) => 320f;
        public override GizmoResult GizmoOnGUI(Vector2 topLeft, float maxWidth, GizmoRenderParms parms)
        {
            Rect rect = new Rect(topLeft.x, topLeft.y, GetWidth(maxWidth), 75f);
            GUI.DrawTexture(rect, Command.BGTex);
            Widgets.DrawHighlightIfMouseover(rect);
            GUI.DrawTexture(new Rect(rect.x + 10f, rect.y + 5f, 65f, 50f), mainTurretIcon, ScaleMode.ScaleToFit);
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.UpperCenter;
            Widgets.Label(new Rect(rect.x, rect.y + 55f, 85f, 20f), comp.Props.label.Translate());
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = new Color(0.4f, 0.4f, 0.4f, 1f);
            Widgets.DrawLineVertical(rect.x + 85f, rect.y + 10f, 55f);
            GUI.color = Color.white;
            float startX = rect.x + 95f;
            float btnSize = 48f;
            float startY = rect.y + (75f - btnSize) / 2f;
            Rect btnMode = new Rect(startX, startY, btnSize, btnSize);
            GUI.DrawTexture(btnMode, Command.BGTex);
            Widgets.DrawHighlightIfMouseover(btnMode);
            if (Widgets.ButtonImage(btnMode, comp.GetModeIcon()))
            {
                comp.currentMode = (ADSMode)(((int)comp.currentMode + 1) % 3);
                comp.ResetTarget();
                SoundDefOf.Click.PlayOneShotOnCamera();
            }
            string warning = comp.parent.Map.roofGrid.Roofed(comp.parent.Position) ? "\n\n" + "KTLaserADS_WarningRoofed".Translate().ToString() : "";
            TooltipHandler.TipRegion(btnMode, "KTLaserADS_ToggleModeDesc".Translate(comp.GetModeLabel()) + warning);
            if (comp.currentMode == ADSMode.AntiAir)
            {
                float rightAreaStartX = startX + btnSize + 15f;
                Rect rightRect = new Rect(rightAreaStartX, rect.y, 150f, 75f);
                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.UpperCenter;
                Widgets.Label(new Rect(rightRect.x, rightRect.y + 10f, rightRect.width, 20f), "KTLaserADS_MinInterceptDamageLabel".Translate());
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleCenter;
                Rect valRect = new Rect(rightRect.x + 35f, rightRect.y + 30f, rightRect.width - 70f, 30f);
                Widgets.DrawBoxSolid(valRect, new Color(0f, 0f, 0f, 0.4f));
                Rect btnMinus = new Rect(rightRect.x, rightRect.y + 30f, 30f, 30f);
                if (Widgets.ButtonText(btnMinus, "-"))
                {
                    comp.minInterceptDamage = Mathf.Clamp(comp.minInterceptDamage - comp.Props.minDamageStep, 0, 3000);
                    SoundDefOf.Click.PlayOneShotOnCamera();
                }
                Widgets.Label(valRect, comp.minInterceptDamage.ToString());
                Rect btnPlus = new Rect(rightRect.xMax - 30f, rightRect.y + 30f, 30f, 30f);
                if (Widgets.ButtonText(btnPlus, "+"))
                {
                    comp.minInterceptDamage = Mathf.Clamp(comp.minInterceptDamage + comp.Props.minDamageStep, 0, 3000);
                    SoundDefOf.Click.PlayOneShotOnCamera();
                }
                Text.Anchor = TextAnchor.UpperLeft;
                Text.Font = GameFont.Small;
                TooltipHandler.TipRegion(rightRect, "KTLaserADS_MinInterceptDamageDesc".Translate());
            }
            else if (comp.currentMode == ADSMode.AntiGround)
            {
                Rect btnAim = new Rect(startX + btnSize + 10f, startY, btnSize, btnSize);
                if (comp.forcedTarget.IsValid)
                {
                    Widgets.DrawBoxSolid(btnAim, new Color(0.8f, 0.4f, 0f, 0.4f));
                    GUI.color = new Color(1f, 0.6f, 0f);
                    Widgets.DrawBox(btnAim, 2);
                    GUI.color = Color.white;
                }
                else GUI.DrawTexture(btnAim, Command.BGTex);
                Widgets.DrawHighlightIfMouseover(btnAim);
                Texture2D currentAimIcon = comp.forcedTarget.IsValid ? (cancelAimTex ?? manualAimTex) : manualAimTex;
                if (currentAimIcon != null) GUI.DrawTexture(btnAim, currentAimIcon, ScaleMode.ScaleToFit);
                else
                {
                    Text.Font = GameFont.Tiny;
                    Text.Anchor = TextAnchor.MiddleCenter;
                    string btnText = comp.forcedTarget.IsValid ? "KTLaserADS_CancelTargeting".Translate() : "KTLaserADS_ManualTargeting".Translate();
                    Widgets.Label(btnAim, btnText);
                    Text.Anchor = TextAnchor.UpperLeft;
                    Text.Font = GameFont.Small;
                }
                if (Widgets.ButtonInvisible(btnAim))
                {
                    if (comp.forcedTarget.IsValid)
                    {
                        comp.ResetTarget();
                        SoundDefOf.Click.PlayOneShotOnCamera();
                    }
                    else
                    {
                        SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                        Find.Targeter.BeginTargeting(new TargetingParameters
                        {
                            canTargetPawns = true,
                            canTargetBuildings = true,
                            canTargetItems = true,
                            mapObjectTargetsMustBeAutoAttackable = false,
                            validator = (TargetInfo targ) =>
                            {
                                if (!targ.HasThing) return false;
                                if (targ.Thing.Position.Roofed(comp.parent.Map)) return false;
                                if (targ.Thing.Position.DistanceToSquared(comp.parent.Position) > comp.Props.groundRange * comp.Props.groundRange) return false;
                                return true;
                            }
                        }, (LocalTargetInfo targ) =>
                        {
                            comp.SetForcedTarget(targ);
                            SoundDefOf.Tick_High.PlayOneShotOnCamera();
                        });
                    }
                }
                TooltipHandler.TipRegion(btnAim, "KTLaserADS_ManualTargetingDesc".Translate());
            }
            return new GizmoResult(GizmoState.Clear);
        }
    }
}