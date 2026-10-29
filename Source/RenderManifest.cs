namespace Celeste.Mod.UltrawideToggle;

// Explicit render-only allowlist, audited against Celeste 1.4.0.0.
internal static class RenderManifest
{
    public static readonly RenderPatch[] Patches =
    [
        new("Celeste.FallEffects", "Render", [new(320f, 1)]),
        new("Celeste.BackgroundFadeIn", "Render", [new(340f, 1)]),
        new("Celeste.StardustFG", "Render", [new(320f, 1)]),
        new("Celeste.LightningRenderer", "OnRenderBloom", [new(320f, 1)]),
        new("Celeste.HeatWave", "Render", [new(320f, 1)]),
        new("Celeste.CoreStarsFG", "Render", [new(320f, 1)]),
        new("Celeste.ReflectionFG", "Render", [new(320f, 1)]),
        new("Celeste.MirrorFG", "Render", [new(320f, 1)]),
        new("Celeste.KeyDoorWipe", "Render", [new(960f, 1), new(1920f, 1)]),
        new("Celeste.FallWipe", "Render", [new(960f, 1)]),
        new("Celeste.MountainWipe", "Render", [new(960f, 1)]),
        new("Celeste.LightingRenderer", "BeforeRender", [new(320f, 1)]),
        new("Celeste.StarfieldWipe", "Render", [new(1922f, 1)]),
        new("Celeste.TalkComponent+TalkComponentUI", "Render", [new(320f, 1)]),
        new("Celeste.ForegroundDebris", "Render", [new(320f, 1)]),
        new("Celeste.DreamStars", "Render", [new(320f, 1)]),
        new("Celeste.StarsBG", "Render", [new(320f, 1)]),
        new("Celeste.BirdTutorialGui", "Render", [new(320f, 1)]),
        new("Celeste.DustEdges", "BeforeRender", [new(320f, 2)]),
        new("Celeste.SpotlightWipe", "Render", [new(320f, 1), new(1920f, 2)]),
        new("Celeste.ExitBlock", "Render", [new(320f, 1)]),
        new("Celeste.FakeWall", "Render", [new(320f, 1)]),
        new("Celeste.BloomRenderer", "Apply", [new(340f, 1)]),
        new("Celeste.BackdropRenderer", "Render", [new(340f, 1)]),
        new("Celeste.Snow", "Render", [new(320f, 1)]),
        new("Celeste.Parallax", "Render", [new(320f, 1)]),
        new("Celeste.Level", "Render", [new(320f, 3), new(322f, 1)]),
    ];
}
