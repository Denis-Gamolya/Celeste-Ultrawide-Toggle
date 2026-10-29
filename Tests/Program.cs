using Celeste.Mod.UltrawideToggle;

int checks = 0;
void Equal(double actual, double expected, string label)
{
    if (Math.Abs(actual - expected) > 1e-8)
        throw new Exception($"{label}: expected {expected}, got {actual}");
    checks++;
}

var reference = ViewGeometry.FromAspect(16d / 9);
Equal(reference.Width, 320, "16:9 width");
Equal(reference.SideExtension, 0, "16:9 extension");
var main = ViewGeometry.FromAspect(3440d / 1440);
Equal(main.Width, 430, "3440 width");
Equal(main.SideExtension, 55, "centered extension");
Equal(main.BufferWidth, 430, "integer target");
Equal(main.HiresWidth, 2580, "hires coverage");
var fractional = ViewGeometry.FromAspect(2560d / 1080);
Equal(fractional.Width, 1280d / 3, "fractional coverage");
Equal(fractional.BufferWidth, 427, "fractional guard column");
Equal(fractional.HiresWidth, 2560, "exact fractional composition");
Equal(ViewGeometry.FromAspect(3840d / 1600).Width, 432, "3840 preset");
Equal(ViewGeometry.FromAspect(32d / 9).Width, 640, "32:9 preset");
Equal(ViewGeometry.FromAspect(48d / 9).Width, 960, "48:9 preset");
Equal(ViewGeometry.FromAspect(4d / 3).Width, 320, "narrow clamp");
Equal(ViewGeometry.FromAspect(20).Width, 960, "allocation limit");
foreach(double invalid in new[] { 0d, -1d, double.NaN, double.PositiveInfinity })
{
    try { ViewGeometry.FromAspect(invalid); throw new Exception("Accepted invalid aspect"); }
    catch (ArgumentOutOfRangeException) { checks++; }
}
Console.WriteLine($"PASS: {checks} viewport geometry assertions");

int previousChecks = checks;
foreach (double display in new[] { 3440d / 1440, 2560d / 1080, 3840d / 1600 })
{
    var adaptive = AspectPresets.Resolve(AspectPreset.Ratio21x9, display);
    Equal(adaptive.Width, display * 180, "21:9 matches display");
    Equal(AspectPresets.Resolve(AspectPreset.Auto, display).Width, adaptive.Width,
        "Auto and adaptive 21:9 agree on common ultrawide displays");
}
Equal(AspectPresets.Resolve(AspectPreset.Ratio21x9, 16d / 9).Width, 420,
    "21:9 literal fallback on 16:9");
Equal(AspectPresets.Resolve(AspectPreset.Ratio21x9, 32d / 9).Width, 420,
    "21:9 literal fallback on 32:9");
Equal(AspectPresets.Resolve(AspectPreset.Original16x9, 32d / 9).Width, 320,
    "menu 16:9 independent of display");
Equal(AspectPresets.Resolve(AspectPreset.Auto, 32d / 9).Width, 640,
    "Auto follows 32:9 display");
foreach (var legacy in new[] { AspectPreset.Resolution3440x1440,
    AspectPreset.Resolution2560x1080, AspectPreset.Resolution3840x1600 })
    Equal((int)AspectPresets.Normalize(legacy), (int)AspectPreset.Ratio21x9,
        "legacy aspect migration");
Console.WriteLine($"PASS: {checks - previousChecks} adaptive-aspect assertions");
