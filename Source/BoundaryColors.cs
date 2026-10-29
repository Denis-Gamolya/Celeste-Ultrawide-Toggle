using Microsoft.Xna.Framework;

namespace Celeste.Mod.UltrawideToggle;

public enum BoundaryColorPreset
{
    Off,
    Yellow,
    White,
    Black,
    Cyan,
    Magenta
}

internal static class BoundaryColors
{
    internal static readonly BoundaryColorPreset[] Choices =
        Enum.GetValues<BoundaryColorPreset>();

    internal static Color Resolve(BoundaryColorPreset preset) => preset switch
    {
        BoundaryColorPreset.Yellow => Color.Yellow,
        BoundaryColorPreset.White => Color.White,
        BoundaryColorPreset.Black => Color.Black,
        BoundaryColorPreset.Cyan => Color.Cyan,
        BoundaryColorPreset.Magenta => Color.Magenta,
        _ => Color.Transparent
    };
}
