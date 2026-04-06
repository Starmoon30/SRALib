using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;
using Verse;

namespace SRA
{
    public class Dialog_NewLayoutDisplay : Dialog_CustomDisplay
    {
        public Dialog_NewLayoutDisplay(EventDef def) : base(def)
        {
            // 构造函数逻辑已在基类中处理
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
            Widgets.Label(nameRect, def.characterName.Translate());
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
            Widgets.Label(textInnerRect, selectedDescription.Translate());

            DrawOptions(optionRect);
        }
    }
}
