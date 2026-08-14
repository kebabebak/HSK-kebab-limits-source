using System;
using System.Reflection;
using HarmonyLib;
using Verse;
using Verse.AI;

namespace HSKKebabLimits
{
    /// <summary>Harmony prefix/postfix for partial pawn drops; clamps count then handles remainder.</summary>
    [HarmonyPatch]
    public static class PawnCarryTrackerDropCountPatch
    {
        private static MethodBase targetMethod;

        private static bool Prepare()
        {
            targetMethod = typeof(Pawn_CarryTracker).GetMethod("TryDropCarriedThing", new[]
            {
                typeof(IntVec3),
                typeof(int),
                typeof(ThingPlaceMode),
                typeof(Thing).MakeByRefType(),
                typeof(Action<Thing, int>)
            });
            if (targetMethod != null)
            {
                return true;
            }

            Log.Error(
                "[HSK kebab limits] Could not find Pawn_CarryTracker.TryDropCarriedThing(count).");
            return false;
        }

        /// <summary>Targets Pawn_CarryTracker.TryDropCarriedThing (count overload).</summary>
        private static MethodBase TargetMethod()
        {
            return targetMethod;
        }

        /// <summary>Reduces drop count to allowed storage space or handles building per-stack drops.</summary>
        public static bool Prefix(Pawn_CarryTracker __instance, IntVec3 dropLoc, ref int count,
            ThingPlaceMode mode, ref Thing resultingThing, Action<Thing, int> placedAction, ref bool __result)
        {
            if (!StockpileCapacityRules.TryLimitFinalDrop(__instance, dropLoc, mode, count, out StockpileCapacityRules.LimitedDropContext context))
            {
                return true;
            }

            if (context.AllowedCount <= 0)
            {
                return StockpileCapacityRules.FinishZeroAllowedCarriedDrop(__instance, context, "drop-count-blocked", ref resultingThing,
                    ref __result);
            }

            if (context.BuildingPerStackOnly)
            {
                __result = StockpileCapacityRules.TryDropBuildingPerStack(__instance, context, placedAction, out resultingThing);
                if (context.AllowedCount < context.RequestedCount)
                {
                    StockpileCapacityRules.HandleLimitedDropRemainder(__instance, context, "drop-count-building-per-stack");
                }

                return false;
            }

            StockpileCapacityRules.PendingDropRemainders[__instance.pawn] = context;
            count = context.AllowedCount;
            return true;
        }

        /// <summary>Drops carry/inventory remainder after a partial deposit was allowed through.</summary>
        public static void Postfix(Pawn_CarryTracker __instance)
        {
            if (__instance?.pawn == null)
            {
                return;
            }

            if (!StockpileCapacityRules.PendingDropRemainders.TryGetValue(__instance.pawn, out StockpileCapacityRules.LimitedDropContext context))
            {
                return;
            }

            StockpileCapacityRules.PendingDropRemainders.Remove(__instance.pawn);
            StockpileCapacityRules.HandleLimitedDropRemainder(__instance, context, "drop-count-after-partial");
        }
    }
}
