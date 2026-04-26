using System.Collections.Generic;
using UnityEngine;
using Verse.Sound;
using RimWorld;
using Verse;

//交互界面
namespace SRA
{
    public class Gizmo_PulseElectrodeController : Gizmo
    {
        public CompPulseElectrode comp;
        private static Dictionary<string, Texture2D> texCache = new Dictionary<string, Texture2D>();
        private static Texture2D GetTex(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return BaseContent.BadTex;
            }
            if (!texCache.TryGetValue(path, out Texture2D tex))
            {
                tex = ContentFinder<Texture2D>.Get(path, false) ?? BaseContent.BadTex;
                texCache[path] = tex;
            }
            return tex;
        }
        public Gizmo_PulseElectrodeController(CompPulseElectrode comp)
        {
            this.comp = comp;
            this.Order = -90f;
        }
        public override float GetWidth(float maxWidth)
        {
            return 280f;
        }
        public override GizmoResult GizmoOnGUI(Vector2 topLeft, float maxWidth, GizmoRenderParms parms)
        {
            Rect rect = new Rect(topLeft.x, topLeft.y, GetWidth(maxWidth), 75f);
            GUI.DrawTexture(rect, Command.BGTex);
            Widgets.DrawHighlightIfMouseover(rect);
            string mainIconPath = !string.IsNullOrEmpty(comp.Props.uiIconPath) ? comp.Props.uiIconPath : comp.Props.turretTexPath;
            Texture2D mainIcon = GetTex(mainIconPath);
            GUI.DrawTexture(new Rect(rect.x + 10, rect.y + 5, 65, 50), mainIcon, ScaleMode.ScaleToFit);
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.UpperCenter;
            Widgets.Label(new Rect(rect.x, rect.y + 55, 85, 20), comp.Props.label.Translate());
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = new Color(0.4f, 0.4f, 0.4f, 1f);
            Widgets.DrawLineVertical(rect.x + 85, rect.y + 10, 55);
            GUI.color = Color.white;
            float currentX = rect.x + 95;
            Texture2D toggleIcon = GetTex(comp.isArmed ? comp.Props.uiIconPath_ModeOn : comp.Props.uiIconPath_ModeOff);
            Rect toggleRect = new Rect(currentX, rect.y + 13.5f, 48, 48);
            if (Widgets.ButtonImage(toggleRect, toggleIcon))
            {
                comp.isArmed = !comp.isArmed;
                comp.ResetTarget();
                SoundDefOf.Click.PlayOneShotOnCamera();
            }
            TooltipHandler.TipRegion(toggleRect, (comp.isArmed ? "KT_Pulse_ModeOn_Tip" : "KT_Pulse_ModeOff_Tip").Translate());
            if (comp.isArmed)
            {
                currentX += 58;
                Rect aimRect = new Rect(currentX, rect.y + 13.5f, 48, 48);
                if (comp.forcedTarget.IsValid)
                {
                    Widgets.DrawBoxSolid(aimRect, new Color(1f, 0.5f, 0f, 0.3f));
                    GUI.color = new Color(1f, 0.5f, 0f);
                    Widgets.DrawBox(aimRect, 2);
                    GUI.color = Color.white;
                }
                if (Widgets.ButtonImage(aimRect, GetTex(comp.Props.uiIconPath_ForcedTarget)))
                {
                    SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                    Find.Targeter.BeginTargeting(new TargetingParameters
                    {
                        canTargetPawns = true,
                        canTargetBuildings = true,
                        canTargetLocations = true,
                        validator = (TargetInfo t) => t.Cell.DistanceToSquared(comp.parent.Position) <= comp.Props.range * comp.Props.range
                    }, (LocalTargetInfo t) => comp.SetForcedTarget(t));
                }
                TooltipHandler.TipRegion(aimRect, "KT_Pulse_ManualAim_Tip".Translate());
                if (comp.forcedTarget.IsValid)
                {
                    currentX += 58;
                    Rect cancelRect = new Rect(currentX, rect.y + 13.5f, 48, 48);
                    if (Widgets.ButtonImage(cancelRect, GetTex(comp.Props.uiIconPath_CancelTarget)))
                    {
                        comp.ResetTarget();
                        SoundDefOf.Click.PlayOneShotOnCamera();
                    }
                    TooltipHandler.TipRegion(cancelRect, "KT_Pulse_CancelTarget_Tip".Translate());
                }
            }
            return new GizmoResult(GizmoState.Clear);
        }
    }
}