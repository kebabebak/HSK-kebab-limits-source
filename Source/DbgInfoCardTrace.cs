using System;
using HarmonyLib;
using RimWorld;
using Verse;

namespace HSKKebabLimits
{
    /// <summary>
    /// Temporary diagnostic: logs stack trace when Dialog_InfoCard opens (debugging +/- click conflicts).
    /// </summary>
    [HarmonyPatch(typeof(WindowStack), "Add")]
    public static class DbgInfoCardTrace
    {
        /// <summary>Writes Player.log entry when info card opens while FilterTreeLimitRowPatch.DebugLog is on.</summary>
        public static void Prefix(Window window)
        {
            if (FilterTreeLimitRowPatch.DebugLog && window is Dialog_InfoCard)
            {
                Log.Message("[SG-DBG] Dialog_InfoCard opening. Stack:\n" + Environment.StackTrace);
            }
        }
    }
}
