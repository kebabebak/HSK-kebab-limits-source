using System;
using UnityEngine;
using Verse;

namespace HSKKebabLimits
{
    /// <summary>
    /// Non-linear mapping between stockpile slider positions (0–1) and absolute stack counts.
    /// </summary>
    internal static class LogarithmicStackScale
    {
        private static int cachedUpperBound = -1;

        /// <summary>Upper stack count used as the slider ceiling (lazy-refreshed).</summary>
        public static int MaxStackSize
        {
            get
            {
                EnsureUpperBound();
                return cachedUpperBound;
            }
        }

        /// <summary>Rebuilds the slider ceiling from defs and mod settings.</summary>
        public static void InvalidateAndRefresh()
        {
            cachedUpperBound = -1;
            EnsureUpperBound();
        }

        /// <summary>
        /// When the max handle sits near zero, nudge it so min/max sliders stay at least <paramref name="minSeparation"/> apart.
        /// </summary>
        public static bool BumpMaxSliderIfTooClose(float normalizedMin, ref float normalizedMax, int minSeparation)
        {
            if (normalizedMax >= 1f)
            {
                return false;
            }

            int minAsCount = ToAbsoluteCount(normalizedMin);
            ToAbsoluteCount(normalizedMax);
            float requiredMax = ToNormalizedFraction(minAsCount + minSeparation);
            if (normalizedMax >= requiredMax)
            {
                return false;
            }

            normalizedMax = requiredMax;
            return true;
        }

        /// <summary>Slider fraction → absolute item count for the stockpile profile.</summary>
        public static int ToAbsoluteCount(float normalized)
        {
            EnsureUpperBound();
            float curved = ApplyDisplayCurve(normalized);
            return Mathf.RoundToInt(cachedUpperBound * curved);
        }

        /// <summary>Absolute count → slider fraction (inverse of <see cref="ToAbsoluteCount"/>).</summary>
        public static float ToNormalizedFraction(int absoluteCount)
        {
            EnsureUpperBound();
            double ratio = (double)absoluteCount / cachedUpperBound;
            double inverseExponent = 1d / KebabLimitsModSettings.DisplayLogFactor;
            return Mathf.Clamp01((float)Math.Pow(ratio, inverseExponent));
        }

        /// <summary>Slider fraction → per-item cap using that item's vanilla stack limit as the scale.</summary>
        public static int ToItemPercentageCount(float normalized, int vanillaStackLimit)
        {
            float curved = ApplyDisplayCurve(normalized);
            return Mathf.RoundToInt(Mathf.Ceil(vanillaStackLimit * curved));
        }

        private static float ApplyDisplayCurve(float normalized)
        {
            return Mathf.Pow(normalized, KebabLimitsModSettings.DisplayLogFactor);
        }

        private static void EnsureUpperBound()
        {
            if (cachedUpperBound > 0)
            {
                return;
            }

            int fromDefs = 0;
            foreach (ThingDef def in DefDatabase<ThingDef>.AllDefs)
            {
                fromDefs = Math.Max(fromDefs, def.stackLimit);
            }

            int cap = KebabLimitsModSettings.MaxStackSliderLimit;
            cachedUpperBound = cap > 0 ? cap : fromDefs;
            if (cachedUpperBound <= 0)
            {
                cachedUpperBound = 75;
            }
        }
    }
}
