using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace HSKKebabLimits
{
    /// <summary>
    /// One white Player.log line listing settings that differ from their defaults, then one white
    /// line whenever any setting changes. Always on: Enable logging does not hide it.
    ///
    /// Одна белая строка Player.log с настройками, которые отличаются от значений по умолчанию,
    /// и ещё одна белая строка при каждом изменении любой настройки. Всегда включено: «Включить
    /// логирование» это не прячет.
    /// </summary>
    public static class SettingsAudit
    {
        const string Prefix = "[HSK kebab limits] ";
        const float SettleSeconds = 0.5f;

        enum Kind
        {
            Bool,
            Int,
            Float,
            Mode
        }

        sealed class Track
        {
            public string Token;
            public Func<int> Read;
            public Func<int> Default;
            public Kind Kind;
            public string[] Modes;
        }

        static readonly List<Track> tracks = Build();

        static int[] scratch;
        static int[] committed;
        static int[] lastSeen;
        static bool installed;
        static bool armed;
        static bool pending;
        static bool pollFaulted;
        static float pendingSince;

        /// <summary>
        /// Watches both UI roots so a change is seen on the main menu and in a colony, including
        /// a write that did not go through the settings window.
        ///
        /// Следит за обоими UI root, чтобы изменение было видно и в главном меню, и в колонии,
        /// в том числе если значение записали не через окно настроек.
        /// </summary>
        public static void Install(Harmony harmony)
        {
            if (installed || harmony == null)
            {
                return;
            }

            installed = true;
            PatchRoot(harmony, typeof(UIRoot_Entry));
            PatchRoot(harmony, typeof(UIRoot_Play));
        }

        /// <summary>
        /// Writes the session line and starts change tracking. Call after settings have loaded.
        ///
        /// Пишет строку этой сессии и включает слежение за изменениями. Вызывать после загрузки настроек.
        /// </summary>
        public static void Publish()
        {
            if (armed)
            {
                return;
            }

            scratch = ReadInto(null);
            CopyTo(scratch, ref committed);
            CopyTo(scratch, ref lastSeen);
            pending = false;
            armed = true;
            Log.Message(Prefix + FormatStartup());
        }

        /// <summary>
        /// Checkboxes and modes log on the next UI frame. Typed numbers and sliders wait until the
        /// value sits still, so one drag is one line from the previous value to the last.
        ///
        /// Чекбоксы и режимы пишутся на следующем кадре UI. Вводимые числа и ползунки ждут, пока
        /// значение перестанет меняться: одно перетаскивание - одна строка от прежнего значения к последнему.
        /// </summary>
        public static void Poll()
        {
            if (!armed || pollFaulted)
            {
                return;
            }

            try
            {
                scratch = ReadInto(scratch);
                if (SameInts(scratch, lastSeen))
                {
                    if (pending && Time.realtimeSinceStartup - pendingSince >= SettleSeconds)
                    {
                        Flush();
                    }

                    return;
                }

                bool debounced = DebouncedMoved(scratch, lastSeen);
                CopyTo(scratch, ref lastSeen);
                if (debounced)
                {
                    pending = true;
                    pendingSince = Time.realtimeSinceStartup;
                    return;
                }

                Flush();
            }
            catch (Exception ex)
            {
                pollFaulted = true;
                Log.Error(Prefix + "Settings audit stopped: " + ex);
            }
        }

        static void PatchRoot(Harmony harmony, Type rootType)
        {
            MethodInfo method = AccessTools.Method(rootType, "UIRootOnGUI");
            if (method == null)
            {
                return;
            }

            harmony.Patch(method, postfix: new HarmonyMethod(typeof(SettingsAudit), nameof(Poll)));
        }

        static void Flush()
        {
            pending = false;
            if (SameInts(scratch, committed))
            {
                return;
            }

            string body = FormatChanges();
            CopyTo(scratch, ref committed);
            if (!body.NullOrEmpty())
            {
                Log.Message(Prefix + body);
            }
        }

        static bool DebouncedMoved(int[] now, int[] prev)
        {
            if (prev == null)
            {
                return true;
            }

            for (int i = 0; i < tracks.Count; i++)
            {
                if (now[i] == prev[i])
                {
                    continue;
                }

                Kind kind = tracks[i].Kind;
                if (kind == Kind.Int || kind == Kind.Float)
                {
                    return true;
                }
            }

            return false;
        }

        static string FormatStartup()
        {
            var parts = new List<string>();
            for (int i = 0; i < tracks.Count; i++)
            {
                Track track = tracks[i];
                int value = committed[i];
                if (value == track.Default())
                {
                    continue;
                }

                parts.Add(track.Token + "=" + Format(track, value));
            }

            return parts.Count == 0 ? "defaults" : string.Join(", ", parts.ToArray());
        }

        static string FormatChanges()
        {
            var parts = new List<string>();
            for (int i = 0; i < tracks.Count; i++)
            {
                int before = committed[i];
                int after = scratch[i];
                if (before == after)
                {
                    continue;
                }

                Track track = tracks[i];
                parts.Add(track.Token + " " + Format(track, before) + " -> " + Format(track, after));
            }

            return parts.Count == 0 ? null : string.Join(", ", parts.ToArray());
        }

        static string Format(Track track, int bits)
        {
            switch (track.Kind)
            {
                case Kind.Bool:
                    return bits != 0 ? "on" : "off";
                case Kind.Float:
                    return (bits / 10000.0).ToString("0.####", CultureInfo.InvariantCulture);
                case Kind.Mode:
                    if (track.Modes != null && bits >= 0 && bits < track.Modes.Length)
                    {
                        return track.Modes[bits];
                    }

                    return bits.ToString(CultureInfo.InvariantCulture);
                default:
                    return bits.ToString(CultureInfo.InvariantCulture);
            }
        }

        static int Quant(float value)
        {
            return (int)Math.Round(value * 10000.0, MidpointRounding.AwayFromZero);
        }

        static int[] ReadInto(int[] dest)
        {
            if (dest == null || dest.Length != tracks.Count)
            {
                dest = new int[tracks.Count];
            }

            for (int i = 0; i < tracks.Count; i++)
            {
                dest[i] = tracks[i].Read();
            }

            return dest;
        }

        static void CopyTo(int[] src, ref int[] dest)
        {
            if (dest == null || dest.Length != src.Length)
            {
                dest = new int[src.Length];
            }

            Array.Copy(src, dest, src.Length);
        }

        static bool SameInts(int[] left, int[] right)
        {
            if (left == null || right == null || left.Length != right.Length)
            {
                return false;
            }

            for (int i = 0; i < left.Length; i++)
            {
                if (left[i] != right[i])
                {
                    return false;
                }
            }

            return true;
        }

        static void Bool(List<Track> list, string token, Func<bool> read, bool fallback)
        {
            list.Add(new Track
            {
                Token = token,
                Read = () => read() ? 1 : 0,
                Default = () => fallback ? 1 : 0,
                Kind = Kind.Bool
            });
        }

        static void Int(List<Track> list, string token, Func<int> read, int fallback)
        {
            list.Add(new Track
            {
                Token = token,
                Read = read,
                Default = () => fallback,
                Kind = Kind.Int
            });
        }

        static void Float(List<Track> list, string token, Func<float> read, float fallback)
        {
            int bits = Quant(fallback);
            list.Add(new Track
            {
                Token = token,
                Read = () => Quant(read()),
                Default = () => bits,
                Kind = Kind.Float
            });
        }

        static void Mode(List<Track> list, string token, Func<int> read, int fallback, params string[] names)
        {
            list.Add(new Track
            {
                Token = token,
                Read = read,
                Default = () => fallback,
                Kind = Kind.Mode,
                Modes = names
            });
        }

        static List<Track> Build()
        {
            var list = new List<Track>(24);
            Bool(list, "logging", () => KebabLimitsModSettings.EnableLogging, false);
            Bool(list, "percent", () => KebabLimitsModSettings.PercentageMode, false);
            Bool(list, "similar", () => KebabLimitsModSettings.GlobalSimilarStackEnabled, true);
            Bool(list, "cache", () => KebabLimitsModSettings.EnableNegativeSolveCache, true);
            Bool(list, "hard-eject", () => KebabLimitsModSettings.EnableHardEjection, false);
            Bool(list, "hard-eject:delay", () => KebabLimitsModSettings.DelayHardEjectionAfterLimitChange, false);
            Int(list, "hard-eject:hours", () => KebabLimitsModSettings.DelayHardEjectionHours,
                KebabLimitsModSettings.DefaultDelayHardEjectionHours);
            Mode(list, "cascade", () => KebabLimitsModSettings.ZoneWideCascadeEjectMode, 0,
                "off", "cells", "most", "least");
            Int(list, "cascade:hours", () => KebabLimitsModSettings.DelayedZoneEjectHours,
                KebabLimitsModSettings.DefaultDelayedZoneEjectHours);
            Mode(list, "multistack", () => KebabLimitsModSettings.GlobalMultistackMode, 0,
                "off", "global", "storage");
            Bool(list, "filter-ui", () => KebabLimitsModSettings.AddFilterDisplaySettings, false);
            Mode(list, "expand", () => KebabLimitsModSettings.ExpandFilterCategoriesMode, 0,
                "off", "storage", "global");
            Bool(list, "above-slider", () => KebabLimitsModSettings.AllowPerItemAboveSlider, false);
            Bool(list, "raised-color", () => KebabLimitsModSettings.SeparateRaisedLimitHighlightColor, false);
            Float(list, "log-factor", () => KebabLimitsModSettings.DisplayLogFactor,
                KebabLimitsModSettings.DefaultDisplayLogFactor);
            Int(list, "slider-max", () => KebabLimitsModSettings.MaxStackSliderLimit,
                KebabLimitsModSettings.DefaultMaxStackSliderLimit);
            Float(list, "color", () => KebabLimitsModSettings.HighlightColorPosition,
                KebabLimitsModSettings.DefaultHighlightColorPosition);
            Float(list, "raised-pos", () => KebabLimitsModSettings.RaisedLimitHighlightColorPosition,
                KebabLimitsModSettings.DefaultRaisedLimitHighlightColorPosition);
            return list;
        }
    }
}
