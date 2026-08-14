using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace HSKKebabLimits
{
    /// <summary>Harmony postfix for direct GenPlace placement; enforces limits after items land in storage.</summary>
    [HarmonyPatch]
    public static class GenPlaceTryPlaceDirectPatch
    {
        private static MethodBase targetMethod;

        private static bool Prepare()
        {
            targetMethod = AccessTools.Method(typeof(GenPlace), "TryPlaceDirect", new[]
            {
                typeof(Thing),
                typeof(IntVec3),
                typeof(Rot4),
                typeof(Map),
                typeof(Thing).MakeByRefType(),
                typeof(Action<Thing, int>)
            });
            if (targetMethod != null)
            {
                return true;
            }

            Log.Error("[HSK kebab limits] Could not find GenPlace.TryPlaceDirect.");
            return false;
        }

        /// <summary>Targets GenPlace.TryPlaceDirect for haul final deposits and similar direct placements.</summary>
        private static MethodBase TargetMethod()
        {
            return targetMethod;
        }

        /// <summary>Ejects overflow after a successful direct placement into limited storage.</summary>
        public static void Postfix(bool __result, Thing thing, IntVec3 loc, Map map)
        {
            if (!__result || thing?.def.category != ThingCategory.Item || map == null)
            {
                return;
            }

            StockpileCapacityRules.EnforceStorageLimitsAfterPlacement(loc, map, thing.def, "gen-place-direct");
        }
    }
}
