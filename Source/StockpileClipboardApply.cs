using HarmonyLib;
using RimWorld;
using Verse;

namespace HSKKebabLimits
{
    /// <summary>
    /// Harmony postfix on vanilla storage clipboard paste — restores HSK limits and optionally cascade-ejects overflow.
    /// </summary>
    [HarmonyPatch(typeof(StorageSettingsClipboard), "PasteInto")]
    public static class StockpileClipboardApplyHook
    {
        /// <summary>Applies copied limit profile to target storage and ejects if hard ejection is enabled.</summary>
        public static void Postfix(StorageSettings s)
        {
            if (StockpileClipboardSnapshot.Data == null)
            {
                return;
            }

            StockpileLimitBundle profile = new StockpileLimitBundle(StockpileClipboardSnapshot.Data);
            profile.RemoveAllCache();
            StockpileProfileStore.Set(s, profile);
            StockpileCapacityRules.InvalidateHaulCachesForParent(s.owner, profile);

            if (KebabLimitsModSettings.EnableHardEjection)
            {
                StockpileCapacityRules.ReconcileOverflowAfterLimitChange(s.owner, profile);
            }
        }
    }
}
