using HarmonyLib;
using RimWorld;
using Verse;

namespace HSKKebabLimits
{
    /// <summary>
    /// Persists StockpileLimitBundle inside each StorageSettings save data under key limitsLimitProfile.
    /// </summary>
    [HarmonyPatch(typeof(StorageSettings), "ExposeData")]
    public static class StockpileProfileSaveHook
    {
        /// <summary>Scribe_Deep hook — saves/loads limit profile bound to this storage settings instance.</summary>
        [HarmonyPostfix]
        public static void ExposeData(StorageSettings __instance)
        {
            StockpileLimitBundle target = StockpileProfileStore.GetOrNull(__instance);
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                Scribe_Deep.Look(ref target, "limitsLimitProfile");
            }
            else
            {
                Scribe_Deep.Look(ref target, "limitsLimitProfile");
            }

            if (target != null)
            {
                StockpileProfileStore.Set(__instance, target);
            }
        }
    }
}
