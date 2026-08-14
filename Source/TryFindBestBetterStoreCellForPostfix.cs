using HarmonyLib;
using RimWorld;
using Verse;

namespace HSKKebabLimits
{
    /// <summary>Rejects store cells that pass vanilla checks but have zero kebab limits space for the haulable.</summary>
    [HarmonyPatch(typeof(StoreUtility), nameof(StoreUtility.TryFindBestBetterStoreCellFor))]
    public static class TryFindBestBetterStoreCellForPostfix
    {
        public static void Postfix(ref bool __result, Thing t, Map map, ref IntVec3 foundCell)
        {
            if (!__result || t?.def == null || map == null || !foundCell.IsValid)
            {
                return;
            }

            IntVec3 vanillaCell = foundCell;
            if (StockpileCapacityRules.RetargetStoreCellIfNeeded(ref foundCell, map, t.def, out int space))
            {
                if (foundCell != vanillaCell)
                {
                    KebabLimitsLog.MessageVerbose(
                        $"[HSK kebab limits] HaulDestRetargeted thing={t.def.defName} old={vanillaCell} new={foundCell} space={space} storage=\"{foundCell.GetSlotGroup(map)?.parent?.SlotYielderLabel() ?? "none"}\" stackCount={t.stackCount}");
                }

                return;
            }

            __result = false;
            KebabLimitsLog.MessageVerbose(
                $"[HSK kebab limits] HaulDestRejected thing={t.def.defName} cell={vanillaCell} storage=\"{vanillaCell.GetSlotGroup(map)?.parent?.SlotYielderLabel() ?? "none"}\" stackCount={t.stackCount}");
        }
    }
}
