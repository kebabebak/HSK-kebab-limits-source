using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;
using Verse.AI;

namespace HSKKebabLimits
{
    public static partial class StockpileCapacityRules
    {
        /// <summary>Counts matching item stacks and total stack count in a held-things list.</summary>
        public static int SumMatchingStacksInCollection(List<Thing> heldItems, ThingDef thing, out int similarStacks)
        {
            int total = 0;
            similarStacks = 0;
            foreach (Thing heldItem in heldItems)
            {
                if (heldItem.def == thing)
                {
                    total += heldItem.stackCount;
                    similarStacks++;
                }
            }

            return total;
        }

        /// <summary>Counts matching and foreign item stacks on a map cell for per-cell limit checks.</summary>
        public static int SumMatchingStacksAtCell(IntVec3 c, Map map, ThingDef thing, out int similarStacks, out int foreignStacks)
        {
            int total = 0;
            List<Thing> list = map.thingGrid.ThingsListAt(c);
            similarStacks = 0;
            foreignStacks = 0;
            for (int i = 0; i < list.Count; i++)
            {
                Thing cellThing = list[i];
                if (!cellThing.Destroyed && cellThing.def.category == ThingCategory.Item)
                {
                    if (cellThing.def == thing && !cellThing.IsRelic())
                    {
                        total += cellThing.stackCount;
                        similarStacks++;
                    }
                    else
                    {
                        foreignStacks++;
                    }
                }
            }

            return total;
        }

        /// <summary>Returns whether the cell belongs to a floor stockpile zone (not building storage).</summary>
        private static bool IsStockpileZoneCell(IntVec3 c, Map map)
        {
            SlotGroup slotGroup = map.haulDestinationManager.SlotGroupAt(c);
            return slotGroup?.parent is Zone_Stockpile;
        }

        /// <summary>
        /// Per-cell similar-stack cap for UnitsRemainingInCell. Zone-wide similar-stack rules are
        /// enforced separately in NoStorageBlockersIn and Solve.
        /// </summary>
        internal static int PerCellMaxSimilarStacks(StockpileLimitBundle profile)
        {
            return profile.EnforcesSimilarStackCap() ? 0 : profile.SimilarStackCount;
        }

        /// <summary>Returns how many more units of an item type can fit on a cell under the mod limit.</summary>
        public static int UnitsRemainingInCell(IntVec3 c, Map map, ThingDef thingdef, int limit, bool multistack, int maxSimilar)
        {
            int similarStacks;
            int foreignStacks;
            int count = SumMatchingStacksAtCell(c, map, thingdef, out similarStacks, out foreignStacks);
            // Mixed types on one cell are blocked only for floor stockpile zones. Building storage
            // (including deep storage) relies on vanilla/LWM per-cell stack capacity instead.
            bool blockForeignOnCell = !multistack && foreignStacks > 0 && IsStockpileZoneCell(c, map);
            if (blockForeignOnCell || (maxSimilar > 0 && similarStacks > maxSimilar))
            {
                return 0;
            }

            return limit - count;
        }

        /// <summary>Counts all item units held in a slot group or thing enumeration.</summary>
        private static int CountAllItemUnits(IEnumerable<Thing> things)
        {
            if (things == null)
            {
                return 0;
            }

            int total = 0;
            foreach (Thing thing in things)
            {
                if (thing?.def.category == ThingCategory.Item)
                {
                    total += thing.stackCount;
                }
            }

            return total;
        }

        /// <summary>Counts all item units on the given storage cells.</summary>
        private static int CountAllItemUnitsInCells(List<IntVec3> cells, Map map)
        {
            if (cells == null || map == null)
            {
                return 0;
            }

            int total = 0;
            foreach (IntVec3 cell in cells)
            {
                foreach (Thing thing in map.thingGrid.ThingsListAt(cell))
                {
                    if (thing.def.category == ThingCategory.Item)
                    {
                        total += thing.stackCount;
                    }
                }
            }

            return total;
        }

        /// <summary>Collects all item stacks on storage cells for cascade ejection.</summary>
        private static List<Thing> CollectAllItemStacks(List<IntVec3> cells, Map map)
        {
            List<Thing> stacks = new List<Thing>();
            if (cells == null || map == null)
            {
                return stacks;
            }

            foreach (IntVec3 cell in cells)
            {
                foreach (Thing thing in map.thingGrid.ThingsListAt(cell))
                {
                    if (thing.def.category == ThingCategory.Item && thing.Spawned)
                    {
                        stacks.Add(thing);
                    }
                }
            }

            return stacks;
        }

        /// <summary>Sorts stacks for storage-wide total overflow ejection using the cascade mode.</summary>
        private static void SortStacksForWideTotalEject(List<Thing> stacks, int mode,
            Dictionary<ThingDef, int> totalsByDef)
        {
            if (stacks == null || stacks.Count <= 1)
            {
                return;
            }

            if (mode == 2)
            {
                stacks.Sort((a, b) =>
                {
                    int cmp = CompareDefsByZoneTotal(a.def, b.def, totalsByDef, descending: true);
                    if (cmp != 0)
                    {
                        return cmp;
                    }

                    return a.stackCount.CompareTo(b.stackCount);
                });
                return;
            }

            if (mode == 1)
            {
                stacks.Sort((a, b) =>
                {
                    int cellCmp = a.Position.GetHashCode().CompareTo(b.Position.GetHashCode());
                    return cellCmp != 0 ? cellCmp : a.stackCount.CompareTo(b.stackCount);
                });
                return;
            }

            stacks.Sort((a, b) =>
            {
                bool aSingleType = a.def.stackLimit <= 1;
                bool bSingleType = b.def.stackLimit <= 1;
                if (aSingleType != bSingleType)
                {
                    return aSingleType ? -1 : 1;
                }

                if (!aSingleType)
                {
                    int stackCmp = a.stackCount.CompareTo(b.stackCount);
                    if (stackCmp != 0)
                    {
                        return stackCmp;
                    }
                }

                int totalCmp = totalsByDef[a.def].CompareTo(totalsByDef[b.def]);
                return totalCmp != 0 ? totalCmp : string.Compare(a.def.defName, b.def.defName, StringComparison.Ordinal);
            });
        }

        /// <summary>Ejects overflow when the sum of all items exceeds the storage-wide total cap.</summary>
        private static void EjectStorageWideTotalOverflow(List<IntVec3> cells, Map map, StockpileLimitBundle profile,
            int mode, string reason)
        {
            if (!profile.AppliesStorageWideTotalCap)
            {
                return;
            }

            int totalCap = profile.StorageWideTotalCap();
            int totalAll = CountAllItemUnitsInCells(cells, map);
            int overflow = totalAll - totalCap;
            if (overflow <= 0)
            {
                KebabLimitsLog.Message(
                    $"[HSK kebab limits] StorageWideTotalNoOverflow reason={reason} totalAll={totalAll} totalCap={totalCap}");
                return;
            }

            Dictionary<ThingDef, int> totalsByDef = new Dictionary<ThingDef, int>();
            List<Thing> stacks = CollectAllItemStacks(cells, map);
            foreach (Thing stack in stacks)
            {
                if (!totalsByDef.ContainsKey(stack.def))
                {
                    totalsByDef[stack.def] = 0;
                }

                totalsByDef[stack.def] += stack.stackCount;
            }

            SortStacksForWideTotalEject(stacks, mode, totalsByDef);
            KebabLimitsLog.MessageImportant(
                $"[HSK kebab limits] StorageWideTotalOverflow reason={reason} totalAll={totalAll} totalCap={totalCap} overflow={overflow} stacks={stacks.Count} mode={mode}");
            foreach (Thing thing in stacks.ToList())
            {
                if (overflow <= 0)
                {
                    break;
                }

                if (!thing.Spawned)
                {
                    continue;
                }

                int ejected = UnloadCountToGround(thing, overflow);
                overflow -= ejected;
                KebabLimitsLog.MessageImportant(
                    $"[HSK kebab limits] StorageWideTotalEjected reason={reason} ejected={ejected} remainingOverflow={overflow} thing={thing.def.defName} stackPos={thing.Position}");
            }
        }

        /// <summary>Upper cap for a min-triggered replenish cycle (explicit max slider or vanilla stack size).</summary>
        private static int ReplenishUpperCap(StockpileLimitBundle profile, ThingDef thingDef)
        {
            if (profile.EnforcesUpperBound() || profile.HasPerItemLimit(thingDef))
            {
                return Math.Min(profile.LimitFor(thingDef), thingDef.stackLimit);
            }

            return thingDef.stackLimit;
        }

        /// <summary>Absolute min-slider count for an item type on a storage profile.</summary>
        private static int MinSliderLimit(StockpileLimitBundle profile, ThingDef thingDef)
        {
            return KebabLimitsModSettings.PercentageMode
                ? LogarithmicStackScale.ToItemPercentageCount(profile.allowedLimitPercents.min, thingDef.stackLimit)
                : LogarithmicStackScale.ToAbsoluteCount(profile.allowedLimitPercents.min);
        }

        /// <summary>
        /// Updates replenish flags after stock or slider changes. Preserves in-flight fill between min and max.
        /// </summary>
        public static void UpdateMinReplenishAfterStockChange(Map map, IntVec3 cell, ThingDef thingDef,
            StockpileLimitBundle profile)
        {
            if (!profile.EnforcesLowerBound() || map == null || thingDef == null || !cell.IsValid)
            {
                return;
            }

            int minLimit = MinSliderLimit(profile, thingDef);
            int maxLimit = ReplenishUpperCap(profile, thingDef);
            int count = SumMatchingStacksAtCell(cell, map, thingDef, out _, out _);
            if (count < minLimit)
            {
                CapacityQueryCache.SetReplenishing(map, cell, thingDef, true);
            }
            else if (count >= maxLimit)
            {
                CapacityQueryCache.SetReplenishing(map, cell, thingDef, false);
            }
        }

        /// <summary>
        /// Applies min-slider haul gate in NoStorageBlockersIn. Returns false when destination must be blocked.
        /// </summary>
        public static bool ApplyMinReplenishHaulGate(Map map, IntVec3 cell, ThingDef thingDef,
            StockpileLimitBundle profile)
        {
            if (!profile.EnforcesLowerBound() || map == null || thingDef == null || !cell.IsValid)
            {
                return true;
            }

            int minLimit = MinSliderLimit(profile, thingDef);
            int maxLimit = ReplenishUpperCap(profile, thingDef);
            int count = SumMatchingStacksAtCell(cell, map, thingDef, out _, out _);
            if (count < minLimit)
            {
                CapacityQueryCache.SetReplenishing(map, cell, thingDef, true);
                return true;
            }

            if (count >= maxLimit)
            {
                CapacityQueryCache.SetReplenishing(map, cell, thingDef, false);
                return true;
            }

            if (!CapacityQueryCache.IsReplenishing(map, cell, thingDef))
            {
                KebabLimitsLog.MessageVerbose(
                    $"[HSK kebab limits] NoStorageBlockers min satisfied thing={thingDef.defName} cell={cell} storage=\"{cell.GetSlotGroup(map)?.parent?.SlotYielderLabel() ?? "none"}\" count={count} minLimit={minLimit} maxLimit={maxLimit}");
                return false;
            }

            return true;
        }

        /// <summary>Returns whether the cell is part of a building storage slot group (shelf, hopper, etc.).</summary>
        private static bool IsBuildingStorageCell(IntVec3 cell, Map map)
        {
            SlotGroup slotGroup = map.haulDestinationManager.SlotGroupAt(cell);
            return slotGroup?.parent is Building_Storage;
        }

        /// <summary>Returns whether building storage uses per-stack caps instead of zone-wide totals.</summary>
        internal static bool UsesBuildingPerStackOnlyMode(IntVec3 cell, Map map, StockpileLimitBundle profile)
        {
            return IsBuildingStorageCell(cell, map) &&
                   !profile.UsesStorageWideLimitFromLimitFor &&
                   !profile.EnforcesSimilarStackCap();
        }

        /// <summary>Computes remaining absorbable space on a building-storage cell using per-stack limits.</summary>
        private static int BuildingStoragePerStackSpace(IntVec3 cell, Map map, ThingDef thingDef, int limit)
        {
            int perStackLimit = Math.Min(limit, thingDef.stackLimit);
            int absorbSpace = 0;
            int itemCount = 0;
            foreach (Thing thing in map.thingGrid.ThingsListAt(cell))
            {
                if (thing.def.category != ThingCategory.Item)
                {
                    continue;
                }

                itemCount++;
                if (thing.def == thingDef)
                {
                    absorbSpace += Math.Max(0, perStackLimit - thing.stackCount);
                }
            }

            int maxItems = cell.GetMaxItemsAllowedInCell(map);
            int freeSlots = Math.Max(0, maxItems - itemCount);
            int space = absorbSpace + freeSlots * perStackLimit;
            KebabLimitsLog.MessageVerbose(
                $"[HSK kebab limits] BuildingStoragePerStackSpace cell={cell} thing={thingDef.defName} perStackLimit={perStackLimit} itemCount={itemCount} maxItems={maxItems} freeSlots={freeSlots} absorbSpace={absorbSpace} totalSpace={space}");
            return space;
        }

        /// <summary>Computes remaining space for similar-stack rules across an entire storage slot group.</summary>
        private static int StorageWideSimilarStackSpace(IntVec3 cell, Map map, ThingDef itemDef,
            StockpileLimitBundle profile, int limit, out int similarStacks, out int totalCount, out bool onTargetCell)
        {
            similarStacks = 0;
            totalCount = 0;
            onTargetCell = false;

            if (!profile.EnforcesSimilarStackCap())
            {
                return int.MaxValue;
            }

            SlotGroup slotGroup = map.haulDestinationManager.SlotGroupAt(cell);
            if (slotGroup == null)
            {
                return int.MaxValue;
            }

            foreach (Thing item in slotGroup.HeldThings)
            {
                if (item.def != itemDef)
                {
                    continue;
                }

                totalCount += item.stackCount;
                similarStacks++;
                if (item.Position == cell)
                {
                    onTargetCell = true;
                }
            }

            int maxSimilarStacks = profile.SimilarStackCount;
            if (maxSimilarStacks <= 0)
            {
                return int.MaxValue;
            }

            if (similarStacks > maxSimilarStacks)
            {
                return 0;
            }

            int perStackLimit = Math.Min(itemDef.stackLimit, limit);
            if (similarStacks == maxSimilarStacks && !onTargetCell)
            {
                return 0;
            }

            return Math.Max(0, maxSimilarStacks * perStackLimit - totalCount);
        }
    }
}
