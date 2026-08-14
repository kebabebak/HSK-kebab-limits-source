using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;

namespace HSKKebabLimits
{
    public static partial class StockpileCapacityRules
    {
        /// <summary>Captures pawn drop state when a haul deposit is clamped by storage limits.</summary>
        internal class LimitedDropContext
        {
            public Pawn Pawn;
            public Map Map;
            public IntVec3 DropLoc;
            public ThingDef ThingDef;
            public int RequestedCount;
            public int AllowedCount;
            public StockpileLimitBundle Profile;
            public SlotGroup SlotGroup;
            public Zone_Stockpile Zone;
            public string StorageLabel;
            public bool BuildingPerStackOnly;
            public int PerStackLimit;

            /// <summary>True when the drop destination is a floor stockpile zone.</summary>
            public bool IsStockpileZone => Zone != null;
        }

        internal static readonly Dictionary<Pawn, LimitedDropContext> PendingDropRemainders =
            new Dictionary<Pawn, LimitedDropContext>();
        /// <summary>Builds a limited-drop context when a pawn's direct deposit would exceed storage limits.</summary>
        internal static bool TryLimitFinalDrop(Pawn_CarryTracker carryTracker, IntVec3 dropLoc,
            ThingPlaceMode mode, int requestedCount, out LimitedDropContext context)
        {
            context = null;
            if (mode != ThingPlaceMode.Direct || requestedCount <= 0 || carryTracker?.CarriedThing == null)
            {
                return false;
            }

            Pawn pawn = carryTracker.pawn;
            Map map = pawn?.MapHeld ?? pawn?.Map;
            Thing carried = carryTracker.CarriedThing;
            if (map == null || carried.def.category != ThingCategory.Item)
            {
                return false;
            }

            StockpileLimitBundle profile = StockpileProfileStore.FindOrNull(dropLoc, map);
            SlotGroup slotGroup = map.haulDestinationManager.SlotGroupAt(dropLoc);
            if (profile == null || (!profile.EnforcesUpperBound() && !profile.EnforcesSimilarStackCap() &&
                                    !profile.HasEnforceableStorageWideLimits))
            {
                return false;
            }

            int limitedCount = requestedCount;
            ApplyCapacityClamp(dropLoc, map, carried.def, ref limitedCount);
            int allowedCount = Math.Max(0, limitedCount);
            bool buildingPerStackOnly = UsesBuildingPerStackOnlyMode(dropLoc, map, profile);
            int perStackLimit = Math.Min(profile.LimitFor(carried.def), carried.def.stackLimit);
            if (!buildingPerStackOnly && allowedCount >= requestedCount)
            {
                return false;
            }

            if (buildingPerStackOnly && requestedCount <= perStackLimit && allowedCount >= requestedCount)
            {
                return false;
            }

            LogFinalDropLimit(pawn, map, dropLoc, carried, requestedCount, allowedCount, profile);
            context = new LimitedDropContext
            {
                Pawn = pawn,
                Map = map,
                DropLoc = dropLoc,
                ThingDef = carried.def,
                RequestedCount = requestedCount,
                AllowedCount = allowedCount,
                Profile = profile,
                SlotGroup = slotGroup,
                Zone = slotGroup?.parent as Zone_Stockpile,
                StorageLabel = slotGroup?.parent?.SlotYielderLabel() ?? "none",
                BuildingPerStackOnly = buildingPerStackOnly,
                PerStackLimit = perStackLimit
            };
            ScheduleDelayedOverflowOnLimitHit(slotGroup, carried.def,
                $"final-drop-limit pawn={pawn?.LabelShort ?? "none"} job={pawn?.CurJobDef?.defName ?? "none"} requested={requestedCount} allowed={allowedCount}");
            return true;
        }

        /// <summary>Drops carried items in per-stack chunks for building storage with per-stack-only limits.</summary>
        internal static bool TryDropBuildingPerStack(Pawn_CarryTracker carryTracker, LimitedDropContext context,
            Action<Thing, int> placedAction, out Thing resultingThing)
        {
            resultingThing = null;
            if (carryTracker?.innerContainer == null || context == null || !context.BuildingPerStackOnly)
            {
                return false;
            }

            int remainingToPlace = context.AllowedCount;
            int placedTotal = 0;
            while (remainingToPlace > 0)
            {
                Thing carried = carryTracker.CarriedThing;
                if (carried == null || carried.def != context.ThingDef || carried.stackCount <= 0)
                {
                    break;
                }

                int chunk = Math.Min(context.PerStackLimit, remainingToPlace);
                chunk = Math.Min(chunk, carried.stackCount);
                if (chunk <= 0)
                {
                    break;
                }

                bool dropped = carryTracker.innerContainer.TryDrop(carried, context.DropLoc, context.Map,
                    ThingPlaceMode.Direct, chunk, out Thing placedThing, placedAction);
                if (!dropped)
                {
                    KebabLimitsLog.Warning(
                        $"[HSK kebab limits] BuildingPerStackDrop failed pawn={context.Pawn?.LabelShort ?? "none"} thing={context.ThingDef.defName} chunk={chunk} placedTotal={placedTotal} remainingToPlace={remainingToPlace} dest={context.DropLoc} storage=\"{context.StorageLabel}\"");
                    break;
                }

                resultingThing = placedThing;
                placedTotal += chunk;
                remainingToPlace -= chunk;
                KebabLimitsLog.MessageImportant(
                    $"[HSK kebab limits] BuildingPerStackDrop placed pawn={context.Pawn?.LabelShort ?? "none"} thing={context.ThingDef.defName} chunk={chunk} placedTotal={placedTotal} remainingToPlace={remainingToPlace} dest={context.DropLoc} storage=\"{context.StorageLabel}\" perStackLimit={context.PerStackLimit}");
            }

            KebabLimitsLog.MessageImportant(
                $"[HSK kebab limits] BuildingPerStackDrop finished pawn={context.Pawn?.LabelShort ?? "none"} thing={context.ThingDef.defName} requested={context.RequestedCount} allowed={context.AllowedCount} placedTotal={placedTotal} leftoverInCarry={carryTracker.CarriedThing?.stackCount ?? 0} dest={context.DropLoc} storage=\"{context.StorageLabel}\"");
            return placedTotal > 0;
        }

        /// <summary>Returns whether the pawn still carries or holds the given def in inventory.</summary>
        private static bool PawnHoldsThingDef(Pawn pawn, ThingDef def)
        {
            if (pawn == null || def == null)
            {
                return false;
            }

            if (pawn.carryTracker?.CarriedThing?.def == def)
            {
                return true;
            }

            ThingOwner<Thing> inventory = pawn.inventory?.innerContainer;
            if (inventory == null)
            {
                return false;
            }

            for (int i = 0; i < inventory.Count; i++)
            {
                if (inventory[i]?.def == def)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Drops leftover carried or inventory items near the pawn after a partial storage deposit.</summary>
        internal static int HandleLimitedDropRemainder(Pawn_CarryTracker carryTracker, LimitedDropContext context,
            string stage)
        {
            Pawn pawn = carryTracker?.pawn ?? context?.Pawn;
            if (pawn == null || context == null || context.ThingDef == null)
            {
                KebabLimitsLog.Warning("[HSK kebab limits] HandleLimitedDropRemainder skipped: missing pawn/context/thingDef.");
                return 0;
            }

            Map map = pawn.MapHeld ?? pawn.Map ?? context.Map;
            if (map == null)
            {
                KebabLimitsLog.Warning(
                    $"[HSK kebab limits] HandleLimitedDropRemainder skipped pawn={pawn.LabelShort} thing={context.ThingDef.defName}: no map.");
                return 0;
            }

            int carriedDropped = DropMatchingFromOwner(pawn.carryTracker?.innerContainer, pawn, map, context, "carry");
            int inventoryDropped = DropMatchingFromOwner(pawn.inventory?.innerContainer, pawn, map, context, "inventory");
            int totalDropped = carriedDropped + inventoryDropped;
            KebabLimitsLog.MessageImportant(
                $"[HSK kebab limits] LimitedDropRemainderHandled stage={stage} pawn={pawn.LabelShort} thing={context.ThingDef.defName} requested={context.RequestedCount} allowed={context.AllowedCount} droppedCarry={carriedDropped} droppedInventory={inventoryDropped} totalDropped={totalDropped} dest={context.DropLoc} storage=\"{context.StorageLabel}\" stockpile={context.IsStockpileZone}");

            EnforceStorageLimitsAfterPlacement(context.DropLoc, map, context.ThingDef, $"limited-drop-{stage}");
            return totalDropped;
        }

        /// <summary>
        /// When storage allows zero at the haul target, eject cargo near the pawn and tell JobDriver the drop step succeeded
        /// so PlaceHauledThingInCell does not search alternate stores with a null haulable (NullReferenceException).
        /// </summary>
        internal static bool FinishZeroAllowedCarriedDrop(Pawn_CarryTracker carryTracker, LimitedDropContext context,
            string stage, ref Thing resultingThing, ref bool __result)
        {
            int totalDropped = HandleLimitedDropRemainder(carryTracker, context, stage);
            Pawn pawn = carryTracker?.pawn ?? context.Pawn;
            bool cleared = !PawnHoldsThingDef(pawn, context.ThingDef);
            resultingThing = null;
            __result = cleared;
            string jobDef = pawn?.CurJobDef?.defName ?? "none";
            if (cleared)
            {
                KebabLimitsLog.MessageImportant(
                    $"[HSK kebab limits] BlockedDropTryCarriedThing success stage={stage} pawn={pawn?.LabelShort ?? "none"} job={jobDef} thing={context.ThingDef.defName} totalDropped={totalDropped} tryDropResult=true dest={context.DropLoc} storage=\"{context.StorageLabel}\"");
            }
            else
            {
                KebabLimitsLog.Warning(
                    $"[HSK kebab limits] BlockedDropTryCarriedThing incomplete stage={stage} pawn={pawn?.LabelShort ?? "none"} job={jobDef} thing={context.ThingDef.defName} totalDropped={totalDropped} tryDropResult=false dest={context.DropLoc} storage=\"{context.StorageLabel}\"");
            }

            return false;
        }

        /// <summary>Drops matching items from carry or inventory to non-storage cells near the pawn.</summary>
        private static int DropMatchingFromOwner(ThingOwner<Thing> owner, Pawn pawn, Map map,
            LimitedDropContext context, string source)
        {
            if (owner == null || owner.Count == 0)
            {
                KebabLimitsLog.Message(
                    $"[HSK kebab limits] DropMatchingFromOwner source={source} pawn={pawn.LabelShort} thing={context.ThingDef.defName}: owner empty.");
                return 0;
            }

            int droppedTotal = 0;
            for (int i = owner.Count - 1; i >= 0; i--)
            {
                Thing item = owner[i];
                if (item?.def != context.ThingDef || item.stackCount <= 0)
                {
                    continue;
                }

                int count = item.stackCount;
                bool dropped = owner.TryDrop(item, pawn.Position, map, ThingPlaceMode.Near, count,
                    out Thing resultingThing, null, cell => IsNonStorageDropCell(cell, map));

                if (dropped)
                {
                    droppedTotal += count;
                    if (resultingThing?.Spawned == true)
                    {
                        EnforceStorageLimitsAfterPlacement(resultingThing.Position, map, context.ThingDef,
                            $"remainder-{source}");
                    }

                    KebabLimitsLog.MessageImportant(
                        $"[HSK kebab limits] DropMatchingFromOwner dropped source={source} pawn={pawn.LabelShort} thing={context.ThingDef.defName} count={count} result={resultingThing?.Position.ToString() ?? "none"} storage=\"{context.StorageLabel}\"");
                }
                else
                {
                    KebabLimitsLog.Warning(
                        $"[HSK kebab limits] DropMatchingFromOwner failed source={source} pawn={pawn.LabelShort} thing={context.ThingDef.defName} count={count} storage=\"{context.StorageLabel}\"");
                }
            }

            return droppedTotal;
        }

        /// <summary>Logs diagnostic details when a pawn's final haul drop is clamped by storage limits.</summary>
        private static void LogFinalDropLimit(Pawn pawn, Map map, IntVec3 dropLoc, Thing carried,
            int requestedCount, int allowedCount, StockpileLimitBundle profile)
        {
            if (!KebabLimitsModSettings.EnableLogging)
            {
                return;
            }

            int cellCount = SumMatchingStacksAtCell(dropLoc, map, carried.def, out int cellStacks, out int foreignStacks);
            SlotGroup slotGroup = map.haulDestinationManager.SlotGroupAt(dropLoc);
            int zoneCount = slotGroup == null ? 0 : SumMatchingStacksInCollection(slotGroup.HeldThings.ToList(), carried.def,
                out int zoneStacks);
            int totalAll = slotGroup == null ? 0 : CountAllItemUnits(slotGroup.HeldThings);
            int similarSpace = StorageWideSimilarStackSpace(dropLoc, map, carried.def, profile,
                profile.LimitFor(carried.def), out int similarStacks, out int similarTotal, out bool onTargetCell);
            string storageLabel = slotGroup?.parent?.SlotYielderLabel() ?? "none";
            string jobDef = pawn?.CurJobDef?.defName ?? "none";
            KebabLimitsLog.MessageImportant(
                $"[HSK kebab limits] FinalDropLimit pawn={pawn?.LabelShort ?? "none"} job={jobDef} thing={carried.def.defName} dest={dropLoc} storage=\"{storageLabel}\" requested={requestedCount} allowed={allowedCount} carried={carried.stackCount} itemLimit={profile.LimitFor(carried.def)} totalCap={profile.StorageWideTotalCap()} totalAll={totalAll} perItemOverride={profile.HasPerItemLimit(carried.def)} similarRaw={profile.SimilarStackCountRaw} similarSpace={similarSpace} similarStacks={similarStacks} similarTotal={similarTotal} onTargetCell={onTargetCell} cellCount={cellCount} cellStacks={cellStacks} foreignStacks={foreignStacks} zoneCount={zoneCount}");
        }
    }
}
