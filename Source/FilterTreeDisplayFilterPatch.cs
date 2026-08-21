using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace HSKKebabLimits
{
    /// <summary>
    /// Hides fully allowed or fully forbidden item and category rows in the storage filter tree.
    ///
    /// Скрывает полностью разрешённые или полностью запрещённые строки предметов и категорий в дереве фильтра склада.
    /// </summary>
    internal static class FilterTreeDisplayFilter
    {
        private static readonly FieldInfo FilterField =
            AccessTools.Field(typeof(Listing_TreeThingFilter), "filter");

        private static readonly MethodInfo VisibleThingDefMethod =
            AccessTools.Method(typeof(Listing_TreeThingFilter), "Visible", new[] { typeof(ThingDef) });

        /// <summary>
        /// Returns the active per-storage list mode when the display-settings row is enabled, or 0.
        ///
        /// Возвращает активный режим списка склада, если строка настроек отображения включена, иначе 0.
        /// </summary>
        public static int ActiveMode()
        {
            if (!KebabLimitsModSettings.AddFilterDisplaySettings ||
                !ActiveStockpileTabContext.DrawingStorageTab ||
                ActiveStockpileTabContext.ActiveStorageSettings == null ||
                ActiveStockpileTabContext.ActiveStorageSettings.owner is Building_Bookcase)
            {
                return StockpileFilterDisplayRow.ModeShowAll;
            }

            StockpileLimitBundle profile =
                StockpileProfileStore.GetOrNull(ActiveStockpileTabContext.ActiveStorageSettings);
            return profile?.FilterListDisplayMode ?? StockpileFilterDisplayRow.ModeShowAll;
        }

        /// <summary>
        /// Returns whether an allowed/forbidden item should stay visible in the current list mode.
        ///
        /// Возвращает, должен ли разрешённый/запрещённый предмет оставаться видимым в текущем режиме списка.
        /// </summary>
        public static bool PassesMode(bool allowed, int mode)
        {
            if (mode == StockpileFilterDisplayRow.ModeAllowedOnly)
            {
                return allowed;
            }

            if (mode == StockpileFilterDisplayRow.ModeForbiddenOnly)
            {
                return !allowed;
            }

            return true;
        }

        /// <summary>
        /// Reads the listing ThingFilter without a hard field name in call sites.
        ///
        /// Читает ThingFilter листинга без жёсткого имени поля в местах вызова.
        /// </summary>
        public static ThingFilter GetFilter(Listing_TreeThingFilter listing)
        {
            if (listing == null || FilterField == null)
            {
                return null;
            }

            return FilterField.GetValue(listing) as ThingFilter;
        }

        /// <summary>
        /// Returns whether any descendant thing of a category would still draw in the current list mode.
        ///
        /// Возвращает, нарисуется ли хотя бы один предмет-потомок категории в текущем режиме списка.
        /// </summary>
        public static bool CategoryHasVisibleThings(Listing_TreeThingFilter listing, TreeNode_ThingCategory node)
        {
            if (listing == null || node?.catDef == null || VisibleThingDefMethod == null)
            {
                return true;
            }

            foreach (ThingDef def in node.catDef.DescendantThingDefs)
            {
                if (def != null && (bool)VisibleThingDefMethod.Invoke(listing, new object[] { def }))
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// Applies the storage list display mode to individual thing rows.
    ///
    /// Применяет режим отображения списка склада к отдельным строкам предметов.
    /// </summary>
    [HarmonyPatch(typeof(Listing_TreeThingFilter), "Visible", new[] { typeof(ThingDef) })]
    public static class FilterTreeDisplayVisibleThingPatch
    {
        private static bool Prepare()
        {
            return AccessTools.Method(typeof(Listing_TreeThingFilter), "Visible", new[] { typeof(ThingDef) }) != null;
        }

        /// <summary>
        /// Hides a thing row when it does not match Show only allowed or Show only forbidden.
        ///
        /// Скрывает строку предмета, если она не подходит под «только разрешённое» или «только запрещённое».
        /// </summary>
        public static void Postfix(ThingDef td, Listing_TreeThingFilter __instance, ref bool __result)
        {
            if (!__result || td == null)
            {
                return;
            }

            int mode = FilterTreeDisplayFilter.ActiveMode();
            if (mode == StockpileFilterDisplayRow.ModeShowAll)
            {
                return;
            }

            ThingFilter filter = FilterTreeDisplayFilter.GetFilter(__instance);
            if (filter == null)
            {
                return;
            }

            if (!FilterTreeDisplayFilter.PassesMode(filter.Allows(td), mode))
            {
                __result = false;
            }
        }
    }

    /// <summary>
    /// Hides a category row when every item inside is filtered out by the storage list display mode.
    ///
    /// Скрывает строку категории, если все предметы внутри отфильтрованы режимом отображения списка склада.
    /// </summary>
    [HarmonyPatch(typeof(Listing_TreeThingFilter), "Visible", new[] { typeof(TreeNode_ThingCategory) })]
    public static class FilterTreeDisplayVisibleCategoryPatch
    {
        private static bool Prepare()
        {
            return AccessTools.Method(typeof(Listing_TreeThingFilter), "Visible",
                new[] { typeof(TreeNode_ThingCategory) }) != null;
        }

        /// <summary>
        /// Hides categories that would have no remaining thing rows after the thing-row filter.
        ///
        /// Скрывает категории, у которых после фильтра строк предметов не осталось ни одной.
        /// </summary>
        public static void Postfix(TreeNode_ThingCategory node, Listing_TreeThingFilter __instance, ref bool __result)
        {
            if (!__result || node?.catDef == null)
            {
                return;
            }

            int mode = FilterTreeDisplayFilter.ActiveMode();
            if (mode == StockpileFilterDisplayRow.ModeShowAll)
            {
                return;
            }

            ThingFilter filter = FilterTreeDisplayFilter.GetFilter(__instance);
            if (filter == null || filter.OnlySpecialFilters)
            {
                return;
            }

            if (!FilterTreeDisplayFilter.CategoryHasVisibleThings(__instance, node))
            {
                __result = false;
            }
        }
    }
}
