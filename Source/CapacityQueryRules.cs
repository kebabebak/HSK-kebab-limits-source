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
        /// <summary>Clamps a haul or placement count to remaining space under the storage limit profile at a cell.</summary>
        public static void ApplyCapacityClamp(IntVec3 cell, Map map, ThingDef itemDef, ref int refCountValue)
        {
            StockpileLimitBundle profile = StockpileProfileStore.FindOrNull(cell, map);
            if (profile == null)
            {
                return;
            }

            int maxSpace = CapacityQueryCache.GetMaxSpace(cell, map, itemDef,
                () => ComputeMaxSpace(cell, map, itemDef, profile));
            refCountValue = Math.Min(refCountValue, maxSpace);
            if (refCountValue < 0)
            {
                refCountValue = 0;
            }
        }

        /// <summary>Returns remaining storable count at a cell after kebab limits limits (minimum query size 1).</summary>
        public static int GetAllocatableUnitsAt(IntVec3 cell, Map map, ThingDef itemDef, int requestedCount = 1)
        {
            int space = Math.Max(requestedCount, 1);
            ApplyCapacityClamp(cell, map, itemDef, ref space);
            return Math.Max(space, 0);
        }

        /// <summary>Maximum storable units at a cell under the active profile (uncached computation).</summary>
        private static int ComputeMaxSpace(IntVec3 cell, Map map, ThingDef itemDef, StockpileLimitBundle profile)
        {
            int space = int.MaxValue;
            SlotGroup slotGroup = map.haulDestinationManager.SlotGroupAt(cell);
            int limit = profile.LimitFor(itemDef);

            if (profile.HasEnforceableStorageWideLimits && slotGroup != null)
            {
                CapacityQueryCache.SlotGroupSnapshot snapshot =
                    CapacityQueryCache.GetSnapshot(map, slotGroup);
                snapshot.GetSimilar(itemDef, out int itemCount, out _);
                int wideRemaining = profile.EffectiveWideModeRemaining(itemDef, itemCount,
                    snapshot.TotalItemUnits);
                space = Math.Min(space, wideRemaining);
                KebabLimitsLog.MessageVerbose(
                    $"[HSK kebab limits] Solve storage-wide remaining cell={cell} thing={itemDef.defName} itemCount={itemCount} totalCount={snapshot.TotalItemUnits} totalCap={profile.StorageWideTotalCap()} refCount={wideRemaining}");
            }

            if (profile.EnforcesSimilarStackCap())
            {
                int similarSpace = StorageWideSimilarStackSpace(cell, map, itemDef, profile, limit,
                    out _, out _, out _);
                space = Math.Min(space, similarSpace);
            }

            if (profile.EnforcesUpperBound() && !profile.UsesStorageWideLimitFromLimitFor)
            {
                int val = UsesBuildingPerStackOnlyMode(cell, map, profile)
                    ? BuildingStoragePerStackSpace(cell, map, itemDef, limit)
                    : UnitsRemainingInCell(cell, map, itemDef, limit, profile.IsAllowedMultistack(),
                        PerCellMaxSimilarStacks(profile));
                space = Math.Min(space, val);
            }

            if (space == int.MaxValue)
            {
                return int.MaxValue;
            }

            return Math.Max(space, 0);
        }

        /// <summary>True when negative cross-tick cache may apply (group-level cap modes only).</summary>
        private static bool ProfileSupportsNegativeGroupCache(StockpileLimitBundle profile)
        {
            return profile != null &&
                   (profile.HasEnforceableStorageWideLimits || profile.EnforcesSimilarStackCap());
        }

        /// <summary>
        /// True when group-level caps leave no room for itemDef anywhere in the slot group.
        /// Only storage-wide total and similar-stack modes; per-cell zones and building per-stack need a full scan.
        /// </summary>
        private static bool SlotGroupDefinitelyFullForDef(SlotGroup slotGroup, Map map, ThingDef itemDef,
            StockpileLimitBundle profile)
        {
            if (slotGroup == null || map == null || itemDef == null || profile == null)
            {
                return false;
            }

            if (profile.HasEnforceableStorageWideLimits)
            {
                CapacityQueryCache.SlotGroupSnapshot snapshot =
                    CapacityQueryCache.GetSnapshot(map, slotGroup);
                snapshot.GetSimilar(itemDef, out int itemCount, out _);
                return profile.EffectiveWideModeRemaining(itemDef, itemCount, snapshot.TotalItemUnits) <= 0;
            }

            if (!profile.EnforcesSimilarStackCap())
            {
                return false;
            }

            int maxStacks = profile.SimilarStackCount;
            if (maxStacks <= 0)
            {
                return false;
            }

            int limit = profile.LimitFor(itemDef);
            int perStackLimit = Math.Min(itemDef.stackLimit, limit);
            CapacityQueryCache.SlotGroupSnapshot similarSnapshot =
                CapacityQueryCache.GetSnapshot(map, slotGroup);
            similarSnapshot.GetSimilar(itemDef, out int totalCount, out int stackCount);

            if (stackCount > maxStacks)
            {
                return true;
            }

            return totalCount >= maxStacks * perStackLimit;
        }

        /// <summary>True when min slider is active and the cell is below threshold or in a replenish cycle.</summary>
        private static bool CellNeedsMinReplenish(IntVec3 cell, Map map, ThingDef itemDef,
            StockpileLimitBundle profile)
        {
            if (!profile.EnforcesLowerBound() || itemDef == null || map == null || !cell.IsValid)
            {
                return false;
            }

            if (CapacityQueryCache.IsReplenishing(map, cell, itemDef))
            {
                return true;
            }

            int minLimit = KebabLimitsModSettings.PercentageMode
                ? LogarithmicStackScale.ToItemPercentageCount(profile.allowedLimitPercents.min, itemDef.stackLimit)
                : LogarithmicStackScale.ToAbsoluteCount(profile.allowedLimitPercents.min);
            int count = SumMatchingStacksAtCell(cell, map, itemDef, out _, out _);
            return count < minLimit;
        }

        /// <summary>Returns whether a cell currently holds an item of the given def.</summary>
        private static bool CellHasItemOfDef(IntVec3 cell, Map map, ThingDef itemDef)
        {
            foreach (Thing thing in map.thingGrid.ThingsListAt(cell))
            {
                if (thing.def == itemDef && thing.def.category == ThingCategory.Item)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Resolves a haul destination cell under kebab limits limits. When vanilla picks an empty Deep Storage
        /// cell with no similar-stack room, retargets to the existing stack cell in the same slot group.
        /// </summary>
        public static bool RetargetStoreCellIfNeeded(ref IntVec3 cell, Map map, ThingDef itemDef, out int space)
        {
            space = 0;
            if (!cell.IsValid || itemDef == null || map == null)
            {
                return false;
            }

            space = GetAllocatableUnitsAt(cell, map, itemDef, 1);
            if (space > 0)
            {
                return true;
            }

            SlotGroup slotGroup = cell.GetSlotGroup(map);
            if (slotGroup?.CellsList == null)
            {
                return false;
            }

            StockpileLimitBundle profile = StockpileProfileStore.FindOrNull(cell, map);
            if (profile != null && ProfileSupportsNegativeGroupCache(profile) &&
                CapacityQueryCache.TryGetNegativeGroupFull(map, slotGroup, itemDef) &&
                !CellNeedsMinReplenish(cell, map, itemDef, profile))
            {
                return false;
            }

            if (profile != null && SlotGroupDefinitelyFullForDef(slotGroup, map, itemDef, profile))
            {
                if (ProfileSupportsNegativeGroupCache(profile))
                {
                    CapacityQueryCache.SetNegativeGroupFull(map, slotGroup, itemDef);
                }

                return false;
            }

            IntVec3 bestEmptyCell = IntVec3.Invalid;
            int bestEmptySpace = 0;
            IntVec3 bestMergeCell = IntVec3.Invalid;
            int bestMergeSpace = 0;
            foreach (IntVec3 candidate in slotGroup.CellsList)
            {
                int candidateSpace = GetAllocatableUnitsAt(candidate, map, itemDef, 1);
                if (candidateSpace <= 0)
                {
                    continue;
                }

                if (CellHasItemOfDef(candidate, map, itemDef))
                {
                    if (candidateSpace > bestMergeSpace)
                    {
                        bestMergeSpace = candidateSpace;
                        bestMergeCell = candidate;
                    }
                }
                else if (candidateSpace > bestEmptySpace)
                {
                    bestEmptySpace = candidateSpace;
                    bestEmptyCell = candidate;
                }
            }

            IntVec3 resolved = bestMergeCell.IsValid ? bestMergeCell : bestEmptyCell;
            int resolvedSpace = bestMergeCell.IsValid ? bestMergeSpace : bestEmptySpace;
            if (!resolved.IsValid)
            {
                if (profile != null && ProfileSupportsNegativeGroupCache(profile))
                {
                    CapacityQueryCache.SetNegativeGroupFull(map, slotGroup, itemDef);
                }

                return false;
            }

            cell = resolved;
            space = resolvedSpace;
            return true;
        }

        /// <summary>True when a HaulToCell job destination can accept at least one unit of the haulable.</summary>
        public static bool HaulDestinationHasSpace(Job job, Thing haulThing, Map map, int requestedCount)
        {
            if (job?.def != JobDefOf.HaulToCell || haulThing?.def == null || map == null || !job.targetB.IsValid)
            {
                return true;
            }

            return GetAllocatableUnitsAt(job.targetB.Cell, map, haulThing.def, Math.Max(requestedCount, 1)) > 0;
        }

        /// <summary>Ends a HaulToCell job when its destination no longer has kebab limits space for the haulable.</summary>
        internal static bool TryAbortHaulToCellWithoutSpace(Pawn pawn, Thing haulThing, string stage)
        {
            Job job = pawn?.jobs?.curJob;
            Map map = pawn?.Map ?? haulThing?.Map ?? haulThing?.MapHeld;
            if (job == null || map == null || haulThing == null)
            {
                return false;
            }

            int requested = job.count > 0 ? job.count : haulThing.stackCount;
            if (HaulDestinationHasSpace(job, haulThing, map, requested))
            {
                return false;
            }

            KebabLimitsLog.MessageVerbose(
                $"[HSK kebab limits] HaulLiveRejected stage={stage} pawn={pawn.LabelShort} thing={haulThing.def.defName} dest={job.targetB.Cell} storage=\"{job.targetB.Cell.GetSlotGroup(map)?.parent?.SlotYielderLabel() ?? "none"}\" requested={requested} stackCount={haulThing.stackCount}");
            pawn.jobs.EndCurrentJob(JobCondition.Incompletable);
            return true;
        }
    }
}
