using System.Reflection;
using Microsoft.Xna.Framework;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;
using Monocle;

namespace Celeste.Mod.UltrawideToggle;

/// <summary>
/// Keeps interface coordinates separate from the gameplay camera. Edge HUD
/// elements share one anchor; menu projections use the selected output aspect.
/// </summary>
internal sealed class InterfaceRenderHooks : IDisposable
{
    private readonly List<ILHook> hooks = new();
    private readonly RenderController controller;
    private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic |
        BindingFlags.Instance | BindingFlags.Static;

    internal InterfaceRenderHooks(RenderController controller)
    {
        this.controller = controller;
        try
        {
            foreach (Type type in new[] { typeof(TalkComponent).GetNestedType("TalkComponentUI", All)!, typeof(BirdTutorialGui) })
                Patch(type, "Render", il =>
                {
                    var cursor = new ILCursor(il);
                    if (!cursor.TryGotoNext(MoveType.Before, i => i.MatchStloc(2)))
                        throw new InvalidOperationException("World prompt screen-position local changed");
                    // The visual camera already includes the side extension.
                    // Cancel the HUD centering offset once for world anchors.
                    cursor.EmitDelegate<Func<Vector2, Vector2>>(AlignWorldPrompt);
                });
            On.Celeste.OuiMainMenu.Render += RenderMainMenu;
            Patch(typeof(TotalStrawberriesDisplay), "Render", il =>
            {
                var cursor = new ILCursor(il);
                int count = 0;
                while (cursor.TryGotoNext(MoveType.After,
                    i => i.MatchCall<Vector2>("Lerp")))
                {
                    // Apply the edge anchor before the game's pixel rounding.
                    cursor.EmitDelegate<Func<Vector2, Vector2>>(position =>
                        position + new Vector2(controller.HudLeftOffset, 0));
                    count++;
                }
                Require(count, 1, "strawberry animation anchor");
            });
            Patch(typeof(SpeedrunTimerDisplay), "Render", il =>
            {
                var cursor = new ILCursor(il);
                if (!cursor.TryGotoNext(MoveType.Before, i => i.MatchStloc(0)))
                    throw new InvalidOperationException("Timer animation local changed");
                cursor.EmitDelegate<Func<float, float>>(position =>
                    position + controller.HudLeftOffset);
            });

            Patch(typeof(HiresRenderer), "get_DrawToBuffer", il =>
            {
                var cursor = new ILCursor(il);
                int count = 0;
                while (cursor.TryGotoNext(MoveType.Before, i => i.OpCode == OpCodes.Ret))
                {
                    // The game's fixed 1922-wide HUD target clips negative edge
                    // coordinates before composition. Use its native direct
                    // rendering path for wide views, including small windows.
                    cursor.EmitDelegate<Func<bool, bool>>(buffered =>
                        buffered && !controller.RenderingWide);
                    cursor.Index++;
                    count++;
                }
                Require(count, 3, "HUD buffering exits");
            });
            Patch(typeof(HudRenderer), "RenderContent", il =>
            {
                var cursor = new ILCursor(il);
                int count = 0;
                while (cursor.TryGotoNext(MoveType.Before, i => i.MatchCall(typeof(Draw), "Rect")))
                {
                    cursor.Remove();
                    cursor.EmitDelegate<Action<float, float, float, float, Color>>(
                        (x, y, width, height, color) =>
                        {
                            float extra = controller.RenderingWide ? controller.OutputWidth - 1920 : 0;
                            Draw.Rect(x - extra / 2, y, width + extra, height, color);
                        });
                    count++;
                }
                Require(count, 1, "HUD background fade");
            });

            Patch(typeof(MountainModel), "ResetRenderTargets", il =>
            {
                ReplaceWidthLimit(il);
                var bufferField = typeof(MountainModel).GetField("buffer", All)!;
                var cursor = new ILCursor(il);
                int count = 0;
                while (cursor.TryGotoNext(MoveType.After,
                    i => i.MatchCallvirt<VirtualAsset>("get_Width")))
                {
                    cursor.Emit(OpCodes.Ldarg_0);
                    cursor.EmitDelegate<Func<int, MountainModel, int>>((width, model) =>
                    {
                        var buffer = (VirtualRenderTarget)bufferField.GetValue(model)!;
                        // The original resize guard checks only width. Aspect
                        // changes in small windows can change only the height.
                        return buffer.Height == Math.Min(1080, Engine.ViewHeight) ? width : -1;
                    });
                    count++;
                }
                Require(count, 1, "mountain buffer height guard");
            });
            // Everest has both a custom-mountain path and the vanilla path.
            foreach (string method in new[] { "BeforeRender", "orig_BeforeRender" })
                Patch(typeof(MountainModel), method, il =>
                {
                    var cursor = new ILCursor(il);
                    int count = 0;
                    while (cursor.TryGotoNext(MoveType.After,
                        i => i.MatchCall<Engine>("get_Width")))
                    {
                        cursor.EmitDelegate<Func<int, int>>(_ => (int)Math.Round(controller.OutputWidth));
                        count++;
                    }
                    Require(count, 1, "mountain projection width");
                });
            Patch(typeof(MountainRenderer), "Render", il =>
            {
                var cursor = new ILCursor(il);
                int count = 0;
                while (cursor.TryGotoNext(MoveType.Before, i => i.MatchCallvirt<MTexture>("Draw")))
                {
                    cursor.Remove();
                    cursor.EmitDelegate<Action<MTexture, Vector2, Vector2, Color>>(
                        (texture, position, origin, color) => texture.Draw(position, origin,
                            color, new Vector2(controller.OutputWidth / 1920, 1)));
                    count++;
                }
                Require(count, 1, "mountain vignette");
            });
            Patch(typeof(HiresSnow), "Render", il =>
            {
                ReplaceWidthLimit(il);
                var cursor = new ILCursor(il);
                int count = 0;
                while (cursor.TryGotoNext(MoveType.After, i => i.OpCode == OpCodes.Ldfld &&
                    i.Operand is Mono.Cecil.FieldReference field && field.Name == "Position" &&
                    field.DeclaringType.FullName == "Celeste.HiresSnow/Particle"))
                {
                    cursor.EmitDelegate<Func<Vector2, Vector2>>(position =>
                        new Vector2(position.X * controller.OutputWidth / 1920, position.Y));
                    count++;
                }
                Require(count, 1, "menu snow coverage");
            });
            PatchTitleFades("orig_Render", 1);
            PatchTitleFades("Render", 3);
            Patch(typeof(TextMenuExt.SubMenu), "Render", BoundaryMenu.PatchArrow);
            Patch(typeof(ScreenWipe), "DrawPrimitives", il =>
            {
                var cursor = new ILCursor(il);
                if (!cursor.TryGotoNext(MoveType.Before,
                    instruction => instruction.MatchCall<Matrix>("CreateScale")))
                    throw new InvalidOperationException("Wipe projection scale site changed");
                // Native wipes share a 1920x1080 canvas. Project both axes to
                // the viewport instead of mixing widened edges with fixed
                // centers, travel distances, and mirrored coordinates.
                cursor.Remove();
                cursor.Emit(OpCodes.Ldloc_0);
                cursor.EmitDelegate<Func<float, Microsoft.Xna.Framework.Graphics.Viewport, Matrix>>(
                    WipeProjection);
            });
            var routine = typeof(LevelExit).GetNestedTypes(All).Single(type =>
                type.Name.StartsWith("<Routine>"));
            var owner = routine.GetField("<>4__this", All)!;
            hooks.Add(new ILHook(routine.GetMethod("MoveNext", All)!, il =>
            {
                var cursor = new ILCursor(il);
                int count = 0;
                while (cursor.TryGotoNext(MoveType.Before, i => i.MatchCall<Engine>("set_Scene") &&
                    i.Previous?.Operand is Mono.Cecil.FieldReference field &&
                    field.DeclaringType.FullName == "Celeste.LevelExit" && field.Name == "overworldLoader"))
                {
                    cursor.Remove();
                    cursor.Emit(OpCodes.Ldarg_0);
                    cursor.Emit(OpCodes.Ldfld, owner);
                    cursor.EmitDelegate<Action<Scene, LevelExit>>(controller.RequestMenuHandoff);
                    count++;
                }
                Require(count, 1, "exit-to-menu scene handoff");
            }));
        }
        catch { Dispose(); throw; }
    }

    private void PatchTitleFades(string method, int expectedCalls)
    {
        Patch(typeof(OuiTitleScreen), method, il =>
        {
            var cursor = new ILCursor(il);
            int count = 0;
            while (cursor.TryGotoNext(MoveType.Before,
                i => i.MatchCall(typeof(Draw), "Rect")))
            {
                cursor.Remove();
                cursor.EmitDelegate<Action<float, float, float, float, Color>>(
                    (x, y, width, height, color) =>
                    {
                        // Only full-screen fades expand. The Everest restart
                        // progress bar in the same method retains its size.
                        float extra = controller.RenderingWide && width >= 1920
                            ? controller.OutputWidth - 1920 : 0;
                        Draw.Rect(x - extra / 2, y, width + extra, height, color);
                    });
                count++;
            }
            Require(count, expectedCalls, $"title {method} fade calls");
        });
    }

    internal static Matrix WipeProjection(float horizontalScale,
        Microsoft.Xna.Framework.Graphics.Viewport viewport)
        => Matrix.CreateScale(horizontalScale, viewport.Height / 1080f, 1);

    internal Vector2 AlignWorldPrompt(Vector2 position)
        => position + new Vector2(controller.RenderingWide
            ? -(float)controller.Geometry.SideExtension : 0, 0);

    internal Vector2 MenuButtonPosition(Vector2 position, Vector2 target, Vector2 hidden)
    {
        if (!controller.RenderingWide || target.X <= hidden.X) return position;
        float progress = Math.Clamp((target.X - position.X) / (target.X - hidden.X), 0, 1);
        return position - new Vector2((float)controller.Geometry.SideExtension * 6 * progress, 0);
    }

    private void RenderMainMenu(On.Celeste.OuiMainMenu.orig_Render original, OuiMainMenu menu)
    {
        if (!controller.RenderingWide) { original(menu); return; }
        var buttons = menu.Buttons;
        var positions = new Vector2[buttons.Count];
        for (int i = 0; i < buttons.Count; i++) positions[i] = buttons[i].Position;
        try
        {
            for (int i = 0; i < buttons.Count; i++)
                buttons[i].Position = MenuButtonPosition(positions[i], buttons[i].TargetPosition, buttons[i].TweenFrom);
            original(menu);
        }
        finally
        {
            for (int i = 0; i < buttons.Count; i++) buttons[i].Position = positions[i];
        }
    }

    private void ReplaceWidthLimit(ILContext il)
    {
        var cursor = new ILCursor(il);
        int count = 0;
        while (cursor.TryGotoNext(MoveType.After, i => i.MatchLdcI4(1920)))
        {
            cursor.EmitDelegate<Func<int, int>>(_ => (int)Math.Round(controller.OutputWidth));
            count++;
        }
        Require(count, 1, "interface width limit");
    }

    private void Patch(Type type, string method, ILContext.Manipulator edit)
        => hooks.Add(new ILHook(type.GetMethod(method, All)!, edit));

    private static void Require(int actual, int expected, string site)
    {
        if (actual != expected)
            throw new InvalidOperationException($"{site}: expected {expected}, found {actual}");
    }

    public void Dispose()
    {
        On.Celeste.OuiMainMenu.Render -= RenderMainMenu;
        for (int index = hooks.Count - 1; index >= 0; index--) hooks[index].Dispose();
        hooks.Clear();
    }
}
