using System;
using UnityEngine;
using Verse;

namespace HSKKebabLimits
{
    /// <summary>
    /// Mod settings and UI for HSK kebab limits storage limits, ejection, multistack, filter list display, and slider options.
    ///
    /// Настройки мода и UI для лимитов хранилища HSK kebab limits, выброса, мультистака, отображения списка фильтра и ползунка.
    /// </summary>
    public class KebabLimitsModSettings : ModSettings
    {
        public const float DefaultDisplayLogFactor = 3f;
        public const int DefaultMaxStackSliderLimit = 5000;
        public const float DefaultHighlightColorPosition = 0.2f;
        public const int DefaultDelayedZoneEjectHours = 1;

        public static bool EnableHardEjection = true;
        public static float DisplayLogFactor = DefaultDisplayLogFactor;
        public static FloatRange DisplayLogExample = FloatRange.ZeroToOne;
        public static int GlobalMultistackMode;
        public static bool PercentageMode;
        public static bool GlobalSimilarStackEnabled = true;
        public static bool AllowPerItemAboveSlider;
        public static bool AddFilterDisplaySettings;
        public static bool EnableLogging;
        public static bool EnableNegativeSolveCache = true;
        public static int ZoneWideCascadeEjectMode;
        public static int DelayedZoneEjectHours = DefaultDelayedZoneEjectHours;
        public static int MaxStackSliderLimit = DefaultMaxStackSliderLimit;
        public static string MaxStackSliderLimit_Buffer;
        public static string DisplayLogFactor_Buffer;
        public static float HighlightColorPosition = DefaultHighlightColorPosition;

        public static Color HighlightColor => LimitHighlight.SampleColor(HighlightColorPosition);

        private const float SettingsCheckboxRowHeight = 32f;
        private const float SettingsCheckboxRowGap = 2f;
        private const float SettingsListingCanvasHeight = 4000f;
        private const float SettingsScrollBarWidth = 16f;
        private const float NumericFieldWidthFraction = 0.25f;
        private const float NumericFieldHeight = 24f;
        private const float DisplayLogFactorMin = 1f;
        private const float DisplayLogFactorMax = 8f;
        private const int MaxStackSliderLimitMax = 10000000;
        private const int MaxStackSliderLimitMaxDigits = 8;
        private const float SettingResetButtonWidth = 80f;
        private const float SettingResetButtonGap = 4f;
        private const float PreviewToHighlightColorGap = 14f;

        private Vector2 settingsScrollPosition;
        private float settingsScrollContentHeight = 600f;

        private static readonly Color ModeDigitActiveColor = new Color(0.25f, 0.9f, 0.25f);
        private static readonly Color ModeDigitInactiveColor = Color.white;
        private static readonly Color ModeDigitDisabledColor = Color.white;
        private static readonly Color SettingsRowSeparatorColor = new Color(0.55f, 0.55f, 0.55f, 0.8f);
        private const float NonDefaultUnderlineThickness = 1f;
        private static readonly Color NonDefaultUnderlineColor = new Color(0.45f, 0.78f, 1f);

        /// <summary>
        /// Draws a thin horizontal separator between rows in the mod settings panel.
        /// </summary>
        private static void DrawSettingsRowSeparator(Listing_Standard listing, float fullWidth)
        {
            Rect lineRect = listing.GetRect(1f);
            lineRect.x = 0f;
            lineRect.width = fullWidth;
            if (Event.current.type == EventType.Repaint)
            {
                Color previousColor = GUI.color;
                GUI.color = SettingsRowSeparatorColor;
                GUI.DrawTexture(lineRect, BaseContent.WhiteTex);
                GUI.color = previousColor;
            }
        }

        /// <summary>
        /// Draws a centered reset button that restores all mod settings to defaults after confirmation.
        /// </summary>
        private void DrawResetSettingsButton(Listing_Standard listing, float fullWidth)
        {
            const float buttonHeight = 30f;
            const float buttonWidth = 220f;
            Rect row = listing.GetRect(buttonHeight);
            Rect buttonRect = new Rect(row.x + (fullWidth - buttonWidth) / 2f, row.y, buttonWidth, buttonHeight);
            if (Widgets.ButtonText(buttonRect, "ResetSettings".Translate()))
            {
                Find.WindowStack.Add(new Dialog_MessageBox(
                    "ResetSettingsConfirm".Translate(),
                    "Confirm".Translate(),
                    ResetToDefaults,
                    "Cancel".Translate(),
                    null,
                    "ResetSettings".Translate(),
                    buttonADestructive: true));
            }

            listing.Gap(SettingsCheckboxRowGap);
        }

        /// <summary>
        /// Restores all kebab limits mod settings to their default values and persists the change.
        /// </summary>
        public void ResetToDefaults()
        {
            bool hadNegativeCache = EnableNegativeSolveCache;

            EnableHardEjection = true;
            DisplayLogFactor = DefaultDisplayLogFactor;
            DisplayLogExample = FloatRange.ZeroToOne;
            GlobalMultistackMode = 0;
            PercentageMode = false;
            GlobalSimilarStackEnabled = true;
            AllowPerItemAboveSlider = false;
            AddFilterDisplaySettings = false;
            EnableLogging = false;
            EnableNegativeSolveCache = true;
            ZoneWideCascadeEjectMode = 0;
            DelayedZoneEjectHours = DefaultDelayedZoneEjectHours;
            MaxStackSliderLimit = DefaultMaxStackSliderLimit;
            MaxStackSliderLimit_Buffer = null;
            DisplayLogFactor_Buffer = null;
            HighlightColorPosition = DefaultHighlightColorPosition;

            if (hadNegativeCache)
            {
                CapacityQueryCache.ClearAllNegative();
            }

            LogarithmicStackScale.InvalidateAndRefresh();
            Write();
        }

        /// <summary>
        /// Draws a labeled checkbox row with an optional tooltip in mod settings.
        /// </summary>
        private static void DrawSettingsCheckboxRow(Listing_Standard listing, string label, ref bool value,
            bool defaultValue, string tooltip = null)
        {
            Rect row = listing.GetRect(SettingsCheckboxRowHeight);
            if (!tooltip.NullOrEmpty())
            {
                if (Mouse.IsOver(row))
                {
                    Widgets.DrawHighlight(row);
                }

                TooltipHandler.TipRegion(row, tooltip);
            }

            Widgets.CheckboxLabeled(row, label, ref value);
            if (value != defaultValue)
            {
                DrawNonDefaultTextUnderline(row, label, TextAnchor.MiddleLeft);
            }

            listing.Gap(SettingsCheckboxRowGap);
        }

        /// <summary>
        /// Light-blue underline under a setting label when the value differs from default.
        ///
        /// Голубая линия под текстом настройки, если значение отличается от дефолта.
        /// </summary>
        private static void DrawNonDefaultTextUnderline(Rect row, string text, TextAnchor anchor)
        {
            if (Event.current.type != EventType.Repaint || text.NullOrEmpty())
            {
                return;
            }

            Vector2 size = Text.CalcSize(text);
            float x;
            if (anchor == TextAnchor.MiddleCenter || anchor == TextAnchor.UpperCenter ||
                anchor == TextAnchor.LowerCenter)
            {
                x = row.x + (row.width - size.x) / 2f;
            }
            else
            {
                x = row.x;
            }

            float y = row.yMax - NonDefaultUnderlineThickness - 3f;
            Color previous = GUI.color;
            GUI.color = NonDefaultUnderlineColor;
            GUI.DrawTexture(new Rect(x, y, size.x, NonDefaultUnderlineThickness), BaseContent.WhiteTex);
            GUI.color = previous;
        }

        /// <summary>
        /// Returns true when a float setting differs from its default beyond slider rounding noise.
        ///
        /// Возвращает true, если float-настройка отличается от дефолта с учётом погрешности ползунка.
        /// </summary>
        private static bool IsNonDefaultFloat(float value, float defaultValue)
        {
            return !Mathf.Approximately(value, defaultValue);
        }

        /// <summary>
        /// Returns a compact right-aligned numeric field rect matching kebab tweaks body rows.
        ///
        /// Возвращает компактное числовое поле справа в стиле body-строк kebab tweaks.
        /// </summary>
        private static Rect CalcNumericFieldRect(Rect row, Rect rightHalf)
        {
            float fieldWidth = rightHalf.width * NumericFieldWidthFraction;
            return new Rect(
                rightHalf.xMax - fieldWidth,
                row.y + (row.height - NumericFieldHeight) / 2f,
                fieldWidth,
                NumericFieldHeight);
        }

        /// <summary>
        /// Clamps max slider ceiling input to the allowed digit count and value range.
        ///
        /// Ограничивает ввод потолка ползунка по числу цифр и допустимому диапазону.
        /// </summary>
        private static void ClampMaxStackSliderLimitInput()
        {
            if (!MaxStackSliderLimit_Buffer.NullOrEmpty() &&
                MaxStackSliderLimit_Buffer.Length > MaxStackSliderLimitMaxDigits)
            {
                MaxStackSliderLimit_Buffer =
                    MaxStackSliderLimit_Buffer.Substring(0, MaxStackSliderLimitMaxDigits);
            }

            if (MaxStackSliderLimit > MaxStackSliderLimitMax)
            {
                MaxStackSliderLimit = MaxStackSliderLimitMax;
            }
            else if (MaxStackSliderLimit < 0)
            {
                MaxStackSliderLimit = 0;
            }
        }

        /// <summary>
        /// Draws the slider scale modifier row with a tooltip label and numeric input field.
        /// </summary>
        private void DrawDisplayLogFactorRow(Listing_Standard listing)
        {
            Rect row = listing.GetRect(SettingsCheckboxRowHeight);
            Rect labelRect = row.LeftHalf();
            Rect fieldRect = CalcNumericFieldRect(row, row.RightHalf());
            string label = "DisplayLogFactor".Translate();
            string tooltip = "DisplayLogFactorInfo".Translate();

            if (!tooltip.NullOrEmpty())
            {
                if (Mouse.IsOver(labelRect))
                {
                    Widgets.DrawHighlight(labelRect);
                }

                TooltipHandler.TipRegion(labelRect, tooltip);
            }

            TextAnchor previousAnchor = Text.Anchor;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(labelRect, label);
            Text.Anchor = previousAnchor;

            if (IsNonDefaultFloat(DisplayLogFactor, DefaultDisplayLogFactor))
            {
                DrawNonDefaultTextUnderline(labelRect, label, TextAnchor.MiddleLeft);
            }

            float previous = DisplayLogFactor;
            Widgets.TextFieldNumeric(fieldRect, ref DisplayLogFactor, ref DisplayLogFactor_Buffer,
                DisplayLogFactorMin, DisplayLogFactorMax);
            if (DisplayLogFactor < DisplayLogFactorMin)
            {
                DisplayLogFactor = DisplayLogFactorMin;
            }
            else if (DisplayLogFactor > DisplayLogFactorMax)
            {
                DisplayLogFactor = DisplayLogFactorMax;
            }

            DisplayLogFactor = Mathf.Round(DisplayLogFactor * 10f) / 10f;
            if (!Mathf.Approximately(previous, DisplayLogFactor))
            {
                LogarithmicStackScale.InvalidateAndRefresh();
            }

            listing.Gap(SettingsCheckboxRowGap);
        }

        /// <summary>
        /// Draws the maximum stockpile limit slider ceiling numeric input row.
        /// </summary>
        private void DrawMaxStackSliderLimitRow(Listing_Standard listing)
        {
            Rect row = listing.GetRect(SettingsCheckboxRowHeight);
            Rect labelRect = row.LeftHalf();
            Rect fieldRect = CalcNumericFieldRect(row, row.RightHalf());
            string label = "LimitSliderMaxValue".Translate();

            TextAnchor previousAnchor = Text.Anchor;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(labelRect, label);
            Text.Anchor = previousAnchor;

            if (MaxStackSliderLimit != DefaultMaxStackSliderLimit)
            {
                DrawNonDefaultTextUnderline(labelRect, label, TextAnchor.MiddleLeft);
            }

            int previousMax = MaxStackSliderLimit;
            Widgets.TextFieldNumeric(fieldRect, ref MaxStackSliderLimit, ref MaxStackSliderLimit_Buffer, 0f,
                MaxStackSliderLimitMax);
            ClampMaxStackSliderLimitInput();

            if (previousMax != MaxStackSliderLimit)
            {
                LogarithmicStackScale.InvalidateAndRefresh();
            }

            listing.Gap(SettingsCheckboxRowGap);
        }

        /// <summary>
        /// Draws the modified-limit highlight color label with a per-setting reset button.
        /// </summary>
        private void DrawModifiedHighlightColorHeader(Listing_Standard listing)
        {
            Rect row = listing.GetRect(SettingsCheckboxRowHeight);
            string label = "ModifiedHighlightColorLabel".Translate();
            Rect resetRect = new Rect(
                row.xMax - SettingResetButtonWidth,
                row.y + (row.height - NumericFieldHeight) / 2f,
                SettingResetButtonWidth,
                NumericFieldHeight);
            Rect labelRect = row;
            labelRect.width = resetRect.xMin - SettingResetButtonGap - row.x;

            TextAnchor previousAnchor = Text.Anchor;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(labelRect, label);
            Text.Anchor = previousAnchor;

            if (IsNonDefaultFloat(HighlightColorPosition, DefaultHighlightColorPosition))
            {
                DrawNonDefaultTextUnderline(labelRect, label, TextAnchor.MiddleLeft);
            }

            DrawSettingResetButton(resetRect, label, ResetHighlightColor);
            listing.Gap(SettingsCheckboxRowGap);
        }

        /// <summary>
        /// Resets the modified-limit highlight color slider to its default position.
        /// </summary>
        private static void ResetHighlightColor()
        {
            HighlightColorPosition = DefaultHighlightColorPosition;
        }

        /// <summary>
        /// Draws a compact reset button with confirmation for a single settings row.
        /// </summary>
        private void DrawSettingResetButton(Rect rect, string settingLabel, Action resetAction)
        {
            if (Widgets.ButtonText(rect, "ResetSetting".Translate()))
            {
                Find.WindowStack.Add(new Dialog_MessageBox(
                    "ResetSettingConfirm".Translate(settingLabel.Named("SETTING")),
                    "Confirm".Translate(),
                    () =>
                    {
                        resetAction();
                        Write();
                    },
                    "Cancel".Translate(),
                    null,
                    "ResetSetting".Translate(),
                    buttonADestructive: true));
            }
        }

        /// <summary>
        /// Updates zone-wide cascade eject mode and logs the change when logging is enabled.
        /// </summary>
        private static void SetZoneWideCascadeEjectMode(int newMode)
        {
            if (ZoneWideCascadeEjectMode == newMode)
            {
                return;
            }

            int previousMode = ZoneWideCascadeEjectMode;
            ZoneWideCascadeEjectMode = newMode;
            if (newMode == 0)
            {
                KebabLimitsLog.Message("[HSK kebab limits] Storage-wide cascade eject disabled (cross).");
            }
            else
            {
                KebabLimitsLog.Message(
                    $"[HSK kebab limits] Storage-wide cascade eject mode {newMode} enabled (previous: {previousMode}).");
            }
        }

        /// <summary>
        /// Updates global multistack mode and logs the change when logging is enabled.
        /// </summary>
        private static void SetGlobalMultistackMode(int newMode)
        {
            if (GlobalMultistackMode == newMode)
            {
                return;
            }

            int previousMode = GlobalMultistackMode;
            GlobalMultistackMode = newMode;
            if (newMode == 0)
            {
                KebabLimitsLog.Message("[HSK kebab limits] Multistack disabled (cross).");
            }
            else
            {
                KebabLimitsLog.Message(
                    $"[HSK kebab limits] Multistack mode {newMode} enabled (previous: {previousMode}).");
            }
        }

        /// <summary>
        /// Updates delayed overflow eject delay and logs the change when logging is enabled.
        /// </summary>
        private static void SetDelayedZoneEjectHours(int newHours)
        {
            if (DelayedZoneEjectHours == newHours)
            {
                return;
            }

            int previousHours = DelayedZoneEjectHours;
            DelayedZoneEjectHours = newHours;
            if (newHours <= 0)
            {
                KebabLimitsLog.Message(
                    $"[HSK kebab limits] Delayed stockpile overflow eject disabled (previous: {previousHours}h).");
            }
            else
            {
                KebabLimitsLog.Message(
                    $"[HSK kebab limits] Delayed stockpile overflow eject set to {newHours} game hours (previous: {previousHours}h).");
            }
        }

        /// <summary>
        /// Draws a vertically centered settings row label with optional tooltip and non-default underline.
        /// </summary>
        private static void DrawSettingsRowLabel(Rect row, string label, string tooltip = null,
            bool underlineNonDefault = false)
        {
            if (!tooltip.NullOrEmpty())
            {
                TooltipHandler.TipRegion(row, tooltip);
            }

            TextAnchor previousAnchor = Text.Anchor;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(row, label);
            Text.Anchor = previousAnchor;

            if (underlineNonDefault)
            {
                DrawNonDefaultTextUnderline(row, label, TextAnchor.MiddleLeft);
            }
        }

        /// <summary>
        /// Draws the zone-wide cascade eject mode selector with off and numbered mode buttons.
        /// </summary>
        private void DrawZoneWideCascadeEjectRow(Listing_Standard listing)
        {
            const float controlSize = 24f;
            const float digitGap = 4f;
            Rect row = listing.GetRect(SettingsCheckboxRowHeight);
            DrawSettingsRowLabel(row, "ZoneWideCascadeEject".Translate(),
                underlineNonDefault: ZoneWideCascadeEjectMode != 0);

            float controlY = row.y + (row.height - controlSize) / 2f;
            float x = row.xMax - controlSize;
            Rect crossRect = new Rect(x, controlY, controlSize, controlSize);
            TooltipHandler.TipRegion(crossRect, "ZoneWideCascadeEjectDisabledTooltip".Translate());
            if (Widgets.ButtonImage(crossRect, Widgets.CheckboxOffTex))
            {
                SetZoneWideCascadeEjectMode(0);
            }

            x -= digitGap + controlSize;
            for (int mode = 3; mode >= 1; mode--)
            {
                Rect digitRect = new Rect(x, controlY, controlSize, controlSize);
                DrawZoneWideCascadeEjectDigit(digitRect, mode);
                x -= digitGap + controlSize;
            }

            listing.Gap(SettingsCheckboxRowGap);
        }

        /// <summary>
        /// Draws one selectable digit button for a cascade eject mode.
        /// </summary>
        private void DrawZoneWideCascadeEjectDigit(Rect rect, int mode)
        {
            string tooltipKey = "ZoneWideCascadeEjectMode" + mode + "Tooltip";
            TooltipHandler.TipRegion(rect, tooltipKey.Translate());
            if (Mouse.IsOver(rect))
            {
                Widgets.DrawHighlight(rect);
            }

            if (Widgets.ButtonInvisible(rect))
            {
                SetZoneWideCascadeEjectMode(mode);
            }

            bool active = ZoneWideCascadeEjectMode == mode;
            if (active)
            {
                DrawInnerBoxBorder(rect, 2f, ModeDigitActiveColor);
            }

            Color digitColor = ZoneWideCascadeEjectMode == 0
                ? ModeDigitDisabledColor
                : active ? ModeDigitActiveColor : ModeDigitInactiveColor;

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;
            Text.Font = GameFont.Medium;
            GUIStyle labelStyle = new GUIStyle(Text.CurFontStyle)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold
            };
            GUI.color = digitColor;
            GUI.Label(rect, mode.ToString(), labelStyle);
            GUI.color = previousColor;
            Text.Anchor = previousAnchor;
            Text.Font = previousFont;
        }

        /// <summary>
        /// Draws the delayed zone overflow eject delay selector with off and hour presets.
        /// </summary>
        private void DrawDelayedZoneEjectRow(Listing_Standard listing)
        {
            const float controlSize = 24f;
            const float digitGap = 4f;
            Rect row = listing.GetRect(SettingsCheckboxRowHeight);
            DrawSettingsRowLabel(row, "DelayedZoneOverflowEject".Translate(),
                underlineNonDefault: DelayedZoneEjectHours != DefaultDelayedZoneEjectHours);

            float controlY = row.y + (row.height - controlSize) / 2f;
            float x = row.xMax - controlSize;
            Rect crossRect = new Rect(x, controlY, controlSize, controlSize);
            TooltipHandler.TipRegion(crossRect, "DelayedZoneOverflowEjectDisabledTooltip".Translate());
            if (Widgets.ButtonImage(crossRect, Widgets.CheckboxOffTex))
            {
                SetDelayedZoneEjectHours(0);
            }

            x -= digitGap + controlSize;
            DrawDelayedZoneEjectDigit(new Rect(x, controlY, controlSize, controlSize), 12);
            x -= digitGap + controlSize;
            DrawDelayedZoneEjectDigit(new Rect(x, controlY, controlSize, controlSize), 6);
            x -= digitGap + controlSize;
            DrawDelayedZoneEjectDigit(new Rect(x, controlY, controlSize, controlSize), 1);

            listing.Gap(SettingsCheckboxRowGap);
        }

        /// <summary>
        /// Draws one selectable hour preset for delayed overflow ejection.
        /// </summary>
        private void DrawDelayedZoneEjectDigit(Rect rect, int hours)
        {
            TooltipHandler.TipRegion(rect, "DelayedZoneOverflowEjectModeTooltip".Translate(hours.Named("HOURS")));
            if (Mouse.IsOver(rect))
            {
                Widgets.DrawHighlight(rect);
            }

            if (Widgets.ButtonInvisible(rect))
            {
                SetDelayedZoneEjectHours(hours);
            }

            bool active = DelayedZoneEjectHours == hours;
            if (active)
            {
                DrawInnerBoxBorder(rect, 2f, ModeDigitActiveColor);
            }

            Color digitColor = DelayedZoneEjectHours <= 0
                ? ModeDigitDisabledColor
                : active ? ModeDigitActiveColor : ModeDigitInactiveColor;

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;
            Text.Font = GameFont.Medium;
            GUIStyle labelStyle = new GUIStyle(Text.CurFontStyle)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold
            };
            GUI.color = digitColor;
            GUI.Label(rect, hours.ToString(), labelStyle);
            GUI.color = previousColor;
            Text.Anchor = previousAnchor;
            Text.Font = previousFont;
        }

        /// <summary>
        /// Draws an inset border around a settings control to mark the active selection.
        /// </summary>
        private static void DrawInnerBoxBorder(Rect rect, float thickness, Color color)
        {
            if (Event.current.type != EventType.Repaint)
            {
                return;
            }

            Color previousColor = GUI.color;
            GUI.color = color;
            Texture2D tex = BaseContent.WhiteTex;
            GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, thickness), tex);
            GUI.DrawTexture(new Rect(rect.x, rect.yMax - thickness, rect.width, thickness), tex);
            float innerHeight = rect.height - thickness * 2f;
            GUI.DrawTexture(new Rect(rect.x, rect.y + thickness, thickness, innerHeight), tex);
            GUI.DrawTexture(new Rect(rect.xMax - thickness, rect.y + thickness, thickness, innerHeight), tex);
            GUI.color = previousColor;
        }

        /// <summary>
        /// Draws the multistack mode selector with a label and off / global / per-storage digit buttons.
        /// </summary>
        private void DrawMultistackModeRow(Listing_Standard listing)
        {
            const float controlSize = 24f;
            const float digitGap = 4f;
            const int controlCount = 3;
            float controlsWidth = controlCount * controlSize + (controlCount - 1) * digitGap;

            Rect row = listing.GetRect(SettingsCheckboxRowHeight);
            Rect labelRect = row;
            labelRect.width = Mathf.Max(0f, row.width - controlsWidth);

            string multistackTooltip = "MultistackInfo".Translate();
            if (!multistackTooltip.NullOrEmpty())
            {
                if (Mouse.IsOver(labelRect))
                {
                    Widgets.DrawHighlight(labelRect);
                }

                TooltipHandler.TipRegion(labelRect, multistackTooltip);
            }

            DrawSettingsRowLabel(labelRect, "Multistack".Translate(),
                underlineNonDefault: GlobalMultistackMode != 0);

            float controlY = row.y + (row.height - controlSize) / 2f;
            float x = row.xMax - controlSize;
            Rect crossRect = new Rect(x, controlY, controlSize, controlSize);
            TooltipHandler.TipRegion(crossRect, "MultistackModeDisabledTooltip".Translate());
            if (Widgets.ButtonImage(crossRect, Widgets.CheckboxOffTex))
            {
                SetGlobalMultistackMode(0);
            }

            x -= digitGap + controlSize;
            DrawMultistackModeDigit(new Rect(x, controlY, controlSize, controlSize), 2);
            x -= digitGap + controlSize;
            DrawMultistackModeDigit(new Rect(x, controlY, controlSize, controlSize), 1);

            listing.Gap(SettingsCheckboxRowGap);
        }

        /// <summary>
        /// Draws one selectable digit button for a multistack mode preset.
        /// </summary>
        private void DrawMultistackModeDigit(Rect rect, int mode)
        {
            string tooltipKey = mode == 1
                ? "MultistackModeEnabledGlobalTooltip"
                : "MultistackModeEnabledPerStorageTooltip";
            TooltipHandler.TipRegion(rect, tooltipKey.Translate());
            if (Mouse.IsOver(rect))
            {
                Widgets.DrawHighlight(rect);
            }

            if (Widgets.ButtonInvisible(rect))
            {
                SetGlobalMultistackMode(mode);
            }

            bool active = GlobalMultistackMode == mode;
            if (active)
            {
                DrawInnerBoxBorder(rect, 2f, ModeDigitActiveColor);
            }

            Color digitColor = GlobalMultistackMode == 0
                ? ModeDigitDisabledColor
                : active ? ModeDigitActiveColor : ModeDigitInactiveColor;

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;
            Text.Font = GameFont.Medium;
            GUIStyle labelStyle = new GUIStyle(Text.CurFontStyle)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold
            };
            GUI.color = digitColor;
            GUI.Label(rect, mode.ToString(), labelStyle);
            GUI.color = previousColor;
            Text.Anchor = previousAnchor;
            Text.Font = previousFont;
        }

        /// <summary>
        /// Renders the full scrollable HSK kebab limits mod settings window.
        /// </summary>
        public void DrawSettings(Rect inRect)
        {
            float viewWidth = inRect.width - SettingsScrollBarWidth;
            float scrollHeight = Mathf.Max(settingsScrollContentHeight, inRect.height);
            Rect viewRect = new Rect(0f, 0f, viewWidth, scrollHeight);
            Widgets.BeginScrollView(inRect, ref settingsScrollPosition, viewRect);

            Listing_Standard listing = new Listing_Standard();
            Color previousColor = GUI.color;
            listing.ColumnWidth = viewWidth;
            listing.Begin(new Rect(0f, 0f, viewWidth, SettingsListingCanvasHeight));
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;

            DrawResetSettingsButton(listing, viewWidth);
            DrawSettingsCheckboxRow(listing, "EnableLogging".Translate(), ref EnableLogging, defaultValue: false);
            DrawSettingsRowSeparator(listing, viewWidth);
            DrawSettingsCheckboxRow(listing, "PercentageMode".Translate(), ref PercentageMode, defaultValue: false);
            DrawSettingsRowSeparator(listing, viewWidth);
            bool similarBefore = GlobalSimilarStackEnabled;
            DrawSettingsCheckboxRow(listing, "EnableSimilarStackLimits".Translate(), ref GlobalSimilarStackEnabled,
                defaultValue: true, "EnableSimilarStackLimitsTooltip".Translate());
            if (!similarBefore && GlobalSimilarStackEnabled)
            {
                // Profiles with SimilarStackCountRaw=-1 become storage-wide again — clamp overrides.
                StockpileProfileStore.ClampAllProfilesToModeCap();
            }

            DrawSettingsRowSeparator(listing, viewWidth);
            bool negativeCacheBefore = EnableNegativeSolveCache;
            DrawSettingsCheckboxRow(listing, "EnableNegativeSolveCache".Translate(), ref EnableNegativeSolveCache,
                defaultValue: true, "EnableNegativeSolveCacheTooltip".Translate());
            if (negativeCacheBefore && !EnableNegativeSolveCache)
            {
                CapacityQueryCache.ClearAllNegative();
            }

            DrawSettingsRowSeparator(listing, viewWidth);
            DrawSettingsCheckboxRow(listing, "EnableHardEjection".Translate(), ref EnableHardEjection,
                defaultValue: true, "EnableHardEjectionTooltip".Translate());
            DrawSettingsRowSeparator(listing, viewWidth);
            DrawZoneWideCascadeEjectRow(listing);
            DrawSettingsRowSeparator(listing, viewWidth);
            DrawDelayedZoneEjectRow(listing);
            DrawSettingsRowSeparator(listing, viewWidth);
            DrawMultistackModeRow(listing);
            DrawSettingsRowSeparator(listing, viewWidth);
            DrawSettingsCheckboxRow(listing, "AddFilterDisplaySettings".Translate(), ref AddFilterDisplaySettings,
                defaultValue: false, "AddFilterDisplaySettingsTooltip".Translate());
            DrawSettingsRowSeparator(listing, viewWidth);
            bool allowAboveBefore = AllowPerItemAboveSlider;
            DrawSettingsCheckboxRow(listing, "AllowPerItemAboveSlider".Translate(), ref AllowPerItemAboveSlider,
                defaultValue: false, "AllowPerItemAboveSliderTooltip".Translate());
            if (allowAboveBefore && !AllowPerItemAboveSlider)
            {
                StockpileProfileStore.ClampAllProfilesToModeCap();
            }

            DrawSettingsRowSeparator(listing, viewWidth);
            DrawDisplayLogFactorRow(listing);
            DrawSettingsRowSeparator(listing, viewWidth);
            DrawMaxStackSliderLimitRow(listing);
            DrawSettingsRowSeparator(listing, viewWidth);
            listing.Gap(15f);

            listing.Label("DisplayLogFactorExample".Translate());
            FloatRange previewRange = DisplayLogExample;
            Rect previewRect = listing.GetRect(28f);
            Widgets.FloatRange(previewRect, 182356235, ref previewRange, 0f, 1f,
                StockpileFilterSliderRow.FormatLimitLabel(previewRange.min, previewRange.max), ToStringStyle.Integer, 0f,
                GameFont.Tiny);
            LimitHighlight.DrawRangeBarOverlay(previewRect, previewRange);
            DisplayLogExample = previewRange;
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.UpperCenter;
            GUI.color = Color.grey;
            listing.Label("DisplayLogFactorExampleNote".Translate());
            GUI.color = previousColor;
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;

            listing.Gap(PreviewToHighlightColorGap);
            DrawModifiedHighlightColorHeader(listing);
            Rect gradientRect = listing.GetRect(24f);
            LimitHighlight.DrawGradient(gradientRect);
            HighlightColorPosition = Widgets.HorizontalSlider(gradientRect, HighlightColorPosition, 0f, 1f, false,
                null, null, null, -1f);
            if (Event.current.type == EventType.Repaint)
            {
                LimitHighlight.DrawGradient(gradientRect);
                float thumbX = gradientRect.x + gradientRect.width * HighlightColorPosition;
                Rect thumb = new Rect(thumbX - 3f, gradientRect.y - 2f, 6f, gradientRect.height + 4f);
                GUI.color = HighlightColor;
                GUI.DrawTexture(thumb, BaseContent.WhiteTex);
                GUI.color = Color.black;
                Widgets.DrawBox(thumb, 1);
                GUI.color = previousColor;
            }

            listing.Gap(24f);
            if (Widgets.ButtonText(listing.GetRect(28f), "PressIfStackSizeChangedRuntime".Translate()))
            {
                LogarithmicStackScale.InvalidateAndRefresh();
            }

            listing.End();
            Widgets.EndScrollView();

            float measured = listing.CurHeight + SettingsCheckboxRowGap;
            if (measured > 1f)
            {
                settingsScrollContentHeight = Mathf.Max(measured, inRect.height);
            }
        }

        /// <summary>
        /// Persists mod settings to and from save data and the config file.
        /// </summary>
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref EnableHardEjection, "EnableHardEjection", defaultValue: true, forceSave: true);
            Scribe_Values.Look(ref DisplayLogFactor, "DisplayLogFactor", DefaultDisplayLogFactor, forceSave: true);
            Scribe_Values.Look(ref GlobalMultistackMode, "GlobalMultistackMode", 0, forceSave: true);
            Scribe_Values.Look(ref PercentageMode, "PercentageMode", defaultValue: false, forceSave: true);
            Scribe_Values.Look(ref GlobalSimilarStackEnabled, "GlobalSimilarStackEnabled", defaultValue: true,
                forceSave: true);
            Scribe_Values.Look(ref AllowPerItemAboveSlider, "AllowPerItemAboveSlider", defaultValue: false,
                forceSave: true);
            Scribe_Values.Look(ref AddFilterDisplaySettings, "AddFilterDisplaySettings", defaultValue: false,
                forceSave: true);
            Scribe_Values.Look(ref EnableLogging, "EnableLogging", defaultValue: false);
            Scribe_Values.Look(ref EnableNegativeSolveCache, "EnableNegativeSolveCache", defaultValue: true);
            Scribe_Values.Look(ref ZoneWideCascadeEjectMode, "ZoneWideCascadeEjectMode", 0);
            Scribe_Values.Look(ref DelayedZoneEjectHours, "DelayedZoneEjectHours", DefaultDelayedZoneEjectHours);
            Scribe_Values.Look(ref MaxStackSliderLimit, "MaxStackSliderLimit", DefaultMaxStackSliderLimit);
            Scribe_Values.Look(ref HighlightColorPosition, "HighlightColorPosition", DefaultHighlightColorPosition);
        }
    }
}
