using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace HSKKebabLimits
{
    /// <summary>
    /// Draws similar-stack limit controls in the stockpile filter config window.
    /// </summary>
    internal static class StockpileFilterSimilarStackRow
    {
        private const int MaxSimilarStackCount = 64;
        private const float RowTextHeight = 22f;

        /// <summary>
        /// Returns a highlight rect that matches the drawn label height instead of the full row slot.
        /// </summary>
        private static Rect LabelHighlightRect(Rect labelRect, string labelText)
        {
            float textHeight = Mathf.Min(Text.CalcHeight(labelText, labelRect.width), labelRect.height);
            textHeight = Mathf.Min(textHeight, RowTextHeight);
            Rect highlightRect = labelRect;
            highlightRect.height = textHeight;
            highlightRect.y += (labelRect.height - textHeight) * 0.5f;
            return highlightRect;
        }

        /// <summary>
        /// Returns the label and tooltip for the current similar-stack limit mode.
        /// </summary>
        private static string LabelForMode(int similarStackCount)
        {
            switch (similarStackCount)
            {
                case -1:
                    return "SimilarStackLimitDefault".Translate();
                case 0:
                    return "SimilarStackLimitDisabled".Translate();
                default:
                    return "SimilarStackLimit".Translate(similarStackCount);
            }
        }

        /// <summary>
        /// Returns the tooltip key for the current similar-stack limit mode.
        /// </summary>
        private static string TooltipForMode(int similarStackCount)
        {
            if (similarStackCount == -1)
            {
                return "SimilarStackLimitDefaultTooltip".Translate();
            }

            if (similarStackCount == 0)
            {
                return "SimilarStackLimitDisabledTooltip".Translate();
            }

            return "SimilarStackLimitTooltip".Translate(similarStackCount.Named("COUNT"));
        }

        /// <summary>
        /// Draws the similar-stack row and returns the updated count and multistack checkbox state.
        /// </summary>
        public static void Draw(ref float y, float width, bool perStorageMultistack, ref int similarStackCount,
            ref bool checkOn)
        {
            if (!KebabLimitsModSettings.GlobalSimilarStackEnabled)
            {
                return;
            }

            Rect similarRect = new Rect(20f, y, width - 28f, 28f);
            int buttonCount = 2;
            if (perStorageMultistack)
            {
                buttonCount++;
            }

            if (similarStackCount >= 10)
            {
                buttonCount++;
            }

            float controlsWidth = buttonCount * 20f;
            Rect labelRect = similarRect;
            labelRect.width = Mathf.Max(0f, similarRect.width - controlsWidth);

            string labelText = LabelForMode(similarStackCount);
            string modeTooltip = TooltipForMode(similarStackCount);
            Rect highlightRect = LabelHighlightRect(labelRect, labelText);
            if (!modeTooltip.NullOrEmpty())
            {
                if (Mouse.IsOver(highlightRect))
                {
                    Widgets.DrawHighlight(highlightRect);
                }

                TooltipHandler.TipRegion(highlightRect, modeTooltip);
            }

            Widgets.Label(labelRect, labelText);

            WidgetRow widgetRow = new WidgetRow(similarRect.xMax, similarRect.y, UIDirection.LeftThenUp,
                similarRect.width);
            if (perStorageMultistack && widgetRow.ButtonIcon(
                    checkOn ? Widgets.CheckboxOnTex : Widgets.CheckboxOffTex,
                    "MultistackStorageTooltip".Translate(), null, null, null, doMouseoverSound: true, 20f))
            {
                checkOn = !checkOn;
                (checkOn ? SoundDefOf.Checkbox_TurnedOff : SoundDefOf.Checkbox_TurnedOn).PlayOneShotOnCamera();
            }

            if (widgetRow.ButtonIcon(TexButton.Plus, null, null, null, null, doMouseoverSound: true, 20f))
            {
                similarStackCount++;
                if (similarStackCount > MaxSimilarStackCount)
                {
                    similarStackCount = MaxSimilarStackCount;
                }

                SoundDefOf.DragSlider.PlayOneShotOnCamera();
            }

            if (widgetRow.ButtonIcon(TexButton.Minus, null, null, null, null, doMouseoverSound: true, 20f))
            {
                similarStackCount--;
                if (similarStackCount < -1)
                {
                    similarStackCount = -1;
                }

                SoundDefOf.DragSlider.PlayOneShotOnCamera();
            }

            if (similarStackCount >= 10 &&
                widgetRow.ButtonIcon(Widgets.CheckboxOffTex, null, null, null, null, doMouseoverSound: true, 20f))
            {
                similarStackCount = -1;
                SoundDefOf.DragSlider.PlayOneShotOnCamera();
            }

            y += 22f;
        }
    }
}
