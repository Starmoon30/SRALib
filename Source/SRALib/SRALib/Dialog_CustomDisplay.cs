using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using UnityEngine;
using Verse;

namespace SRA
{
    public class Dialog_CustomDisplay : Window
    {
        protected EventDef def;
        protected Texture2D portrait;
        protected Texture2D background;
        protected string selectedDescription;

        protected static EventUIConfigDef Config = DefDatabase<EventUIConfigDef>.GetNamed("SRA_EventUIConfig");

        public override Vector2 InitialSize
        {
            get
            {
                if (def.windowSize != Vector2.zero)
                {
                    return def.windowSize;
                }
                return Config.defaultWindowSize;
            }
        }

        public Dialog_CustomDisplay(EventDef def)
        {
            this.def = def;
            this.forcePause = true;
            this.absorbInputAroundWindow = true;
            this.doCloseX = true;
            if (def.eventUIConfig != null)
            {
                Config = def.eventUIConfig;
            }
            var eventVarManager = Find.World.GetComponent<EventVariableManager>();
            if (!def.descriptions.NullOrEmpty())
            {
                if (def.descriptionMode == DescriptionSelectionMode.Random)
                {
                    selectedDescription = def.descriptions.RandomElement();
                }
                else 
                {
                    string indexVarName = $"_seq_desc_index_{def.defName}";
                    int currentIndex = eventVarManager.GetVariable<int>(indexVarName, 0);

                    selectedDescription = def.descriptions[currentIndex];

                    int nextIndex = (currentIndex + 1) % def.descriptions.Count;
                    eventVarManager.SetVariable(indexVarName, nextIndex);
                }
            }
            else
            {
                selectedDescription = "Error: No descriptions found in def.";
            }
        }

        public override void PreOpen()
        {
            base.PreOpen();

            if (def.eventUIConfig != null)
            {
                Config = def.eventUIConfig;
            }


            if (!def.portraitPath.NullOrEmpty())
            {
                portrait = ContentFinder<Texture2D>.Get(def.portraitPath);
            }

            string bgPath = !def.backgroundImagePath.NullOrEmpty() ? def.backgroundImagePath : Config.defaultBackgroundImagePath;
            if (!bgPath.NullOrEmpty())
            {
                background = ContentFinder<Texture2D>.Get(bgPath);
            }

            HandleAction(def.immediateEffects);
            
            if (!def.conditionalDescriptions.NullOrEmpty())
            {
                foreach (var condDesc in def.conditionalDescriptions)
                {
                    string reason;
                    if (AreConditionsMet(condDesc.conditions, out reason))
                    {
                        selectedDescription += "\n\n" + condDesc.text;
                    }
                }
            }
            
            selectedDescription = FormatDescription(selectedDescription);
        }

        public override void DoWindowContents(Rect inRect)
        {
            if (background != null)
            {
                GUI.DrawTexture(inRect, background, ScaleMode.ScaleToFit);
            }

            if (Config.showDefName)
            {
                Text.Font = GameFont.Tiny;
                GUI.color = Color.gray;
                Widgets.Label(new Rect(5, 5, inRect.width - 10, 20f), def.defName.Translate());
                GUI.color = Color.white;
            }

            if (Config.showLabel)
            {
                Text.Font = Config.labelFont;
                Widgets.Label(new Rect(5, 20f, inRect.width - 10, 30f), def.label.Translate());
                Text.Font = GameFont.Small;
            }

            float virtualWidth = Config.portraitSize.x + Config.textSize.x;
            float virtualHeight = Config.portraitSize.y;

            float scaleX = inRect.width / virtualWidth;
            float scaleY = inRect.height / virtualHeight;
            float scale = Mathf.Min(scaleX, scaleY) * 0.95f;

            float scaledLihuiWidth = Config.portraitSize.x * scale;
            float scaledLihuiHeight = Config.portraitSize.y * scale;
            float scaledNameWidth = Config.nameSize.x * scale;
            float scaledNameHeight = Config.nameSize.y * scale;
            float scaledTextWidth = Config.textSize.x * scale;
            float scaledTextHeight = Config.textSize.y * scale;
            float scaledOptionsWidth = Config.optionsWidth * scale;

            float totalContentWidth = scaledLihuiWidth + scaledTextWidth;
            float totalContentHeight = scaledLihuiHeight;
            float startX = (inRect.width - totalContentWidth) / 2;
            float startY = (inRect.height - totalContentHeight) / 2;

            Rect portraitRect = new Rect(startX, startY, scaledLihuiWidth, scaledLihuiHeight);
            if (portrait != null)
            {
                GUI.DrawTexture(portraitRect, portrait, ScaleMode.ScaleToFit);
            }
            if (Config.drawBorders)
            {
                Widgets.DrawBox(portraitRect);
            }

            Rect nameRect = new Rect(portraitRect.xMax, portraitRect.y, scaledNameWidth, scaledNameHeight);
            if (Config.drawBorders)
            {
                Widgets.DrawBox(nameRect);
            }
            Text.Anchor = TextAnchor.MiddleCenter;
            Text.Font = GameFont.Medium;
            Widgets.Label(nameRect, def.characterName.Translate());
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;

            Rect textRect = new Rect(nameRect.x, nameRect.yMax + Config.textNameOffset * scale, scaledTextWidth, scaledTextHeight);
            if (Config.drawBorders)
            {
                Widgets.DrawBox(textRect);
            }
            Rect textInnerRect = textRect.ContractedBy(10f * scale);
            Widgets.Label(textInnerRect, selectedDescription.Translate());

            Rect optionRect = new Rect(nameRect.x, textRect.yMax + Config.optionsTextOffset * scale, scaledOptionsWidth, portraitRect.height - nameRect.height - textRect.height - (Config.textNameOffset + Config.optionsTextOffset) * scale);

            DrawOptions(optionRect);
        }

        protected virtual void DrawOptions(Rect optionRect)
        {
            Listing_Standard listing = new Listing_Standard();
            listing.Begin(optionRect); // 使用完整的 optionRect
            if (def.options != null)
            {
                foreach (var option in def.options)
                {
                    string reason;
                    bool conditionsMet = AreConditionsMet(option.conditions, out reason);

                    if (conditionsMet)
                    {
                        if (listing.ButtonText(option.label.Translate()))
                        {
                            HandleAction(option.optionEffects);
                        }
                    }
                    else
                    {
                        if (option.hideWhenDisabled)
                        {
                            continue;
                        }
                        Rect rect = listing.GetRect(30f);
                        Widgets.ButtonText(rect, option.label.Translate(), true, true, Color.gray, false);
                        TooltipHandler.TipRegion(rect, GetDisabledReason(option, reason));
                    }
                }
            }
            listing.End();
        }
        protected virtual void HandleAction(List<ConditionalEffects> conditionalEffects)
        {
            if (conditionalEffects.NullOrEmpty())
            {
                return;
            }

            foreach (var ce in conditionalEffects)
            {
                if (AreConditionsMet(ce.conditions, out _))
                {
                    ce.Execute(this);
                }
            }
        }

        protected virtual bool AreConditionsMet(List<Condition> conditions, out string reason)
        {
            reason = "";
            if (conditions.NullOrEmpty())
            {
                return true;
            }

            foreach (var condition in conditions)
            {
                if (!condition.IsMet(out string singleReason))
                {
                    reason = singleReason;
                    return false;
                }
            }
            return true;
        }

        protected virtual string GetDisabledReason(EventOption option, string reason)
        {
            if (!option.disabledReason.NullOrEmpty())
            {
                return option.disabledReason.Translate();
            }
            return reason;
        }

        public override void PostClose()
        {
            base.PostClose();
            HandleAction(def.dismissEffects);
        }

        protected virtual string FormatDescription(string description)
        {
            var eventVarManager = Find.World.GetComponent<EventVariableManager>();
            // Use regex to find all placeholders like {variableName}
            return Regex.Replace(description, @"\{(.+?)\}", match =>
            {
                string varName = match.Groups[1].Value;
                if (eventVarManager.HasVariable(varName))
                {
                    // Important: GetVariable<object> to get any type
                    return eventVarManager.GetVariable<object>(varName)?.ToString() ?? "";
                }
                return match.Value; // Keep placeholder if variable not found
            });
        }
    }
}
