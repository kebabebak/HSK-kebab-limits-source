using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace HSKKebabLimits
{
    /// <summary>
    /// MapComponent that schedules delayed overflow cleanup for stockpile zones and storage buildings (1/6/12/X hours).
    /// </summary>
    public class DelayedZoneOverflowEjectComponent : MapComponent
    {
        /// <summary>Pending eject job: target storage, item type, due tick, and trigger reason for logs.</summary>
        private class ScheduledEject
        {
            public Zone_Stockpile Zone;
            public Building_Storage Building;
            public ThingDef ThingDef;
            public int DueTick;
            public string Reason;
        }

        private const int CheckIntervalTicks = 250;

        private readonly List<ScheduledEject> scheduledEjects = new List<ScheduledEject>();

        /// <summary>Creates per-map scheduler instance registered automatically by RimWorld.</summary>
        public DelayedZoneOverflowEjectComponent(Map map)
            : base(map)
        {
        }

        /// <summary>Queues delayed per-item cleanup for a stockpile zone (deduped by zone + ThingDef).</summary>
        public static void Schedule(Zone_Stockpile zone, ThingDef thingDef, string reason)
        {
            if (zone?.Map == null || thingDef == null)
            {
                KebabLimitsLog.Warning("[HSK kebab limits] DelayedZoneOverflowEject schedule skipped: missing zone/map/thingDef.");
                return;
            }

            if (KebabLimitsModSettings.DelayedZoneEjectHours <= 0)
            {
                KebabLimitsLog.MessageVerbose(
                    $"[HSK kebab limits] DelayedZoneOverflowEject schedule disabled zone=\"{zone.SlotYielderLabel()}\" thing={thingDef.defName} reason={reason}");
                return;
            }

            DelayedZoneOverflowEjectComponent component =
                zone.Map.GetComponent<DelayedZoneOverflowEjectComponent>();
            component.ScheduleInternal(zone, null, thingDef, reason);
        }

        /// <summary>Queues delayed per-item cleanup for a storage building (deduped by building + ThingDef).</summary>
        public static void ScheduleBuilding(Building_Storage building, ThingDef thingDef, string reason)
        {
            if (building?.Map == null || thingDef == null)
            {
                KebabLimitsLog.Warning("[HSK kebab limits] DelayedBuildingOverflowEject schedule skipped: missing building/map/thingDef.");
                return;
            }

            if (KebabLimitsModSettings.DelayedZoneEjectHours <= 0)
            {
                KebabLimitsLog.MessageVerbose(
                    $"[HSK kebab limits] DelayedBuildingOverflowEject schedule disabled building=\"{building.SlotYielderLabel()}\" thing={thingDef.defName} reason={reason}");
                return;
            }

            DelayedZoneOverflowEjectComponent component =
                building.Map.GetComponent<DelayedZoneOverflowEjectComponent>();
            component.ScheduleInternal(null, building, thingDef, reason);
        }

        /// <summary>Adds one timer entry unless the same zone/building + ThingDef is already scheduled.</summary>
        private void ScheduleInternal(Zone_Stockpile zone, Building_Storage building, ThingDef thingDef, string reason)
        {
            ScheduledEject existing = scheduledEjects.FirstOrDefault(entry =>
                entry.ThingDef == thingDef &&
                ((zone != null && entry.Zone == zone) || (building != null && entry.Building == building)));
            int now = Find.TickManager.TicksGame;
            string targetLabel = zone?.SlotYielderLabel() ?? building?.SlotYielderLabel() ?? "none";
            if (existing != null)
            {
                KebabLimitsLog.MessageVerbose(
                    $"[HSK kebab limits] DelayedOverflowEject already scheduled target=\"{targetLabel}\" thing={thingDef.defName} dueTick={existing.DueTick} ticksLeft={existing.DueTick - now} existingReason={existing.Reason} newReason={reason}");
                return;
            }

            int delayTicks = KebabLimitsModSettings.DelayedZoneEjectHours * GenDate.TicksPerHour;
            ScheduledEject entry = new ScheduledEject
            {
                Zone = zone,
                Building = building,
                ThingDef = thingDef,
                DueTick = now + delayTicks,
                Reason = reason
            };
            scheduledEjects.Add(entry);
            KebabLimitsLog.MessageImportant(
                $"[HSK kebab limits] DelayedOverflowEject scheduled target=\"{targetLabel}\" building={building != null} thing={thingDef.defName} delayHours={KebabLimitsModSettings.DelayedZoneEjectHours} dueTick={entry.DueTick} reason={reason}");
        }

        /// <summary>Every 250 ticks fires due jobs — runs targeted overflow eject for one ThingDef only.</summary>
        public override void MapComponentTick()
        {
            base.MapComponentTick();
            if (scheduledEjects.Count == 0 || Find.TickManager.TicksGame % CheckIntervalTicks != 0)
            {
                return;
            }

            int now = Find.TickManager.TicksGame;
            for (int i = scheduledEjects.Count - 1; i >= 0; i--)
            {
                ScheduledEject entry = scheduledEjects[i];
                bool validZone = entry.Zone != null && entry.Zone.Map == map;
                bool validBuilding = entry.Building != null && entry.Building.Map == map && !entry.Building.Destroyed;
                if ((!validZone && !validBuilding) || entry.ThingDef == null)
                {
                    KebabLimitsLog.Warning(
                        $"[HSK kebab limits] DelayedOverflowEject removed invalid entry index={i} reason={entry?.Reason ?? "none"}");
                    scheduledEjects.RemoveAt(i);
                    continue;
                }

                if (entry.DueTick > now)
                {
                    continue;
                }

                scheduledEjects.RemoveAt(i);
                string targetLabel = entry.Zone?.SlotYielderLabel() ?? entry.Building?.SlotYielderLabel() ?? "none";
                KebabLimitsLog.MessageImportant(
                    $"[HSK kebab limits] DelayedOverflowEject firing target=\"{targetLabel}\" building={entry.Building != null} thing={entry.ThingDef.defName} dueTick={entry.DueTick} now={now} reason={entry.Reason}");
                if (entry.Zone != null)
                {
                    StockpileCapacityRules.DelayedZoneEjectNow(entry.Zone, entry.ThingDef);
                }
                else
                {
                    StockpileCapacityRules.DelayedBuildingEjectNow(entry.Building, entry.ThingDef);
                }
            }
        }
    }
}
