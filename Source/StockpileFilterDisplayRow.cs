using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace HSKKebabLimits
{
    /// <summary>
    /// Draws the per-storage Show all / allowed only / forbidden only row in the filter window,
    /// and the expand-categories checkbox when expand-categories is per-storage.
    ///
    /// Рисует строку склада «Показывать все / только разрешённое / только запрещённое» в окне фильтра
    /// и чекбокс разворачивания категорий, когда разворачивание действует на отдельные склады.
    /// </summary>
    internal static class StockpileFilterDisplayRow
    {
        public const int ModeShowAll = 0;
        public const int ModeAllowedOnly = 1;
        public const int ModeForbiddenOnly = 2;

        private const int ModeCount = 3;
        private const float RowTextHeight = 22f;

        /// <summary>
        /// Returns a highlight rect that matches the drawn label height instead of the full row slot.
        ///
        /// Возвращает прямоугольник подсветки по высоте текста, а не по всей строке.
        /// </summary>
        private static Rect LabelHighlightRect(Rect labelRect)
        {
            Rect highlightRect = labelRect;
            highlightRect.height = Mathf.Min(RowTextHeight, labelRect.height);
            highlightRect.y += (labelRect.height - highlightRect.height) * 0.5f;
            return highlightRect;
        }

        /// <summary>
        /// Returns the label for the current filter-list display mode.
        ///
        /// Возвращает подпись текущего режима отображения списка фильтра.
        /// </summary>
        private static string LabelForMode(int mode)
        {
            switch (mode)
            {
                case ModeAllowedOnly:
                    return "FilterListShowAllowedOnly".Translate();
                case ModeForbiddenOnly:
                    return "FilterListShowForbiddenOnly".Translate();
                default:
                    return "FilterListShowAll".Translate();
            }
        }

        /// <summary>
        /// Returns the tooltip for the current filter-list display mode.
        ///
        /// Возвращает подсказку текущего режима отображения списка фильтра.
        /// </summary>
        private static string TooltipForMode(int mode)
        {
            switch (mode)
            {
                case ModeAllowedOnly:
                    return "FilterListShowAllowedOnlyTooltip".Translate();
                case ModeForbiddenOnly:
                    return "FilterListShowForbiddenOnlyTooltip".Translate();
                default:
                    return "FilterListShowAllTooltip".Translate();
            }
        }

        /// <summary>
        /// Draws the display-mode row when the mod setting is enabled.
        ///
        /// Рисует строку режима отображения, если настройка мода включена.
        /// </summary>
        public static void Draw(ref float y, float width, ref int displayMode, ref bool expandCategories)
        {
            if (!KebabLimitsModSettings.AddFilterDisplaySettings)
            {
                return;
            }

            Rect rowRect = new Rect(20f, y, width - 28f, 28f);
            const float controlsWidth = 40f;
            Rect labelRect = rowRect;
            labelRect.width = Mathf.Max(0f, rowRect.width - controlsWidth);

            string labelText = LabelForMode(displayMode);
            string modeTooltip = TooltipForMode(displayMode);
            Rect highlightRect = LabelHighlightRect(labelRect);
            if (!modeTooltip.NullOrEmpty())
            {
                if (Mouse.IsOver(highlightRect))
                {
                    Widgets.DrawHighlight(highlightRect);
                }

                TooltipHandler.TipRegion(highlightRect, modeTooltip);
            }

            OverflowMarqueeLabel.Draw(labelRect, labelText, highlightRect);

            WidgetRow widgetRow = new WidgetRow(rowRect.xMax, rowRect.y, UIDirection.LeftThenUp, rowRect.width);
            if (widgetRow.ButtonIcon(TexButton.Plus, null, null, null, null, doMouseoverSound: true, 20f))
            {
                displayMode = (displayMode + 1) % ModeCount;
                SoundDefOf.DragSlider.PlayOneShotOnCamera();
            }

            if (widgetRow.ButtonIcon(TexButton.Minus, null, null, null, null, doMouseoverSound: true, 20f))
            {
                displayMode = (displayMode + ModeCount - 1) % ModeCount;
                SoundDefOf.DragSlider.PlayOneShotOnCamera();
            }

            y += 22f;
            DrawExpandCategoriesRow(ref y, width, ref expandCategories);
        }

        /// <summary>
        /// Draws the per-storage expand-categories checkbox under the list display row.
        ///
        /// Рисует чекбокс разворачивания категорий этого склада под строкой режима списка.
        /// </summary>
        private static void DrawExpandCategoriesRow(ref float y, float width, ref bool expandCategories)
        {
            if (KebabLimitsModSettings.ExpandFilterCategoriesMode != 1)
            {
                return;
            }

            Rect rowRect = new Rect(20f, y, width - 28f, 28f);
            const float controlsWidth = 20f;
            Rect labelRect = rowRect;
            labelRect.width = Mathf.Max(0f, rowRect.width - controlsWidth);

            string labelText = "ExpandFilterCategories".Translate();
            string tooltip = "ExpandFilterCategoriesStorageTooltip".Translate();
            Rect highlightRect = LabelHighlightRect(labelRect);
            if (!tooltip.NullOrEmpty())
            {
                if (Mouse.IsOver(highlightRect))
                {
                    Widgets.DrawHighlight(highlightRect);
                }

                TooltipHandler.TipRegion(highlightRect, tooltip);
            }

            OverflowMarqueeLabel.Draw(labelRect, labelText, highlightRect);

            WidgetRow widgetRow = new WidgetRow(rowRect.xMax, rowRect.y, UIDirection.LeftThenUp, rowRect.width);
            if (widgetRow.ButtonIcon(
                    expandCategories ? Widgets.CheckboxOnTex : Widgets.CheckboxOffTex,
                    tooltip, null, null, null, doMouseoverSound: true, 20f))
            {
                expandCategories = !expandCategories;
                (expandCategories ? SoundDefOf.Checkbox_TurnedOn : SoundDefOf.Checkbox_TurnedOff)
                    .PlayOneShotOnCamera();
            }

            y += 22f;
        }
    }
}
