using UnityEngine;
using Verse;

namespace HSKKebabLimits
{
    /// <summary>
    /// Draws a single-line label. Overflow slides only while the pointer is over that label's hover rect.
    ///
    /// Рисует однострочную подпись. Сдвиг при переполнении только пока курсор над hover-прямоугольником этой подписи.
    /// </summary>
    internal static class OverflowMarqueeLabel
    {
        private const float PixelsPerSecond = 36f;
        private const float EndPauseSeconds = 0.85f;

        private static string hoverKey;
        private static float hoverStartedAt;

        /// <summary>
        /// Draws text clipped to rect. Fits statically; overflow ping-pongs only during hover on hoverRect.
        ///
        /// Рисует текст с обрезкой по прямоугольнику. Если влезает — статично; иначе ping-pong только при наведении на hoverRect.
        /// </summary>
        public static void Draw(Rect rect, string text, Rect hoverRect)
        {
            if (text.NullOrEmpty() || rect.width <= 0f || rect.height <= 0f)
            {
                return;
            }

            bool wordWrap = Text.WordWrap;
            TextAnchor anchor = Text.Anchor;
            Text.WordWrap = false;
            Text.Anchor = TextAnchor.MiddleLeft;
            try
            {
                float textWidth = Text.CalcSize(text).x;
                string key = rect.y.ToString("F1") + "@" + rect.x.ToString("F1") + "|" + text;
                if (textWidth <= rect.width + 0.5f)
                {
                    Widgets.Label(rect, text);
                    ClearHoverIfCurrent(key);
                    return;
                }

                bool hovering = Mouse.IsOver(hoverRect);
                if (hovering)
                {
                    if (hoverKey != key)
                    {
                        hoverKey = key;
                        hoverStartedAt = Time.realtimeSinceStartup;
                    }
                }
                else
                {
                    ClearHoverIfCurrent(key);
                }

                float shift = hovering
                    ? HorzShift(textWidth - rect.width, Time.realtimeSinceStartup - hoverStartedAt)
                    : 0f;

                Widgets.BeginGroup(rect);
                Widgets.Label(new Rect(-shift, 0f, textWidth, rect.height), text);
                Widgets.EndGroup();
            }
            finally
            {
                Text.Anchor = anchor;
                Text.WordWrap = wordWrap;
            }
        }

        private static void ClearHoverIfCurrent(string key)
        {
            if (hoverKey == key)
            {
                hoverKey = null;
            }
        }

        /// <summary>
        /// Horizontal ping-pong offset after hoverElapsed seconds.
        ///
        /// Горизонтальный ping-pong сдвиг спустя hoverElapsed секунд.
        /// </summary>
        private static float HorzShift(float overflow, float elapsed)
        {
            float travelTime = Mathf.Max(0.2f, overflow / PixelsPerSecond);
            float halfCycle = EndPauseSeconds + travelTime;
            float t = elapsed % (halfCycle * 2f);
            if (t < EndPauseSeconds)
            {
                return 0f;
            }

            if (t < halfCycle)
            {
                return Mathf.Lerp(0f, overflow, (t - EndPauseSeconds) / travelTime);
            }

            if (t < halfCycle + EndPauseSeconds)
            {
                return overflow;
            }

            return Mathf.Lerp(overflow, 0f, (t - halfCycle - EndPauseSeconds) / travelTime);
        }
    }
}
