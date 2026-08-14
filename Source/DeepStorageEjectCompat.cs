using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace HSKKebabLimits
{
    /// <summary>
    /// LWM Deep Storage compat: uses Deep Storage's EjectTarget API when splitting stacks out of deep storage cells.
    /// </summary>
    internal static class DeepStorageEjectCompat
    {
        private static MethodInfo ejectTargetMethod;
        private static bool resolveAttempted;

        /// <summary>Lazy-reflects LWM.DeepStorage ITab_DeepStorage_Inventory.EjectTarget once.</summary>
        private static void Resolve()
        {
            if (resolveAttempted)
            {
                return;
            }

            resolveAttempted = true;
            Type tabType = AccessTools.TypeByName("LWM.DeepStorage.ITab_DeepStorage_Inventory");
            if (tabType == null)
            {
                return;
            }

            ejectTargetMethod = AccessTools.Method(tabType, "EjectTarget", new[] { typeof(Thing) });
        }

        /// <summary>Returns true if the thing sits in a cell owned by CompDeepStorage.</summary>
        private static bool IsInDeepStorage(Thing thing)
        {
            if (!thing.Spawned)
            {
                return false;
            }

            SlotGroup slotGroup = thing.Position.GetSlotGroup(thing.Map);
            if (slotGroup?.parent is ThingWithComps thingWithComps)
            {
                foreach (ThingComp comp in thingWithComps.AllComps)
                {
                    if (comp.GetType().FullName == "LWM.DeepStorage.CompDeepStorage")
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>Ejects entire stack via Deep Storage API when vanilla SplitOff cannot remove it from the shelf.</summary>
        public static bool TryEjectFullStack(Thing thing)
        {
            if (!thing.Spawned || thing.stackCount <= 0 || !IsInDeepStorage(thing))
            {
                return false;
            }

            Resolve();
            if (ejectTargetMethod == null)
            {
                return false;
            }

            try
            {
                IntVec3 origin = thing.Position;
                Map map = thing.Map;
                int removedCount = thing.stackCount;
                ejectTargetMethod.Invoke(null, new object[] { thing });
                if (!thing.Spawned)
                {
                    return true;
                }

                if (thing.Map != map || thing.Position != origin)
                {
                    return true;
                }

                return !IsInDeepStorage(thing);
            }
            catch (Exception ex)
            {
                KebabLimitsLog.Warning($"[HSK kebab limits] LWM DeepStorage EjectTarget failed for {thing}: {ex.Message}");
                return false;
            }
        }
    }
}
