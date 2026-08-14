using HarmonyLib;
using RimWorld;
using Verse;

namespace HSKKebabLimits
{
    /// <summary>Blocks haul destinations that violate upper, lower, zone-wide, or similar-stack limits.</summary>
    [HarmonyPatch(typeof(StoreUtility), "NoStorageBlockersIn")]
    public static class NoStorageBlockersInPatch
    {
        public static void Postfix(ref bool __result, IntVec3 c, Map map, Thing thing)
        {
            StockpileCapacityRules.ApplyNoStorageBlockersInPostfix(ref __result, c, map, thing);
        }
    }
}
