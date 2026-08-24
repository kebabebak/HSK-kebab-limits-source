using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace HSKKebabLimits
{
    /// <summary>
    /// Draws limit sliders, hover-only min/max fields, and multistack controls for the active stockpile.
    ///
    /// Рисует ползунки лимитов, min/max поля только при наведении и элементы мультистака для активного склада.
    /// </summary>
    internal static class StockpileFilterSliderRow
    {
        private const float LabelBandHeight = 22f;
        private const float SliderBandHeight = 16f;
        private const float RowHeight = LabelBandHeight + SliderBandHeight;
        private const float FieldGap = 6f;

        private static StorageSettings directInputOwner;
        private static string directInputBufferLow;
        private static string directInputBufferUpp;
        private static bool showingDirectInputFields;

        private static readonly FieldInfo RangeControlTextColorField =
            AccessTools.Field(typeof(Widgets), "RangeControlTextColor");

        /// <summary>
        /// Vanilla Widgets.RangeControlTextColor (private); matches quality / hit-points slider captions.
        ///
        /// Цвет подписей vanilla Widgets.RangeControlTextColor (private); как у ползунков качества / HP.
        /// </summary>
        private static Color RangeControlTextColor
        {
            get
            {
                if (RangeControlTextColorField != null)
                {
                    return (Color)RangeControlTextColorField.GetValue(null);
                }

                return new Color(0.5f, 0.5f, 0.5f);
            }
        }

        /// <summary>
        /// Formats the stack limit slider label as absolute counts or percentages per mod settings.
        ///
        /// Форматирует подпись ползунка лимита стака как абсолютные значения или проценты по настройкам мода.
        /// </summary>
        public static string FormatLimitLabel(float min, float max)
        {
            string text = "KebabLimitsStackLimit".Translate();
            if (KebabLimitsModSettings.PercentageMode)
            {
                return text + " " + min.ToStringByStyle(ToStringStyle.PercentZero) + " - " +
                       max.ToStringByStyle(ToStringStyle.PercentZero);
            }

            int limit = LogarithmicStackScale.ToAbsoluteCount(min);
            int limit2 = LogarithmicStackScale.ToAbsoluteCount(max);
            return $"{text} {limit} - {limit2}";
        }

        /// <summary>
        /// Draws stockpile limit controls when the storage tab filter window is active.
        ///
        /// Рисует элементы лимита склада, когда активно окно фильтра вкладки хранилища.
        /// </summary>
        public static void Draw(ref float y, float width)
        {
            if (!ActiveStockpileTabContext.DrawingStorageTab ||
                ActiveStockpileTabContext.ActiveStorageSettings == null ||
                ActiveStockpileTabContext.ActiveStorageSettings.owner is Building_Bookcase)
            {
                ResetDirectInputState();
                directInputOwner = null;
                return;
            }

            bool perStorageMultistack = KebabLimitsModSettings.GlobalMultistackMode == 2;
            float rowWidth = width - ((perStorageMultistack && !KebabLimitsModSettings.GlobalSimilarStackEnabled) ? 55f : 20f);
            Rect row = new Rect(20f, y, rowWidth, RowHeight);
            StockpileLimitBundle profile = StockpileProfileStore.Get(ActiveStockpileTabContext.ActiveStorageSettings);
            FloatRange range = profile.allowedLimitPercents;
            bool checkOn = profile.allowedMultistack;
            int similarStackCount = profile.SimilarStackCountRaw;
            int filterDisplayMode = profile.FilterListDisplayMode;
            DrawSliderOrDirectInput(row, ref range);
            DrawMultistackCheckbox(perStorageMultistack, width, row, ref checkOn);

            y += RowHeight;
            y += 5f;
            StockpileFilterSimilarStackRow.Draw(ref y, width, perStorageMultistack, ref similarStackCount, ref checkOn);
            StockpileFilterDisplayRow.Draw(ref y, width, ref filterDisplayMode);
            CommitProfileChanges(profile, range, checkOn, similarStackCount, filterDisplayMode);
            Text.Font = GameFont.Small;
        }

        private static void GetFieldRects(Rect labelBand, out Rect lowRect, out Rect highRect)
        {
            float fieldWidth = Mathf.Max(0f, (labelBand.width - FieldGap) * 0.5f);
            lowRect = new Rect(labelBand.x, labelBand.y, fieldWidth, labelBand.height);
            highRect = new Rect(lowRect.xMax + FieldGap, labelBand.y, fieldWidth, labelBand.height);
        }

        /// <summary>
        /// Drops typed min/max text and IMGUI focus so a later stockpile cannot reuse the previous editor.
        ///
        /// Сбрасывает введённый min/max текст и IMGUI-фокус, чтобы следующий склад не подхватил предыдущий редактор.
        /// </summary>
        private static void ResetDirectInputState()
        {
            directInputBufferLow = null;
            directInputBufferUpp = null;
            if (showingDirectInputFields)
            {
                UI.UnfocusCurrentTextField();
                showingDirectInputFields = false;
            }
        }

        /// <summary>
        /// Binds the numeric editor to this StorageSettings. Grouped stockpiles share one settings object.
        ///
        /// Привязывает числовой редактор к этим StorageSettings. Сгруппированные склады делят один объект настроек.
        /// </summary>
        private static void BindDirectInputTo(StorageSettings settings)
        {
            if (directInputOwner == settings)
            {
                return;
            }

            directInputOwner = settings;
            ResetDirectInputState();
        }

        /// <summary>
        /// Draws the stack-limit caption, or min/max fields while the pointer is over that caption.
        ///
        /// Рисует подпись лимита стака или min/max поля, пока курсор над этой подписью.
        /// </summary>
        private static void DrawSliderOrDirectInput(Rect row, ref FloatRange range)
        {
            BindDirectInputTo(ActiveStockpileTabContext.ActiveStorageSettings);

            Rect labelBand = new Rect(row.x, row.y, row.width, LabelBandHeight);
            Rect sliderBand = new Rect(row.x, labelBand.yMax, row.width, SliderBandHeight);
            GetFieldRects(labelBand, out Rect lowRect, out Rect highRect);

            Rect captionHoverRect = LimitCaptionHoverRect(labelBand, range);
            bool hoveringCaption = Mouse.IsOver(captionHoverRect);
            bool hoveringFields = showingDirectInputFields && Mouse.IsOver(labelBand);
            if (hoveringCaption || hoveringFields)
            {
                DrawLimitFields(lowRect, highRect, ref range);
                showingDirectInputFields = true;
            }
            else
            {
                if (showingDirectInputFields)
                {
                    UI.UnfocusCurrentTextField();
                    showingDirectInputFields = false;
                }

                directInputBufferLow = null;
                directInputBufferUpp = null;
                DrawLimitLabel(labelBand, range);
            }

            // Empty label: FloatRange owns only the bar band, so the caption never starts a drag.
            Color? labelColor = LimitHighlight.IsCustomLimit(range) ? LimitHighlight.GetColor() : (Color?)null;
            Widgets.FloatRange(sliderBand, 11263124, ref range, 0f, 1f, string.Empty, ToStringStyle.Integer, 0f,
                GameFont.Tiny, labelColor);
            LimitHighlight.DrawRangeBarOverlay(sliderBand, range);
            LogarithmicStackScale.BumpMaxSliderIfTooClose(range.min, ref range.max, 1);
        }

        private static Rect LimitCaptionHoverRect(Rect labelBand, FloatRange range)
        {
            GameFont previousFont = Text.Font;
            Text.Font = GameFont.Small;
            Vector2 size = Text.CalcSize(FormatLimitLabel(range.min, range.max));
            Text.Font = previousFont;
            float width = Mathf.Min(size.x, labelBand.width);
            float height = Mathf.Min(size.y, labelBand.height);
            return new Rect(
                labelBand.x + (labelBand.width - width) * 0.5f,
                labelBand.y + (labelBand.height - height) * 0.5f,
                width,
                height);
        }

        private static void DrawLimitLabel(Rect labelBand, FloatRange range)
        {
            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;
            // Match vanilla FloatRange / QualityRange caption: Small + RangeControlTextColor + LabelFit.
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = LimitHighlight.IsCustomLimit(range)
                ? LimitHighlight.GetColor()
                : RangeControlTextColor;

            Rect captionHoverRect = LimitCaptionHoverRect(labelBand, range);
            if (Mouse.IsOver(captionHoverRect))
            {
                Widgets.DrawHighlight(captionHoverRect);
            }

            Widgets.LabelFit(labelBand, FormatLimitLabel(range.min, range.max));
            GUI.color = previousColor;
            Text.Anchor = previousAnchor;
            Text.Font = previousFont;
        }

        private static void DrawLimitFields(Rect lowRect, Rect highRect, ref FloatRange range)
        {
            int maxLimit = LogarithmicStackScale.MaxStackSize;
            int lowVal = LogarithmicStackScale.ToAbsoluteCount(range.min);
            int highVal = LogarithmicStackScale.ToAbsoluteCount(range.max);

            if (directInputBufferLow.NullOrEmpty())
            {
                directInputBufferLow = lowVal.ToString();
            }

            if (directInputBufferUpp.NullOrEmpty())
            {
                directInputBufferUpp = highVal.ToString();
            }

            int previousLow = lowVal;
            int previousHigh = highVal;
            Widgets.TextFieldNumeric(lowRect, ref lowVal, ref directInputBufferLow, 0f, maxLimit);
            Widgets.TextFieldNumeric(highRect, ref highVal, ref directInputBufferUpp, 0f, maxLimit);

            lowVal = Mathf.Clamp(lowVal, 0, maxLimit);
            highVal = Mathf.Clamp(highVal, 0, maxLimit);
            if (highVal < lowVal)
            {
                highVal = lowVal;
                directInputBufferUpp = highVal.ToString();
            }

            if (lowVal != previousLow || highVal != previousHigh)
            {
                range.min = LogarithmicStackScale.ToNormalizedFraction(lowVal);
                range.max = LogarithmicStackScale.ToNormalizedFraction(highVal);
                LogarithmicStackScale.BumpMaxSliderIfTooClose(range.min, ref range.max, 1);
            }
        }

        private static void DrawMultistackCheckbox(bool perStorageMultistack, float width, Rect row, ref bool checkOn)
        {
            if (!perStorageMultistack || KebabLimitsModSettings.GlobalSimilarStackEnabled)
            {
                return;
            }

            float checkboxY = row.y + (row.height - 20f) * 0.5f;
            Widgets.Checkbox(new Vector2(width - 26f, checkboxY), ref checkOn, 20f);
            Rect checkboxRect = new Rect(width - 28f, checkboxY - 2f, 24f, 24f);
            Widgets.DrawHighlightIfMouseover(checkboxRect);
            if (Mouse.IsOver(checkboxRect))
            {
                GUI.DrawTexture(checkboxRect, TexUI.HighlightTex);
            }

            TooltipHandler.TipRegion(checkboxRect, "MultistackStorageTooltip".Translate());
        }

        private static void CommitProfileChanges(StockpileLimitBundle profile, FloatRange range, bool checkOn,
            int similarStackCount, int filterDisplayMode)
        {
            // Keep stored values when their editor is not drawn (global toggle off). Item rows hidden
            // by show-allowed/forbidden stay on the profile and are not part of this commit.
            // Сохранять значения, если их редактор не рисуется (глобальный тумблер выключен). Строки
            // предметов, скрытые «только разрешённое/запрещённое», в профиле остаются и сюда не входят.
            bool storeMultistack = KebabLimitsModSettings.GlobalMultistackMode == 2
                ? checkOn
                : profile.allowedMultistack;
            int storeSimilar = KebabLimitsModSettings.GlobalSimilarStackEnabled
                ? similarStackCount
                : profile.SimilarStackCountRaw;
            int storeDisplay = KebabLimitsModSettings.AddFilterDisplaySettings
                ? filterDisplayMode
                : profile.FilterListDisplayMode;

            if (profile.allowedLimitPercents == range && profile.allowedMultistack == storeMultistack &&
                profile.SimilarStackCountRaw == storeSimilar &&
                profile.FilterListDisplayMode == storeDisplay)
            {
                return;
            }

            bool eject = profile.allowedLimitPercents.max != range.max ||
                         profile.SimilarStackCountRaw != storeSimilar;
            profile.allowedLimitPercents = range;
            profile.allowedMultistack = storeMultistack;
            profile.SimilarStackCountRaw = storeSimilar;
            profile.FilterListDisplayMode = storeDisplay;
            // Set clamps per-item overrides when storage-wide / setting disallows exceeding the slider.
            StockpileProfileStore.Set(ActiveStockpileTabContext.ActiveStorageSettings, profile);
            profile.RemoveAllCache();
            StockpileCapacityRules.InvalidateHaulCachesForParent(ActiveStockpileTabContext.ActiveStorageSettings.owner,
                profile);
            if (eject)
            {
                StockpileCapacityRules.RequestOverflowReconcileAfterLimitChange(
                    ActiveStockpileTabContext.ActiveStorageSettings.owner, profile);
            }
        }
    }
}
