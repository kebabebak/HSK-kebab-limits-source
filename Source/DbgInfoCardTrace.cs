using System;
using HarmonyLib;
using RimWorld;
using Verse;

namespace HSKKebabLimits
{
    /// <summary>
    /// Logs a stack trace when Dialog_InfoCard opens while FilterTreeLimitRowPatch.DebugLog is on,
    /// to show what opened the card versus the per-item +/- controls.
    ///
    /// Пишет стек в лог при открытии Dialog_InfoCard, если включён FilterTreeLimitRowPatch.DebugLog,
    /// чтобы видеть, что открыло карточку, а не кнопки лимита +/-.
    /// </summary>
    [HarmonyPatch(typeof(WindowStack), "Add")]
    public static class DbgInfoCardTrace
    {
        /// <summary>
        /// Writes a Player.log entry when an info card opens while FilterTreeLimitRowPatch.DebugLog is on.
        ///
        /// Пишет запись в Player.log при открытии карточки, если включён FilterTreeLimitRowPatch.DebugLog.
        /// </summary>
        public static void Prefix(Window window)
        {
            if (FilterTreeLimitRowPatch.DebugLog && window is Dialog_InfoCard)
            {
                Log.Message("[SG-DBG] Dialog_InfoCard opening. Stack:\n" + Environment.StackTrace);
            }
        }
    }
}
