using System;
using Multiplayer.API;
using Verse;

namespace HSKKebabLimits
{
    /// <summary>
    /// Per-stockpile storage limit data: slider range, multistack rules, and optional per-item overrides.
    /// </summary>
    public partial class StockpileLimitBundle : IExposable
    {
        internal readonly PerItemOverrides perItemOverrides = new PerItemOverrides();
        internal readonly CategoryLimitIndex categoryLimits = new CategoryLimitIndex();

        public FloatRange allowedLimitPercents = FloatRange.ZeroToOne;
        public bool allowedMultistack = true;
        private int allowedSimilarStackCount = -1;

        public int SimilarStackCount =>
            allowedSimilarStackCount > 0 ? allowedSimilarStackCount : 0;

        public int SimilarStackCountRaw
        {
            get => allowedSimilarStackCount;
            set => allowedSimilarStackCount = value;
        }

        /// <summary>
        /// Creates a stockpile limit profile with default full-range slider settings.
        /// </summary>
        public StockpileLimitBundle()
        {
        }

        /// <summary>
        /// Deep-copies limit settings from another stockpile profile.
        /// </summary>
        public StockpileLimitBundle(StockpileLimitBundle other)
        {
            perItemOverrides.CopyFrom(other.perItemOverrides);
            allowedLimitPercents = other.allowedLimitPercents;
            allowedMultistack = other.allowedMultistack;
            allowedSimilarStackCount = other.allowedSimilarStackCount;
        }

        /// <summary>
        /// Saves and loads limit data with the parent storage settings in save games.
        /// </summary>
        public void ExposeData()
        {
            Scribe_Values.Look(ref allowedLimitPercents, "limitsLimitPercents", FloatRange.ZeroToOne);
            Scribe_Values.Look(ref allowedMultistack, "limitsAllowedMultistack", defaultValue: true);
            Scribe_Values.Look(ref allowedSimilarStackCount, "allowedSimilarStackCount", -1);
            perItemOverrides.ExposeData();
        }

        /// <summary>
        /// Clears cached category limit calculations after profile changes.
        /// </summary>
        public void RemoveAllCache()
        {
            categoryLimits.Clear();
        }

        /// <summary>
        /// Synchronizes limit profile fields across multiplayer clients.
        /// </summary>
        [SyncWorker]
        public static void SyncStockpileLimitBundle(SyncWorker sync, ref StockpileLimitBundle data)
        {
            sync.Bind(ref data.allowedLimitPercents);
            sync.Bind(ref data.allowedMultistack);
            sync.Bind(ref data.allowedSimilarStackCount);
        }
    }
}
