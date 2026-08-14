using RimWorld;
using Verse;
using Verse.AI;

namespace HSKKebabLimits
{
    /// <summary>
    /// Resolves haul job destination map/cell/slot group for limit checks on HaulToContainer and HaulToCell jobs.
    /// </summary>
    public static class HaulJobDestinationResolver
    {
        /// <summary>
        /// Extracts destination map, cell, and storage slot group from a haul job (container or cell target).
        /// </summary>
        public static bool TryGetHaulingDestination(Job job, out Map map, out IntVec3 dest, out SlotGroup slotGroup)
        {
            map = null;
            slotGroup = null;
            dest = default;
            if (job == null)
            {
                return false;
            }

            if (job.def == JobDefOf.HaulToContainer)
            {
                Thing thing = job.targetB.Thing;
                if (thing == null)
                {
                    return false;
                }

                map = thing.Map;
                dest = thing.Position;
            }
            else
            {
                dest = job.targetB.Cell;
                map = job.targetA.Thing.Map ?? job.targetA.Thing.MapHeld;
            }

            if (map == null)
            {
                return false;
            }

            slotGroup = dest.GetSlotGroup(map);
            return slotGroup != null;
        }
    }
}
