using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace HSKKebabLimits
{
    public partial class StockpileLimitBundle
    {
        /// <summary>
        /// Per-item stack override dictionary keyed by ThingDef.defName.
        /// </summary>
        internal sealed class PerItemOverrides
        {
            private Dictionary<string, int> values = new Dictionary<string, int>();

            public bool TryGet(ThingDef thingDef, out int limit)
            {
                return values.TryGetValue(thingDef.defName, out limit);
            }

            public bool Contains(ThingDef thingDef)
            {
                return values != null && values.ContainsKey(thingDef.defName);
            }

            public bool HasAny()
            {
                return values != null && values.Count > 0;
            }

            public void Set(ThingDef thingDef, int value)
            {
                values ??= new Dictionary<string, int>();
                values[thingDef.defName] = value;
            }

            public void Remove(ThingDef thingDef)
            {
                values?.Remove(thingDef.defName);
            }

            public void SetForCategory(ThingCategoryDef category, int value, Func<ThingDef, bool> filter)
            {
                values ??= new Dictionary<string, int>();
                foreach (ThingDef descendant in category.DescendantThingDefs)
                {
                    if (filter(descendant) && descendant.stackLimit > 1)
                    {
                        values[descendant.defName] = value;
                    }
                }
            }

            public void RemoveForCategory(ThingCategoryDef category, Func<ThingDef, bool> filter)
            {
                if (values == null)
                {
                    return;
                }

                foreach (ThingDef descendant in category.DescendantThingDefs)
                {
                    if (filter(descendant))
                    {
                        values.Remove(descendant.defName);
                    }
                }
            }

            public void CopyFrom(PerItemOverrides other)
            {
                values = new Dictionary<string, int>(other.values);
            }

            public void ExposeData()
            {
                Scribe_Collections.Look(ref values, "allowedPerItem", LookMode.Value, LookMode.Value);
                if (values == null)
                {
                    values = new Dictionary<string, int>();
                }
            }

            internal void ClearKv()
            {
                values?.Clear();
            }

            internal IEnumerable<KeyValuePair<string, int>> EnumeratePairs()
            {
                if (values == null)
                {
                    yield break;
                }

                foreach (KeyValuePair<string, int> pair in values)
                {
                    yield return pair;
                }
            }

            internal void SetDefName(string defName, int value)
            {
                values ??= new Dictionary<string, int>();
                values[defName] = value;
            }
        }

        /// <summary>
        /// Returns the effective stack limit for an item, including any per-item override.
        /// </summary>
        public int LimitFor(ThingDef thingDef)
        {
            return LimitFor(thingDef, out _, out _);
        }

        /// <summary>
        /// True when per-item overrides may exceed the main slider max: setting enabled and not storage-wide mode.
        ///
        /// True, если per-item лимит может быть выше ползунка: настройка включена и не режим общего лимита склада.
        /// </summary>
        public bool AllowsPerItemAboveSlider()
        {
            return KebabLimitsModSettings.AllowPerItemAboveSlider && !UsesStorageWideLimitFromLimitFor;
        }

        /// <summary>
        /// Max value the per-item editor may set (slider max, or MaxStackSize when exceeding is allowed).
        ///
        /// Максимум поля редактора per-item (лимит ползунка или MaxStackSize, если превышение разрешено).
        /// </summary>
        public int PerItemEditCeiling(ThingDef thingDef)
        {
            if (AllowsPerItemAboveSlider())
            {
                return LogarithmicStackScale.MaxStackSize;
            }

            return LimitRaw(thingDef);
        }

        /// <summary>
        /// Drops or clamps per-item overrides that are above the current slider cap when exceeding is not allowed
        /// (storage-wide mode, or the optional setting is off). Returns true if any override changed.
        ///
        /// Сбрасывает или зажимает per-item overrides выше капа ползунка, когда превышение запрещено
        /// (режим общего лимита склада или выключена опция). True, если что-то изменилось.
        /// </summary>
        public bool ClampPerItemOverridesToModeCap()
        {
            if (AllowsPerItemAboveSlider() || !perItemOverrides.HasAny())
            {
                return false;
            }

            bool changed = false;
            foreach (System.Collections.Generic.KeyValuePair<string, int> pair in
                     new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, int>>(
                         perItemOverrides.EnumeratePairs()))
            {
                ThingDef thingDef = DefDatabase<ThingDef>.GetNamedSilentFail(pair.Key);
                if (thingDef == null)
                {
                    continue;
                }

                int cap = LimitRaw(thingDef);
                if (pair.Value <= cap)
                {
                    continue;
                }

                // Recalc to warehouse/slider max: equals default → drop redundant override.
                perItemOverrides.Remove(thingDef);
                changed = true;
            }

            if (changed)
            {
                RemoveAllCache();
            }

            return changed;
        }

        /// <summary>
        /// Returns the effective limit and reports the raw slider cap and whether a per-item override applies.
        /// In storage-wide mode (or when the optional setting is off), overrides cannot exceed the slider max.
        /// When the setting is on in other modes, an override is used as-is (may be above or below the slider).
        ///
        /// Возвращает эффективный лимит и сообщает сырой кап ползунка и наличие per-item override.
        /// В режиме общего лимита склада (или при выключенной опции) override не выше ползунка.
        /// При включённой опции в остальных режимах override берётся как есть.
        /// </summary>
        public int LimitFor(ThingDef thingDef, out int limit, out bool spec)
        {
            limit = LimitRaw(thingDef);
            if (perItemOverrides.TryGet(thingDef, out int perItem))
            {
                spec = true;
                if (AllowsPerItemAboveSlider())
                {
                    return perItem;
                }

                return Math.Min(limit, perItem);
            }

            spec = false;
            return limit;
        }

        /// <summary>
        /// Returns how many more stacks of an item the zone can accept before hitting its limit.
        /// </summary>
        public int ZoneLimitRemaining(ThingDef thingDef, int zoneItemCount)
        {
            return Math.Max(0, LimitFor(thingDef) - zoneItemCount);
        }

        /// <summary>Returns whether this item has an explicit per-item override in the filter tree.</summary>
        public bool HasPerItemLimit(ThingDef thingDef)
        {
            return perItemOverrides.Contains(thingDef);
        }

        /// <summary>
        /// Returns the stockpile-wide stack cap from the slider max without per-item overrides.
        /// </summary>
        public int LimitRaw(ThingDef thingDef)
        {
            return KebabLimitsModSettings.PercentageMode
                ? LogarithmicStackScale.ToItemPercentageCount(allowedLimitPercents.max, thingDef.stackLimit)
                : LogarithmicStackScale.ToAbsoluteCount(allowedLimitPercents.max);
        }

        /// <summary>
        /// Sets a per-item stack override for this stockpile and clears cached category limits.
        /// Values above the allowed ceiling are clamped when exceeding the slider is not permitted.
        ///
        /// Задаёт per-item override и сбрасывает кэш категорий.
        /// Значения выше потолка зажимаются, если превышение ползунка запрещено.
        /// </summary>
        public void SetLimitFor(ThingDef thingDef, int value)
        {
            if (!AllowsPerItemAboveSlider())
            {
                int cap = LimitRaw(thingDef);
                if (value >= cap)
                {
                    RemoveLimitFor(thingDef);
                    return;
                }

                value = Math.Min(value, cap);
            }

            perItemOverrides.Set(thingDef, value);
            RemoveAllCache();
        }

        /// <summary>
        /// Applies the same stack override to all stackable items in a filter category.
        ///
        /// Применяет один и тот же override ко всем стакаемым предметам категории фильтра.
        /// </summary>
        public void SetGroupLimitFor(ThingCategoryDef category, int value, Func<ThingDef, bool> filter)
        {
            if (!AllowsPerItemAboveSlider())
            {
                foreach (ThingDef descendant in category.DescendantThingDefs)
                {
                    if (!filter(descendant) || descendant.stackLimit <= 1)
                    {
                        continue;
                    }

                    SetLimitFor(descendant, value);
                }

                return;
            }

            perItemOverrides.SetForCategory(category, value, filter);
            RemoveAllCache();
        }

        /// <summary>
        /// Removes per-item overrides for items in a filter category.
        /// </summary>
        public void RemoveGroupLimitFor(ThingCategoryDef category, Func<ThingDef, bool> filter)
        {
            perItemOverrides.RemoveForCategory(category, filter);
            RemoveAllCache();
        }

        /// <summary>
        /// Clears the per-item override for a single item type.
        /// </summary>
        public void RemoveLimitFor(ThingDef thingDef)
        {
            if (perItemOverrides.Contains(thingDef))
            {
                perItemOverrides.Remove(thingDef);
                RemoveAllCache();
            }
        }

        /// <summary>
        /// Returns whether this stockpile has any per-item limit overrides configured.
        /// </summary>
        public bool HasLimitsPerItem()
        {
            return perItemOverrides.HasAny();
        }
    }
}
