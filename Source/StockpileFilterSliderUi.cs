using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace HSKKebabLimits
{
    /// <summary>
    /// Injects stockpile stack limit sliders and related controls into the thing filter config window.
    /// </summary>
    [HarmonyPatch(typeof(Verse.ThingFilterUI), nameof(Verse.ThingFilterUI.DoThingFilterConfigWindow))]
    public static class StockpileFilterSliderUi
    {
        public static bool DrawingThingFilter;
        public static bool DirectInputMode;
        public static string DirectInputMode_Buffer_Low = "";
        public static string DirectInputMode_Buffer_Upp = "";

        private static bool Prepare()
        {
            MethodInfo drawMentalBreakFilterConfig = AccessTools.Method(typeof(Verse.ThingFilterUI),
                "DrawMentalBreakFilterConfig",
                new[] { typeof(float).MakeByRefType(), typeof(float), typeof(ThingFilter) });
            if (drawMentalBreakFilterConfig != null)
            {
                return true;
            }

            Log.Error(
                "[HSK kebab limits] DrawMentalBreakFilterConfig not found; stack limit UI hook skipped.");
            return false;
        }

        /// <summary>
        /// Inserts a call to draw stack limit controls after the mental break filter row in the config window.
        /// </summary>
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo drawMentalBreakFilterConfig = AccessTools.Method(typeof(Verse.ThingFilterUI),
                "DrawMentalBreakFilterConfig",
                new[] { typeof(float).MakeByRefType(), typeof(float), typeof(ThingFilter) });

            MethodInfo drawStackFilterConfigInfo =
                AccessTools.Method(typeof(StockpileFilterSliderUi), nameof(DrawStackFilterConfig));
            MethodInfo rectWidthInfo = AccessTools.Method(typeof(Rect), "get_width");
            List<CodeInstruction> instList = instructions.ToList();
            bool patched = false;

            for (int i = 0; i < instList.Count; i++)
            {
                CodeInstruction inst = instList[i];
                yield return inst;

                if (!inst.Calls(drawMentalBreakFilterConfig))
                {
                    continue;
                }

                i++;
                yield return instList[i];
                i++;
                yield return instList[i];
                yield return new CodeInstruction(OpCodes.Ldloca_S, (byte)9);
                yield return new CodeInstruction(OpCodes.Ldloca_S, (byte)3);
                yield return new CodeInstruction(OpCodes.Call, rectWidthInfo);
                yield return new CodeInstruction(OpCodes.Call, drawStackFilterConfigInfo);
                yield return new CodeInstruction(OpCodes.Ldloc_S, (byte)9);
                yield return new CodeInstruction(OpCodes.Stloc_S, (byte)10);
                patched = true;
            }

            if (!patched)
            {
                KebabLimitsLog.Warning("[HSK kebab limits] Stack limit UI transpiler did not patch DoThingFilterConfigWindow.");
            }
        }

        /// <summary>
        /// Entry point called from the transpiler to draw stockpile limit controls in the filter window.
        /// </summary>
        public static void DrawStackFilterConfig(ref float y, float width)
        {
            DrawingThingFilter = true;
            try
            {
                StockpileFilterSliderHighlight.TryApplyOnce();
                StockpileFilterSliderRow.Draw(ref y, width);
            }
            finally
            {
                DrawingThingFilter = false;
            }
        }
    }
}
