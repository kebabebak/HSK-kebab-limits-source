using System;
using RimWorld;
using Verse;

namespace HSKKebabLimits
{
    public partial class StockpileLimitBundle
    {
        /// <summary>
        /// Cached aggregate limit values for thing categories in the filter tree.
        /// </summary>
        internal sealed class CategoryLimitIndex
        {
            private sealed class CategoryLimitCache
            {
                public readonly int Limit;
                public readonly int OrgLimit;
                public readonly bool Spec;

                public CategoryLimitCache(int limit, int orgLimit, bool spec)
                {
                    Limit = limit;
                    OrgLimit = orgLimit;
                    Spec = spec;
                }
            }

            private System.Collections.Generic.Dictionary<string, CategoryLimitCache> cache;

            public bool TryGet(ThingCategoryDef category, out int limit, out int orgLimit, out bool spec)
            {
                if (cache != null && cache.TryGetValue(category.defName, out CategoryLimitCache cached))
                {
                    limit = cached.Limit;
                    orgLimit = cached.OrgLimit;
                    spec = cached.Spec;
                    return true;
                }

                limit = 0;
                orgLimit = 0;
                spec = false;
                return false;
            }

            public void Store(ThingCategoryDef category, int limit, int orgLimit, bool spec)
            {
                cache ??= new System.Collections.Generic.Dictionary<string, CategoryLimitCache>();
                cache[category.defName] = new CategoryLimitCache(limit, orgLimit, spec);
            }

            public void Clear()
            {
                cache?.Clear();
            }
        }

        /// <summary>
        /// Computes the displayed limit for a filter category from descendant items and overrides.
        /// </summary>
        public int GroupLimitFor(ThingCategoryDef category, out int limit, out bool spec,
            Func<ThingDef, bool> filter)
        {
            if (categoryLimits.TryGet(category, out limit, out int orgLimit, out spec))
            {
                return orgLimit;
            }

            int maxStack = 0;
            int minPerItem = int.MaxValue;
            bool hasStackable = false;
            bool hasPerItem = false;

            foreach (ThingDef descendant in category.DescendantThingDefs)
            {
                if (!filter(descendant) || descendant.stackLimit <= 1)
                {
                    continue;
                }

                hasStackable = true;
                maxStack = Math.Max(descendant.stackLimit, maxStack);
                if (perItemOverrides.TryGet(descendant, out int perItem))
                {
                    hasPerItem = true;
                    minPerItem = Math.Min(perItem, minPerItem);
                }
            }

            if (!hasStackable)
            {
                limit = 0;
                spec = hasPerItem;
                categoryLimits.Store(category, limit, limit, spec);
                return limit;
            }

            limit = KebabLimitsModSettings.PercentageMode
                ? LogarithmicStackScale.ToItemPercentageCount(allowedLimitPercents.max, maxStack)
                : LogarithmicStackScale.ToAbsoluteCount(allowedLimitPercents.max);

            if (minPerItem != int.MaxValue)
            {
                spec = hasPerItem;
                int org = AllowsPerItemAboveSlider() ? minPerItem : Math.Min(limit, minPerItem);
                categoryLimits.Store(category, limit, org, spec);
                return org;
            }

            spec = false;
            categoryLimits.Store(category, limit, limit, spec);
            return limit;
        }
    }
}
