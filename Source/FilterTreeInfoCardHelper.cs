using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace HSKKebabLimits
{
    /// <summary>
    /// Opens Dialog_InfoCard on filter-tree row clicks outside excluded regions (checkbox, limit controls).
    /// Only ThingDef rows open a card; ThingCategoryDef rows are ignored (categories have no item stats).
    /// </summary>
    public static class FilterTreeInfoCardHelper
    {
        private static readonly PropertyInfo LabelWidthProperty =
            AccessTools.Property(typeof(Listing_Tree), "LabelWidth");

        /// <summary>
        /// Set by <see cref="FilterTreeLimitRowPatch"/> when the current row drew custom limit +/- controls.
        /// </summary>
        public static bool LastRowDrewCustomLimitControls { get; set; }

        /// <summary>
        /// Row rectangle captured at the start of DoThingDef / DoCategory before EndLine advances curY.
        /// </summary>
        public static Rect CurrentFilterRowRect { get; set; }

        /// <summary>
        /// Records the filter-tree row bounds for the row currently being drawn.
        /// </summary>
        public static void BeginFilterRow(Listing_Tree listing)
        {
            LastRowDrewCustomLimitControls = false;
            float lineHeight = ((Listing_Lines)listing).lineHeight;
            CurrentFilterRowRect = new Rect(0f, listing.CurHeight, listing.ColumnWidth, lineHeight);
        }

        /// <summary>
        /// Returns the checkbox column on the right of a filter-tree row (vanilla DoThingDef / DoCategory layout).
        /// </summary>
        public static Rect FilterCheckboxRect(Listing_Tree listing, Rect rowRect)
        {
            float labelWidth = (float)LabelWidthProperty.GetValue(listing, null);
            float lineHeight = ((Listing_Lines)listing).lineHeight;
            return new Rect(labelWidth, rowRect.y, lineHeight, lineHeight);
        }

        /// <summary>
        /// Opens an info card when the player left-clicks the row outside the excluded rectangle.
        /// </summary>
        public static void TryOpenOnRowClick(ThingDef thingDef, ThingCategoryDef thingCategory, Rect fullRow,
            Rect excludeRect, string logContext)
        {
            if (Event.current == null || Event.current.type != EventType.MouseDown ||
                Event.current.button != 0 || Mouse.IsOver(excludeRect))
            {
                return;
            }

            Rect infoClickRect = fullRow;
            infoClickRect.xMax = excludeRect.xMin - 2f;
            if (!Mouse.IsOver(infoClickRect))
            {
                return;
            }

            if (thingDef == null)
            {
                return;
            }

            Find.WindowStack.Add(new Dialog_InfoCard(thingDef));
            Event.current.Use();
            if (logContext == "storage")
            {
                KebabLimitsLog.MessageImportant(
                    $"[HSK kebab limits] StorageTreeInfoCardOpened thing={thingDef.defName} area=row-click " +
                    $"rect=({infoClickRect.x:F0},{infoClickRect.y:F0},{infoClickRect.width:F0},{infoClickRect.height:F0})");
            }
            else
            {
                KebabLimitsLog.MessageImportant(
                    $"[HSK kebab limits] FilterTreeInfoCardOpened context={logContext} thing={thingDef.defName} " +
                    $"area=row-click rect=({infoClickRect.x:F0},{infoClickRect.y:F0},{infoClickRect.width:F0},{infoClickRect.height:F0})");
            }
        }

        /// <summary>
        /// Restores info-card clicks on non-storage filter trees (workbench bills, ingredient pickers, etc.).
        /// </summary>
        public static void TryOpenOnNonStorageFilterRow(ThingDef thingDef, ThingCategoryDef thingCategory,
            Listing_TreeThingFilter listing)
        {
            if (ActiveStockpileTabContext.DrawingStorageTab)
            {
                if (LastRowDrewCustomLimitControls)
                {
                    return;
                }

                TryOpenOnRowClick(thingDef, thingCategory, CurrentFilterRowRect,
                    FilterCheckboxRect(listing, CurrentFilterRowRect), "storage-vanilla-row");
                return;
            }

            TryOpenOnRowClick(thingDef, thingCategory, CurrentFilterRowRect,
                FilterCheckboxRect(listing, CurrentFilterRowRect), "workbench");
        }
    }
}
