using HarmonyLib;
using Verse;
using Verse.AI;

namespace HSKKebabLimits
{
    /// <summary>Cancels pickup when a HaulToCell destination became full after the job was assigned.</summary>
    [HarmonyPatch(typeof(Pawn_CarryTracker), nameof(Pawn_CarryTracker.TryStartCarry), new[] { typeof(Thing), typeof(int), typeof(bool) })]
    public static class HaulTryStartCarryRevalidatePatch
    {
        public static bool Prefix(Pawn_CarryTracker __instance, Thing item, ref int __result)
        {
            if (__instance?.pawn == null || item == null)
            {
                return true;
            }

            if (!StockpileCapacityRules.TryAbortHaulToCellWithoutSpace(__instance.pawn, item, "try-start-carry"))
            {
                return true;
            }

            __result = 0;
            return false;
        }
    }
}
