using Microsoft.Xna.Framework;

namespace Celeste.Mod.UltrawideToggle;

public enum BoundaryOutlinePreset { Off, Black, White, Auto }
public enum BoundaryStylePreset { Solid, Dashed, CornersOnly }

internal static class BoundaryAppearance
{
    internal static readonly int[] Thicknesses = [1, 2, 3, 4, 6, 8, 12];
    internal static readonly BoundaryColorPreset[] Colors =
        BoundaryColors.Choices.Where(color => color != BoundaryColorPreset.Off).ToArray();

    internal static int NormalizeThickness(int value)
        => Thicknesses.MinBy(choice => Math.Abs((long)value - choice));

    internal static Color OutlineColor(BoundaryOutlinePreset preset, Color border)
        => preset switch
        {
            BoundaryOutlinePreset.Black => Color.Black,
            BoundaryOutlinePreset.White => Color.White,
            BoundaryOutlinePreset.Auto =>
                border.R * 299 + border.G * 587 + border.B * 114 >= 128000
                    ? Color.Black : Color.White,
            _ => Color.Transparent
        };
}
