using HarmonyLib;
using RimWorld;
using Verse;

namespace HSKKebabLimits
{
    /// <summary>
    /// Invalidates cross-tick negative Solve cache when slot-group item counts change.
    /// Vanilla hooks verified on RimWorld 1.5 Assembly-CSharp via ILSpy:
    /// SpawnSetup → Notify_ReceivedThing; DeSpawn → Notify_LostThing; TryAbsorbStack / SplitOff in-place.
    /// </summary>
    internal static class NegativeCacheInvalidation
    {
        public static void OnSlotGroupContentsChanged(Map map, SlotGroup slotGroup)
        {
            if (map == null || slotGroup == null)
            {
                return;
            }

            if (KebabLimitsModSettings.EnableNegativeSolveCache)
            {
                CapacityQueryCache.InvalidateSlotGroup(map, slotGroup);
            }
        }

        public static void OnThingInSlotGroupChanged(Thing thing)
        {
            if (thing == null || !thing.Spawned)
            {
                return;
            }

            if (thing.def?.category != ThingCategory.Item)
            {
                return;
            }

            Map map = thing.Map;
            if (map == null)
            {
                return;
            }

            IntVec3 cell = thing.Position;
            StockpileCapacityRules.MarkCellStockDirty(map, cell, thing.def);

            SlotGroup slotGroup = map.haulDestinationManager.SlotGroupAt(cell);
            if (slotGroup != null)
            {
                OnSlotGroupContentsChanged(map, slotGroup);
            }
        }
    }

    [HarmonyPatch(typeof(Thing), nameof(Thing.SpawnSetup))]
    internal static class NegativeCacheThingSpawnSetupPatch
    {
        public static void Postfix(Thing __instance, Map map, bool respawningAfterLoad)
        {
            if (respawningAfterLoad || __instance == null || !__instance.Spawned)
            {
                return;
            }

            NegativeCacheInvalidation.OnThingInSlotGroupChanged(__instance);
        }
    }

    [HarmonyPatch(typeof(Thing), nameof(Thing.DeSpawn))]
    internal static class NegativeCacheThingDeSpawnPatch
    {
        public struct DeSpawnCapture
        {
            public Map Map;
            public IntVec3 Cell;
            public ThingDef ThingDef;
            public SlotGroup Group;
            public bool Valid;
        }

        public static void Prefix(Thing __instance, ref DeSpawnCapture __state)
        {
            __state = default;
            if (__instance == null || !__instance.Spawned)
            {
                return;
            }

            if (__instance.def?.category != ThingCategory.Item)
            {
                return;
            }

            Map map = __instance.Map;
            if (map == null)
            {
                return;
            }

            SlotGroup slotGroup = map.haulDestinationManager.SlotGroupAt(__instance.Position);
            if (slotGroup == null)
            {
                return;
            }

            __state.Map = map;
            __state.Cell = __instance.Position;
            __state.ThingDef = __instance.def;
            __state.Group = slotGroup;
            __state.Valid = true;
        }

        public static void Postfix(ref DeSpawnCapture __state)
        {
            if (!__state.Valid || __state.Map == null || !__state.Cell.IsValid || __state.ThingDef == null)
            {
                return;
            }

            StockpileCapacityRules.MarkCellStockDirty(__state.Map, __state.Cell, __state.ThingDef);

            if (__state.Group != null)
            {
                NegativeCacheInvalidation.OnSlotGroupContentsChanged(__state.Map, __state.Group);
            }
        }
    }

    [HarmonyPatch(typeof(Thing), nameof(Thing.TryAbsorbStack))]
    internal static class NegativeCacheThingTryAbsorbStackPatch
    {
        public static void Postfix(Thing __instance, bool __result)
        {
            if (!__result)
            {
                return;
            }

            NegativeCacheInvalidation.OnThingInSlotGroupChanged(__instance);
        }
    }

    [HarmonyPatch(typeof(Thing), nameof(Thing.SplitOff))]
    internal static class NegativeCacheThingSplitOffPatch
    {
        public static void Postfix(Thing __instance)
        {
            NegativeCacheInvalidation.OnThingInSlotGroupChanged(__instance);
        }
    }

    [HarmonyPatch(typeof(SlotGroup), nameof(SlotGroup.Notify_AddedCell))]
    internal static class NegativeCacheSlotGroupAddedCellPatch
    {
        public static void Postfix(SlotGroup __instance, IntVec3 c)
        {
            if (__instance?.parent?.Map == null)
            {
                return;
            }

            NegativeCacheInvalidation.OnSlotGroupContentsChanged(__instance.parent.Map, __instance);
        }
    }

    [HarmonyPatch(typeof(SlotGroup), nameof(SlotGroup.Notify_LostCell))]
    internal static class NegativeCacheSlotGroupLostCellPatch
    {
        public static void Postfix(SlotGroup __instance, IntVec3 c)
        {
            if (__instance?.parent?.Map == null)
            {
                return;
            }

            NegativeCacheInvalidation.OnSlotGroupContentsChanged(__instance.parent.Map, __instance);
        }
    }

    [HarmonyPatch(typeof(Building_Storage), nameof(Building_Storage.Notify_SettingsChanged))]
    internal static class NegativeCacheBuildingStorageSettingsPatch
    {
        public static void Postfix(Building_Storage __instance)
        {
            if (!__instance.Spawned || __instance.slotGroup == null)
            {
                return;
            }

            NegativeCacheInvalidation.OnSlotGroupContentsChanged(__instance.Map, __instance.slotGroup);
        }
    }

    [HarmonyPatch(typeof(Zone_Stockpile), nameof(Zone_Stockpile.Notify_SettingsChanged))]
    internal static class NegativeCacheZoneStockpileSettingsPatch
    {
        public static void Postfix(Zone_Stockpile __instance)
        {
            if (__instance.Map == null)
            {
                return;
            }

            SlotGroup slotGroup = __instance.GetSlotGroup();
            if (slotGroup != null)
            {
                NegativeCacheInvalidation.OnSlotGroupContentsChanged(__instance.Map, slotGroup);
            }
        }
    }
}
