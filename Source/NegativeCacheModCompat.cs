using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace HSKKebabLimits
{
    /// <summary>
    /// Optional negative-cache invalidation for third-party storage mods. Prepare() skips each patch
    /// when the target type/method is absent so vanilla-only loads stay safe.
    /// </summary>
    internal static class NegativeCacheModCompat
    {
        /// <summary>LWM Deep Storage: EjectTarget can reshuffle stacks without a full GenPlace path.</summary>
        [HarmonyPatch]
        internal static class DeepStorageEjectTargetPatch
        {
            private static MethodBase targetMethod;

            private static bool Prepare()
            {
                Type tabType = AccessTools.TypeByName("LWM.DeepStorage.ITab_DeepStorage_Inventory");
                if (tabType == null)
                {
                    return false;
                }

                targetMethod = AccessTools.Method(tabType, "EjectTarget", new[] { typeof(Thing) });
                return targetMethod != null;
            }

            private static MethodBase TargetMethod()
            {
                return targetMethod;
            }

            public static void Postfix(Thing thing)
            {
                if (thing?.Spawned == true)
                {
                    NegativeCacheInvalidation.OnThingInSlotGroupChanged(thing);
                }
            }
        }

        /// <summary>
        /// LWM Deep Storage postfix hooks on Building_Storage notify — fires on internal store/eject
        /// after vanilla notify chain.
        /// </summary>
        [HarmonyPatch(typeof(Building_Storage), nameof(Building_Storage.Notify_ReceivedThing))]
        internal static class DeepStorageBuildingReceivedThingPatch
        {
            private static bool Prepare()
            {
                return AccessTools.TypeByName("LWM.DeepStorage.CompDeepStorage") != null;
            }

            public static void Postfix(Building_Storage __instance, Thing newItem)
            {
                if (__instance?.Spawned != true || __instance.slotGroup == null)
                {
                    return;
                }

                NegativeCacheInvalidation.OnSlotGroupContentsChanged(__instance.Map, __instance.slotGroup);
            }
        }

        [HarmonyPatch(typeof(Building_Storage), nameof(Building_Storage.Notify_LostThing))]
        internal static class DeepStorageBuildingLostThingPatch
        {
            private static bool Prepare()
            {
                return AccessTools.TypeByName("LWM.DeepStorage.CompDeepStorage") != null;
            }

            public static void Postfix(Building_Storage __instance, Thing newItem)
            {
                if (__instance?.Spawned != true || __instance.slotGroup == null)
                {
                    return;
                }

                NegativeCacheInvalidation.OnSlotGroupContentsChanged(__instance.Map, __instance.slotGroup);
            }
        }
    }
}
