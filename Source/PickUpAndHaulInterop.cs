using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace HSKKebabLimits
{
    /// <summary>
    /// Pick Up And Haul compat: caps inventory haul capacity when destination stockpile has HSK limits.
    /// </summary>
    internal static class PickUpAndHaulCompat
    {
        private const string HaulToInventoryDefName = "HaulToInventory";

        /// <summary>Returns whether Pick Up And Haul is loaded and its haul-to-inventory work giver exists.</summary>
        public static bool IsActive(out Type haulToInventoryWorkerType)
        {
            haulToInventoryWorkerType = null;
            if (ModLister.GetActiveModWithIdentifier("Mehni.PickUpAndHaul") == null &&
                ModLister.GetActiveModWithIdentifier("Mlie.PickUpAndHaul") == null)
            {
                return false;
            }

            haulToInventoryWorkerType = AccessTools.TypeByName("PickUpAndHaul.WorkGiver_HaulToInventory");
            return haulToInventoryWorkerType != null;
        }

        /// <summary>True when the job is PUAH HaulToInventory.</summary>
        public static bool IsHaulToInventoryJob(Job job)
        {
            return job?.def?.defName == HaulToInventoryDefName;
        }

        /// <summary>Effective PUAH store capacity at a cell after kebab limits CapacityAt rules.</summary>
        public static int EffectiveCapacityAt(Thing thing, IntVec3 storeCell, Map map)
        {
            if (thing == null || map == null || !storeCell.IsValid)
            {
                return 0;
            }

            int capacity = storeCell.GetItemStackSpaceLeftFor(map, thing.def);
            CapacityAtPostfix(ref capacity, thing, storeCell, map);
            return Math.Max(capacity, 0);
        }

        /// <summary>
        /// Drops invalid PUAH countQueue rows and falls back to vanilla haul when the batch job is empty.
        /// </summary>
        public static void FinalizeHaulToInventoryJob(Pawn pawn, Thing thing, bool forced, ref Job job)
        {
            if (!IsHaulToInventoryJob(job))
            {
                return;
            }

            PruneInvalidCountQueueEntries(job, pawn);
            if (!job.targetQueueA.NullOrEmpty())
            {
                return;
            }

            KebabLimitsLog.MessageVerbose(
                $"[HSK kebab limits] PUAHJobFallbackToHaul pawn={pawn?.LabelShort ?? "none"} thing={thing?.def?.defName ?? "none"} reason=empty-count-queue-after-prune");
            job = CreateVanillaHaulJobFallback(pawn, thing, forced);
        }

        /// <summary>Single-item haul when PUAH batch job is invalid (HSK HaulToCellStorageJob API).</summary>
        private static Job CreateVanillaHaulJobFallback(Pawn pawn, Thing thing, bool forced)
        {
            if (pawn == null || thing == null)
            {
                return null;
            }

            Map map = pawn.Map;
            if (map == null)
            {
                return null;
            }

            StoragePriority priority = StoreUtility.CurrentStoragePriorityOf(thing);
            if (!StoreUtility.TryFindBestBetterStoreCellFor(thing, pawn, map, priority, pawn.Faction, out IntVec3 storeCell))
            {
                return null;
            }

            return HaulAIUtility.HaulToCellStorageJob(pawn, thing, storeCell, false);
        }

        /// <summary>Removes zero/negative countQueue entries that PUAH can produce under kebab limits limits.</summary>
        private static void PruneInvalidCountQueueEntries(Job job, Pawn pawn)
        {
            if (job.countQueue == null)
            {
                return;
            }

            List<LocalTargetInfo> targets = job.targetQueueA;
            for (int i = job.countQueue.Count - 1; i >= 0; i--)
            {
                if (job.countQueue[i] > 0)
                {
                    continue;
                }

                int removed = job.countQueue[i];
                Thing targetThing = targets != null && i < targets.Count ? targets[i].Thing : null;
                job.countQueue.RemoveAt(i);
                if (targets != null && i < targets.Count)
                {
                    targets.RemoveAt(i);
                }

                KebabLimitsLog.MessageVerbose(
                    $"[HSK kebab limits] PUAHCountQueueDropped pawn={pawn?.LabelShort ?? "none"} thing={targetThing?.def?.defName ?? "none"} count={removed}");
            }
        }

        /// <summary>
        /// Postfix on PUAH CapacityAt — clamps batch capacity via Solve when kebab limits limits apply.
        /// Must not zero capacity for every limited storage (that disabled PUAH inventory haul entirely);
        /// clamp via Solve and keep countQueue prune / carry-check so inventory haul still works safely.
        /// </summary>
        public static void CapacityAtPostfix(ref int __result, Thing thing, IntVec3 storeCell, Map map)
        {
            if (__result <= 0 || thing == null)
            {
                return;
            }

            StockpileLimitBundle profile = StockpileProfileStore.FindOrNull(storeCell, map);
            if (profile == null || (!profile.EnforcesUpperBound() && !profile.EnforcesSimilarStackCap() &&
                                    !profile.HasEnforceableStorageWideLimits))
            {
                return;
            }

            int previous = __result;
            int count = __result;
            StockpileCapacityRules.ApplyCapacityClamp(storeCell, map, thing.def, ref count);
            __result = Math.Max(count, 0);

            if (__result <= 0)
            {
                KebabLimitsLog.MessageVerbose(
                    $"[HSK kebab limits] PUAHCapacityZero thing={thing.def.defName} dest={storeCell} previousCapacity={previous} itemLimit={profile.LimitFor(thing.def)} similarRaw={profile.SimilarStackCountRaw}");
            }
            else if (__result < previous)
            {
                KebabLimitsLog.MessageVerbose(
                    $"[HSK kebab limits] PUAHCapacityClamped thing={thing.def.defName} dest={storeCell} previousCapacity={previous} clampedCapacity={__result} itemLimit={profile.LimitFor(thing.def)} similarRaw={profile.SimilarStackCountRaw}");
            }
        }
    }

    [HarmonyPatch]
    internal static class PickUpAndHaulCapacityAtPatch
    {
        private static MethodBase targetMethod;

        private static bool Prepare()
        {
            if (!PickUpAndHaulCompat.IsActive(out Type workerType))
            {
                return false;
            }

            targetMethod = AccessTools.Method(workerType, "CapacityAt");
            return targetMethod != null;
        }

        private static MethodBase TargetMethod()
        {
            return targetMethod;
        }

        public static void Postfix(ref int __result, Thing thing, IntVec3 storeCell, Map map)
        {
            PickUpAndHaulCompat.CapacityAtPostfix(ref __result, thing, storeCell, map);
        }
    }

    [HarmonyPatch]
    internal static class PickUpAndHaulJobOnThingPatch
    {
        private static MethodBase targetMethod;

        private static bool Prepare()
        {
            if (!PickUpAndHaulCompat.IsActive(out Type workerType))
            {
                return false;
            }

            targetMethod = AccessTools.Method(workerType, "JobOnThing");
            return targetMethod != null;
        }

        private static MethodBase TargetMethod()
        {
            return targetMethod;
        }

        public static void Postfix(Pawn pawn, Thing thing, bool forced, ref Job __result)
        {
            if (__result == null)
            {
                return;
            }

            PickUpAndHaulCompat.FinalizeHaulToInventoryJob(pawn, thing, forced, ref __result);
        }
    }

    [HarmonyPatch(typeof(Toils_Haul), nameof(Toils_Haul.ErrorCheckForCarry))]
    internal static class PickUpAndHaulErrorCheckForCarryPatch
    {
        private static bool Prepare()
        {
            return PickUpAndHaulCompat.IsActive(out _);
        }

        public static bool Prefix(Pawn pawn, Thing haulThing)
        {
            Job job = pawn?.jobs?.curJob;
            if (!PickUpAndHaulCompat.IsHaulToInventoryJob(job) || job.count > 0)
            {
                return true;
            }

            Map map = pawn.Map;
            if (map == null || haulThing == null || !job.targetB.IsValid)
            {
                return true;
            }

            int capacity = PickUpAndHaulCompat.EffectiveCapacityAt(haulThing, job.targetB.Cell, map);
            if (capacity <= 0)
            {
                KebabLimitsLog.MessageVerbose(
                    $"[HSK kebab limits] PUAHCarryRejected pawn={pawn.LabelShort} thing={haulThing.def.defName} jobCount={job.count} capacity={capacity} dest={job.targetB.Cell}");
                pawn.jobs.EndCurrentJob(JobCondition.Incompletable);
                return false;
            }

            int previous = job.count;
            job.count = Math.Min(haulThing.stackCount, capacity);
            KebabLimitsLog.MessageVerbose(
                $"[HSK kebab limits] PUAHCountClamped stage=carry-check pawn={pawn.LabelShort} thing={haulThing.def.defName} old={previous} new={job.count} capacity={capacity}");
            return true;
        }
    }
}
