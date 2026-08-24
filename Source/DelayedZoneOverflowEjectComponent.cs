using System.Collections.Generic;
using System.Linq;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace HSKKebabLimits
{
    /// <summary>
    /// MapComponent that schedules delayed overflow cleanup for stockpile zones and storage buildings.
    /// Also delays forced ejection after a storage limit change when that option is enabled.
    ///
    /// Компонент карты: отложенная очистка переполнения зон и хранилищ.
    /// Также откладывает принудительный сброс после смены лимита склада, если эта опция включена.
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

        /// <summary>
        /// Queues delayed forced ejection after a storage limit change. One job per zone or building; later changes reset the timer.
        ///
        /// Ставит отложенный принудительный сброс после смены лимита склада. Одна задача на зону или здание; повторное изменение сбрасывает таймер.
        /// </summary>
        [SyncMethod(SyncContext.None)]
        public static void ScheduleAfterLimitChange(IStoreSettingsParent parent)
        {
            if (parent == null || !KebabLimitsModSettings.EnableHardEjection ||
                !KebabLimitsModSettings.DelayHardEjectionAfterLimitChange)
            {
                return;
            }

            Zone_Stockpile zone = parent as Zone_Stockpile;
            Building_Storage building = parent as Building_Storage;
            Map map = zone?.Map ?? building?.Map;
            if (map == null || (zone == null && building == null))
            {
                KebabLimitsLog.Warning(
                    "[HSK kebab limits] DelayedHardEjectAfterLimitChange schedule skipped: missing zone/building/map.");
                return;
            }

            DelayedZoneOverflowEjectComponent component = map.GetComponent<DelayedZoneOverflowEjectComponent>();
            component.ScheduleLimitChangeInternal(zone, building);
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

        /// <summary>
        /// Adds or refreshes one whole-storage eject timer after a limit change.
        ///
        /// Добавляет или обновляет таймер сброса всего склада после смены лимита.
        /// </summary>
        private void ScheduleLimitChangeInternal(Zone_Stockpile zone, Building_Storage building)
        {
            ScheduledEject existing = scheduledEjects.FirstOrDefault(entry =>
                entry.ThingDef == null &&
                ((zone != null && entry.Zone == zone) || (building != null && entry.Building == building)));
            int now = Find.TickManager.TicksGame;
            int delayHours = KebabLimitsModSettings.DelayHardEjectionHours;
            if (delayHours < KebabLimitsModSettings.DelayHardEjectionHoursMin)
            {
                delayHours = KebabLimitsModSettings.DelayHardEjectionHoursMin;
            }

            int delayTicks = delayHours * GenDate.TicksPerHour;
            int dueTick = now + delayTicks;
            string targetLabel = zone?.SlotYielderLabel() ?? building?.SlotYielderLabel() ?? "none";
            if (existing != null)
            {
                existing.DueTick = dueTick;
                existing.Reason = "limit-change";
                KebabLimitsLog.MessageImportant(
                    $"[HSK kebab limits] DelayedHardEjectAfterLimitChange rescheduled target=\"{targetLabel}\" building={building != null} delayHours={delayHours} dueTick={dueTick}");
                return;
            }

            scheduledEjects.Add(new ScheduledEject
            {
                Zone = zone,
                Building = building,
                ThingDef = null,
                DueTick = dueTick,
                Reason = "limit-change"
            });
            KebabLimitsLog.MessageImportant(
                $"[HSK kebab limits] DelayedHardEjectAfterLimitChange scheduled target=\"{targetLabel}\" building={building != null} delayHours={delayHours} dueTick={dueTick}");
        }

        /// <summary>
        /// Runs due overflow jobs: per-item cleanup or delayed eject after a storage limit change.
        ///
        /// Выполняет созревшие задачи сброса: попредметную очистку или отложенный сброс после смены лимита склада.
        /// </summary>
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
                if (!validZone && !validBuilding)
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
                if (entry.ThingDef == null)
                {
                    KebabLimitsLog.MessageImportant(
                        $"[HSK kebab limits] DelayedHardEjectAfterLimitChange firing target=\"{targetLabel}\" building={entry.Building != null} dueTick={entry.DueTick} now={now} reason={entry.Reason}");
                    FireLimitChangeReconcile(entry);
                    continue;
                }

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

        /// <summary>
        /// Runs storage overflow eject for a due limit-change timer if forced ejection is still enabled.
        ///
        /// Выполняет сброс переполнения склада по сработавшему таймеру смены лимита, если принудительный сброс всё ещё включён.
        /// </summary>
        private static void FireLimitChangeReconcile(ScheduledEject entry)
        {
            if (!KebabLimitsModSettings.EnableHardEjection)
            {
                KebabLimitsLog.Message(
                    "[HSK kebab limits] DelayedHardEjectAfterLimitChange skipped: forced ejection disabled.");
                return;
            }

            IStoreSettingsParent parent = entry.Zone != null
                ? (IStoreSettingsParent)entry.Zone
                : entry.Building;
            if (parent == null)
            {
                return;
            }

            StockpileLimitBundle profile = StockpileProfileStore.GetOrNull(parent.GetStoreSettings());
            if (profile == null)
            {
                KebabLimitsLog.Message(
                    "[HSK kebab limits] DelayedHardEjectAfterLimitChange skipped: no profile.");
                return;
            }

            StockpileCapacityRules.ReconcileOverflowAfterLimitChange(parent, profile);
        }
    }
}
