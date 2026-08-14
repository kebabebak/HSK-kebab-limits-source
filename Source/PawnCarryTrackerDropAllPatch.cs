using System;
using System.Reflection;
using HarmonyLib;
using Verse;
using Verse.AI;

namespace HSKKebabLimits
{
    /// <summary>Harmony prefix for full-stack pawn drops; clamps deposit count to storage limits.</summary>
    [HarmonyPatch]
    public static class PawnCarryTrackerDropAllPatch
    {
        private static MethodBase targetMethod;

        private static bool Prepare()
        {
            targetMethod = typeof(Pawn_CarryTracker).GetMethod("TryDropCarriedThing", new[]
            {
                typeof(IntVec3),
                typeof(ThingPlaceMode),
                typeof(Thing).MakeByRefType(),
                typeof(Action<Thing, int>)
            });
            if (targetMethod != null)
            {
                return true;
            }

            Log.Error(
                "[HSK kebab limits] Could not find Pawn_CarryTracker.TryDropCarriedThing(full stack).");
            return false;
        }

        /// <summary>Targets Pawn_CarryTracker.TryDropCarriedThing (full stack overload).</summary>
        private static MethodBase TargetMethod()
        {
            return targetMethod;
        }

        /// <summary>Limits or redirects a full carried-stack drop before vanilla placement runs.</summary>
        public static bool Prefix(Pawn_CarryTracker __instance, IntVec3 dropLoc, ThingPlaceMode mode,
            ref Thing resultingThing, Action<Thing, int> placedAction, ref bool __result)
        {
            Thing carried = __instance?.CarriedThing;
            if (carried == null || !StockpileCapacityRules.TryLimitFinalDrop(__instance, dropLoc, mode, carried.stackCount,
                    out StockpileCapacityRules.LimitedDropContext context))
            {
                return true;
            }

            if (context.AllowedCount <= 0)
            {
                return StockpileCapacityRules.FinishZeroAllowedCarriedDrop(__instance, context, "drop-all-blocked", ref resultingThing,
                    ref __result);
            }

            if (context.BuildingPerStackOnly)
            {
                __result = StockpileCapacityRules.TryDropBuildingPerStack(__instance, context, placedAction, out resultingThing);
                if (context.AllowedCount < context.RequestedCount)
                {
                    StockpileCapacityRules.HandleLimitedDropRemainder(__instance, context, "drop-all-building-per-stack");
                }

                return false;
            }

            __result = __instance.TryDropCarriedThing(dropLoc, context.AllowedCount, mode, out resultingThing,
                placedAction);
            StockpileCapacityRules.HandleLimitedDropRemainder(__instance, context, "drop-all-after-partial");
            return false;
        }
    }
}
