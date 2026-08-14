namespace HSKKebabLimits
{
    public partial class StockpileLimitBundle
    {
        /// <summary>
        /// Returns whether upper-bound limits can reduce allowed stack sizes for hauling.
        /// </summary>
        public bool EnforcesUpperBound()
        {
            if (allowedLimitPercents.max < 1f)
            {
                return true;
            }

            return HasLimitsPerItem();
        }

        /// <summary>
        /// Returns whether the minimum slider bound is active (replenish threshold, not a haul ceiling).
        /// </summary>
        public bool EnforcesLowerBound()
        {
            return allowedLimitPercents.min > 0f;
        }

        /// <summary>
        /// Returns whether this stockpile allows multiple stacks of the same item under current mod rules.
        /// </summary>
        public bool IsAllowedMultistack()
        {
            if (KebabLimitsModSettings.GlobalMultistackMode == 0)
            {
                return false;
            }

            if (KebabLimitsModSettings.GlobalMultistackMode == 1)
            {
                return true;
            }

            return allowedMultistack;
        }

        /// <summary>
        /// Returns whether similar-stack counting is active for this stockpile profile.
        /// </summary>
        public bool EnforcesSimilarStackCap()
        {
            if (allowedSimilarStackCount > 0)
            {
                return KebabLimitsModSettings.GlobalSimilarStackEnabled;
            }

            return false;
        }
    }
}
