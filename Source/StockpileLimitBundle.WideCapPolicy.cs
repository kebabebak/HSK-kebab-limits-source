using System;
using Verse;

namespace HSKKebabLimits
{
    public partial class StockpileLimitBundle
    {
        public bool UsesStorageWideLimitFromLimitFor =>
            allowedSimilarStackCount == -1 && KebabLimitsModSettings.GlobalSimilarStackEnabled;

        /// <summary>
        /// True when storage-wide mode enforces a warehouse total cap (upper slider below default).
        /// At max slider (e.g. 0–2000 with both handles at 2000) there is no total cap — same as EnforcesUpperBound.
        /// </summary>
        public bool AppliesStorageWideTotalCap =>
            UsesStorageWideLimitFromLimitFor && allowedLimitPercents.max < 0.999f;

        /// <summary>
        /// True when storage-wide mode has any enforceable limit (warehouse total cap or per-item overrides).
        /// </summary>
        public bool HasEnforceableStorageWideLimits =>
            UsesStorageWideLimitFromLimitFor &&
            (AppliesStorageWideTotalCap || HasLimitsPerItem());

        /// <summary>Slider cap for storage-wide total mode (sum of all item units in the slot group).</summary>
        public int StorageWideTotalCap()
        {
            return LogarithmicStackScale.ToAbsoluteCount(allowedLimitPercents.max);
        }

        /// <summary>Remaining unit capacity under the storage-wide total cap.</summary>
        public int StorageWideTotalRemaining(int totalUnitsInStorage)
        {
            return Math.Max(0, StorageWideTotalCap() - totalUnitsInStorage);
        }

        /// <summary>
        /// Remaining haul space in storage-wide mode: total cap minus per-item override when set.
        /// Items without an override share only the warehouse total.
        /// </summary>
        public int EffectiveWideModeRemaining(ThingDef thingDef, int itemUnitsInStorage, int totalUnitsInStorage)
        {
            if (!HasPerItemLimit(thingDef))
            {
                if (!AppliesStorageWideTotalCap)
                {
                    return int.MaxValue;
                }

                return StorageWideTotalRemaining(totalUnitsInStorage);
            }

            int perItemRemaining = Math.Max(0, LimitFor(thingDef) - itemUnitsInStorage);
            if (!AppliesStorageWideTotalCap)
            {
                return perItemRemaining;
            }

            return Math.Min(perItemRemaining, StorageWideTotalRemaining(totalUnitsInStorage));
        }
    }
}
