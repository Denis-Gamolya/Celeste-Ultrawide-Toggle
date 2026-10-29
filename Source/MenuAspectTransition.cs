namespace Celeste.Mod.UltrawideToggle;

/// <summary>
/// Eases presentation width while the native exit scene shows its dark
/// background, particles, and saving UI. It never changes gameplay geometry.
/// </summary>
internal sealed class MenuAspectTransition
{
    internal const double Duration = .35;
    private double from = 320;
    private double to = 320;
    private double elapsed = Duration;
    internal bool Complete => elapsed >= Duration;
    internal ViewGeometry Geometry
    {
        get
        {
            double progress = Math.Clamp(elapsed / Duration, 0, 1);
            double eased = progress * progress * (3 - 2 * progress);
            return new(from + (to - from) * eased);
        }
    }

    internal void Begin(ViewGeometry source, ViewGeometry destination)
    {
        from = source.Width;
        to = destination.Width;
        elapsed = Math.Abs(from - to) < .001 ? Duration : 0;
    }

    internal void Advance(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds < 0)
            throw new ArgumentOutOfRangeException(nameof(seconds));
        elapsed = Math.Min(Duration, elapsed + seconds);
    }
}
