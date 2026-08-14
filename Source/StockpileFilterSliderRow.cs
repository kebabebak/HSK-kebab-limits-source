using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace HSKKebabLimits
{
    /// <summary>
    /// Draws limit sliders, optional label numeric fields, and multistack controls for the active stockpile.
    ///
    /// Рисует ползунки лимитов, опциональные числовые поля на подписи и элементы мультистака для активного склада.
    /// </summary>
    internal static class StockpileFilterSliderRow
    {
        private const float LabelBandHeight = 22f;
        private const float SliderBandHeight = 16f;
        private const float RowHeight = LabelBandHeight + SliderBandHeight;
        private const float FieldGap = 6f;

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
                return;
            }

            bool perStorageMultistack = KebabLimitsModSettings.GlobalMultistackMode == 2;
            float rowWidth = width - ((perStorageMultistack && !KebabLimitsModSettings.GlobalSimilarStackEnabled) ? 55f : 20f);
            Rect row = new Rect(20f, y, rowWidth, RowHeight);
            StockpileLimitBundle profile = StockpileProfileStore.Get(ActiveStockpileTabContext.ActiveStorageSettings);
            FloatRange range = profile.allowedLimitPercents;
            bool checkOn = profile.allowedMultistack;
            int similarStackCount = profile.SimilarStackCountRaw;
            DrawSliderOrDirectInput(row, ref range);
            DrawMultistackCheckbox(perStorageMultistack, width, row, ref checkOn);

            y += RowHeight;
            y += 5f;
            StockpileFilterSimilarStackRow.Draw(ref y, width, perStorageMultistack, ref similarStackCount, ref checkOn);
            CommitProfileChanges(profile, range, checkOn, similarStackCount);
            Text.Font = GameFont.Small;
        }

        private static void GetFieldRects(Rect labelBand, out Rect lowRect, out Rect highRect)
        {
            float fieldWidth = Mathf.Max(0f, (labelBand.width - FieldGap) * 0.5f);
            lowRect = new Rect(labelBand.x, labelBand.y, fieldWidth, labelBand.height);
            highRect = new Rect(lowRect.xMax + FieldGap, labelBand.y, fieldWidth, labelBand.height);
        }

        /// <summary>
        /// Draws the caption/fields in a band above the slider so Widgets.FloatRange cannot steal label clicks.
        ///
        /// Рисует подпись/поля в полосе над шкалой, чтобы Widgets.FloatRange не перехватывал клики по тексту.
        /// </summary>
        private static void DrawSliderOrDirectInput(Rect row, ref FloatRange range)
        {
            Rect labelBand = new Rect(row.x, row.y, row.width, LabelBandHeight);
            Rect sliderBand = new Rect(row.x, labelBand.yMax, row.width, SliderBandHeight);
            GetFieldRects(labelBand, out Rect lowRect, out Rect highRect);

            bool overLabel = Mouse.IsOver(labelBand);
            bool overFields = Mouse.IsOver(lowRect) || Mouse.IsOver(highRect);
            bool pinned = StockpileFilterSliderUi.DirectInputMode;

            if (Event.current.type == EventType.MouseDown && Event.current.button == 0)
            {
                if (overFields)
                {
                    StockpileFilterSliderUi.DirectInputMode = true;
                    pinned = true;
                }
                else if (pinned)
                {
                    // Any click outside the two fields collapses back to the caption.
                    StockpileFilterSliderUi.DirectInputMode = false;
                    pinned = false;
                }
            }

            if (Event.current.type == EventType.KeyDown &&
                (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.Escape))
            {
                StockpileFilterSliderUi.DirectInputMode = false;
                pinned = false;
                Event.current.Use();
            }

            pinned = StockpileFilterSliderUi.DirectInputMode;
            bool showFields = pinned || overLabel || overFields;

            if (showFields)
            {
                DrawLimitFields(labelBand, lowRect, highRect, pinned, ref range);
            }
            else
            {
                StockpileFilterSliderUi.DirectInputMode_Buffer_Low = "";
                StockpileFilterSliderUi.DirectInputMode_Buffer_Upp = "";
                DrawLimitLabel(labelBand, range);
            }

            // Empty label: FloatRange owns only the bar band, so the caption never starts a drag.
            Color? labelColor = LimitHighlight.IsCustomLimit(range) ? LimitHighlight.GetColor() : (Color?)null;
            Widgets.FloatRange(sliderBand, 11263124, ref range, 0f, 1f, string.Empty, ToStringStyle.Integer, 0f,
                GameFont.Tiny, labelColor);
            LimitHighlight.DrawRangeBarOverlay(sliderBand, range);
            LogarithmicStackScale.BumpMaxSliderIfTooClose(range.min, ref range.max, 1);
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

            if (Mouse.IsOver(labelBand))
            {
                Widgets.DrawHighlight(labelBand);
            }

            Widgets.LabelFit(labelBand, FormatLimitLabel(range.min, range.max));
            GUI.color = previousColor;
            Text.Anchor = previousAnchor;
            Text.Font = previousFont;
        }

        private static void DrawLimitFields(Rect labelBand, Rect lowRect, Rect highRect, bool pinned,
            ref FloatRange range)
        {
            int maxLimit = LogarithmicStackScale.MaxStackSize;
            int lowVal = LogarithmicStackScale.ToAbsoluteCount(range.min);
            int highVal = LogarithmicStackScale.ToAbsoluteCount(range.max);

            if (!pinned)
            {
                StockpileFilterSliderUi.DirectInputMode_Buffer_Low = lowVal.ToString();
                StockpileFilterSliderUi.DirectInputMode_Buffer_Upp = highVal.ToString();
            }
            else
            {
                if (StockpileFilterSliderUi.DirectInputMode_Buffer_Low.NullOrEmpty())
                {
                    StockpileFilterSliderUi.DirectInputMode_Buffer_Low = lowVal.ToString();
                }

                if (StockpileFilterSliderUi.DirectInputMode_Buffer_Upp.NullOrEmpty())
                {
                    StockpileFilterSliderUi.DirectInputMode_Buffer_Upp = highVal.ToString();
                }
            }

            int previousLow = lowVal;
            int previousHigh = highVal;
            Widgets.TextFieldNumeric(lowRect, ref lowVal, ref StockpileFilterSliderUi.DirectInputMode_Buffer_Low, 0f,
                maxLimit);
            Widgets.TextFieldNumeric(highRect, ref highVal, ref StockpileFilterSliderUi.DirectInputMode_Buffer_Upp, 0f,
                maxLimit);

            lowVal = Mathf.Clamp(lowVal, 0, maxLimit);
            highVal = Mathf.Clamp(highVal, 0, maxLimit);
            if (highVal < lowVal)
            {
                highVal = lowVal;
                StockpileFilterSliderUi.DirectInputMode_Buffer_Upp = highVal.ToString();
            }

            if (pinned || lowVal != previousLow || highVal != previousHigh)
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
            int similarStackCount)
        {
            if (profile.allowedLimitPercents == range && profile.allowedMultistack == checkOn &&
                profile.SimilarStackCountRaw == similarStackCount)
            {
                return;
            }

            bool eject = profile.allowedLimitPercents.max != range.max ||
                         profile.SimilarStackCountRaw != similarStackCount;
            profile.allowedLimitPercents = range;
            profile.allowedMultistack = checkOn;
            profile.SimilarStackCountRaw = similarStackCount;
            // Set clamps per-item overrides when storage-wide / setting disallows exceeding the slider.
            StockpileProfileStore.Set(ActiveStockpileTabContext.ActiveStorageSettings, profile);
            profile.RemoveAllCache();
            StockpileCapacityRules.InvalidateHaulCachesForParent(ActiveStockpileTabContext.ActiveStorageSettings.owner,
                profile);
            if (eject && KebabLimitsModSettings.EnableHardEjection)
            {
                StockpileCapacityRules.ReconcileOverflowAfterLimitChange(ActiveStockpileTabContext.ActiveStorageSettings.owner, profile);
            }
        }
    }
}
