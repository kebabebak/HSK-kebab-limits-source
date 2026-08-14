using System;
using HarmonyLib;
using RimWorld;
using Verse;

namespace HSKKebabLimits
{
    /// <summary>Limits stack absorption on existing piles to remaining per-cell storage space.</summary>
    [HarmonyPatch(typeof(ThingUtility), "TryAbsorbStackNumToTake")]
    public static class TryAbsorbStackNumToTakePatch
    {
        public static void Postfix(ref int __result, Thing thing, bool respectStackLimit)
        {
            if (!respectStackLimit || !thing.Spawned)
            {
                return;
            }

            SlotGroup slotGroup = thing.Map.haulDestinationManager.SlotGroupAt(thing.Position);
            if (slotGroup == null)
            {
                return;
            }

            StockpileLimitBundle profile = StockpileProfileStore.Get(slotGroup.Settings);
            if (profile != null)
            {
                if (profile.HasEnforceableStorageWideLimits)
                {
                    int spaceLeft = StockpileCapacityRules.GetAllocatableUnitsAt(thing.Position, thing.Map, thing.def, __result);
                    if (__result > spaceLeft)
                    {
                        KebabLimitsLog.MessageVerbose(
                            $"[HSK kebab limits] TryAbsorbStackLimit storage-wide thing={thing.def.defName} pos={thing.Position} original={__result} limited={Math.Max(spaceLeft, 0)} stackCount={thing.stackCount} totalCap={profile.StorageWideTotalCap()}");
                        __result = Math.Max(spaceLeft, 0);
                    }
                }
                else if (profile.EnforcesUpperBound())
                {
                    int limit = profile.LimitFor(thing.def);
                    int spaceLeft = StockpileCapacityRules.UsesBuildingPerStackOnlyMode(thing.Position, thing.Map, profile)
                        ? Math.Max(0, Math.Min(limit, thing.def.stackLimit) - thing.stackCount)
                        : StockpileCapacityRules.UnitsRemainingInCell(thing.Position, thing.Map, thing.def, limit,
                            profile.IsAllowedMultistack(), StockpileCapacityRules.PerCellMaxSimilarStacks(profile));
                    if (__result > spaceLeft)
                    {
                        KebabLimitsLog.MessageVerbose(
                            $"[HSK kebab limits] TryAbsorbStackLimit thing={thing.def.defName} pos={thing.Position} original={__result} limited={Math.Max(spaceLeft, 0)} buildingPerStack={StockpileCapacityRules.UsesBuildingPerStackOnlyMode(thing.Position, thing.Map, profile)} stackCount={thing.stackCount} itemLimit={limit}");
                        __result = Math.Max(spaceLeft, 0);
                    }
                }
            }
        }
    }
}
