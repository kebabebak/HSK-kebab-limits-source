using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace HSKKebabLimits
{
    /// <summary>
    /// Replaces stockpile filter tree labels with inline per-item and per-category stack limit editors.
    /// </summary>
    [HarmonyPatch(typeof(Listing_Tree), "LabelLeft")]
    public static class FilterTreeLimitRowPatch
    {
        private static string textBufferLimitPerItem = "";
        private static StockpileLimitBundle cachedProfile;
        private static ThingCategoryDef cachedThingCategory;
        private static ThingDef cachedThingDef;

        public static ThingCategoryDef HookedThingCategory;
        public static ThingDef HookedThingDef;

        private static readonly MethodInfo XAtIndentLevel =
            typeof(Listing_Tree).GetMethod("XAtIndentLevel", BindingFlags.Instance | BindingFlags.NonPublic);

        private static readonly MethodInfo FilterThingDef =
            typeof(Listing_TreeThingFilter).GetMethod("Visible", BindingFlags.Instance | BindingFlags.NonPublic,
                null, new[] { typeof(ThingDef) }, null);

        public static bool DebugLog => KebabLimitsModSettings.EnableLogging;

        /// <summary>
        /// Draws a clickable icon button and returns true when the user presses it.
        /// </summary>
        private static bool ButtonIcon(Rect rect, Texture2D tex)
        {
            bool over = Mouse.IsOver(rect);
            if (over)
            {
                MouseoverSounds.DoRegion(rect);
            }

            if (Event.current.type == EventType.Repaint)
            {
                GUI.color = over ? GenUI.MouseoverColor : Color.white;
                GUI.DrawTexture(rect, tex);
                GUI.color = Color.white;
            }

            if (over && Event.current.type == EventType.MouseDown && Event.current.button == 0)
            {
                Event.current.Use();
                return true;
            }

            return false;
        }

        /// <summary>
        /// Returns whether a thing def is visible in the current stockpile filter listing.
        /// </summary>
        private static bool CheckThingDef(ThingDef thingDef)
        {
            if (FilterThingDef == null || FilterTreeThingRowHooks.Instance == null)
            {
                return false;
            }

            return (bool)FilterThingDef.Invoke(FilterTreeThingRowHooks.Instance, new object[] { thingDef });
        }

        /// <summary>
        /// Opens the vanilla item/category info card when the player clicks the row outside limit controls.
        /// </summary>
        private static void TryOpenInfoCardOnRowClick(ThingDef thingDef, ThingCategoryDef thingCategory,
            Rect fullRow, Rect controlBlock)
        {
            FilterTreeInfoCardHelper.LastRowDrewCustomLimitControls = true;
            FilterTreeInfoCardHelper.TryOpenOnRowClick(thingDef, thingCategory, fullRow, controlBlock, "storage");
        }

        /// <summary>
        /// Draws limit controls on filter-tree rows or defers to vanilla labeling when limits do not apply.
        /// </summary>
        public static bool Prefix(Listing_Tree __instance, string label, string tipText, int indentLevel,
            float widthOffset = 0f, Color? textColor = null, float leftOffset = 0f)
        {
            ThingDef thingDef = HookedThingDef;
            ThingCategoryDef thingCategory = HookedThingCategory;

            FilterTreeInfoCardHelper.LastRowDrewCustomLimitControls = false;

            if ((thingDef == null && thingCategory == null) ||
                (thingDef != null && thingDef.stackLimit <= 1) ||
                !ActiveStockpileTabContext.DrawingStorageTab ||
                ActiveStockpileTabContext.ActiveStorageSettings == null ||
                ActiveStockpileTabContext.ActiveStorageSettings.owner is Building_Bookcase)
            {
                cachedThingDef = thingDef;
                cachedThingCategory = thingCategory;
                return true;
            }

            if (!GenText.NullOrEmpty(label) && label.StartsWith("*"))
            {
                return true;
            }

            StockpileLimitBundle profile = StockpileProfileStore.Get(ActiveStockpileTabContext.ActiveStorageSettings);
            if (profile == null)
            {
                cachedThingDef = thingDef;
                cachedThingCategory = thingCategory;
                return true;
            }

            int displayLimit = -1;
            bool spec = false;
            int sliderDefault = -1;

            if (thingDef != null)
            {
                displayLimit = profile.LimitFor(thingDef, out sliderDefault, out spec);
            }
            else if (thingCategory != null)
            {
                displayLimit = profile.GroupLimitFor(thingCategory, out sliderDefault, out spec, CheckThingDef);
            }

            int editCeiling = profile.AllowsPerItemAboveSlider()
                ? LogarithmicStackScale.MaxStackSize
                : sliderDefault;

            Rect row = new Rect(0f, __instance.CurHeight, __instance.ColumnWidth,
                ((Listing_Lines)__instance).lineHeight);
            Rect fullRow = row;
            bool hovering = Mouse.IsOver(row);
            if ((!hovering && !spec) || displayLimit == 0)
            {
                cachedThingDef = thingDef;
                cachedThingCategory = thingCategory;
                return true;
            }

            row.xMin = (float)XAtIndentLevel.Invoke(__instance, new object[] { indentLevel }) + 18f + leftOffset;
            Widgets.DrawHighlightIfMouseover(row);

            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = textColor ?? Color.white;
            row.width = __instance.ColumnWidth - 26f - row.xMin + widthOffset;
            row.yMax += 5f;
            row.yMin -= 5f;

            const float iconSize = 18f;
            float iconOffset = row.height * 0.5f - iconSize * 0.5f;
            Rect plusRect = new Rect(row.xMax - iconSize + (thingDef != null ? 10f : 0f), row.y + iconOffset,
                iconSize, iconSize);
            Rect fieldRect = new Rect(plusRect.x - 50f, row.y + iconOffset, 50f, iconSize);
            Rect minusRect = new Rect(fieldRect.x - iconSize, row.y + iconOffset, iconSize, iconSize);
            Rect controlBlock = new Rect(minusRect.xMin, row.y, plusRect.xMax - minusRect.xMin, row.height);

            if (!GenText.NullOrEmpty(tipText))
            {
                Rect tipRect = row;
                if (hovering)
                {
                    tipRect.xMax = minusRect.xMin;
                }

                if (Mouse.IsOver(tipRect))
                {
                    GUI.DrawTexture(tipRect, TexUI.HighlightTex);
                }

                TooltipHandler.TipRegion(tipRect, tipText);
            }

            if (DebugLog && hovering &&
                (Event.current.type == EventType.MouseDown || Event.current.type == EventType.Used))
            {
                Log.Message(
                    $"[SG-DBG] row thing={thingDef?.defName} cat={thingCategory?.defName} evt={Event.current.type} " +
                    $"overMinus={Mouse.IsOver(minusRect)} overField={Mouse.IsOver(fieldRect)} " +
                    $"overPlus={Mouse.IsOver(plusRect)} overBlock={Mouse.IsOver(controlBlock)} " +
                    $"mouse=({Event.current.mousePosition.x:F0},{Event.current.mousePosition.y:F0}) " +
                    $"minusRect=({minusRect.x:F0},{minusRect.y:F0},{minusRect.width:F0},{minusRect.height:F0}) " +
                    $"disp={displayLimit} max={editCeiling} sliderDefault={sliderDefault}");
            }

            if (hovering)
            {
                Widgets.Label(row,
                    GenText.Truncate(label, row.width - controlBlock.width, (Dictionary<string, string>)null));

                int editedLimit = displayLimit;
                if (GenText.NullOrEmpty(textBufferLimitPerItem) || cachedProfile != profile ||
                    (thingDef != null && cachedThingDef != thingDef) ||
                    (thingCategory != null && cachedThingCategory != thingCategory))
                {
                    textBufferLimitPerItem = null;
                }

                Widgets.TextFieldNumeric(fieldRect, ref editedLimit, ref textBufferLimitPerItem, 0f, editCeiling);
                if (ButtonIcon(plusRect, TexButton.Plus))
                {
                    if (DebugLog)
                    {
                        Log.Message($"[SG-DBG] PLUS pressed thing={thingDef?.defName} cat={thingCategory?.defName}");
                    }

                    editedLimit++;
                    if (editedLimit > editCeiling)
                    {
                        editedLimit = editCeiling;
                    }

                    textBufferLimitPerItem = editedLimit.ToString();
                }

                if (ButtonIcon(minusRect, TexButton.Minus))
                {
                    if (DebugLog)
                    {
                        Log.Message($"[SG-DBG] MINUS pressed thing={thingDef?.defName} cat={thingCategory?.defName}");
                    }

                    editedLimit--;
                    textBufferLimitPerItem = editedLimit.ToString();
                }

                if (editedLimit <= 0)
                {
                    editedLimit = sliderDefault;
                    textBufferLimitPerItem = editedLimit.ToString();
                }

                if (editedLimit != displayLimit)
                {
                    if (DebugLog)
                    {
                        Log.Message(
                            $"[SG-DBG] APPLY thing={thingDef?.defName} cat={thingCategory?.defName} " +
                            $"{displayLimit}->{editedLimit} (ceiling={editCeiling} default={sliderDefault})");
                    }

                    // Clear override when value matches the slider default (no special case).
                    bool clearOverride = editedLimit == sliderDefault;
                    if (thingDef != null)
                    {
                        if (clearOverride)
                        {
                            profile.RemoveLimitFor(thingDef);
                        }
                        else
                        {
                            profile.SetLimitFor(thingDef, editedLimit);
                        }
                    }
                    else if (thingCategory != null)
                    {
                        if (clearOverride)
                        {
                            profile.RemoveGroupLimitFor(thingCategory, CheckThingDef);
                        }
                        else
                        {
                            profile.SetGroupLimitFor(thingCategory, editedLimit, CheckThingDef);
                        }
                    }

                    profile.RemoveAllCache();
                }
            }
            else
            {
                string limitText = displayLimit.ToString();
                Vector2 textSize = Text.CalcSize(limitText);
                plusRect.xMax -= 16f;
                if (thingDef == null)
                {
                    plusRect.xMax -= 16f;
                }

                plusRect.xMin = plusRect.xMax - textSize.x;
                if (thingDef != null)
                {
                    Text.Font = GameFont.Tiny;
                    Text.Anchor = TextAnchor.MiddleRight;
                    GUI.color = Color.gray;
                    Widgets.Label(plusRect, limitText);
                    Text.Font = GameFont.Small;
                    GenUI.ResetLabelAlign();
                    GUI.color = Color.white;
                }

                Text.Anchor = TextAnchor.MiddleLeft;
                GUI.color = spec ? LimitHighlight.GetColor() : Color.yellow;
                Widgets.Label(row,
                    GenText.Truncate("~" + label, row.width - textSize.x, (Dictionary<string, string>)null));
            }

            TryOpenInfoCardOnRowClick(thingDef, thingCategory, fullRow, controlBlock);

            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
            cachedProfile = profile;
            cachedThingDef = thingDef;
            cachedThingCategory = thingCategory;
            return false;
        }
    }
}
