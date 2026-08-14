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
        /// <summary>Recalculates haulables and ejects overflow after storage settings change on a zone or building.</summary>
        [SyncMethod(SyncContext.None)]
        public static void ReconcileOverflowAfterLimitChange(IStoreSettingsParent storeSettingsParent, StockpileLimitBundle profile)
        {
            if (storeSettingsParent == null || profile == null)
            {
                return;
            }

            List<IntVec3> cells = new List<IntVec3>();
            Map map = null;
            if (storeSettingsParent is Building_Storage buildingStorage)
            {
                if (buildingStorage.slotGroup != null)
                {
                    if (!buildingStorage.slotGroup.HeldThings.Any())
                    {
                        return;
                    }

                    map = buildingStorage.Map;
                    cells = buildingStorage.slotGroup.CellsList;
                }
            }
            else if (storeSettingsParent is Zone_Stockpile zoneStockpile)
            {
                map = zoneStockpile.Map;
                cells = zoneStockpile.AllSlotCellsList();
            }

            if (!cells.Any() || map == null)
            {
                return;
            }

            map.listerHaulables.RecalcAllInCells(cells);
            if (profile.UsesStorageWideLimitFromLimitFor && KebabLimitsModSettings.ZoneWideCascadeEjectMode > 0)
            {
                ZoneWideCascadeEject(cells, map, profile, KebabLimitsModSettings.ZoneWideCascadeEjectMode);
            }
            else
            {
                PerCellCascadeEject(cells, map, profile.allowedLimitPercents.max);
            }
        }

        /// <summary>Notifies min-replenish state after item count changes on a storage cell.</summary>
        public static void MarkCellStockDirty(Map map, IntVec3 cell, ThingDef thingDef)
        {
            if (map == null || thingDef == null || !cell.IsValid)
            {
                return;
            }

            StockpileLimitBundle profile = StockpileProfileStore.FindOrNull(cell, map);
            if (profile == null)
            {
                return;
            }

            UpdateMinReplenishAfterStockChange(map, cell, thingDef, profile);
        }

        /// <summary>
        /// Invalidates solve caches, seeds replenish flags when stock is below min, and recalculates haulables.
        /// Called on any limit slider change (including min-only).
        /// </summary>
        public static void InvalidateHaulCachesForParent(IStoreSettingsParent storeSettingsParent,
            StockpileLimitBundle profile)
        {
            if (storeSettingsParent == null || profile == null)
            {
                return;
            }

            List<IntVec3> cells = new List<IntVec3>();
            Map map = null;
            if (storeSettingsParent is Building_Storage buildingStorage)
            {
                if (buildingStorage.slotGroup != null)
                {
                    map = buildingStorage.Map;
                    cells = buildingStorage.slotGroup.CellsList;
                }
            }
            else if (storeSettingsParent is Zone_Stockpile zoneStockpile)
            {
                map = zoneStockpile.Map;
                cells = zoneStockpile.AllSlotCellsList();
            }

            if (!cells.Any() || map == null)
            {
                return;
            }

            SlotGroup slotGroup = map.haulDestinationManager.SlotGroupAt(cells[0]);
            if (slotGroup != null)
            {
                CapacityQueryCache.ClearReplenishForSlotGroup(map, slotGroup);
                CapacityQueryCache.InvalidateSlotGroup(map, slotGroup);
            }

            if (profile.EnforcesLowerBound())
            {
                foreach (IntVec3 cell in cells)
                {
                    List<Thing> things = map.thingGrid.ThingsListAt(cell);
                    if (!things.Any())
                    {
                        continue;
                    }

                    Dictionary<ThingDef, int> totals = new Dictionary<ThingDef, int>();
                    foreach (Thing thing in things)
                    {
                        if (thing.def.category != ThingCategory.Item)
                        {
                            continue;
                        }

                        if (totals.ContainsKey(thing.def))
                        {
                            totals[thing.def] += thing.stackCount;
                        }
                        else
                        {
                            totals[thing.def] = thing.stackCount;
                        }
                    }

                    foreach (KeyValuePair<ThingDef, int> pair in totals)
                    {
                        UpdateMinReplenishAfterStockChange(map, cell, pair.Key, profile);
                    }
                }
            }

            map.listerHaulables.RecalcAllInCells(cells);
        }

        /// <summary>Ejects per-cell overflow when zone-wide cascade mode is off or unavailable.</summary>
        private static void PerCellCascadeEject(List<IntVec3> cells, Map map, float max)
        {
            int limit = LogarithmicStackScale.ToAbsoluteCount(max);
            Dictionary<ThingDef, int> totals = new Dictionary<ThingDef, int>();
            foreach (IntVec3 cell in cells)
            {
                List<Thing> things = map.thingGrid.ThingsListAt(cell);
                if (!things.Any())
                {
                    continue;
                }

                totals.Clear();
                foreach (Thing thing in things)
                {
                    if (thing.def.category == ThingCategory.Item)
                    {
                        if (totals.ContainsKey(thing.def))
                        {
                            totals[thing.def] += thing.stackCount;
                        }
                        else
                        {
                            totals[thing.def] = thing.stackCount;
                        }
                    }
                }

                if (!totals.Any())
                {
                    continue;
                }

                foreach (Thing thing in things.ToList())
                {
                    if (thing.def.category == ThingCategory.Item && totals.ContainsKey(thing.def))
                    {
                        int overflow = totals[thing.def] - limit;
                        if (overflow <= 0)
                        {
                            totals.Remove(thing.def);
                            continue;
                        }

                        int ejected = UnloadCountToGround(thing, overflow);
                        totals[thing.def] -= ejected;
                    }
                }
            }
        }

        /// <summary>Ejects zone-wide overflow using the configured cascade eject mode.</summary>
        private static void ZoneWideCascadeEject(List<IntVec3> cells, Map map, StockpileLimitBundle profile, int mode)
        {
            Dictionary<ThingDef, int> zoneTotals = new Dictionary<ThingDef, int>();
            Dictionary<ThingDef, List<Thing>> thingsByDef = new Dictionary<ThingDef, List<Thing>>();
            foreach (IntVec3 cell in cells)
            {
                List<Thing> things = map.thingGrid.ThingsListAt(cell);
                foreach (Thing thing in things)
                {
                    if (thing.def.category != ThingCategory.Item)
                    {
                        continue;
                    }

                    ThingDef def = thing.def;
                    if (!zoneTotals.ContainsKey(def))
                    {
                        zoneTotals[def] = 0;
                        thingsByDef[def] = new List<Thing>();
                    }

                    zoneTotals[def] += thing.stackCount;
                    thingsByDef[def].Add(thing);
                }
            }

            Dictionary<ThingDef, int> remainingOverflow = new Dictionary<ThingDef, int>();
            foreach (KeyValuePair<ThingDef, int> pair in zoneTotals)
            {
                if (!profile.HasPerItemLimit(pair.Key))
                {
                    continue;
                }

                int limit = profile.LimitFor(pair.Key);
                int overflow = pair.Value - limit;
                if (overflow > 0)
                {
                    remainingOverflow[pair.Key] = overflow;
                }
            }

            if (remainingOverflow.Any())
            {
                if (mode == 1)
                {
                    ZoneWideCascadeEjectCellOrder(cells, map, remainingOverflow);
                }
                else if (mode == 2)
                {
                    List<ThingDef> orderedDefs = remainingOverflow.Keys.ToList();
                    orderedDefs.Sort((a, b) => CompareDefsByZoneTotal(a, b, zoneTotals, descending: true));
                    ZoneWideCascadeEjectDefsInOrder(orderedDefs, thingsByDef, remainingOverflow);
                }
                else
                {
                    ZoneWideCascadeEjectLeastFirst(remainingOverflow, thingsByDef, zoneTotals);
                }
            }

            EjectStorageWideTotalOverflow(cells, map, profile, mode, "cascade-eject");
        }

        /// <summary>Compares item defs by zone total count for cascade eject ordering.</summary>
        private static int CompareDefsByZoneTotal(ThingDef a, ThingDef b, Dictionary<ThingDef, int> zoneTotals,
            bool descending)
        {
            int cmp = descending
                ? zoneTotals[b].CompareTo(zoneTotals[a])
                : zoneTotals[a].CompareTo(zoneTotals[b]);
            return cmp != 0 ? cmp : string.Compare(a.defName, b.defName, StringComparison.Ordinal);
        }

        /// <summary>Ejects zone overflow by item type in a predefined def order (mode 2).</summary>
        private static void ZoneWideCascadeEjectDefsInOrder(List<ThingDef> orderedDefs,
            Dictionary<ThingDef, List<Thing>> thingsByDef, Dictionary<ThingDef, int> remainingOverflow)
        {
            foreach (ThingDef def in orderedDefs)
            {
                int overflow = remainingOverflow[def];
                foreach (Thing thing in thingsByDef[def].ToList())
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
                }
            }
        }

        /// <summary>
        /// Mode 3: non-stackable item types (stackLimit &lt;= 1) first, then stackable types by zone total ascending.
        /// Within stackable types, smallest physical stacks first.
        /// </summary>
        private static void ZoneWideCascadeEjectLeastFirst(Dictionary<ThingDef, int> remainingOverflow,
            Dictionary<ThingDef, List<Thing>> thingsByDef, Dictionary<ThingDef, int> zoneTotals)
        {
            List<Thing> stacks = new List<Thing>();
            foreach (ThingDef def in remainingOverflow.Keys)
            {
                stacks.AddRange(thingsByDef[def]);
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

                return CompareDefsByZoneTotal(a.def, b.def, zoneTotals, descending: false);
            });

            foreach (Thing thing in stacks)
            {
                if (!remainingOverflow.TryGetValue(thing.def, out int overflow) || overflow <= 0)
                {
                    continue;
                }

                if (!thing.Spawned)
                {
                    continue;
                }

                int ejected = UnloadCountToGround(thing, overflow);
                remainingOverflow[thing.def] = overflow - ejected;
            }
        }

        /// <summary>Ejects zone overflow by scanning cells in zone cell order (mode 1).</summary>
        private static void ZoneWideCascadeEjectCellOrder(List<IntVec3> cells, Map map,
            Dictionary<ThingDef, int> remainingOverflow)
        {
            foreach (IntVec3 cell in cells)
            {
                foreach (Thing thing in map.thingGrid.ThingsListAt(cell).ToList())
                {
                    if (thing.def.category != ThingCategory.Item || !thing.Spawned)
                    {
                        continue;
                    }

                    if (!remainingOverflow.TryGetValue(thing.def, out int overflow) || overflow <= 0)
                    {
                        continue;
                    }

                    int ejected = UnloadCountToGround(thing, overflow);
                    remainingOverflow[thing.def] = overflow - ejected;
                }
            }
        }

        /// <summary>Ejects an entire item stack when it exceeds storage limits.</summary>
        public static void UnloadEntireStackToGround(Thing thing)
        {
            UnloadCountToGround(thing, thing.stackCount);
        }

        /// <summary>Places ejected overflow near the source cell, avoiding other storage destinations.</summary>
        private static bool TryPlaceEjectedThing(Thing toPlace, IntVec3 position, Map map)
        {
            return GenPlace.TryPlaceThing(toPlace, position, map, ThingPlaceMode.Near, null,
                newLoc => IsNonStorageDropCell(newLoc, map));
        }

        /// <summary>Returns whether a cell is safe for dumping overflow outside stockpiles and shelves.</summary>
        public static bool IsNonStorageDropCell(IntVec3 cell, Map map)
        {
            if (!cell.InBounds(map))
            {
                return false;
            }

            if (map.zoneManager.ZoneAt(cell) is Zone_Stockpile)
            {
                return false;
            }

            SlotGroup slotGroup = map.haulDestinationManager.SlotGroupAt(cell);
            if (slotGroup?.parent is Building_Storage)
            {
                return false;
            }

            foreach (Thing item in map.thingGrid.ThingsListAtFast(cell))
            {
                if (item is Building_Storage)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Runs delayed overflow ejection for one item type across a stockpile zone.</summary>
        public static void DelayedZoneEjectNow(Zone_Stockpile zone, ThingDef thingDef)
        {
            if (zone?.Map == null || thingDef == null)
            {
                KebabLimitsLog.Warning("[HSK kebab limits] DelayedZoneEjectNow skipped: missing zone/map/thingDef.");
                return;
            }

            StockpileLimitBundle profile = StockpileProfileStore.GetOrNull(zone.GetStoreSettings());
            if (profile == null)
            {
                KebabLimitsLog.Message(
                    $"[HSK kebab limits] DelayedZoneEjectNow skipped zone=\"{zone.SlotYielderLabel()}\" thing={thingDef.defName}: no profile.");
                return;
            }

            List<IntVec3> cells = zone.AllSlotCellsList();
            KebabLimitsLog.Message(
                $"[HSK kebab limits] DelayedZoneEjectNow start zone=\"{zone.SlotYielderLabel()}\" thing={thingDef.defName} cells={cells.Count} itemLimit={profile.LimitFor(thingDef)} similarRaw={profile.SimilarStackCountRaw} zoneMode={KebabLimitsModSettings.ZoneWideCascadeEjectMode}");

            if (profile.UsesStorageWideLimitFromLimitFor)
            {
                DelayedZoneWideEjectThing(cells, zone.Map, profile, thingDef);
            }
            else
            {
                DelayedPerCellEjectThing(cells, zone.Map, profile, thingDef);
            }

            zone.Map.listerHaulables.RecalcAllInCells(cells);
        }

        /// <summary>Ejects per-cell overflow for one item type in a stockpile zone.</summary>
        private static void DelayedPerCellEjectThing(List<IntVec3> cells, Map map, StockpileLimitBundle profile,
            ThingDef thingDef)
        {
            int limit = profile.LimitFor(thingDef);
            foreach (IntVec3 cell in cells)
            {
                int cellTotal = SumMatchingStacksAtCell(cell, map, thingDef, out _, out _);
                int overflow = cellTotal - limit;
                if (overflow <= 0)
                {
                    continue;
                }

                KebabLimitsLog.Message(
                    $"[HSK kebab limits] DelayedPerCellEjectThing cell={cell} thing={thingDef.defName} total={cellTotal} limit={limit} overflow={overflow}");
                foreach (Thing thing in map.thingGrid.ThingsListAt(cell).ToList())
                {
                    if (overflow <= 0)
                    {
                        break;
                    }

                    if (thing.def != thingDef || !thing.Spawned)
                    {
                        continue;
                    }

                    int ejected = UnloadCountToGround(thing, overflow);
                    overflow -= ejected;
                    KebabLimitsLog.Message(
                        $"[HSK kebab limits] DelayedPerCellEjectThing ejected={ejected} remainingOverflow={overflow} thing={thingDef.defName} cell={cell}");
                }
            }
        }

        /// <summary>Ejects zone-wide overflow for one item type using the cascade eject sort mode.</summary>
        private static void DelayedZoneWideEjectThing(List<IntVec3> cells, Map map, StockpileLimitBundle profile,
            ThingDef thingDef)
        {
            if (profile.HasPerItemLimit(thingDef))
            {
                List<Thing> stacks = new List<Thing>();
                int total = 0;
                foreach (IntVec3 cell in cells)
                {
                    foreach (Thing thing in map.thingGrid.ThingsListAt(cell))
                    {
                        if (thing.def != thingDef || thing.def.category != ThingCategory.Item)
                        {
                            continue;
                        }

                        total += thing.stackCount;
                        stacks.Add(thing);
                    }
                }

                int limit = profile.LimitFor(thingDef);
                int overflow = total - limit;
                if (overflow > 0)
                {
                    int mode = KebabLimitsModSettings.ZoneWideCascadeEjectMode;
                    if (mode == 3 || mode == 0)
                    {
                        stacks.Sort((a, b) => a.stackCount.CompareTo(b.stackCount));
                    }
                    else if (mode == 2)
                    {
                        stacks.Sort((a, b) => b.stackCount.CompareTo(a.stackCount));
                    }

                    KebabLimitsLog.MessageImportant(
                        $"[HSK kebab limits] DelayedZoneWideEjectThing per-item overflow thing={thingDef.defName} total={total} itemLimit={limit} overflow={overflow} stacks={stacks.Count} mode={mode}");
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
                            $"[HSK kebab limits] DelayedZoneWideEjectThing ejected={ejected} remainingOverflow={overflow} thing={thingDef.defName} stackPos={thing.Position}");
                    }
                }
                else
                {
                    KebabLimitsLog.Message(
                        $"[HSK kebab limits] DelayedZoneWideEjectThing no per-item overflow thing={thingDef.defName} total={total} itemLimit={limit} stacks={stacks.Count}");
                }
            }

            EjectStorageWideTotalOverflow(cells, map, profile, KebabLimitsModSettings.ZoneWideCascadeEjectMode,
                $"delayed-zone thing={thingDef.defName}");
        }

        /// <summary>Splits and ejects a partial stack count, placing overflow near the source cell.</summary>
        public static int UnloadCountToGround(Thing thing, int unloadCount)
        {
            if (!thing.Spawned || unloadCount <= 0)
            {
                return 0;
            }

            int toRemove = Math.Min(unloadCount, thing.stackCount);
            if (toRemove >= thing.stackCount && DeepStorageEjectCompat.TryEjectFullStack(thing))
            {
                Map ejectMap = thing.Map;
                if (ejectMap != null)
                {
                    SlotGroup ejectGroup = ejectMap.haulDestinationManager.SlotGroupAt(thing.Position);
                    if (ejectGroup != null)
                    {
                        CapacityQueryCache.InvalidateSlotGroup(ejectMap, ejectGroup);
                    }
                    else
                    {
                        CapacityQueryCache.InvalidateCell(ejectMap, thing.Position);
                    }
                }

                return toRemove;
            }

            IntVec3 position = thing.Position;
            Map map = thing.Map;
            Thing toPlace = thing.SplitOff(toRemove);
            if (toPlace == null)
            {
                return 0;
            }

            int removedCount = toPlace.stackCount;
            if (!TryPlaceEjectedThing(toPlace, position, map))
            {
                if (!toPlace.Spawned)
                {
                    GenSpawn.Spawn(toPlace, position, map);
                }

                return 0;
            }

            SlotGroup sourceGroup = map.haulDestinationManager.SlotGroupAt(position);
            if (sourceGroup != null)
            {
                CapacityQueryCache.InvalidateSlotGroup(map, sourceGroup);
            }
            else
            {
                CapacityQueryCache.InvalidateCell(map, position);
            }

            return removedCount;
        }

        /// <summary>Queues a deferred eject tick when a storage limit is hit during haul or placement.</summary>
        public static void ScheduleDelayedOverflowOnLimitHit(SlotGroup slotGroup, ThingDef thingDef, string reason)
        {
            if (slotGroup?.parent == null || thingDef == null)
            {
                KebabLimitsLog.MessageVerbose(
                    $"[HSK kebab limits] ScheduleDelayedOverflow skipped: missing slotGroup/parent/thingDef reason={reason}");
                return;
            }

            if (slotGroup.parent is Zone_Stockpile zone)
            {
                DelayedZoneOverflowEjectComponent.Schedule(zone, thingDef, reason);
            }
            else if (slotGroup.parent is Building_Storage building)
            {
                DelayedZoneOverflowEjectComponent.ScheduleBuilding(building, thingDef, reason);
            }
            else
            {
                KebabLimitsLog.MessageVerbose(
                    $"[HSK kebab limits] ScheduleDelayedOverflow skipped unsupported parent={slotGroup.parent.GetType().Name} reason={reason}");
            }
        }

        /// <summary>Estimates how many units of an item type exceed the active storage limit profile.</summary>
        private static int GetStorageOverflow(SlotGroup slotGroup, Map map, StockpileLimitBundle profile, ThingDef thingDef)
        {
            if (slotGroup == null || map == null || profile == null || thingDef == null)
            {
                return 0;
            }

            List<IntVec3> cells = slotGroup.CellsList;
            int limit = profile.LimitFor(thingDef);

            if (profile.UsesStorageWideLimitFromLimitFor)
            {
                int itemTotal = SumMatchingStacksInCollection(slotGroup.HeldThings.ToList(), thingDef, out _);
                int itemOverflow = profile.HasPerItemLimit(thingDef)
                    ? Math.Max(0, itemTotal - profile.LimitFor(thingDef))
                    : 0;
                int totalAll = CountAllItemUnits(slotGroup.HeldThings);
                int totalOverflow = profile.AppliesStorageWideTotalCap
                    ? Math.Max(0, totalAll - profile.StorageWideTotalCap())
                    : 0;
                return Math.Max(itemOverflow, totalOverflow);
            }

            if (profile.EnforcesSimilarStackCap())
            {
                int perStackLimit = Math.Min(thingDef.stackLimit, limit);
                int maxStacks = profile.SimilarStackCount;
                List<Thing> stacks = slotGroup.HeldThings
                    .Where(item => item != null && item.Spawned && item.def == thingDef)
                    .ToList();
                int overflow = 0;
                if (stacks.Count > maxStacks)
                {
                    for (int i = maxStacks; i < stacks.Count; i++)
                    {
                        overflow += stacks[i].stackCount;
                    }
                }

                int countedStacks = Math.Min(stacks.Count, maxStacks);
                for (int i = 0; i < countedStacks; i++)
                {
                    overflow += Math.Max(0, stacks[i].stackCount - perStackLimit);
                }

                int total = stacks.Sum(stack => stack.stackCount);
                overflow = Math.Max(overflow, total - maxStacks * perStackLimit);
                return overflow;
            }

            if (cells.Count > 0 && UsesBuildingPerStackOnlyMode(cells[0], map, profile))
            {
                int perStackLimit = Math.Min(limit, thingDef.stackLimit);
                int overflow = 0;
                foreach (IntVec3 cell in cells)
                {
                    foreach (Thing thing in map.thingGrid.ThingsListAt(cell))
                    {
                        if (thing.def != thingDef || !thing.Spawned)
                        {
                            continue;
                        }

                        overflow += Math.Max(0, thing.stackCount - perStackLimit);
                    }
                }

                return overflow;
            }

            int perCellOverflow = 0;
            foreach (IntVec3 cell in cells)
            {
                int cellTotal = SumMatchingStacksAtCell(cell, map, thingDef, out _, out _);
                perCellOverflow += Math.Max(0, cellTotal - limit);
            }

            return perCellOverflow;
        }

        private static int enforceStorageLimitsDepth;
        private static int placementEnforceCallDepth;

        /// <summary>Ejects overflow immediately after an item is placed into limited storage.</summary>
        public static void EnforceStorageLimitsAfterPlacement(IntVec3 cell, Map map, ThingDef thingDef, string reason)
        {
            if (enforceStorageLimitsDepth > 0 || map == null || thingDef == null || !cell.InBounds(map))
            {
                return;
            }

            placementEnforceCallDepth++;
            if (placementEnforceCallDepth > 1)
            {
                placementEnforceCallDepth--;
                return;
            }

            StockpileLimitBundle profile = StockpileProfileStore.FindOrNull(cell, map);
            SlotGroup slotGroup = map.haulDestinationManager.SlotGroupAt(cell);
            if (profile == null || slotGroup == null)
            {
                placementEnforceCallDepth--;
                return;
            }

            bool enforceOverflow = profile.EnforcesUpperBound() || profile.EnforcesSimilarStackCap() ||
                                   profile.HasEnforceableStorageWideLimits;

            if (enforceOverflow)
            {
                enforceStorageLimitsDepth++;
                try
                {
                    int overflowBefore = GetStorageOverflow(slotGroup, map, profile, thingDef);
                    int beforeCount = SumMatchingStacksInCollection(slotGroup.HeldThings.ToList(), thingDef, out _);
                    EnforceStorageLimitsInternal(slotGroup, map, profile, thingDef);
                    int afterCount = SumMatchingStacksInCollection(slotGroup.HeldThings.ToList(), thingDef, out _);
                    int ejectedEstimate = Math.Max(0, beforeCount - afterCount);
                    int overflowAfter = GetStorageOverflow(slotGroup, map, profile, thingDef);

                    if (ejectedEstimate > 0)
                    {
                        map.listerHaulables.RecalcAllInCells(slotGroup.CellsList);
                        KebabLimitsLog.MessageImportant(
                            $"[HSK kebab limits] PostPlaceEnforce reason={reason} thing={thingDef.defName} cell={cell} storage=\"{slotGroup.parent?.SlotYielderLabel() ?? "none"}\" before={beforeCount} after={afterCount} ejected~={ejectedEstimate} overflowBefore={overflowBefore} overflowAfter={overflowAfter}");
                    }

                    if (overflowBefore > 0 || overflowAfter > 0 || ejectedEstimate > 0)
                    {
                        ScheduleDelayedOverflowOnLimitHit(slotGroup, thingDef,
                            $"post-place reason={reason} overflowBefore={overflowBefore} overflowAfter={overflowAfter} ejected~={ejectedEstimate}");
                    }
                }
                finally
                {
                    enforceStorageLimitsDepth--;
                }
            }

            MarkCellStockDirty(map, cell, thingDef);
            CapacityQueryCache.InvalidateSlotGroup(map, slotGroup);
            placementEnforceCallDepth--;
        }

        /// <summary>Applies the correct eject strategy for the storage profile (per-cell, zone-wide, or similar-stack).</summary>
        private static void EnforceStorageLimitsInternal(SlotGroup slotGroup, Map map,
            StockpileLimitBundle profile, ThingDef thingDef)
        {
            List<IntVec3> cells = slotGroup.CellsList;
            int limit = profile.LimitFor(thingDef);

            if (profile.UsesStorageWideLimitFromLimitFor)
            {
                DelayedZoneWideEjectThing(cells, map, profile, thingDef);
                return;
            }

            if (profile.EnforcesSimilarStackCap())
            {
                EnforceSimilarStackLimits(slotGroup, map, profile, thingDef, limit);
                return;
            }

            if (cells.Count > 0 && UsesBuildingPerStackOnlyMode(cells[0], map, profile))
            {
                EnforceBuildingPerStackLimits(cells, map, thingDef, limit);
                return;
            }

            foreach (IntVec3 cell in cells)
            {
                int cellTotal = SumMatchingStacksAtCell(cell, map, thingDef, out _, out _);
                int overflow = cellTotal - limit;
                if (overflow <= 0)
                {
                    continue;
                }

                KebabLimitsLog.MessageImportant(
                    $"[HSK kebab limits] PostPlacePerCellOverflow cell={cell} thing={thingDef.defName} total={cellTotal} limit={limit} overflow={overflow}");
                foreach (Thing thing in map.thingGrid.ThingsListAt(cell).ToList())
                {
                    if (overflow <= 0)
                    {
                        break;
                    }

                    if (thing.def != thingDef || !thing.Spawned)
                    {
                        continue;
                    }

                    int ejected = UnloadCountToGround(thing, overflow);
                    overflow -= ejected;
                }
            }
        }

        /// <summary>Ejects per-stack overflow on building storage cells when only stack caps apply.</summary>
        private static void EnforceBuildingPerStackLimits(List<IntVec3> cells, Map map, ThingDef thingDef, int limit)
        {
            int perStackLimit = Math.Min(limit, thingDef.stackLimit);
            foreach (IntVec3 cell in cells)
            {
                foreach (Thing thing in map.thingGrid.ThingsListAt(cell).ToList())
                {
                    if (thing.def != thingDef || !thing.Spawned)
                    {
                        continue;
                    }

                    int overflow = thing.stackCount - perStackLimit;
                    if (overflow <= 0)
                    {
                        continue;
                    }

                    int ejected = UnloadCountToGround(thing, overflow);
                    KebabLimitsLog.MessageImportant(
                        $"[HSK kebab limits] PostPlaceBuildingPerStackOverflow cell={cell} thing={thingDef.defName} stackCount={thing.stackCount + ejected} perStackLimit={perStackLimit} ejected={ejected}");
                }
            }
        }

        /// <summary>Ejects excess similar stacks and over-full stacks under similar-stack limit rules.</summary>
        private static void EnforceSimilarStackLimits(SlotGroup slotGroup, Map map, StockpileLimitBundle profile,
            ThingDef thingDef, int limit)
        {
            int perStackLimit = Math.Min(thingDef.stackLimit, limit);
            int maxStacks = profile.SimilarStackCount;
            List<Thing> stacks = slotGroup.HeldThings
                .Where(item => item != null && item.Spawned && item.def == thingDef)
                .ToList();

            while (stacks.Count > maxStacks)
            {
                Thing victim = stacks[stacks.Count - 1];
                int ejected = UnloadCountToGround(victim, victim.stackCount);
                KebabLimitsLog.MessageImportant(
                    $"[HSK kebab limits] PostPlaceSimilarStackOverflow excess-stack thing={thingDef.defName} stacks={stacks.Count} maxStacks={maxStacks} ejected={ejected} cell={victim.Position}");
                stacks.RemoveAt(stacks.Count - 1);
            }

            foreach (Thing stack in stacks.ToList())
            {
                int overflow = stack.stackCount - perStackLimit;
                if (overflow <= 0)
                {
                    continue;
                }

                int ejected = UnloadCountToGround(stack, overflow);
                KebabLimitsLog.MessageImportant(
                    $"[HSK kebab limits] PostPlaceSimilarStackOverflow per-stack thing={thingDef.defName} cell={stack.Position} stackCount={stack.stackCount + ejected} perStackLimit={perStackLimit} ejected={ejected}");
            }

            int total = stacks.Where(stack => stack.Spawned).Sum(stack => stack.stackCount);
            int maxTotal = maxStacks * perStackLimit;
            int totalOverflow = total - maxTotal;
            while (totalOverflow > 0)
            {
                stacks = slotGroup.HeldThings
                    .Where(item => item != null && item.Spawned && item.def == thingDef)
                    .ToList();
                if (stacks.Count == 0)
                {
                    break;
                }

                Thing victim = stacks[stacks.Count - 1];
                int ejectCount = Math.Min(totalOverflow, victim.stackCount);
                int ejected = UnloadCountToGround(victim, ejectCount);
                if (ejected <= 0)
                {
                    break;
                }

                totalOverflow -= ejected;
                KebabLimitsLog.MessageImportant(
                    $"[HSK kebab limits] PostPlaceSimilarStackOverflow total-cap thing={thingDef.defName} remainingOverflow={totalOverflow} ejected={ejected} cell={victim.Position}");
            }
        }

        /// <summary>Runs delayed overflow ejection for one item type on a building storage slot group.</summary>
        public static void DelayedBuildingEjectNow(Building_Storage building, ThingDef thingDef)
        {
            if (building?.Map == null || building.slotGroup == null || thingDef == null)
            {
                KebabLimitsLog.Warning("[HSK kebab limits] DelayedBuildingEjectNow skipped: missing building/slotGroup/thingDef.");
                return;
            }

            StockpileLimitBundle profile = StockpileProfileStore.GetOrNull(building.GetStoreSettings());
            if (profile == null)
            {
                KebabLimitsLog.MessageImportant(
                    $"[HSK kebab limits] DelayedBuildingEjectNow skipped building=\"{building.SlotYielderLabel()}\" thing={thingDef.defName}: no profile.");
                return;
            }

            List<IntVec3> cells = building.slotGroup.CellsList;
            KebabLimitsLog.MessageImportant(
                $"[HSK kebab limits] DelayedBuildingEjectNow start building=\"{building.SlotYielderLabel()}\" thing={thingDef.defName} cells={cells.Count} itemLimit={profile.LimitFor(thingDef)} similarRaw={profile.SimilarStackCountRaw}");

            EnforceStorageLimitsInternal(building.slotGroup, building.Map, profile, thingDef);
            building.Map.listerHaulables.RecalcAllInCells(cells);
        }
    }
}
