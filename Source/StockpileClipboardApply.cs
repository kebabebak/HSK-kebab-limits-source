using HarmonyLib;
using RimWorld;
using Verse;

namespace HSKKebabLimits
{
    /// <summary>
    /// Harmony postfix on vanilla storage clipboard paste: restores HSK limits and may eject overflow.
    ///
    /// Harmony postfix на вставку буфера склада: восстанавливает лимиты HSK и может сбросить переполнение.
    /// </summary>
    [HarmonyPatch(typeof(StorageSettingsClipboard), "PasteInto")]
    public static class StockpileClipboardApplyHook
    {
        /// <summary>
        /// Applies the copied limit profile to the target storage and may eject overflow.
        ///
        /// Применяет скопированный профиль лимита к целевому складу и может сбросить переполнение.
        /// </summary>
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

            StockpileCapacityRules.RequestOverflowReconcileAfterLimitChange(s.owner, profile);
        }
    }
}
