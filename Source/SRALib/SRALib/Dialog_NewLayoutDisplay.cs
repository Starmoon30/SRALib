using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;
using Verse;

namespace SRA
{
    public class Dialog_NewLayoutDisplay : Window
    {
        private EventDef def;
        private Texture2D portrait;
        private Texture2D background;
        private string selectedDescription;

        private static EventUIConfigDef config;
        public static EventUIConfigDef Config
        {
            get
            {
                if (config == null)
                {
                    config = DefDatabase<EventUIConfigDef>.GetNamed("SRA_EventUIConfig");
                }
                return config;
            }
        }

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

        public Dialog_NewLayoutDisplay(EventDef def)
        {
            this.def = def;
            this.forcePause = true;
            this.absorbInputAroundWindow = true;
            this.doCloseX = true;

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
                Widgets.Label(new Rect(5, 5, inRect.width - 10, 20f), def.defName);
                GUI.color = Color.white;
            }

            if (Config.showLabel)
            {
                Text.Font = Config.labelFont;
                Widgets.Label(new Rect(5, 20f, inRect.width - 10, 30f), def.label);
                Text.Font = GameFont.Small;
            }

            // 假设一个统一的边距
            float padding = Config.newLayoutPadding;

            // 名称区域
            float nameHeight = Config.newLayoutNameSize.y;
            float nameWidth = Config.newLayoutNameSize.x;
            Rect nameRect = new Rect(inRect.x + (inRect.width - nameWidth) / 2f, inRect.y + padding, nameWidth, nameHeight);
            if (Config.drawBorders)
            {
                Widgets.DrawBox(nameRect);
            }
            Text.Anchor = TextAnchor.MiddleCenter;
            Text.Font = GameFont.Medium;
            Widgets.Label(nameRect, def.characterName);
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;

            // 立绘区域
            float lihuiWidth = Config.newLayoutportraitSize.x;
            float lihuiHeight = Config.newLayoutportraitSize.y;
            Rect lihuiRect = new Rect(inRect.x + (inRect.width - lihuiWidth) / 2f, nameRect.yMax + padding, lihuiWidth, lihuiHeight);
            if (portrait != null)
            {
                GUI.DrawTexture(lihuiRect, portrait, ScaleMode.ScaleToFit);
            }
            if (Config.drawBorders)
            {
                Widgets.DrawBox(lihuiRect);
            }

            // 选项区域 (预先计算高度)
            float optionButtonHeight = 30f; // 每个按钮的高度
            float optionSpacing = 5f; // 按钮之间的间距
            float calculatedOptionHeight = 0f;
            if (def.options != null && def.options.Any())
            {
                calculatedOptionHeight = def.options.Count * optionButtonHeight + (def.options.Count - 1) * optionSpacing;
            }
            calculatedOptionHeight = Mathf.Max(calculatedOptionHeight, 100f); // 最小高度

            float optionsWidth = Config.newLayoutOptionsWidth;
            Rect optionRect = new Rect(inRect.x + (inRect.width - optionsWidth) / 2f, inRect.yMax - padding - calculatedOptionHeight, optionsWidth, calculatedOptionHeight);

            // 描述区域
            float textWidth = Config.newLayoutTextSize.x;
            Rect textRect = new Rect(inRect.x + (inRect.width - textWidth) / 2f, lihuiRect.yMax + padding, textWidth, optionRect.y - (lihuiRect.yMax + padding) - padding);
            if (Config.drawBorders)
            {
                Widgets.DrawBox(textRect);
            }
            Rect textInnerRect = textRect.ContractedBy(padding);
            Widgets.Label(textInnerRect, selectedDescription);

            // 选项列表的绘制
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
                        if (listing.ButtonText(option.label))
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
                        Widgets.ButtonText(rect, option.label, false, true, false);
                        TooltipHandler.TipRegion(rect, GetDisabledReason(option, reason));
                    }
                }
            }
            listing.End();
        }

        private void HandleAction(List<ConditionalEffects> conditionalEffects)
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

        private bool AreConditionsMet(List<Condition> conditions, out string reason)
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

        private string GetDisabledReason(EventOption option, string reason)
        {
            if (!option.disabledReason.NullOrEmpty())
            {
                return option.disabledReason;
            }
            return reason;
        }

        public override void PostClose()
        {
            base.PostClose();
            HandleAction(def.dismissEffects);
        }
        
        private string FormatDescription(string description)
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
