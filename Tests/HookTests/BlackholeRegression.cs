using System.Reflection;
using System.Runtime.CompilerServices;
using Celeste;
using Celeste.Mod.UltrawideToggle;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Monocle;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;

internal static class BlackholeRegression
{
    private const BindingFlags All = BindingFlags.Instance | BindingFlags.Public |
        BindingFlags.NonPublic | BindingFlags.Static;
    private static readonly FieldInfo Buffer = typeof(BlackholeBG).GetField("buffer", All)!;
    private static readonly FieldInfo Center = typeof(BlackholeBG).GetField("center", All)!;
    private static int assertions;

    internal static void Run()
    {
        assertions = 0;
        int allocations = 0;
        bool failAllocation = false;
        var released = new HashSet<VirtualRenderTarget>();
        using var disposedHook = new Hook(typeof(VirtualRenderTarget).GetProperty("IsDisposed")!.GetMethod!,
            (Func<Func<VirtualRenderTarget, bool>, VirtualRenderTarget, bool>)
                ((_, target) => released.Contains(target)));
        using var boundsHook = new Hook(typeof(VirtualRenderTarget).GetProperty("Bounds")!.GetMethod!,
            (Func<Func<VirtualRenderTarget, Rectangle>, VirtualRenderTarget, Rectangle>)
                ((_, target) => new(0, 0, target.Width, target.Height)));
        Vector4 bounds = default;
        Vector2 drawScale = default;
        using var drawHook = new ILHook(typeof(BlackholeBG).GetMethod("Render")!, il =>
        {
            var cursor = new ILCursor(il);
            if (!cursor.TryGotoNext(MoveType.Before, instruction =>
                instruction.MatchCallvirt<SpriteBatch>("Draw")))
                throw new InvalidOperationException("Native black-hole draw site changed");
            cursor.Remove();
            cursor.EmitDelegate<Action<SpriteBatch, Texture2D, Vector2, Rectangle?, Color,
                float, Vector2, float, SpriteEffects, float>>(
                (_, _, position, source, _, _, origin, scale, _, _) =>
                {
                    bounds = new(position.X - origin.X * scale,
                        position.Y - origin.Y * scale,
                        source!.Value.Width * scale, source.Value.Height * scale);
                    drawScale = new(scale, scale);
                });
        });
        var controller = new RenderController();
        using var adapter = new BlackholeBackgroundHooks(controller, (_, width) =>
        {
            if (failAllocation) throw new IOException("Simulated allocation failure");
            allocations++;
            return FakeTarget(width);
        }, target =>
        {
            Check(released.Add(target), "Live background target must be released exactly once");
        });
        var background = New<BlackholeBG>();
        background.Scale = 1;
        background.Alpha = 1;
        background.FadeAlphaMultiplier = 1;
        Vector2 originalCenter = new(163, 95);
        Center.SetValue(background, originalCenter);
        foreach (double width in new[] { 320d, 430d, 1280d / 3, 640d, 960d, 320d, 430d })
        {
            var geometry = new ViewGeometry(width);
            typeof(RenderController).GetProperty("Geometry")!.SetValue(controller, geometry);
            adapter.RenderToBuffer((_, _) =>
            {
                Check((Vector2)Center.GetValue(background)! == originalCenter +
                    new Vector2((float)geometry.SideExtension, 0),
                    "Render center must move by the exact side extension, including fractional aspects");
                var target = (VirtualRenderTarget)Buffer.GetValue(background)!;
                Check(target.Width == geometry.BufferWidth && target.Height == 180,
                    "Private target must cover the gameplay buffer at unchanged height");
            }, background, null!);
            Check((Vector2)Center.GetValue(background)! == originalCenter,
                "Drawing must not leak its shifted center into the next animation update");
            background.Render(null!);
            Check(bounds == new Vector4(0, 0, geometry.BufferWidth, 180),
                "Actual native background composition must cover both physical edges");
            Check(drawScale == Vector2.One, "Artwork must retain uniform native scale");
            int before = allocations;
            adapter.RenderToBuffer((_, _) => { }, background, null!);
            Check(allocations == before, "Unchanged aspect must reuse the private target");
        }
        var previous = (VirtualRenderTarget)Buffer.GetValue(background)!;
        typeof(RenderController).GetProperty("Geometry")!.SetValue(controller, new ViewGeometry(640));
        failAllocation = true;
        try
        {
            adapter.RenderToBuffer((_, _) => throw new Exception("Unexpected drawing"), background, null!);
            throw new Exception("Expected allocation failure");
        }
        catch (IOException)
        {
            Check(ReferenceEquals(Buffer.GetValue(background), previous) && !released.Contains(previous),
                "Failed allocation must retain the existing target and ownership");
            Check((Vector2)Center.GetValue(background)! == originalCenter,
                "Failed allocation must leave animation state unchanged");
        }
        failAllocation = false;
        try
        {
            adapter.RenderToBuffer((_, _) => throw new IOException("Simulated render failure"), background, null!);
            throw new Exception("Expected render failure");
        }
        catch (IOException)
        {
            Check((Vector2)Center.GetValue(background)! == originalCenter,
                "Failed drawing must restore the center in finally");
        }
        var ended = New<BlackholeBG>();
        Center.SetValue(ended, Vector2.Zero);
        adapter.RenderToBuffer((_, _) => { }, ended, null!);
        var endedTarget = (VirtualRenderTarget)Buffer.GetValue(ended)!;
        released.Add(endedTarget);
        int countBeforeDispose = allocations;
        adapter.Dispose();
        Check(((VirtualRenderTarget)Buffer.GetValue(background)!).Width == 320,
            "Hook unload must restore a still-live background target to native width");
        Check(ReferenceEquals(Buffer.GetValue(ended), endedTarget) && allocations == countBeforeDispose + 1,
            "Hook unload must not resurrect a backdrop already disposed by its scene");
        Console.WriteLine($"PASS: {assertions} black-hole target, centering, composition, failure, and unload assertions");
    }

    private static T New<T>() where T : class
        => (T)RuntimeHelpers.GetUninitializedObject(typeof(T));

    private static VirtualRenderTarget FakeTarget(int width)
    {
        var target = New<VirtualRenderTarget>();
        foreach (var (name, value) in new[] { ("Width", width), ("Height", 180) })
            typeof(VirtualAsset).GetField($"<{name}>k__BackingField", All)!.SetValue(target, value);
        return target;
    }

    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
        assertions++;
    }
}