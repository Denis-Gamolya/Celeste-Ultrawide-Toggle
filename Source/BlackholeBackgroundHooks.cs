using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Xna.Framework;
using Monocle;
using MonoMod.RuntimeDetour;

namespace Celeste.Mod.UltrawideToggle;

/// <summary>
/// Gives Farewell's private background target the same width as the gameplay
/// surface, without stretching its artwork or changing update-time animation.
/// </summary>
internal sealed class BlackholeBackgroundHooks : IDisposable
{
    private const BindingFlags All = BindingFlags.Instance | BindingFlags.Public |
        BindingFlags.NonPublic;
    private static readonly FieldInfo Buffer = typeof(BlackholeBG).GetField("buffer", All)!;
    private static readonly FieldInfo Center = typeof(BlackholeBG).GetField("center", All)!;
    private readonly RenderController controller;
    private readonly Func<VirtualRenderTarget?, int, VirtualRenderTarget> allocate;
    private readonly Action<VirtualRenderTarget> release;
    private readonly Hook hook;
    private readonly ConditionalWeakTable<BlackholeBG, object> backgrounds = new();

    internal BlackholeBackgroundHooks(RenderController controller,
        Func<VirtualRenderTarget?, int, VirtualRenderTarget>? allocate = null,
        Action<VirtualRenderTarget>? release = null)
    {
        this.controller = controller;
        this.allocate = allocate ?? ((previous, width) => VirtualContent.CreateRenderTarget(
            "Black Hole", width, 180, previous?.Depth ?? false,
            previous?.Preserve ?? true, previous?.MultiSampleCount ?? 0));
        this.release = release ?? (target => target.Dispose());
        hook = new Hook(typeof(BlackholeBG).GetMethod("BeforeRender", All)!,
            (Action<Action<BlackholeBG, Scene>, BlackholeBG, Scene>)RenderToBuffer);
    }

    internal void RenderToBuffer(Action<BlackholeBG, Scene> original,
        BlackholeBG background, Scene scene)
    {
        backgrounds.GetValue(background, _ => new object());
        EnsureBuffer(background, controller.BufferWidth);

        Vector2 center = (Vector2)Center.GetValue(background)!;
        try
        {
            Center.SetValue(background, center +
                new Vector2((float)controller.Geometry.SideExtension, 0));
            original(background, scene);
        }
        finally { Center.SetValue(background, center); }
    }

    private void EnsureBuffer(BlackholeBG background, int width)
    {
        var previous = (VirtualRenderTarget?)Buffer.GetValue(background);
        if (previous is null || previous.IsDisposed ||
            previous.Width != width || previous.Height != 180)
        {
            // Allocate before swapping so a failed resize leaves the current
            // target owned by the backdrop. Native Ended still disposes it.
            var next = allocate(previous, width);
            Buffer.SetValue(background, next);
            if (previous is not null) release(previous);
        }

    }

    public void Dispose()
    {
        hook.Dispose();
        foreach (var entry in backgrounds)
        {
            var target = (VirtualRenderTarget?)Buffer.GetValue(entry.Key);
            if (target is not null && !target.IsDisposed && target.Width != 320)
                EnsureBuffer(entry.Key, 320);
        }
        backgrounds.Clear();
    }
}