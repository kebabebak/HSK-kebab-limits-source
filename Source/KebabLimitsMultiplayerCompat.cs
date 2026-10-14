using System;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Verse;

namespace HSKKebabLimits
{
    /// <summary>
    /// Registers sync workers with RimWorld Multiplayer when that API is loaded and enabled.
    /// The API is resolved by name. A missing assembly does not fail this constructor.
    ///
    /// Регистрирует sync-воркеры RimWorld Multiplayer, если этот API загружен и включён.
    /// API ищется по имени. Отсутствие сборки не роняет этот конструктор.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class KebabLimitsMultiplayerCompat
    {
        /// <summary>
        /// Registers storage-limit sync when Multiplayer is loaded and MP.enabled is true.
        ///
        /// Регистрирует синхронизацию лимитов склада, если Multiplayer загружен и MP.enabled истинен.
        /// </summary>
        static KebabLimitsMultiplayerCompat()
        {
            try
            {
                Type mp = AccessTools.TypeByName("Multiplayer.API.MP");
                if (mp == null)
                {
                    Log.Warning(
                        "[HSK kebab limits] Multiplayer API is not loaded; storage-limit sync skipped.");
                    return;
                }

                FieldInfo enabled = AccessTools.Field(mp, "enabled");
                if (enabled == null || !(enabled.GetValue(null) is bool on) || !on)
                {
                    return;
                }

                Type syncContext = AccessTools.TypeByName("Multiplayer.API.SyncContext");
                object none = syncContext == null ? null : Enum.ToObject(syncContext, 0);
                MethodInfo registerMethod = FindMethod(mp, "RegisterSyncMethod", typeof(MethodInfo));
                MethodInfo registerField = FindMethod(mp, "RegisterSyncField", typeof(FieldInfo));
                if (registerMethod == null || none == null)
                {
                    Log.Warning(
                        "[HSK kebab limits] Multiplayer API is loaded but sync registration was not found; storage-limit sync skipped.");
                    return;
                }

                Register(registerMethod, none, AccessTools.Method(
                    typeof(DelayedZoneOverflowEjectComponent),
                    nameof(DelayedZoneOverflowEjectComponent.ScheduleAfterLimitChange)));
                Register(registerMethod, none, AccessTools.Method(
                    typeof(StockpileCapacityRules),
                    nameof(StockpileCapacityRules.ReconcileOverflowAfterLimitChange)));
                Register(registerMethod, none, AccessTools.Method(
                    typeof(StockpileProfileStore),
                    nameof(StockpileProfileStore.Set)));
                if (registerField != null)
                {
                    FieldInfo remapper = AccessTools.Field(typeof(StockpileProfileStore), "Remapper");
                    if (remapper != null)
                    {
                        registerField.Invoke(null, new object[] { remapper });
                    }
                }

                RegisterBundleWorker(mp);
                KebabLimitsLog.Message("[kebabebak|HSKKebabLimits] Multiplayer compat enabled!");
            }
            catch (Exception ex)
            {
                Log.Warning("[HSK kebab limits] Multiplayer sync for storage limits was skipped: " + ex.Message);
            }
        }

        static MethodInfo FindMethod(Type type, string name, Type firstParameter)
        {
            foreach (MethodInfo candidate in type.GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                if (candidate.Name != name)
                {
                    continue;
                }

                ParameterInfo[] parameters = candidate.GetParameters();
                if (parameters.Length == 0 || parameters[0].ParameterType != firstParameter)
                {
                    continue;
                }

                if (name == "RegisterSyncMethod" && parameters.Length != 2)
                {
                    continue;
                }

                if (name == "RegisterSyncField" && parameters.Length != 1)
                {
                    continue;
                }

                return candidate;
            }

            return null;
        }

        /// <summary>
        /// Registers sync for the fields of a stockpile limit profile.
        ///
        /// Регистрирует синхронизацию полей профиля лимита склада.
        /// </summary>
        static void RegisterBundleWorker(Type mp)
        {
            Type bundleType = typeof(StockpileLimitBundle);
            Type syncWorkerType = AccessTools.TypeByName("Multiplayer.API.SyncWorker");
            Type delegateOpen = AccessTools.TypeByName("Multiplayer.API.SyncWorkerDelegate`1");
            if (syncWorkerType == null || delegateOpen == null)
            {
                return;
            }

            Type delegateType = delegateOpen.MakeGenericType(bundleType);
            DynamicMethod method = new DynamicMethod(
                "SyncStockpileLimitBundle",
                typeof(void),
                new[] { syncWorkerType, bundleType.MakeByRefType() },
                typeof(KebabLimitsMultiplayerCompat).Module,
                skipVisibility: true);
            ILGenerator il = method.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldind_Ref);
            il.Emit(OpCodes.Call, AccessTools.Method(typeof(KebabLimitsMultiplayerCompat), nameof(SyncBundleFields)));
            il.Emit(OpCodes.Ret);
            Delegate worker = method.CreateDelegate(delegateType);

            MethodInfo register = null;
            foreach (MethodInfo candidate in mp.GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                if (candidate.Name == "RegisterSyncWorker" && candidate.IsGenericMethodDefinition)
                {
                    register = candidate.MakeGenericMethod(bundleType);
                    break;
                }
            }

            if (register == null)
            {
                return;
            }

            register.Invoke(null, new object[] { worker, null, false, false });
        }

        /// <summary>
        /// Writes the stockpile limit profile fields through the sync binder.
        ///
        /// Пишет поля профиля лимита склада через sync-биндер.
        /// </summary>
        static void SyncBundleFields(object sync, StockpileLimitBundle data)
        {
            if (sync == null || data == null)
            {
                return;
            }

            MethodInfo bind = AccessTools.Method(sync.GetType(), "Bind", new[] { typeof(object), typeof(string) });
            if (bind == null)
            {
                return;
            }

            bind.Invoke(sync, new object[] { data, "allowedLimitPercents" });
            bind.Invoke(sync, new object[] { data, "allowedMultistack" });
            bind.Invoke(sync, new object[] { data, "expandFilterCategories" });
            bind.Invoke(sync, new object[] { data, "allowedSimilarStackCount" });
            bind.Invoke(sync, new object[] { data, "filterListDisplayMode" });
        }

        /// <summary>
        /// Registers one method and clears its sync context so the call is not tied to a map.
        ///
        /// Регистрирует один метод и очищает контекст синхронизации, чтобы вызов не был привязан к карте.
        /// </summary>
        static void Register(MethodInfo registerMethod, object none, MethodInfo target)
        {
            if (target == null)
            {
                return;
            }

            ParameterInfo[] parameters = registerMethod.GetParameters();
            Type syncType = parameters[1].ParameterType.GetElementType();
            Array syncTypes = Array.CreateInstance(syncType, 0);
            object sync = registerMethod.Invoke(null, new object[] { target, syncTypes });
            if (sync == null)
            {
                return;
            }

            MethodInfo setContext = AccessTools.Method(sync.GetType(), "SetContext");
            if (setContext != null)
            {
                setContext.Invoke(sync, new object[] { none });
            }
        }
    }
}
