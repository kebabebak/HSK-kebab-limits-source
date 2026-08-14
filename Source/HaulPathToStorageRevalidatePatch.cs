using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace HSKKebabLimits
{
    /// <summary>Cancels travel to a full storage cell when the pawn is already carrying the haulable.</summary>
    [HarmonyPatch(typeof(Pawn_PathFollower), nameof(Pawn_PathFollower.StartPath), new[] { typeof(LocalTargetInfo), typeof(PathEndMode) })]
    public static class HaulPathToStorageRevalidatePatch
    {
        public static bool Prefix(LocalTargetInfo dest, Pawn ___pawn)
        {
            Pawn pawn = ___pawn;
            Thing carried = pawn?.carryTracker?.CarriedThing;
            Job job = pawn?.jobs?.curJob;
            if (carried == null || job?.def != JobDefOf.HaulToCell || !job.targetB.IsValid || !dest.IsValid)
            {
                return true;
            }

            Map map = pawn.Map;
            if (map == null)
            {
                return true;
            }

            IntVec3 storeCell = job.targetB.Cell;
            SlotGroup storeGroup = storeCell.GetSlotGroup(map);
            if (storeGroup == null)
            {
                return true;
            }

            IntVec3 pathCell = dest.Cell;
            if (pathCell != storeCell &&
                (pathCell.GetSlotGroup(map) != storeGroup || !pathCell.InHorDistOf(storeCell, 1.9f)))
            {
                return true;
            }

            if (!StockpileCapacityRules.TryAbortHaulToCellWithoutSpace(pawn, carried, "path-to-storage"))
            {
                return true;
            }

            return false;
        }
    }
}
