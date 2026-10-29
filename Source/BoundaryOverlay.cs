using Microsoft.Xna.Framework;

namespace Celeste.Mod.UltrawideToggle;

/// <summary>Builds border rectangles in the game's 1080-high HUD coordinates.</summary>
internal static class BoundaryOverlay
{
    private const float Height = 1080;
    private const float ReferenceWidth = 1920;
    private const float DashLength = 24;
    private const float DashPeriod = 40;
    private const float CornerLength = 72;

    internal static void Draw(ViewGeometry geometry, UltrawideSettings settings,
        Action<float, float, float, float, Color> draw)
    {
        if (!settings.BoundaryEnabled || geometry.Width <= 320) return;
        float left = MathF.Floor((float)geometry.SideExtension * 6);
        float right = left + ReferenceWidth;
        if (settings.BoundaryDim > 0)
        {
            Color dim = Color.Black * (settings.BoundaryDim / 100f);
            draw(0, 0, left, Height, dim);
            draw(right, 0, left, Height, dim);
        }

        Color border = BoundaryColors.Resolve(settings.BoundaryColor);
        Color outline = BoundaryAppearance.OutlineColor(settings.BoundaryOutline, border);
        float opacity = settings.BoundaryOpacity / 100f;
        bool outlined = outline.A != 0;
        float thickness = settings.BoundaryThickness;

        float total = thickness + (outlined ? 2 : 0);
        float outerLeft = left - total;
        float outerWidth = ReferenceWidth + 2 * total;

        // Nested rectangular rings share one outer frame. Vertical bands own
        // the corner pixels; horizontal bands meet their inner edges exactly.
        // This keeps outline/color ordering consistent around every corner.
        if (outlined)
        {
            Bands(0, 1, outline * opacity);
            Bands(1, thickness, border * opacity);
            Bands(1 + thickness, 1, outline * opacity);
        }
        else Bands(0, thickness, border * opacity);

        void Bands(float inset, float width, Color color)
        {
            Segments(Height, inset, Height - inset, (start, length) =>
            {
                draw(outerLeft + inset, start, width, length, color);
                draw(right + total - inset - width, start, width, length, color);
            });
            Segments(outerWidth, inset + width, outerWidth - inset - width,
                (start, length) =>
                {
                    draw(outerLeft + start, inset, length, width, color);
                    draw(outerLeft + start, Height - inset - width, length, width, color);
                });
        }

        void Segments(float length, float clipStart, float clipEnd,
            Action<float, float> segment)
        {
            if (settings.BoundaryStyle == BoundaryStylePreset.Solid)
                Emit(0, length);
            else if (settings.BoundaryStyle == BoundaryStylePreset.CornersOnly)
            {
                float arm = Math.Min(CornerLength, length / 2);
                Emit(0, arm);
                Emit(length - arm, length);
            }
            else
            {
                // Anchor a complete dash to both ends, with evenly spaced,
                // integer-aligned gaps. Every corner has matching joined arms.
                int count = Math.Max(2, (int)MathF.Round(
                    (length + DashPeriod - DashLength) / DashPeriod));
                for (int index = 0; index < count; index++)
                {
                    float start = MathF.Round(index * (length - DashLength) / (count - 1));
                    Emit(start, start + DashLength);
                }
            }

            void Emit(float start, float end)
            {
                start = Math.Max(start, clipStart);
                end = Math.Min(end, clipEnd);
                if (end > start) segment(start, end - start);
            }
        }
    }
}