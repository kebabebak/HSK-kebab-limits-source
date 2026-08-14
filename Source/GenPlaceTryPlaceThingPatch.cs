using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace HSKKebabLimits
{
    /// <summary>Harmony postfix for near-mode GenPlace; enforces limits after non-direct item placement.</summary>
    [HarmonyPatch]
    public static class GenPlaceTryPlaceThingPatch
    {
        private static List<MethodInfo> targetMethods;

        private static bool Prepare()
        {
            targetMethods = new List<MethodInfo>();
            foreach (MethodInfo method in typeof(GenPlace).GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                if (method.Name != nameof(GenPlace.TryPlaceThing))
                {
                    continue;
                }

                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length < 4
                    || parameters[0].ParameterType != typeof(Thing)
                    || parameters[1].ParameterType != typeof(IntVec3)
                    || parameters[2].ParameterType != typeof(Map)
                    || parameters[3].ParameterType != typeof(ThingPlaceMode))
                {
                    continue;
                }

                targetMethods.Add(method);
            }

            if (targetMethods.Count > 0)
            {
                return true;
            }

            Log.Error("[HSK kebab limits] Could not find GenPlace.TryPlaceThing.");
            return false;
        }

        /// <summary>Targets every GenPlace.TryPlaceThing overload (1.5 Rot4 and 1.6 Nullable&lt;Rot4&gt; + int).</summary>
        private static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (MethodInfo method in targetMethods)
            {
                yield return method;
            }
        }

        /// <summary>Ejects overflow after a successful near-mode placement into limited storage.</summary>
        public static void Postfix(bool __result, Thing thing, IntVec3 center, Map map, ThingPlaceMode mode)
        {
            if (!__result || mode == ThingPlaceMode.Direct || thing?.def.category != ThingCategory.Item ||
                map == null)
            {
                return;
            }

            StockpileCapacityRules.EnforceStorageLimitsAfterPlacement(center, map, thing.def, "gen-place-near");
        }
    }
}
