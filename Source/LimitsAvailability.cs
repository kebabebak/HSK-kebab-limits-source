using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace HSKKebabLimits
{
    /// <summary>
    /// At startup, veils each settings control whose game member is missing and writes one yellow warning.
    ///
    /// При запуске закрывает каждую настройку, чей член игры не найден, и пишет одно жёлтое предупреждение.
    /// </summary>
    public static class LimitsAvailability
    {
        public const string Slider = "slider";
        public const string FilterDisplay = "filter-display";
        public const string ExpandCategories = "expand-categories";
        public const string PerItem = "per-item";
        public const string Store = "store";
        public const string Place = "place";

        static readonly HashSet<string> blocked = new HashSet<string>();
        static bool probed;

        /// <summary>
        /// Checks each settings control once. A later call does nothing.
        ///
        /// Проверяет каждую настройку один раз. Повторный вызов ничего не делает.
        /// </summary>
        public static void Probe()
        {
            if (probed)
            {
                return;
            }

            probed = true;
            MethodInfo window = AccessTools.Method(
                typeof(ThingFilterUI),
                nameof(ThingFilterUI.DoThingFilterConfigWindow));
            MethodInfo mental = AccessTools.Method(
                typeof(ThingFilterUI),
                "DrawMentalBreakFilterConfig",
                new[] { typeof(float).MakeByRefType(), typeof(float), typeof(ThingFilter) });
            if (window == null || mental == null || AccessTools.Field(typeof(ThingDef), "stackLimit") == null)
            {
                Skip(Slider, "Storage limit slider",
                    "ThingFilterUI.DoThingFilterConfigWindow, DrawMentalBreakFilterConfig, or ThingDef.stackLimit was not found.");
            }

            if (AccessTools.Method(typeof(Listing_TreeThingFilter), "Visible", new[] { typeof(ThingDef) }) == null)
            {
                Skip(FilterDisplay, "Storage list display",
                    "Listing_TreeThingFilter.Visible(ThingDef) was not found.");
            }

            if (AccessTools.Method(typeof(Listing_TreeThingFilter), "DoCategory") == null)
            {
                Skip(ExpandCategories, "Expand categories",
                    "Listing_TreeThingFilter.DoCategory was not found.");
            }

            if (AccessTools.Method(typeof(Listing_Tree), "LabelLeft") == null)
            {
                Skip(PerItem, "Per-item limits",
                    "Listing_Tree.LabelLeft was not found.");
            }

            if (AccessTools.Method(typeof(StoreUtility), nameof(StoreUtility.TryFindBestBetterStoreCellFor)) == null
                || AccessTools.Method(typeof(GridsUtility), "GetItemStackSpaceLeftFor") == null)
            {
                Skip(Store, "Storage capacity",
                    "StoreUtility.TryFindBestBetterStoreCellFor or GridsUtility.GetItemStackSpaceLeftFor was not found.");
            }

            if (!HasTryPlaceThing())
            {
                Skip(Place, "Forced ejection", "GenPlace.TryPlaceThing was not found.");
            }
        }

        /// <summary>
        /// True when Probe marked this control unavailable.
        ///
        /// Истина, если Probe отметил эту настройку недоступной.
        /// </summary>
        public static bool IsBlocked(string id)
        {
            return !id.NullOrEmpty() && blocked.Contains(id);
        }

        static bool HasTryPlaceThing()
        {
            foreach (MethodInfo method in typeof(GenPlace).GetMethods(
                BindingFlags.Public | BindingFlags.Static))
            {
                if (method.Name != nameof(GenPlace.TryPlaceThing))
                {
                    continue;
                }

                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length >= 4
                    && parameters[0].ParameterType == typeof(Thing)
                    && parameters[1].ParameterType == typeof(IntVec3)
                    && parameters[2].ParameterType == typeof(Map)
                    && parameters[3].ParameterType == typeof(ThingPlaceMode))
                {
                    return true;
                }
            }

            return false;
        }

        static void Skip(string id, string name, string reason)
        {
            if (!blocked.Add(id))
            {
                return;
            }

            Log.Warning("[HSK kebab limits] " + name + " skipped: " + reason);
        }
    }
}
