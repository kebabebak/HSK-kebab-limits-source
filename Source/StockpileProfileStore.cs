using System.Collections.Generic;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace HSKKebabLimits
{
    /// <summary>
    /// Associates each stockpile's StorageSettings with its HSK kebab limits limit profile and resolves limits on the map.
    /// </summary>
    public static class StockpileProfileStore
    {
        /// <summary>
        /// Clears limit profiles when a new map session starts so stale settings are not reused.
        /// </summary>
        [HarmonyPatch(typeof(Root), "Start")]
        public static class Map_Loading
        {
            /// <summary>
            /// Empties the storage-settings remapper before RimWorld loads or starts a game.
            /// </summary>
            public static void Prefix()
            {
                Remapper.Clear();
                CapacityQueryCache.ClearAllNegative();
            }
        }

        [SyncField(SyncContext.None)]
        private static readonly Dictionary<StorageSettings, StockpileLimitBundle> Remapper =
            new Dictionary<StorageSettings, StockpileLimitBundle>();

        /// <summary>
        /// Returns the maximum stack allowed for an item at a map cell, or unlimited if no profile applies.
        /// </summary>
        public static int GetMaxAllowedStack(Map map, IntVec3 cell, ThingDef thingDef)
        {
            StockpileLimitBundle profile = FindOrNull(cell, map);
            return profile?.LimitFor(thingDef) ?? int.MaxValue;
        }

        /// <summary>
        /// Looks up the limit profile for the stockpile zone containing a map cell.
        /// </summary>
        public static StockpileLimitBundle Find(IntVec3 cell, Map map)
        {
            SlotGroup slotGroup = StoreUtility.GetSlotGroup(cell, map);
            if (slotGroup?.Settings == null)
            {
                return null;
            }

            return Get(slotGroup.Settings);
        }

        /// <summary>
        /// Looks up an existing limit profile for a map cell without creating a new one.
        /// </summary>
        public static StockpileLimitBundle FindOrNull(IntVec3 cell, Map map)
        {
            SlotGroup slotGroup = StoreUtility.GetSlotGroup(cell, map);
            if (slotGroup?.Settings == null)
            {
                return null;
            }

            return GetOrNull(slotGroup.Settings);
        }

        /// <summary>
        /// Returns the limit profile for a slot group's storage settings, creating one if needed.
        /// </summary>
        public static StockpileLimitBundle Find(SlotGroup slotGroup)
        {
            if (slotGroup?.Settings != null)
            {
                return Get(slotGroup.Settings);
            }

            return null;
        }

        /// <summary>
        /// Gets or creates the limit profile bound to a stockpile's storage settings.
        /// </summary>
        public static StockpileLimitBundle Get(StorageSettings settings)
        {
            if (Remapper.TryGetValue(settings, out StockpileLimitBundle data))
            {
                return data;
            }

            StockpileLimitBundle profile = new StockpileLimitBundle();
            Remapper[settings] = profile;
            return profile;
        }

        /// <summary>
        /// Returns an existing limit profile for storage settings, or null if none was stored.
        /// </summary>
        public static StockpileLimitBundle GetOrNull(StorageSettings settings)
        {
            Remapper.TryGetValue(settings, out StockpileLimitBundle data);
            return data;
        }

        /// <summary>
        /// Stores an updated limit profile for a stockpile's storage settings.
        /// </summary>
        [SyncMethod(SyncContext.None)]
        public static void Set(StorageSettings settings, StockpileLimitBundle value)
        {
            if (value != null)
            {
                value.ClampPerItemOverridesToModeCap();
                Remapper[settings] = value;
            }
        }

        /// <summary>
        /// Clamps every stored profile so per-item overrides respect the current mode and setting.
        ///
        /// Зажимает все сохранённые профили, чтобы per-item overrides соблюдали текущий режим и настройку.
        /// </summary>
        public static void ClampAllProfilesToModeCap()
        {
            foreach (KeyValuePair<StorageSettings, StockpileLimitBundle> pair in Remapper)
            {
                pair.Value?.ClampPerItemOverridesToModeCap();
            }
        }
    }
}
