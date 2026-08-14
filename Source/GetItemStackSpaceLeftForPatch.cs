using HarmonyLib;
using Verse;

namespace HSKKebabLimits
{
    /// <summary>Clamps vanilla cell stack-space queries to mod storage limit rules.</summary>
    [HarmonyPatch(typeof(GridsUtility), "GetItemStackSpaceLeftFor")]
    public static class GetItemStackSpaceLeftForPatch
    {
        public static void Postfix(ref int __result, IntVec3 c, Map map, ThingDef itemDef)
        {
            StockpileCapacityRules.ApplyCapacityClamp(c, map, itemDef, ref __result);
        }
    }
}
