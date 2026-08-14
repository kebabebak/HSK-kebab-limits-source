using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace HSKKebabLimits
{
    /// <summary>Same ApplyCapacityClamp gate for per-slot-group worker scans (WhileYoureUp / PerformanceFish paths).</summary>
    [HarmonyPatch]
    public static class TryFindBestBetterStoreCellForWorkerPatch
    {
        private static MethodBase targetMethod;

        private static bool Prepare()
        {
            targetMethod = AccessTools.Method(typeof(StoreUtility), "TryFindBestBetterStoreCellForWorker");
            return targetMethod != null;
        }

        private static MethodBase TargetMethod()
        {
            return targetMethod;
        }

        public static void Postfix(Thing t, Map map, ref IntVec3 closestSlot)
        {
            if (t?.def == null || map == null || !closestSlot.IsValid)
            {
                return;
            }

            IntVec3 vanillaCell = closestSlot;
            if (StockpileCapacityRules.RetargetStoreCellIfNeeded(ref closestSlot, map, t.def, out int space))
            {
                if (closestSlot != vanillaCell)
                {
                    KebabLimitsLog.MessageVerbose(
                        $"[HSK kebab limits] HaulWorkerRetargeted thing={t.def.defName} old={vanillaCell} new={closestSlot} space={space} storage=\"{closestSlot.GetSlotGroup(map)?.parent?.SlotYielderLabel() ?? "none"}\" stackCount={t.stackCount}");
                }

                return;
            }

            closestSlot = IntVec3.Invalid;
        }
    }
}
