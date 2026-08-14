using System.Reflection;
using UnityEngine;
using Verse;

namespace HSKKebabLimits
{
    /// <summary>
    /// Draws colored overlays on storage limit sliders to show custom min/max ranges in the UI.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class LimitHighlight
    {
        private static Texture2D sliderHandleTex;
        private static bool sliderHandleResolved;

        private static Texture2D SliderHandleTex
        {
            get
            {
                if (!sliderHandleResolved)
                {
                    sliderHandleResolved = true;
                    FieldInfo handleField = typeof(Widgets).GetField("FloatRangeSliderTex",
                        BindingFlags.NonPublic | BindingFlags.Static);
                    sliderHandleTex = handleField?.GetValue(null) as Texture2D;
                }

                return sliderHandleTex;
            }
        }

        public static readonly Color[] PresetColors =
        {
            new Color(1f, 1f, 0.2f),
            new Color(1f, 0.55f, 0.1f),
            new Color(0.35f, 1f, 0.45f),
            new Color(0.45f, 0.75f, 1f),
            new Color(1f, 0.45f, 0.75f),
            new Color(0.9f, 0.9f, 0.9f)
        };

        /// <summary>
        /// Returns whether the limit range differs from the full 0–100% stockpile default.
        /// </summary>
        public static bool IsCustomLimit(FloatRange range)
        {
            return range.min > 0.001f || range.max < 0.999f;
        }

        /// <summary>
        /// Interpolates a highlight color along the configurable preset gradient.
        /// </summary>
        public static Color SampleColor(float position)
        {
            position = Mathf.Clamp01(position);
            if (PresetColors.Length <= 1)
            {
                return PresetColors[0];
            }

            float scaled = position * (PresetColors.Length - 1);
            int left = Mathf.FloorToInt(scaled);
            int right = Mathf.Min(left + 1, PresetColors.Length - 1);
            return Color.Lerp(PresetColors[left], PresetColors[right], scaled - left);
        }

        /// <summary>
        /// Returns the user-selected highlight color from mod settings.
        /// </summary>
        public static Color GetColor()
        {
            return SampleColor(KebabLimitsModSettings.HighlightColorPosition);
        }

        /// <summary>
        /// Returns a dimmed variant of the highlight color for slider track backgrounds.
        /// </summary>
        public static Color GetDarkColor()
        {
            Color c = GetColor();
            return new Color(c.r * 0.5f, c.g * 0.5f, c.b * 0.5f, c.a);
        }

        /// <summary>
        /// Paints a horizontal color gradient preview in the mod settings window.
        /// </summary>
        public static void DrawGradient(Rect rect)
        {
            if (Event.current.type != EventType.Repaint)
            {
                return;
            }

            const float step = 2f;
            Color previous = GUI.color;
            for (float x = rect.xMin; x < rect.xMax; x += step)
            {
                float t = Mathf.InverseLerp(rect.xMin, rect.xMax, x);
                GUI.color = SampleColor(t);
                GUI.DrawTexture(new Rect(x, rect.yMin, Mathf.Min(step, rect.xMax - x), rect.height),
                    BaseContent.WhiteTex);
            }

            GUI.color = previous;
        }

        /// <summary>
        /// Detects vanilla yellow slider label text so custom limit colors can replace it.
        /// </summary>
        public static bool IsVanillaModifiedTextColor(Color color)
        {
            return color.r > 0.75f && color.g > 0.75f && color.b < 0.35f;
        }

        /// <summary>
        /// Draws a colored bar and handles over a FloatRange slider for active limit bounds.
        /// </summary>
        public static void DrawRangeBarOverlay(Rect rect, FloatRange range, float min = 0f, float max = 1f)
        {
            if (!IsCustomLimit(range) || Event.current.type != EventType.Repaint)
            {
                return;
            }

            Rect inner = rect;
            inner.xMin += 8f;
            inner.xMax -= 8f;

            float left = inner.x + inner.width * Mathf.InverseLerp(min, max, range.min);
            float right = inner.x + inner.width * Mathf.InverseLerp(min, max, range.max);

            Color previous = GUI.color;

            GUI.color = GetDarkColor();
            GUI.DrawTexture(new Rect(inner.x, inner.yMax - 9f, inner.width, 2f), BaseContent.WhiteTex);

            GUI.color = GetColor();
            GUI.DrawTexture(new Rect(left, inner.yMax - 10f, right - left, 4f), BaseContent.WhiteTex);

            Texture2D handle = SliderHandleTex;
            if (handle != null)
            {
                GUI.DrawTexture(new Rect(left - 16f, inner.yMax - 16f, 16f, 16f), handle);
                GUI.DrawTexture(new Rect(right + 16f, inner.yMax - 16f, -16f, 16f), handle);
            }

            GUI.color = previous;
        }
    }
}
