using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace HSKKebabLimits
{
    /// <summary>Clamps haul-to-storage job count to remaining space under mod storage limits.</summary>
    [HarmonyPatch(typeof(HaulAIUtility), "HaulToCellStorageJob")]
    public static class HaulToCellStorageJobPatch
    {
        public static void Postfix(ref Job __result, Thing t)
        {
            if (__result == null || t?.def == null)
            {
                return;
            }

            IntVec3 dest = __result.targetB.Cell;
            Map map = t.Map ?? t.MapHeld;
            if (map == null && __result.targetA.Thing != null)
            {
                map = __result.targetA.Thing.Map ?? __result.targetA.Thing.MapHeld;
            }

            if (map == null || !dest.IsValid)
            {
                return;
            }

            int count = __result.count > 0 ? __result.count : t.stackCount;
            StockpileCapacityRules.ApplyCapacityClamp(dest, map, t.def, ref count);
            if (count <= 0)
            {
                KebabLimitsLog.MessageVerbose(
                    $"[HSK kebab limits] HaulJobRejected thing={t.def.defName} dest={dest} storage=\"{dest.GetSlotGroup(map)?.parent?.SlotYielderLabel() ?? "none"}\" stackCount={t.stackCount}");
                __result = null;
                return;
            }

            __result.count = count;
        }
    }
}
