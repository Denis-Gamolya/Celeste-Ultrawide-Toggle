using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Xna.Framework;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;
using Monocle;

namespace Celeste.Mod.UltrawideToggle;

/// <summary>Draws vanilla hazards in the extra view without activating their collision or setup.</summary>
internal sealed class HazardVisualHooks : IDisposable
{
    private const BindingFlags All = BindingFlags.Instance | BindingFlags.Static |
        BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly FieldInfo Disappearing = typeof(Lightning).GetField("disappearing", All)!;
    private static readonly MethodInfo Clone = typeof(object).GetMethod("MemberwiseClone", All)!;
    private static readonly FieldInfo[] DustNodes = new[] { "nodes", "LeftNodes", "RightNodes", "TopNodes", "BottomNodes" }
        .Select(name => typeof(DustGraphic).GetField(name, All)!).ToArray();
    private static readonly FieldInfo DustEyes = typeof(DustGraphic).GetField("eyes", All)!;
    private static readonly FieldInfo DustCounter = typeof(DustEdges).GetField("DustGraphicEstabledCounter", All)!;
    private static readonly Func<DustGraphic, bool> DustInView = typeof(DustGraphic)
        .GetMethod("get_InView", All)!.CreateDelegate<Func<DustGraphic, bool>>();
    private readonly ConditionalWeakTable<DustGraphic, DustGraphic> previews = new();
    private readonly ConditionalWeakTable<LavaRect, LavaRect> lavaPreviews = new();
    private readonly Random lavaRandom = new();
    private readonly List<IDisposable> hooks = new();
    private readonly RenderController controller;
    private bool preparingDustPreview;

    internal HazardVisualHooks(RenderController controller)
    {
        this.controller = controller;
        try
        {
            foreach (string name in new[] { "Render", "OnRenderBloom" })
            {
                PatchRenderer(typeof(LightningRenderer), name, lightning: true);
                PatchRenderer(typeof(SeekerBarrierRenderer), name, lightning: false);
            }
            hooks.Add(new Hook(typeof(DustGraphic).GetMethod(nameof(DustGraphic.Render), All)!,
                (Action<Action<DustGraphic>, DustGraphic>)RenderDust));
            hooks.Add(new ILHook(typeof(DustGraphic).GetMethod(nameof(DustGraphic.AddDustNodesIfInCamera), All)!, il =>
            {
                var cursor = new ILCursor(il);
                if (!cursor.TryGotoNext(MoveType.Before, i => i.MatchCallvirt<Scene>("Add")))
                    throw new InvalidOperationException("Dust preview eye registration site changed");
                cursor.Remove();
                cursor.EmitDelegate<Action<Scene, Entity>>((scene, eyes) =>
                {
                    if (!preparingDustPreview) scene.Add(eyes);
                });
            }));
            On.Celeste.LavaRect.Render += RenderLava;
            On.Celeste.LavaRect.Update += UpdateLava;
        }
        catch { Dispose(); throw; }
    }

    private void PatchRenderer(Type renderer, string name, bool lightning)
    {
        Type edgeType = renderer.GetNestedType("Edge", All)!;
        MethodInfo inView = edgeType.GetMethod("InView", All)!;
        MethodInfo? updateWave = lightning ? null : edgeType.GetMethod("UpdateWave", All)!;
        FieldInfo parent = edgeType.GetField("Parent", All)!;
        hooks.Add(new ILHook(renderer.GetMethod(name, All)!, il =>
        {
            var cursor = new ILCursor(il);
            int edges = 0, fills = 0;
            while (cursor.TryGotoNext(MoveType.Before, i => i.OpCode == OpCodes.Ldfld &&
                i.Operand is FieldReference f && f.Name == "Visible"))
            {
                var field = (FieldReference)cursor.Next!.Operand;
                if (field.DeclaringType.Name == "Edge")
                {
                    cursor.Emit(OpCodes.Dup);
                    cursor.Index++;
                    cursor.EmitDelegate<Func<object, bool, bool>>((edge, visible) =>
                    {
                        if (visible || !controller.RenderingWide) return visible;
                        var owner = (Entity)parent.GetValue(edge)!;
                        if (owner.GetType() != (lightning ? typeof(Lightning) : typeof(SeekerBarrier)) ||
                            owner.Scene is not Level level)
                            return false;
                        var camera = level.Camera;
                        var bounds = new Rectangle((int)Math.Floor(camera.Left) - 4,
                            (int)Math.Floor(camera.Top) - 4, camera.Viewport.Width + 8, camera.Viewport.Height + 8);
                        if (!(bool)inView.Invoke(edge, [bounds])!) return false;
                        updateWave?.Invoke(edge, [level.TimeActive * 3]);
                        return true;
                    });
                    edges++;
                }
                else if (lightning && field.DeclaringType.FullName == typeof(Entity).FullName)
                {
                    cursor.Emit(OpCodes.Dup);
                    cursor.Index++;
                    cursor.EmitDelegate<Func<Lightning, bool, bool>>(DrawLightning);
                    fills++;
                }
                else cursor.Index++;
            }
            if (edges != 1 || fills != (lightning ? 1 : 0))
                throw new InvalidOperationException($"{renderer.Name}.{name} visibility sites changed");
        }));
    }

    private bool DrawLightning(Lightning lightning, bool visible)
    {
        if (visible || !controller.RenderingWide || lightning.GetType() != typeof(Lightning) ||
            lightning.Scene is not Level level || (bool)Disappearing.GetValue(lightning)!)
            return visible;
        Camera camera = level.Camera;
        return lightning.Right > camera.Left && lightning.Left < camera.Right &&
            lightning.Bottom > camera.Top && lightning.Top < camera.Bottom;
    }

    private void RenderDust(Action<DustGraphic> original, DustGraphic graphic)
    {
        if (!controller.RenderingWide || graphic.Estableshed || !DustInView(graphic) ||
            graphic.Entity.GetType() != typeof(DustStaticSpinner) &&
            graphic.Entity.GetType() != typeof(DustTrackSpinner) &&
            graphic.Entity.GetType() != typeof(DustRotateSpinner))
        {
            original(graphic);
            return;
        }

        // Build the preview on a detached copy. Native setup, its callbacks, and
        // the native establishment budget stay with the original component.
        DustGraphic preview = previews.GetValue(graphic, PrepareDustPreview);
        original(preview);
        if (DustEyes.GetValue(preview) is Entity eyes)
            eyes.Render();
    }

    private DustGraphic PrepareDustPreview(DustGraphic graphic)
    {
        var preview = (DustGraphic)Clone.Invoke(graphic, null)!;
        foreach (var list in DustNodes)
            list.SetValue(preview, Activator.CreateInstance(list.FieldType));
        preview.OnEstablish = null;

        int counter = (int)DustCounter.GetValue(null)!;
        try
        {
            DustCounter.SetValue(null, 0);
            preparingDustPreview = true;
            preview.AddDustNodesIfInCamera();
            return preview;
        }
        finally
        {
            preparingDustPreview = false;
            DustCounter.SetValue(null, counter);
        }
    }

    private void RenderLava(On.Celeste.LavaRect.orig_Render original, LavaRect surface)
    {
        Type? owner = surface.Entity?.GetType();
        if (!controller.RenderingWideLevel || owner != typeof(RisingLava) && owner != typeof(SandwichLava))
        {
            lavaPreviews.Remove(surface);
            original(surface);
            return;
        }

        float side = (float)controller.Geometry.SideExtension;
        var preview = lavaPreviews.GetValue(surface, source => (LavaRect)Clone.Invoke(source, null)!);
        float width = surface.Width + side * 2;
        if (preview.Width != width)
        {
            // Resize rebuilds the geometry and bubbles. Keep their random stream
            // separate from the one used by the original component and gameplay.
            Calc.PushRandom(lavaRandom);
            try { preview.Resize(width, surface.Height, surface.SurfaceStep); }
            finally { Calc.PopRandom(); }
        }

        preview.Position = surface.Position - new Vector2(side, 0);
        preview.Fade = surface.Fade;
        preview.Spikey = surface.Spikey;
        preview.UpdateMultiplier = surface.UpdateMultiplier;
        preview.SurfaceColor = surface.SurfaceColor;
        preview.EdgeColor = surface.EdgeColor;
        preview.CenterColor = surface.CenterColor;
        original(preview);
    }

    private void UpdateLava(On.Celeste.LavaRect.orig_Update original, LavaRect surface)
    {
        original(surface);
        if (!lavaPreviews.TryGetValue(surface, out var preview)) return;

        Calc.PushRandom(lavaRandom);
        try { original(preview); }
        finally { Calc.PopRandom(); }
    }

    public void Dispose()
    {
        On.Celeste.LavaRect.Update -= UpdateLava;
        On.Celeste.LavaRect.Render -= RenderLava;
        for (int i = hooks.Count - 1; i >= 0; i--) hooks[i].Dispose();
        hooks.Clear();
        previews.Clear();
        lavaPreviews.Clear();
    }
}
