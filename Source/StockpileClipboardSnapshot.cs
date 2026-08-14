using HarmonyLib;
using RimWorld;

namespace HSKKebabLimits
{
    /// <summary>
    /// Harmony prefix on vanilla storage clipboard copy — snapshots the HSK limit profile alongside filter settings.
    /// </summary>
    [HarmonyPatch(typeof(StorageSettingsClipboard), "Copy")]
    public static class StockpileClipboardSnapshot
    {
        /// <summary>Copied limit profile kept until PasteInto runs.</summary>
        public static StockpileLimitBundle Data;

        /// <summary>Stores current StockpileLimitBundle before vanilla copies filter settings.</summary>
        public static void Prefix(StorageSettings s)
        {
            Data = StockpileProfileStore.Get(s);
        }
    }
}
