namespace Celeste.Mod.UltrawideToggle;

/// <summary>Viewport math in Celeste's 180-pixel-high gameplay coordinates.</summary>
public readonly record struct ViewGeometry(double Width)
{
    public const int ReferenceWidth = 320;
    public const int ReferenceHeight = 180;
    public int BufferWidth => (int)Math.Ceiling(Width);
    public double SideExtension => (Width - ReferenceWidth) / 2;
    public double HiresWidth => Width * 6;

    /// <summary>Clamp unsupported/narrow aspects; retain fractional coverage.</summary>
    public static ViewGeometry FromAspect(double aspect)
    {
        if (!double.IsFinite(aspect) || aspect <= 0)
            throw new ArgumentOutOfRangeException(nameof(aspect));
        return new(Math.Clamp(ReferenceHeight * aspect, ReferenceWidth, 960));
    }
}

public enum AspectPreset
{
    Auto,
    Resolution3440x1440,
    Resolution2560x1080,
    Resolution3840x1600,
    Ratio32x9,
    Ratio48x9,
    Ratio21x9,
    Original16x9
}

internal static class AspectPresets
{
    internal static readonly AspectPreset[] Menus =
        [AspectPreset.Original16x9, AspectPreset.Auto, AspectPreset.Ratio21x9,
            AspectPreset.Ratio32x9, AspectPreset.Ratio48x9];

    // Retain old enum names so existing YAML can be read, then normalize.
    internal static AspectPreset Normalize(AspectPreset preset) => preset switch
    {
        AspectPreset.Resolution3440x1440 or AspectPreset.Resolution2560x1080 or
            AspectPreset.Resolution3840x1600 => AspectPreset.Ratio21x9,
        _ => preset
    };

    internal static string Label(AspectPreset preset) => Normalize(preset) switch
    {
        AspectPreset.Original16x9 => "16:9",
        AspectPreset.Ratio21x9 => "21:9",
        AspectPreset.Ratio32x9 => "32:9",
        AspectPreset.Ratio48x9 => "48:9",
        _ => "Auto"
    };

    internal static ViewGeometry Resolve(AspectPreset preset, double displayAspect)
    {
        if (!double.IsFinite(displayAspect) || displayAspect <= 0)
            displayAspect = 16d / 9;
        double aspect = Normalize(preset) switch
        {
            AspectPreset.Original16x9 => 16d / 9,
            // Common "21:9" displays range from 64:27 to 12:5. The small
            // tolerance also accommodates rounding in windowed viewports.
            AspectPreset.Ratio21x9 => displayAspect >= 2.3 && displayAspect <= 2.45
                ? displayAspect : 21d / 9,
            AspectPreset.Ratio32x9 => 32d / 9,
            AspectPreset.Ratio48x9 => 48d / 9,
            _ => displayAspect
        };
        return ViewGeometry.FromAspect(aspect);
    }
}
