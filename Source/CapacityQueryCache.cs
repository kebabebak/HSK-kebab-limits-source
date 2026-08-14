using System.Collections.Generic;
using RimWorld;
using Verse;

namespace HSKKebabLimits
{
    /// <summary>
    /// Per-tick cache for Solve max-space and slot-group item counts. Cuts repeated HeldThings scans
    /// during haul store search.
    /// Cross-tick negative cache remembers provably-full slot groups when enabled in settings.
    /// Per-cell replenish flag tracks fill-to-max cycles started when count drops below min slider.
    /// </summary>
    internal static class CapacityQueryCache
    {
        internal struct SimilarDefCounts
        {
            public int ItemUnits;
            public int StackCount;
        }

        internal sealed class SlotGroupSnapshot
        {
            public int TotalItemUnits;
            public Dictionary<ThingDef, SimilarDefCounts> SimilarByDef;

            public void GetSimilar(ThingDef def, out int itemUnits, out int stackCount)
            {
                if (def != null && SimilarByDef != null &&
                    SimilarByDef.TryGetValue(def, out SimilarDefCounts counts))
                {
                    itemUnits = counts.ItemUnits;
                    stackCount = counts.StackCount;
                    return;
                }

                itemUnits = 0;
                stackCount = 0;
            }
        }

        private static int activeTick = -1;
        private static readonly Dictionary<int, int> maxSpaceCache = new Dictionary<int, int>();
        private static readonly Dictionary<int, List<int>> spaceKeysByCell = new Dictionary<int, List<int>>();
        private static readonly Dictionary<int, SlotGroupSnapshot> slotGroupSnapshots = new Dictionary<int, SlotGroupSnapshot>();
        private static readonly Dictionary<int, bool> negativeGroupFullByKey = new Dictionary<int, bool>();
        private static readonly Dictionary<int, HashSet<int>> negativeKeysByGroup = new Dictionary<int, HashSet<int>>();
        private static readonly Dictionary<int, Dictionary<int, bool>> replenishByCell = new Dictionary<int, Dictionary<int, bool>>();

        public static bool NegativeCacheActive => KebabLimitsModSettings.EnableNegativeSolveCache;

        public static void EnsureCurrentTick()
        {
            int tick = Find.TickManager.TicksGame;
            if (tick == activeTick)
            {
                return;
            }

            activeTick = tick;
            maxSpaceCache.Clear();
            spaceKeysByCell.Clear();
            slotGroupSnapshots.Clear();
        }

        public static void ClearAllNegative()
        {
            negativeGroupFullByKey.Clear();
            negativeKeysByGroup.Clear();
        }

        /// <summary>True when a fill-to-max cycle is active after count dropped below the min slider.</summary>
        public static bool IsReplenishing(Map map, IntVec3 cell, ThingDef itemDef)
        {
            if (map == null || itemDef == null || !cell.IsValid)
            {
                return false;
            }

            int cellBucket = CellBucket(map, cell);
            if (!replenishByCell.TryGetValue(cellBucket, out Dictionary<int, bool> byDef))
            {
                return false;
            }

            return byDef.TryGetValue(itemDef.shortHash, out bool active) && active;
        }

        /// <summary>Marks or clears an active min-triggered replenish cycle for a cell and item type.</summary>
        public static void SetReplenishing(Map map, IntVec3 cell, ThingDef itemDef, bool active)
        {
            if (map == null || itemDef == null || !cell.IsValid)
            {
                return;
            }

            int cellBucket = CellBucket(map, cell);
            if (!replenishByCell.TryGetValue(cellBucket, out Dictionary<int, bool> byDef))
            {
                if (!active)
                {
                    return;
                }

                byDef = new Dictionary<int, bool>();
                replenishByCell[cellBucket] = byDef;
            }

            if (active)
            {
                byDef[itemDef.shortHash] = true;
            }
            else
            {
                byDef.Remove(itemDef.shortHash);
                if (byDef.Count == 0)
                {
                    replenishByCell.Remove(cellBucket);
                }
            }
        }

        /// <summary>Clears replenish flags for every cell in a slot group (limit slider reset).</summary>
        public static void ClearReplenishForSlotGroup(Map map, SlotGroup slotGroup)
        {
            if (map == null || slotGroup?.CellsList == null)
            {
                return;
            }

            List<IntVec3> cells = slotGroup.CellsList;
            for (int i = 0; i < cells.Count; i++)
            {
                replenishByCell.Remove(CellBucket(map, cells[i]));
            }
        }

        public static bool TryGetNegativeGroupFull(Map map, SlotGroup slotGroup, ThingDef itemDef)
        {
            if (!NegativeCacheActive || map == null || slotGroup == null || itemDef == null)
            {
                return false;
            }

            int entryKey = NegativeEntryKey(SlotGroupKey(map, slotGroup), itemDef.shortHash);
            return negativeGroupFullByKey.TryGetValue(entryKey, out bool full) && full;
        }

        public static void SetNegativeGroupFull(Map map, SlotGroup slotGroup, ThingDef itemDef)
        {
            if (!NegativeCacheActive || map == null || slotGroup == null || itemDef == null)
            {
                return;
            }

            int groupKey = SlotGroupKey(map, slotGroup);
            int entryKey = NegativeEntryKey(groupKey, itemDef.shortHash);
            negativeGroupFullByKey[entryKey] = true;
            if (!negativeKeysByGroup.TryGetValue(groupKey, out HashSet<int> keys))
            {
                keys = new HashSet<int>();
                negativeKeysByGroup[groupKey] = keys;
            }

            keys.Add(entryKey);
        }

        public static bool TryGetCachedMaxSpace(IntVec3 cell, Map map, ThingDef itemDef, out int maxSpace)
        {
            maxSpace = 0;
            EnsureCurrentTick();
            if (map == null || itemDef == null || !cell.IsValid)
            {
                return false;
            }

            return maxSpaceCache.TryGetValue(SpaceKey(map, cell, itemDef), out maxSpace);
        }

        public static int GetMaxSpace(IntVec3 cell, Map map, ThingDef itemDef, System.Func<int> compute)
        {
            EnsureCurrentTick();
            if (map == null || itemDef == null || !cell.IsValid)
            {
                return 0;
            }

            int key = SpaceKey(map, cell, itemDef);
            if (maxSpaceCache.TryGetValue(key, out int cached))
            {
                return cached;
            }

            int space = compute();
            maxSpaceCache[key] = space;
            int cellBucket = CellBucket(map, cell);
            if (!spaceKeysByCell.TryGetValue(cellBucket, out List<int> keys))
            {
                keys = new List<int>();
                spaceKeysByCell[cellBucket] = keys;
            }

            keys.Add(key);
            return space;
        }

        public static SlotGroupSnapshot GetSnapshot(Map map, SlotGroup slotGroup)
        {
            EnsureCurrentTick();
            if (map == null || slotGroup == null)
            {
                return null;
            }

            int key = SlotGroupKey(map, slotGroup);
            if (slotGroupSnapshots.TryGetValue(key, out SlotGroupSnapshot snapshot))
            {
                return snapshot;
            }

            snapshot = BuildSnapshot(slotGroup);
            slotGroupSnapshots[key] = snapshot;
            return snapshot;
        }

        public static void InvalidateCell(Map map, IntVec3 cell)
        {
            EnsureCurrentTick();
            if (map == null || !cell.IsValid)
            {
                return;
            }

            int cellBucket = CellBucket(map, cell);
            if (!spaceKeysByCell.TryGetValue(cellBucket, out List<int> keys))
            {
                return;
            }

            for (int i = 0; i < keys.Count; i++)
            {
                maxSpaceCache.Remove(keys[i]);
            }

            spaceKeysByCell.Remove(cellBucket);
        }

        public static void InvalidateSlotGroup(Map map, SlotGroup slotGroup)
        {
            EnsureCurrentTick();
            if (map == null || slotGroup == null)
            {
                return;
            }

            int groupKey = SlotGroupKey(map, slotGroup);
            slotGroupSnapshots.Remove(groupKey);
            ClearNegativeForGroupKey(groupKey);

            List<IntVec3> cells = slotGroup.CellsList;
            if (cells == null)
            {
                return;
            }

            for (int i = 0; i < cells.Count; i++)
            {
                InvalidateCell(map, cells[i]);
            }
        }

        private static void ClearNegativeForGroupKey(int groupKey)
        {
            if (!negativeKeysByGroup.TryGetValue(groupKey, out HashSet<int> keys))
            {
                return;
            }

            foreach (int entryKey in keys)
            {
                negativeGroupFullByKey.Remove(entryKey);
            }

            negativeKeysByGroup.Remove(groupKey);
        }

        private static SlotGroupSnapshot BuildSnapshot(SlotGroup slotGroup)
        {
            SlotGroupSnapshot snapshot = new SlotGroupSnapshot
            {
                SimilarByDef = new Dictionary<ThingDef, SimilarDefCounts>()
            };

            foreach (Thing item in slotGroup.HeldThings)
            {
                if (item?.def?.category != ThingCategory.Item)
                {
                    continue;
                }

                snapshot.TotalItemUnits += item.stackCount;
                if (!snapshot.SimilarByDef.TryGetValue(item.def, out SimilarDefCounts counts))
                {
                    counts = default;
                }

                counts.ItemUnits += item.stackCount;
                counts.StackCount++;
                snapshot.SimilarByDef[item.def] = counts;
            }

            return snapshot;
        }

        private static int CellBucket(Map map, IntVec3 cell)
        {
            return Gen.HashCombineInt(map.uniqueID, cell.GetHashCode());
        }

        private static int SpaceKey(Map map, IntVec3 cell, ThingDef itemDef)
        {
            return Gen.HashCombineInt(CellBucket(map, cell), itemDef.shortHash);
        }

        private static int NegativeEntryKey(int groupKey, int defShortHash)
        {
            return Gen.HashCombineInt(groupKey, defShortHash);
        }

        private static int SlotGroupKey(Map map, SlotGroup slotGroup)
        {
            int parentId;
            if (slotGroup.parent is Thing parentThing)
            {
                parentId = parentThing.thingIDNumber;
            }
            else if (slotGroup.parent is Zone zone)
            {
                parentId = zone.ID;
            }
            else
            {
                parentId = slotGroup.GetHashCode();
            }

            return Gen.HashCombineInt(map.uniqueID, parentId);
        }
    }
}
