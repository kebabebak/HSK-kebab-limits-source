using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace HSKKebabLimits
{
    /// <summary>
    /// Draws a storage category's children as a flat item list without a category header when expand-categories is on.
    ///
    /// Рисует потомков категории склада сплошным списком предметов без заголовка, если включено разворачивание категорий.
    /// </summary>
    [HarmonyPatch(typeof(Listing_TreeThingFilter), "DoCategory")]
    public static class FilterTreeFlattenCategoriesPatch
    {
        private static readonly MethodInfo DoCategoryChildrenMethod =
            AccessTools.Method(typeof(Listing_TreeThingFilter), "DoCategoryChildren",
                new[] { typeof(TreeNode_ThingCategory), typeof(int), typeof(int), typeof(Map), typeof(bool) });

        private static readonly HashSet<ThingDef> DrawnThingDefs = new HashSet<ThingDef>();

        private static int flattenWalkDepth;

        public static bool SkipDuplicateThingDefPostfix;

        private static bool Prepare()
        {
            return DoCategoryChildrenMethod != null;
        }

        /// <summary>
        /// Draws this category's children without a header row, at the same indent as the category.
        ///
        /// Рисует потомков категории без строки заголовка, с тем же отступом, что и сама категория.
        /// </summary>
        [HarmonyPriority(Priority.Last)]
        public static bool Prefix(Listing_TreeThingFilter __instance, TreeNode_ThingCategory node,
            int indentLevel, int openMask, Map map, bool subtreeMatchedSearch)
        {
            if (!FilterTreeDisplayFilter.FlattenCategoriesActive())
            {
                return true;
            }

            flattenWalkDepth++;
            try
            {
                DoCategoryChildrenMethod.Invoke(__instance,
                    new object[] { node, indentLevel, openMask, map, subtreeMatchedSearch });
            }
            finally
            {
                flattenWalkDepth--;
            }

            return false;
        }

        public static void ResetPass()
        {
            DrawnThingDefs.Clear();
            flattenWalkDepth = 0;
            SkipDuplicateThingDefPostfix = false;
        }

        public static bool ShouldSkipDuplicateThingDef(ThingDef tDef)
        {
            SkipDuplicateThingDefPostfix = false;
            if (!FilterTreeDisplayFilter.FlattenCategoriesActive() || tDef == null)
            {
                return false;
            }

            if (DrawnThingDefs.Add(tDef))
            {
                return false;
            }

            SkipDuplicateThingDefPostfix = true;
            return true;
        }

        public static bool ShouldSkipNestedNonItemRow()
        {
            return FilterTreeDisplayFilter.FlattenCategoriesActive() && flattenWalkDepth > 0;
        }
    }

    /// <summary>
    /// Clears flatten tracking at the start of each filter-tree listing pass.
    ///
    /// Сбрасывает учёт сплошного списка в начале каждого прохода листинга дерева фильтра.
    /// </summary>
    [HarmonyPatch(typeof(Listing_TreeThingFilter), nameof(Listing_TreeThingFilter.ListCategoryChildren))]
    public static class FilterTreeFlattenResetPassPatch
    {
        public static void Prefix()
        {
            FilterTreeFlattenCategoriesPatch.ResetPass();
        }
    }

    /// <summary>
    /// Omits a ThingDef row that already appeared under an earlier category in the flat list.
    ///
    /// Пропускает строку ThingDef, которая уже была показана в более ранней категории сплошного списка.
    /// </summary>
    [HarmonyPatch(typeof(Listing_TreeThingFilter), "DoThingDef")]
    public static class FilterTreeFlattenDuplicateThingPatch
    {
        [HarmonyPriority(Priority.First)]
        public static bool Prefix(ThingDef tDef)
        {
            return !FilterTreeFlattenCategoriesPatch.ShouldSkipDuplicateThingDef(tDef);
        }
    }

    /// <summary>
    /// Keeps nested SpecialThingFilterDef rows out of the flattened item list.
    ///
    /// Убирает вложенные строки SpecialThingFilterDef из сплошного списка предметов.
    /// </summary>
    [HarmonyPatch(typeof(Listing_TreeThingFilter), "DoSpecialFilter")]
    public static class FilterTreeFlattenSpecialFilterPatch
    {
        private static bool Prepare()
        {
            return AccessTools.Method(typeof(Listing_TreeThingFilter), "DoSpecialFilter") != null;
        }

        public static bool Prefix()
        {
            return !FilterTreeFlattenCategoriesPatch.ShouldSkipNestedNonItemRow();
        }
    }

    /// <summary>
    /// Keeps nested undiscovered-item rows out of the flattened item list.
    ///
    /// Убирает вложенные строки неоткрытых предметов из сплошного списка.
    /// </summary>
    [HarmonyPatch(typeof(Listing_TreeThingFilter), "DoUndiscoveredEntry")]
    public static class FilterTreeFlattenUndiscoveredPatch
    {
        private static bool Prepare()
        {
            return AccessTools.Method(typeof(Listing_TreeThingFilter), "DoUndiscoveredEntry") != null;
        }

        public static bool Prefix()
        {
            return !FilterTreeFlattenCategoriesPatch.ShouldSkipNestedNonItemRow();
        }
    }

    /// <summary>
    /// Grows the ThingFilterUI tree listing canvas past the vanilla 9999 height when viewHeight is larger.
    ///
    /// Увеличивает холст дерева ThingFilterUI выше ванильных 9999, если viewHeight больше.
    /// </summary>
    [HarmonyPatch(typeof(ThingFilterUI), nameof(ThingFilterUI.DoThingFilterConfigWindow))]
    public static class ThingFilterUIListingHeightPatch
    {
        private const float VanillaListingCanvasHeight = 9999f;

        private static readonly FieldInfo ViewHeightField =
            AccessTools.Field(typeof(ThingFilterUI), "viewHeight");

        /// <summary>
        /// Replaces the fixed listing canvas height with a value that can grow with viewHeight.
        ///
        /// Заменяет фиксированную высоту холста листинга значением, которое может расти вместе с viewHeight.
        /// </summary>
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo listingHeightMethod =
                AccessTools.Method(typeof(ThingFilterUIListingHeightPatch), nameof(ListingCanvasHeight));
            bool patched = false;
            foreach (CodeInstruction instruction in instructions)
            {
                if (!patched && instruction.opcode == OpCodes.Ldc_R4 && instruction.operand is float height &&
                    Mathf.Approximately(height, VanillaListingCanvasHeight))
                {
                    yield return new CodeInstruction(OpCodes.Call, listingHeightMethod);
                    patched = true;
                    continue;
                }

                yield return instruction;
            }

            if (!patched)
            {
                KebabLimitsLog.Warning(
                    "[HSK kebab limits] ThingFilterUI listing-height transpiler did not find the 9999 canvas constant.");
            }
        }

        public static float ListingCanvasHeight()
        {
            float cached = 0f;
            if (ViewHeightField != null)
            {
                cached = (float)ViewHeightField.GetValue(null);
            }

            return Mathf.Max(VanillaListingCanvasHeight, cached);
        }
    }
}
