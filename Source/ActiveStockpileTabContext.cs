using System;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;

namespace HSKKebabLimits
{
    /// <summary>
    /// Harmony patch that tracks which stockpile storage settings tab is being drawn for limit UI hooks.
    /// </summary>
    [HarmonyPatch(typeof(ITab_Storage), "FillTab")]
    public class ActiveStockpileTabContext
    {
        public static StorageSettings ActiveStorageSettings;
        public static bool DrawingStorageTab;

        private static readonly Func<ITab_Storage, IStoreSettingsParent> GetSelStoreSettingsParent =
            GetPropertyGetter<ITab_Storage, IStoreSettingsParent>("SelStoreSettingsParent");

        /// <summary>
        /// Builds a fast delegate to read a non-public instance property via IL emit.
        /// </summary>
        public static Func<T, P> GetPropertyGetter<T, P>(string propertyName)
        {
            MethodInfo getMethod = typeof(T).GetProperty(propertyName, BindingFlags.Instance | BindingFlags.NonPublic)
                .GetGetMethod(nonPublic: true);
            DynamicMethod dynamicMethod = new DynamicMethod(string.Empty, typeof(P), new[] { typeof(T) }, typeof(T));
            ILGenerator ilGenerator = dynamicMethod.GetILGenerator();
            ilGenerator.Emit(OpCodes.Ldarg_0);
            ilGenerator.Emit(OpCodes.Callvirt, getMethod);
            ilGenerator.Emit(OpCodes.Ret);
            return (Func<T, P>)dynamicMethod.CreateDelegate(typeof(Func<T, P>));
        }

        /// <summary>
        /// Captures the active storage settings before the stockpile tab renders.
        /// </summary>
        public static bool Prefix(ITab_Storage __instance)
        {
            KebabLimitsMod.EnsureDubsMintMenusUnpatched();
            ActiveStorageSettings = null;
            DrawingStorageTab = false;

            if (__instance.GetType().Assembly == typeof(ITab_Storage).Assembly)
            {
                ActiveStorageSettings = GetSelStoreSettingsParent(__instance).GetStoreSettings();
                DrawingStorageTab = ActiveStorageSettings != null;
            }

            return true;
        }

        /// <summary>
        /// Clears captured storage tab state after the tab finishes drawing.
        /// </summary>
        public static void Postfix()
        {
            ActiveStorageSettings = null;
            DrawingStorageTab = false;
        }
    }
}
