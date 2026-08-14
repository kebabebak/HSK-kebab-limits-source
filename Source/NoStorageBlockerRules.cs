using System;
using RimWorld;
using Verse;

namespace HSKKebabLimits
{
    public static partial class StockpileCapacityRules
    {
        /// <summary>Shared NoStorageBlockersIn postfix logic (ApplyCapacityClamp final gate).</summary>
        internal static void ApplyNoStorageBlockersInPostfix(ref bool __result, IntVec3 c, Map map, Thing thing)
        {
            StockpileLimitBundle profile = StockpileProfileStore.FindOrNull(c, map);
            if (profile == null)
            {
                return;
            }

            int limit = profile.LimitFor(thing.def);
            if (profile.HasEnforceableStorageWideLimits)
            {
                SlotGroup slotGroup = c.GetSlotGroup(map);
                if (slotGroup != null)
                {
                    CapacityQueryCache.SlotGroupSnapshot snapshot =
                        CapacityQueryCache.GetSnapshot(map, slotGroup);
                    snapshot.GetSimilar(thing.def, out int itemCount, out _);
                    if (profile.EffectiveWideModeRemaining(thing.def, itemCount,
                            snapshot.TotalItemUnits) <= 0)
                    {
                        __result = false;
                        return;
                    }
                }
            }
            else if (profile.EnforcesSimilarStackCap())
            {
                SlotGroup slotGroup = c.GetSlotGroup(map);
                if (slotGroup != null)
                {
                    CapacityQueryCache.SlotGroupSnapshot snapshot =
                        CapacityQueryCache.GetSnapshot(map, slotGroup);
                    snapshot.GetSimilar(thing.def, out int totalCount, out int stackCount);
                    bool onCell = CellHasItemOfDef(c, map, thing.def);
                    int perStackLimit = Math.Min(thing.def.stackLimit, limit);

                    if (stackCount > profile.SimilarStackCount)
                    {
                        __result = false;
                        return;
                    }

                    if (stackCount == profile.SimilarStackCount)
                    {
                        if (!onCell)
                        {
                            __result = false;
                            return;
                        }

                        if (totalCount >= perStackLimit * stackCount)
                        {
                            __result = false;
                            return;
                        }
                    }
                }
            }

            int upperSpace = UsesBuildingPerStackOnlyMode(c, map, profile)
                ? BuildingStoragePerStackSpace(c, map, thing.def, limit)
                : UnitsRemainingInCell(c, map, thing.def, limit,
                    profile.IsAllowedMultistack(), PerCellMaxSimilarStacks(profile));
            if (profile.EnforcesUpperBound() && !profile.UsesStorageWideLimitFromLimitFor && upperSpace <= 0)
            {
                KebabLimitsLog.MessageVerbose(
                    $"[HSK kebab limits] NoStorageBlockers upper limit blocked thing={thing.def.defName} cell={c} storage=\"{c.GetSlotGroup(map)?.parent?.SlotYielderLabel() ?? "none"}\" buildingPerStack={UsesBuildingPerStackOnlyMode(c, map, profile)} upperSpace={upperSpace} itemLimit={limit} similarRaw={profile.SimilarStackCountRaw}");
                __result = false;
            }
            else if (profile.EnforcesLowerBound())
            {
                if (!ApplyMinReplenishHaulGate(map, c, thing.def, profile))
                {
                    __result = false;
                }
            }

            if (!__result || thing?.def == null)
            {
                return;
            }

            if (!profile.EnforcesUpperBound() && !profile.EnforcesSimilarStackCap() &&
                !profile.HasEnforceableStorageWideLimits)
            {
                return;
            }

            if (CapacityQueryCache.TryGetCachedMaxSpace(c, map, thing.def, out int cachedMax) &&
                cachedMax <= 0)
            {
                KebabLimitsLog.MessageVerbose(
                    $"[HSK kebab limits] NoStorageBlockersSolveBlocked thing={thing.def.defName} cell={c} storage=\"{c.GetSlotGroup(map)?.parent?.SlotYielderLabel() ?? "none"}\" stackCount={thing.stackCount} itemLimit={profile.LimitFor(thing.def)} similarRaw={profile.SimilarStackCountRaw} cached=true");
                __result = false;
                return;
            }

            int limitsSpace = 1;
            ApplyCapacityClamp(c, map, thing.def, ref limitsSpace);
            if (limitsSpace <= 0)
            {
                KebabLimitsLog.MessageVerbose(
                    $"[HSK kebab limits] NoStorageBlockersSolveBlocked thing={thing.def.defName} cell={c} storage=\"{c.GetSlotGroup(map)?.parent?.SlotYielderLabel() ?? "none"}\" stackCount={thing.stackCount} itemLimit={profile.LimitFor(thing.def)} similarRaw={profile.SimilarStackCountRaw}");
                __result = false;
            }
        }
    }
}
