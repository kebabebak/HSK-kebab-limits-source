using HarmonyLib;
using UnityEngine;
using Verse;

namespace HSKKebabLimits
{
    /// <summary>
    /// Hooks thing-def rows in the stockpile filter tree so per-item limit controls know the current item.
    /// </summary>
    [HarmonyPatch(typeof(Listing_TreeThingFilter), "DoThingDef")]
    public static class FilterTreeThingRowHooks
    {
        public static Listing_TreeThingFilter Instance;

        /// <summary>
        /// Records the listing instance and thing def while a filter-tree item row is drawn.
        /// </summary>
        public static void Prefix(ThingDef tDef, Listing_TreeThingFilter __instance)
        {
            Instance = __instance;
            FilterTreeLimitRowPatch.HookedThingDef = tDef;
            FilterTreeInfoCardHelper.BeginFilterRow(__instance);

            if (FilterTreeLimitRowPatch.DebugLog && Event.current != null &&
                Event.current.type == EventType.MouseDown)
            {
                Rect row = new Rect(0f, __instance.CurHeight, __instance.ColumnWidth,
                    ((Listing_Lines)__instance).lineHeight);
                if (Mouse.IsOver(row))
                {
                    Log.Message(
                        $"[SG-DBG] DoThingDef MouseDown item={tDef?.defName} stackLimit={tDef?.stackLimit} " +
                        $"hookedCat={FilterTreeLimitRowPatch.HookedThingCategory?.defName} " +
                        $"settings={(ActiveStockpileTabContext.ActiveStorageSettings != null)}");
                }
            }
        }

        /// <summary>
        /// Clears thing-def hook state after the filter-tree item row finishes drawing.
        /// </summary>
        public static void Postfix(ThingDef tDef, Listing_TreeThingFilter __instance)
        {
            if (FilterTreeFlattenCategoriesPatch.SkipDuplicateThingDefPostfix)
            {
                FilterTreeFlattenCategoriesPatch.SkipDuplicateThingDefPostfix = false;
                return;
            }

            FilterTreeInfoCardHelper.TryOpenOnNonStorageFilterRow(tDef, null, __instance);
            Instance = null;
            FilterTreeLimitRowPatch.HookedThingDef = null;
        }
    }

    /// <summary>
    /// Hooks category rows in the stockpile filter tree so group limit controls know the current category.
    /// </summary>
    [HarmonyPatch(typeof(Listing_TreeThingFilter), "DoCategory")]
    public static class StorageTreeCategoryHooks
    {
        /// <summary>
        /// Records the listing instance and category while a filter-tree category row is drawn.
        /// </summary>
        public static void Prefix(TreeNode_ThingCategory node, Listing_TreeThingFilter __instance)
        {
            if (FilterTreeDisplayFilter.FlattenCategoriesActive())
            {
                return;
            }

            FilterTreeThingRowHooks.Instance = __instance;
            FilterTreeLimitRowPatch.HookedThingCategory = node.catDef;
            FilterTreeInfoCardHelper.BeginFilterRow(__instance);
        }

        /// <summary>
        /// Clears category hook state after the filter-tree category row finishes drawing.
        /// </summary>
        public static void Postfix(TreeNode_ThingCategory node, Listing_TreeThingFilter __instance)
        {
            if (FilterTreeDisplayFilter.FlattenCategoriesActive())
            {
                return;
            }

            FilterTreeInfoCardHelper.TryOpenOnNonStorageFilterRow(null, node?.catDef, __instance);
            FilterTreeThingRowHooks.Instance = null;
            FilterTreeLimitRowPatch.HookedThingCategory = null;
        }
    }
}
