using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace HSKKebabLimits
{
    /// <summary>
    /// Main RimWorld mod entry for HSK kebab limits: applies Harmony patches and exposes mod settings.
    /// </summary>
    public class KebabLimitsMod : Mod
    {
        private readonly KebabLimitsModSettings settings;

        public static Harmony HarmonyInstance;

        /// <summary>
        /// Initializes Harmony, applies storage-limit patches, and loads mod settings on startup.
        /// </summary>
        public KebabLimitsMod(ModContentPack content)
            : base(content)
        {
            settings = GetSettings<KebabLimitsModSettings>();
            HarmonyInstance = new Harmony("kebabebak.hsk.kebab.limits");
            KebabLimitsLog.Message("[HSK kebab limits] Initializing mod class.");

            try
            {
                HarmonyInstance.PatchAll(Assembly.GetExecutingAssembly());
                KebabLimitsLog.Message("[HSK kebab limits] Harmony patching completed.");
            }
            catch (Exception ex)
            {
                KebabLimitsLog.Error($"[HSK kebab limits] Harmony patching failed: {ex}");
                throw;
            }
        }

        private static bool dubsMintMenusUnpatched;

        // Dubs Mint Menus adds a DoThingDef prefix that opens the item info card on click, which
        // eats the click before our -/+ controls (it does not touch DoCategory, hence categories work).
        // Unpatch runs from [StaticConstructorOnStartup] and is retried lazily on first storage-tab draw
        // so the timing is independent of mod load/patch order.
        /// <summary>
        /// Removes Dubs Mint Menus click handlers that block per-item limit +/- buttons in the filter tree.
        /// </summary>
        public static void EnsureDubsMintMenusUnpatched()
        {
            if (dubsMintMenusUnpatched || HarmonyInstance == null)
            {
                return;
            }

            if (ModLister.GetActiveModWithIdentifier("Dubwise.DubsMintMenus") == null)
            {
                dubsMintMenusUnpatched = true;
                return;
            }

            MethodInfo doThingDef = AccessTools.Method(typeof(Listing_TreeThingFilter), "DoThingDef");
            if (doThingDef == null)
            {
                return;
            }

            Patches patchInfo = Harmony.GetPatchInfo(doThingDef);
            if (patchInfo?.Prefixes == null)
            {
                return;
            }

            bool removed = false;
            foreach (Patch prefix in patchInfo.Prefixes)
            {
                string declaringType = prefix.PatchMethod?.DeclaringType?.FullName;
                if (declaringType != null && declaringType.Contains("DubsMintMenus"))
                {
                    HarmonyInstance.Unpatch(doThingDef, prefix.PatchMethod);
                    KebabLimitsLog.Message(
                        $"[HSK kebab limits] Removed Dubs Mint Menus DoThingDef prefix: {declaringType}.{prefix.PatchMethod.Name}");
                    removed = true;
                }
            }

            if (removed)
            {
                dubsMintMenusUnpatched = true;
            }
        }

        /// <summary>
        /// Returns the translated label shown for this mod in RimWorld's mod options list.
        /// </summary>
        public override string SettingsCategory()
        {
            return "KebabLimitsSettingsCategory".Translate();
        }

        /// <summary>
        /// Renders the mod settings UI in the RimWorld options dialog.
        /// </summary>
        public override void DoSettingsWindowContents(Rect inRect)
        {
            settings.DrawSettings(inRect);
        }
    }

    /// <summary>
    /// Runs Dubs Mint Menus compatibility cleanup at startup before the first storage tab is opened.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class KebabLimitsDubsMintMenusCompat
    {
        /// <summary>
        /// Attempts to unpatch Dubs Mint Menus as early as possible during game load.
        /// </summary>
        static KebabLimitsDubsMintMenusCompat()
        {
            KebabLimitsMod.EnsureDubsMintMenusUnpatched();
        }
    }

    // All HSK kebab limits logging routes through here so it only reaches Player.log when the
    // "EnableLogging" mod setting is on.
    /// <summary>
    /// Gated logging helper that respects the mod's EnableLogging setting and caps verbose spam.
    /// </summary>
    public static class KebabLimitsLog
    {
        private const int MaxRoutineMessages = 400;
        private const int SuppressionSummaryIntervalTicks = 6000;

        private static int routineMessageCount;
        private static int suppressedRoutineCount;
        private static int lastSuppressionSummaryTick = -1;

        /// <summary>
        /// Writes a log line when logging is enabled; routine lines are capped unless marked important.
        /// </summary>
        public static void Message(string text, bool important = false)
        {
            if (!KebabLimitsModSettings.EnableLogging)
            {
                return;
            }

            if (!important)
            {
                if (routineMessageCount >= MaxRoutineMessages)
                {
                    suppressedRoutineCount++;
                    MaybeLogSuppressionSummary();
                    return;
                }

                routineMessageCount++;
            }

            Log.Message(text);
        }

        /// <summary>
        /// Writes a routine diagnostic log line subject to the verbose message cap.
        /// </summary>
        public static void MessageVerbose(string text)
        {
            Message(text, important: false);
        }

        /// <summary>
        /// Writes a log line that bypasses the routine message cap.
        /// </summary>
        public static void MessageImportant(string text)
        {
            Message(text, important: true);
        }

        /// <summary>
        /// Writes a warning to Player.log when mod logging is enabled.
        /// </summary>
        public static void Warning(string text)
        {
            if (KebabLimitsModSettings.EnableLogging)
            {
                Log.Warning(text);
            }
        }

        /// <summary>
        /// Writes an error to Player.log when mod logging is enabled.
        /// </summary>
        public static void Error(string text)
        {
            if (KebabLimitsModSettings.EnableLogging)
            {
                Log.Error(text);
            }
        }

        /// <summary>
        /// Periodically reports how many verbose log lines were suppressed after the cap was reached.
        /// </summary>
        private static void MaybeLogSuppressionSummary()
        {
            int now = Find.TickManager?.TicksGame ?? 0;
            if (lastSuppressionSummaryTick >= 0 &&
                now - lastSuppressionSummaryTick < SuppressionSummaryIntervalTicks)
            {
                return;
            }

            lastSuppressionSummaryTick = now;
            Log.Message(
                $"[HSK kebab limits] Routine log cap reached ({MaxRoutineMessages} lines). Suppressed {suppressedRoutineCount} verbose lines; important events still log. Disable logging or reduce hauling activity to avoid this.");
            suppressedRoutineCount = 0;
        }
    }
}
