using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using HarmonyLib;
using RimWorld;
using Verse;

namespace HSKKebabLimits
{
    /// <summary>
    /// Optional integration with [KV] Save Storage Settings preset files for stockpile limit profiles.
    ///
    /// Опциональная интеграция с файлами пресетов [KV] Save Storage Settings для профилей лимитов склада.
    /// </summary>
    internal static class StockpileKvPresetCompat
    {
        private const string IoUtilTypeName = "SaveStorageSettings.IOUtil";
        private const string KebabLimitsSectionKey = "kebabLimitsSection";
        private const string KebabLimitsSectionValue = "1";

        private static MethodInfo writeFieldMethod;
        private static MethodInfo readFieldMethod;

        /// <summary>
        /// Harmony postfix on KV IOUtil.SaveStorageSettings — appends kebab limits fields to the preset file.
        ///
        /// Postfix Harmony на KV IOUtil.SaveStorageSettings — дописывает поля kebab limits в файл пресета.
        /// </summary>
        [HarmonyPatch]
        internal static class SaveStorageSettingsPostfix
        {
            private static MethodBase targetMethod;

            private static bool Prepare()
            {
                Type ioUtil = AccessTools.TypeByName(IoUtilTypeName);
                if (ioUtil == null)
                {
                    return false;
                }

                targetMethod = AccessTools.Method(ioUtil, "SaveStorageSettings",
                    new[] { typeof(ThingFilter), typeof(FileInfo) });
                if (targetMethod == null)
                {
                    return false;
                }

                writeFieldMethod = AccessTools.FirstMethod(ioUtil,
                    m => m.Name == "WriteField" && m.IsStatic && m.GetParameters().Length == 3);
                return true;
            }

            private static MethodBase TargetMethod()
            {
                return targetMethod;
            }

            public static void Postfix(ThingFilter filter, FileInfo fi, bool __result)
            {
                if (!__result || filter == null || fi == null)
                {
                    return;
                }

                StorageSettings settings = ResolveStorageSettings(filter);
                if (settings == null)
                {
                    return;
                }

                try
                {
                    AppendProfileToFile(fi, StockpileProfileStore.Get(settings));
                }
                catch (Exception ex)
                {
                    KebabLimitsLog.Warning(
                        $"[HSK kebab limits] Failed to append kebab limits data to KV preset '{fi.Name}': {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Harmony postfix on KV IOUtil.LoadFilters — restores kebab limits fields from the preset file.
        ///
        /// Postfix Harmony на KV IOUtil.LoadFilters — восстанавливает поля kebab limits из файла пресета.
        /// </summary>
        [HarmonyPatch]
        internal static class LoadFiltersPostfix
        {
            private static MethodBase targetMethod;

            private static bool Prepare()
            {
                Type ioUtil = AccessTools.TypeByName(IoUtilTypeName);
                if (ioUtil == null)
                {
                    return false;
                }

                targetMethod = AccessTools.Method(ioUtil, "LoadFilters", new[] { typeof(ThingFilter), typeof(FileInfo) });
                if (targetMethod == null)
                {
                    return false;
                }

                readFieldMethod = AccessTools.FirstMethod(ioUtil,
                    m => m.Name == "ReadField" && m.IsStatic && m.GetParameters().Length == 2);
                return true;
            }

            private static MethodBase TargetMethod()
            {
                return targetMethod;
            }

            public static void Postfix(ThingFilter filter, FileInfo fi, bool __result)
            {
                if (!__result || filter == null || fi == null || !fi.Exists)
                {
                    return;
                }

                StorageSettings settings = ResolveStorageSettings(filter);
                if (settings == null)
                {
                    return;
                }

                try
                {
                    if (!TryReadProfileFromFile(fi, out StockpileLimitBundle loaded))
                    {
                        return;
                    }

                    StockpileLimitBundle profile = new StockpileLimitBundle(loaded);
                    profile.RemoveAllCache();
                    StockpileProfileStore.Set(settings, profile);
                    StockpileCapacityRules.InvalidateHaulCachesForParent(settings.owner, profile);

                    StockpileCapacityRules.RequestOverflowReconcileAfterLimitChange(settings.owner, profile);
                }
                catch (Exception ex)
                {
                    KebabLimitsLog.Warning(
                        $"[HSK kebab limits] Failed to load kebab limits data from KV preset '{fi.Name}': {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Finds the live StorageSettings instance that owns a ThingFilter reference from a storage UI.
        ///
        /// Находит экземпляр StorageSettings, которому принадлежит ThingFilter из UI хранилища.
        /// </summary>
        internal static StorageSettings ResolveStorageSettings(ThingFilter filter)
        {
            if (filter == null)
            {
                return null;
            }

            Map map = Find.CurrentMap;
            if (map == null)
            {
                return null;
            }

            foreach (Zone zone in map.zoneManager.AllZones)
            {
                if (zone is Zone_Stockpile stockpile && stockpile.settings?.filter == filter)
                {
                    return stockpile.settings;
                }
            }

            foreach (Building building in map.listerBuildings.allBuildingsColonist)
            {
                if (building is Building_Storage storage && storage.settings?.filter == filter)
                {
                    return storage.settings;
                }

                if (building is IStoreSettingsParent parent)
                {
                    StorageSettings settings = parent.GetStoreSettings();
                    if (settings?.filter == filter)
                    {
                        return settings;
                    }
                }
            }

            return null;
        }

        private static void AppendProfileToFile(FileInfo fi, StockpileLimitBundle profile)
        {
            using (FileStream stream = File.Open(fi.FullName, FileMode.Append, FileAccess.Write))
            using (StreamWriter writer = new StreamWriter(stream))
            {
                WriteKvField(writer, KebabLimitsSectionKey, KebabLimitsSectionValue);
                WriteProfileFields(writer, profile);
            }
        }

        private static bool TryReadProfileFromFile(FileInfo fi, out StockpileLimitBundle profile)
        {
            profile = null;
            bool inSection;
            Dictionary<string, string> fields = ReadKvFields(fi, out inSection);
            if (!inSection || fields.Count == 0)
            {
                return false;
            }

            profile = new StockpileLimitBundle();
            if (fields.TryGetValue("limitsLimitPercents", out string percents) &&
                TryParseFloatRange(percents, out FloatRange range))
            {
                profile.allowedLimitPercents = range;
            }

            if (fields.TryGetValue("limitsAllowedMultistack", out string multistack) &&
                bool.TryParse(multistack, out bool allowedMultistack))
            {
                profile.allowedMultistack = allowedMultistack;
            }

            if (fields.TryGetValue("allowedSimilarStackCount", out string similar) &&
                int.TryParse(similar, NumberStyles.Integer, CultureInfo.InvariantCulture, out int similarCount))
            {
                profile.SimilarStackCountRaw = similarCount;
            }

            if (fields.TryGetValue("filterListDisplayMode", out string displayMode) &&
                int.TryParse(displayMode, NumberStyles.Integer, CultureInfo.InvariantCulture, out int filterDisplayMode))
            {
                profile.FilterListDisplayMode = filterDisplayMode;
            }

            if (fields.TryGetValue("allowedPerItem", out string perItem) && !perItem.NullOrEmpty())
            {
                profile.ImportPerItemKvPairs(perItem);
            }

            profile.RemoveAllCache();
            profile.ClampPerItemOverridesToModeCap();
            return true;
        }

        private static Dictionary<string, string> ReadKvFields(FileInfo fi, out bool inSection)
        {
            Dictionary<string, string> fields = new Dictionary<string, string>();
            inSection = false;

            using (StreamReader reader = new StreamReader(fi.FullName))
            {
                string[] kv;
                while (TryReadKvField(reader, out kv))
                {
                    if (kv[0] == KebabLimitsSectionKey)
                    {
                        inSection = KebabLimitsSectionValue.Equals(kv[1]);
                        continue;
                    }

                    if (!inSection)
                    {
                        continue;
                    }

                    fields[kv[0]] = kv[1];
                }
            }

            return fields;
        }

        /// <summary>
        /// Writes every per-storage profile field. List display mode does not drop hidden item overrides.
        /// Global UI toggles only hide editors; they do not omit fields from the preset.
        ///
        /// Пишет все поля профиля склада. Режим списка не отбрасывает скрытые per-item overrides.
        /// Глобальные тумблеры UI только прячут редакторы и не убирают поля из пресета.
        /// </summary>
        private static void WriteProfileFields(StreamWriter writer, StockpileLimitBundle profile)
        {
            WriteKvField(writer, "limitsLimitPercents",
                profile.allowedLimitPercents.min.ToString("N4", CultureInfo.InvariantCulture) + ":" +
                profile.allowedLimitPercents.max.ToString("N4", CultureInfo.InvariantCulture));
            WriteKvField(writer, "limitsAllowedMultistack", profile.allowedMultistack.ToString());
            WriteKvField(writer, "allowedSimilarStackCount", profile.SimilarStackCountRaw.ToString(CultureInfo.InvariantCulture));
            WriteKvField(writer, "filterListDisplayMode", profile.FilterListDisplayMode.ToString(CultureInfo.InvariantCulture));
            WriteKvField(writer, "allowedPerItem", profile.ExportPerItemKvPairs());
        }

        private static void WriteKvField(StreamWriter writer, string name, string value)
        {
            if (writeFieldMethod != null)
            {
                writeFieldMethod.Invoke(null, new object[] { writer, name, value });
                return;
            }

            writer.WriteLine(name + ":" + (value ?? "null"));
        }

        private static bool TryReadKvField(StreamReader reader, out string[] nameValue)
        {
            if (readFieldMethod != null)
            {
                object[] args = { reader, null };
                bool result = (bool)readFieldMethod.Invoke(null, args);
                nameValue = args[1] as string[];
                return result;
            }

            string line = reader.ReadLine();
            if (line != null && line.Length > 0 && !line.Equals("---"))
            {
                int separator = line.IndexOf(':');
                if (separator > 0)
                {
                    string value = separator < line.Length - 1 ? line.Substring(separator + 1) : string.Empty;
                    if ("null".Equals(value))
                    {
                        value = null;
                    }

                    nameValue = new[] { line.Substring(0, separator), value };
                    return true;
                }
            }

            nameValue = null;
            return false;
        }

        private static bool TryParseFloatRange(string value, out FloatRange range)
        {
            range = FloatRange.ZeroToOne;
            if (value.NullOrEmpty())
            {
                return false;
            }

            string[] parts = value.Split(':');
            if (parts.Length != 2)
            {
                return false;
            }

            if (!float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float min) ||
                !float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float max))
            {
                return false;
            }

            range = new FloatRange(min, max);
            return true;
        }
    }

    public partial class StockpileLimitBundle
    {
        /// <summary>
        /// Serializes every per-item override into KV pair syntax (def/limit/def/limit). Rows hidden by the
        /// storage list display mode are still included.
        ///
        /// Сериализует все per-item переопределения в синтаксис пар KV (def/limit/def/limit). Строки, скрытые
        /// режимом отображения списка склада, тоже входят в запись.
        /// </summary>
        internal string ExportPerItemKvPairs()
        {
            StringBuilder sb = new StringBuilder();
            foreach (KeyValuePair<string, int> pair in perItemOverrides.EnumeratePairs())
            {
                if (sb.Length > 0)
                {
                    sb.Append('/');
                }

                sb.Append(pair.Key);
                sb.Append('/');
                sb.Append(pair.Value.ToString(CultureInfo.InvariantCulture));
            }

            return sb.ToString();
        }

        /// <summary>
        /// Restores per-item overrides from KV preset pair syntax.
        ///
        /// Восстанавливает per-item переопределения из синтаксиса пар KV.
        /// </summary>
        internal void ImportPerItemKvPairs(string data)
        {
            perItemOverrides.ClearKv();
            if (data.NullOrEmpty())
            {
                return;
            }

            string[] parts = data.Split('/');
            for (int i = 0; i + 1 < parts.Length; i += 2)
            {
                if (int.TryParse(parts[i + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int limit))
                {
                    perItemOverrides.SetDefName(parts[i], limit);
                }
            }
        }
    }
}
