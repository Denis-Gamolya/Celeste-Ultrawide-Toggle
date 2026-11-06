using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;
using Monocle;

namespace Celeste.Mod.UltrawideToggle;

internal sealed class VanillaEffectHooks : IDisposable
{
    private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic |
        BindingFlags.Instance | BindingFlags.Static;
    private readonly List<ILHook> hooks = new();
    private readonly RenderController controller;
    private readonly ConditionalWeakTable<object, List<FieldInfo>> resizedOwners = new();
    private VertexPositionColor[] bossStars = [];
    private bool resizedWipe;

    internal VanillaEffectHooks(RenderController controller)
    {
        this.controller = controller;
        try
        {
            PatchParticles(typeof(RainFG), 384, scaled: true);
            PatchParticles(typeof(Petals), 352, scaled: true);
            PatchParticles(typeof(Starfield), 448, scaled: false);
            PatchParticles(typeof(WindSnowFG), 640, scaled: true);
            PatchParticles(typeof(Planets), 640, scaled: false);
            PatchVertices<VertexPositionColor>(typeof(Godrays), "Render",
                (matrix, vertices, count, effect, blend) =>
                    RepeatVertices(matrix, vertices, count, effect, blend, 384, Side, 32));
            PatchFloats(typeof(FinalBossStarfield), "Render", 330, 3,
                x => Wide ? Width + 10 : x);
            PatchVertices<VertexPositionColor>(typeof(FinalBossStarfield), "Render", DrawBossStars);

            ResizePrivateTarget(typeof(StarJumpController), "BlockFill");
            PatchVertices<VertexPositionColor>(typeof(StarJumpController), "BeforeRender",
                (matrix, vertices, count, effect, blend) =>
                    RepeatVertices(matrix, vertices, count, effect, blend, 320, Side, 0));
            ResizePrivateTarget(typeof(GlassBlockBg), "starsTarget", "beamsTarget");
            Patch(typeof(GlassBlockBg), "BeforeRender", il =>
            {
                var cursor = new ILCursor(il);
                if (!cursor.TryGotoNext(MoveType.After, i => i.MatchLdcI4(320)))
                    throw new InvalidOperationException("Glass block drawing width site changed");
                cursor.EmitDelegate<Func<int, int>>(_ => controller.BufferWidth);
                int positions = 0;
                while (cursor.TryGotoNext(MoveType.After,
                    i => i.MatchLdflda(typeof(GlassBlockBg).FullName + "/Star", "Position") ||
                        i.MatchLdflda(typeof(GlassBlockBg).FullName + "/Ray", "Position"),
                    i => i.MatchLdfld<Vector2>("X")))
                {
                    cursor.EmitDelegate<Func<float, float>>(x => Wide ? x * Width / 320 : x);
                    positions++;
                }
                if (positions != 2)
                    throw new InvalidOperationException($"Glass block effect position count: {positions}");
            });
            ResizePrivateTarget(typeof(MirrorSurfaces), "target");
            ResizePrivateTarget(typeof(NorthernLights), "buffer");
            Patch(typeof(NorthernLights), "BeforeRender", il =>
            {
                var cursor = new ILCursor(il);
                int layers = 0;
                while (cursor.TryGotoNext(MoveType.After,
                    i => i.MatchCall<Matrix>("get_Identity"),
                    i => i.MatchLdarg(0),
                    i => i.MatchLdfld<NorthernLights>("gradient") || i.MatchLdfld<NorthernLights>("verts")))
                {
                    cursor.Index -= 2;
                    // Other effects find the native DrawVertices calls to
                    // adjust colors and blur. Change only the matrix input.
                    cursor.EmitDelegate<Func<Matrix, Matrix>>(matrix =>
                        Wide ? Matrix.CreateScale(Width / 320, 1, 1) * matrix : matrix);
                    layers++;
                }
                if (layers != 2)
                    throw new InvalidOperationException($"Northern lights layer matrix count: {layers}");

                cursor.Index = 0;
                if (!cursor.TryGotoNext(MoveType.Before, i => i.MatchCall(typeof(Draw), "Rect")))
                    throw new InvalidOperationException("Northern lights particle draw site changed");
                cursor.Remove();
                cursor.EmitDelegate<Action<Vector2, float, float, Color>>((p, w, h, color) =>
                {
                    Draw.Rect(p, w, h, color);
                    if (!Wide) return;
                    for (p.X += 320; p.X < Width; p.X += 320)
                        Draw.Rect(p, w, h, color);
                });
            });

            PatchFloats(typeof(SummitCloud), "get_RenderPosition", 160, 1, x => x + Side);
            PatchFloats(typeof(BigWaterfall), "get_RenderPosition", 160, 1, x => x + Side);
            Patch(typeof(SeekerEffectsController), "Update", il =>
            {
                var cursor = new ILCursor(il);
                if (!cursor.TryGotoNext(MoveType.Before, i => i.MatchCall(typeof(Distort), "set_AnxietyOrigin")))
                    throw new InvalidOperationException("Seeker anxiety origin site changed");
                cursor.EmitDelegate<Func<Vector2, Vector2>>(p =>
                {
                    if (Engine.Scene is Level level)
                    {
                        var geometry = controller.SelectGeometry(level);
                        p.X = (float)((p.X * 320 + geometry.SideExtension) / geometry.Width);
                    }
                    return p;
                });
            });
            PatchFloats(typeof(FormationBackdrop), "Render", 322, 1, x => x + Side * 2);
            Patch(typeof(Overlay), "RenderFade", il =>
            {
                var cursor = new ILCursor(il);
                if (!cursor.TryGotoNext(MoveType.Before, i => i.MatchCall(typeof(Draw), "Rect")))
                    throw new InvalidOperationException("Overlay fade draw site changed");
                cursor.Remove();
                cursor.EmitDelegate<Action<float, float, float, float, Color>>((x, y, w, h, color) =>
                {
                    float extra = Wide ? controller.OutputWidth - 1920 : 0;
                    Draw.Rect(x - extra / 2, y, w + extra, h, color);
                });
            });
            Patch(typeof(StarfieldWipe), "BeforeRender", il =>
            {
                var cursor = new ILCursor(il);
                cursor.EmitDelegate<Action>(() =>
                {
                    int width = (int)Math.Ceiling(controller.OutputWidth) + 2;
                    if (Celeste.WipeTarget is { } old && old.Width != width)
                    {
                        Celeste.WipeTarget = Resize(old, width);
                        resizedWipe = width != 1922;
                    }
                });
                cursor.Index = 0;
                if (!cursor.TryGotoNext(MoveType.After, i => i.MatchLdcR4(1922)))
                    throw new InvalidOperationException("Starfield wipe fill width site changed");
                cursor.EmitDelegate<Func<float, float>>(_ => controller.OutputWidth + 2);
                cursor.Index = 0;
                if (!cursor.TryGotoNext(MoveType.Before, i => i.MatchCall<Matrix>("CreateTranslation") &&
                    i.Previous?.OpCode == OpCodes.Ldc_R4))
                    throw new InvalidOperationException("Starfield wipe star position site changed");
                // Scale the native star positions and travel as a group while
                // leaving the geometry of the individual stars unchanged.
                cursor.EmitDelegate<Func<float, float, float, Vector3>>((x, y, z) =>
                    new Vector3(x * controller.OutputWidth / 1920, y, z));
                cursor.Remove();
                cursor.Emit(OpCodes.Call, typeof(Matrix).GetMethod(nameof(Matrix.CreateTranslation), [typeof(Vector3)])!);
            });
        }
        catch { Dispose(); throw; }
    }

    private bool Wide => controller.RenderingWideLevel;
    private float Width => controller.VisualWidth;
    private float Side => Wide ? (float)controller.Geometry.SideExtension : 0;

    private void PatchParticles(Type type, float period, bool scaled)
    {
        Patch(type, "Render", il =>
        {
            var cursor = new ILCursor(il);
            int matches = 0;
            while (cursor.TryGotoNext(MoveType.Before, i =>
                (i.OpCode == OpCodes.Callvirt || i.OpCode == OpCodes.Call) &&
                i.Operand is MethodReference m && m.DeclaringType.FullName == typeof(MTexture).FullName &&
                m.Name == "DrawCentered" && m.Parameters.Count == (scaled ? 4 : 2)))
            {
                var draw = (MethodReference)cursor.Next!.Operand;
                cursor.Remove();
                if (scaled && draw.Parameters[2].ParameterType.FullName == typeof(Vector2).FullName)
                    cursor.EmitDelegate<Action<MTexture, Vector2, Color, Vector2, float>>((texture, p, color, scale, rotation) =>
                    {
                        texture.DrawCentered(p, color, scale, rotation);
                        if (!Wide) return;
                        float padding = Math.Max(texture.Width * Math.Abs(scale.X), texture.Height * Math.Abs(scale.Y));
                        for (p.X += period; p.X < Width + padding; p.X += period)
                            texture.DrawCentered(p, color, scale, rotation);
                    });
                else if (scaled && draw.Parameters[2].ParameterType.MetadataType == MetadataType.Single)
                    cursor.EmitDelegate<Action<MTexture, Vector2, Color, float, float>>((texture, p, color, scale, rotation) =>
                    {
                        texture.DrawCentered(p, color, scale, rotation);
                        if (!Wide) return;
                        float padding = Math.Max(texture.Width, texture.Height) * Math.Abs(scale);
                        for (p.X += period; p.X < Width + padding; p.X += period)
                            texture.DrawCentered(p, color, scale, rotation);
                    });
                else if (!scaled)
                    cursor.EmitDelegate<Action<MTexture, Vector2, Color>>((texture, p, color) =>
                    {
                        texture.DrawCentered(p, color);
                        if (!Wide) return;
                        for (p.X += period; p.X < Width + texture.Width; p.X += period)
                            texture.DrawCentered(p, color);
                    });
                else throw new InvalidOperationException($"Unexpected {type.Name} particle draw signature: {draw}");
                matches++;
            }
            if (matches != 1)
                throw new InvalidOperationException($"{type.Name}.Render particle draw count: {matches}. " +
                    string.Join(", ", il.Body.Instructions.Where(i => i.Operand is MethodReference m &&
                        m.Name.Contains("Draw")).Select(i => i.ToString())));
        });
    }

    private void DrawBossStars(Matrix matrix, VertexPositionColor[] vertices, int count,
        Effect effect, BlendState blend)
    {
        GFX.DrawVertices(matrix, vertices, count, effect, blend);
        if (!Wide || count <= 6) return;

        // The first two triangles are the solid background, drawn only once.
        if (bossStars.Length < count - 6)
            bossStars = new VertexPositionColor[vertices.Length];
        Array.Copy(vertices, 6, bossStars, 0, count - 6);
        for (float offset = 384; offset < Width + 32; offset += 384)
            GFX.DrawVertices(Matrix.CreateTranslation(offset, 0, 0) * matrix,
                bossStars, count - 6, effect, blend);
    }

    private void RepeatVertices<T>(Matrix matrix, T[] vertices, int count, Effect effect,
        BlendState blend, int period, float origin, int margin) where T : struct, IVertexType
    {
        if (!Wide)
        {
            GFX.DrawVertices(matrix, vertices, count, effect, blend);
            return;
        }

        for (float offset = origin - period * MathF.Ceiling(origin / period);
            offset < Width + margin; offset += period)
            GFX.DrawVertices(Matrix.CreateTranslation(offset, 0, 0) * matrix, vertices, count, effect, blend);
    }

    private void PatchVertices<T>(Type type, string method, Action<Matrix, T[], int, Effect, BlendState> draw)
        where T : struct, IVertexType
    {
        Patch(type, method, il =>
        {
            var cursor = new ILCursor(il);
            if (!cursor.TryGotoNext(MoveType.Before, i => i.MatchCall(typeof(GFX), "DrawVertices") &&
                i.Operand is GenericInstanceMethod generic &&
                generic.GenericArguments[0].FullName == typeof(T).FullName))
                throw new InvalidOperationException($"{type.Name}.{method} vertex draw site changed");
            cursor.Remove();
            cursor.EmitDelegate(draw);
        });
    }

    private void ResizePrivateTarget(Type type, params string[] names)
    {
        var fields = names.Select(name => type.GetField(name, All) ??
            throw new MissingFieldException(type.Name, name)).ToArray();
        Patch(type, "BeforeRender", il =>
        {
            var cursor = new ILCursor(il);
            foreach (FieldInfo field in fields)
            {
                cursor.Index = 0;
                if (!cursor.TryGotoNext(MoveType.Before, i => i.MatchLdfld(type, field.Name)))
                    throw new InvalidOperationException($"{type.Name}.{field.Name} allocation guard changed");
                cursor.Emit(OpCodes.Dup);
                cursor.Index++;
                cursor.EmitDelegate<Func<object, VirtualRenderTarget?, VirtualRenderTarget?>>((owner, current) =>
                {
                    int width = controller.BufferWidth;
                    if (width != 320)
                    {
                        var tracked = resizedOwners.GetValue(owner, _ => new List<FieldInfo>());
                        if (!tracked.Contains(field)) tracked.Add(field);
                    }
                    if (current is not null && !current.IsDisposed && current.Width == width)
                        return current;
                    if (current is null || current.IsDisposed) return null;

                    var replacement = Resize(current, width);
                    field.SetValue(owner, replacement);
                    return replacement;
                });
                if (!cursor.TryGotoNext(MoveType.After, i => i.MatchLdcI4(320)))
                    throw new InvalidOperationException($"{type.Name}.{field.Name} allocation width changed");
                cursor.EmitDelegate<Func<int, int>>(_ => controller.BufferWidth);
            }
        });
    }

    private static VirtualRenderTarget Resize(VirtualRenderTarget old, int width)
    {
        var target = VirtualContent.CreateRenderTarget(old.Name, width, old.Height,
            old.Depth, old.Preserve, old.MultiSampleCount);
        old.Dispose();
        return target;
    }

    private void PatchFloats(Type type, string method, float value, int expected, Func<float, float> replacement)
    {
        Patch(type, method, il =>
        {
            var cursor = new ILCursor(il);
            int count = 0;
            while (cursor.TryGotoNext(MoveType.After, i => i.MatchLdcR4(value)))
            {
                cursor.EmitDelegate(replacement);
                count++;
            }
            if (count != expected)
                throw new InvalidOperationException($"{type.Name}.{method}: expected {expected} matches for {value}, got {count}");
        });
    }

    private void Patch(Type type, string name, ILContext.Manipulator patch)
        => hooks.Add(new ILHook(type.GetMethod(name, All)!, patch));

    public void Dispose()
    {
        for (int i = hooks.Count - 1; i >= 0; i--) hooks[i].Dispose();
        hooks.Clear();
        foreach (var entry in resizedOwners)
            foreach (FieldInfo field in entry.Value)
                if (field.GetValue(entry.Key) is VirtualRenderTarget { IsDisposed: false } old && old.Width != 320)
                    field.SetValue(entry.Key, Resize(old, 320));
        resizedOwners.Clear();
        if (resizedWipe && Celeste.WipeTarget is { IsDisposed: false } wipe)
            Celeste.WipeTarget = Resize(wipe, 1922);
        resizedWipe = false;
    }
}
