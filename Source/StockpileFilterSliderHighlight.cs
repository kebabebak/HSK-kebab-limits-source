using HarmonyLib;
using UnityEngine;
using Verse;

namespace HSKKebabLimits
{
    /// <summary>
    /// Patches Widgets.FloatRange to tint labels and draw limit overlays during filter editing.
    /// </summary>
    internal static class StockpileFilterSliderHighlight
    {
        private static bool applied;

        /// <summary>
        /// Applies FloatRange highlight patches once when the filter UI is first drawn.
        /// </summary>
        public static void TryApplyOnce()
        {
            if (applied || KebabLimitsMod.HarmonyInstance == null)
            {
                return;
            }

            applied = true;
            Apply(KebabLimitsMod.HarmonyInstance);
        }

        /// <summary>
        /// Patches Widgets.FloatRange for stockpile limit slider highlighting.
        /// </summary>
        public static void Apply(Harmony harmony)
        {
            System.Reflection.MethodInfo target = AccessTools.Method(typeof(Widgets), "FloatRange", new[]
            {
                typeof(Rect), typeof(int), typeof(FloatRange).MakeByRefType(), typeof(float), typeof(float),
                typeof(string), typeof(ToStringStyle), typeof(float), typeof(GameFont), typeof(Color?)
            });

            if (target == null)
            {
                KebabLimitsLog.Warning("[HSK kebab limits] Widgets.FloatRange not found; slider highlight disabled.");
                return;
            }

            harmony.Patch(target,
                prefix: new HarmonyMethod(typeof(StockpileFilterSliderHighlight), nameof(FloatRangePrefix)),
                postfix: new HarmonyMethod(typeof(StockpileFilterSliderHighlight), nameof(FloatRangePostfix)));
            KebabLimitsLog.Message("[HSK kebab limits] Patched Widgets.FloatRange for storage filter highlights.");
        }

        /// <summary>
        /// Sets custom slider label color when drawing a non-default stockpile limit range.
        /// </summary>
        public static void FloatRangePrefix(FloatRange range, ref Color? sliderLabelColor)
        {
            if (!StockpileFilterSliderUi.DrawingThingFilter || !LimitHighlight.IsCustomLimit(range))
            {
                return;
            }

            if (sliderLabelColor == null)
            {
                sliderLabelColor = LimitHighlight.GetColor();
            }
        }

        /// <summary>
        /// Draws the colored limit bar overlay after a stockpile FloatRange slider renders.
        /// </summary>
        public static void FloatRangePostfix(Rect rect, FloatRange range, float min, float max)
        {
            if (!StockpileFilterSliderUi.DrawingThingFilter)
            {
                return;
            }

            LimitHighlight.DrawRangeBarOverlay(rect, range, min, max);
        }
    }
}
