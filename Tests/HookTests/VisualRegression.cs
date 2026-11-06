using System.Reflection;
using System.Runtime.CompilerServices;
using Celeste;
using Celeste.Mod.UltrawideToggle;
using Microsoft.Xna.Framework;
using Monocle;
using MonoMod.RuntimeDetour;

internal static class VisualRegression
{
    private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic |
        BindingFlags.Instance | BindingFlags.Static;
    private static int assertions;

    internal static void Run()
    {
        var controller = new RenderController();
        using var widthHooks = new RenderHooks(controller);
        using var hooks = new InterfaceRenderHooks(controller);
        var level = New<Level>();
        level.Camera = new Camera(320, 180);
        var uiType = typeof(TalkComponent).GetNestedType("TalkComponentUI", All)!;
        var ui = (Entity)RuntimeHelpers.GetUninitializedObject(uiType);
        var talk = New<TalkComponent>();
        var npc = New<Entity>();
        npc.Position = new Vector2(100, 80);
        typeof(Component).GetProperty("Entity")!.SetValue(talk, npc);
        typeof(Entity).GetProperty("Scene")!.SetValue(ui, level);
        Set(ui, "Handler", talk);
        Set(ui, "slide", 1f);
        Set(ui, "alpha", 1f);
        Set(ui, "wiggler", New<Wiggler>());
        Vector2 captured = default;
        var oldMatrix = Engine.ScreenMatrix;
        var oldAtlas = GFX.Gui;
        var oldSave = SaveData.Instance;
        using var atlasHook = new Hook(typeof(Atlas).GetProperty("Item")!.GetMethod!,
            (Func<Func<Atlas, string, MTexture>, Atlas, string, MTexture>)((_, _, _) => New<MTexture>()));
        using var highlightHook = new Hook(uiType.GetProperty("Highlighted", All)!.GetMethod!,
            (Func<Func<object, bool>, object, bool>)((_, _) => false));
        using var drawHook = new Hook(typeof(MTexture).GetMethod("DrawJustified",
            [typeof(Vector2), typeof(Vector2), typeof(Color), typeof(float)])!,
            (Action<Action<MTexture, Vector2, Vector2, Color, float>, MTexture, Vector2, Vector2, Color, float>)
                ((_, _, position, _, _, _) => captured = Vector2.Transform(position, Engine.ScreenMatrix)));
        try
        {
            GFX.Gui = New<Atlas>();
            SaveData.Instance = New<SaveData>();
            foreach (double width in new[] { 320d, 430d, 1280d / 3, 640d, 960d })
            {
                var geometry = new ViewGeometry(width);
                typeof(RenderController).GetProperty("Geometry")!.SetValue(controller, geometry);
                Set(controller, "rendering", true);
                level.Camera.Position = new Vector2(-(float)geometry.SideExtension, 0);
                Engine.ScreenMatrix = Matrix.Identity;
                controller.RenderCenteredHud(() => ui.Render());
                float expected = (100 - MathF.Floor(level.Camera.X)) * 6;
                Check(Math.Abs(captured.X - expected) < .001f, "Actual interaction prompt must cancel duplicate HUD translation");
                SaveData.Instance.Assists.MirrorMode = true;
                controller.RenderCenteredHud(() => ui.Render());
                Check(Math.Abs(captured.X - (geometry.HiresWidth - expected)) < .001,
                    $"Mirrored prompt at {width}: expected {geometry.HiresWidth - expected}, drew at {captured.X}");
                SaveData.Instance.Assists.MirrorMode = false;

                var menu = New<OuiMainMenu>();
                var button = New<MainMenuClimb>();
                button.TargetPosition = new Vector2(320, 160);
                button.TweenFrom = new Vector2(-320, 160);
                button.Position = button.TweenFrom;
                Set(menu, "buttons", new List<MenuButton> { button });
                var savedPosition = button.Position;
                using var entityRender = new Hook(typeof(MainMenuClimb).GetMethod("Render")!,
                    (Action<Action<MainMenuClimb>, MainMenuClimb>)((_, entity) =>
                    {
                        if (ReferenceEquals(entity, button))
                        {
                            Check(Math.Abs(entity.X + geometry.SideExtension * 6 + 320) < .001,
                                "Hidden menu button must clear the true left edge at every aspect");
                            throw new InvalidOperationException("Simulated draw failure");
                        }
                    }));
                try { menu.Render(); }
                catch (InvalidOperationException error) when (error.Message == "Simulated draw failure") { }
                Check(button.Position == savedPosition, "Menu animation state must restore even if drawing fails");
                Check(hooks.MenuButtonPosition(button.TargetPosition, button.TargetPosition, button.TweenFrom) == button.TargetPosition,
                    "Visible menu layout must remain unchanged");

                CheckSpinners(geometry);
            }
        }
        finally { Engine.ScreenMatrix = oldMatrix; GFX.Gui = oldAtlas; SaveData.Instance = oldSave; }
        CheckLava(controller, level);
        Console.WriteLine($"PASS: {assertions} prompt, menu, spinner, and lava visual-state assertions");
    }

    private static void CheckLava(RenderController controller, Level level)
    {
        using var sceneHook = new Hook(typeof(Engine).GetProperty("Scene")!.GetMethod!,
            (Func<Func<Scene>, Scene>)(_ => level));
        using var atlasHook = new Hook(typeof(Atlas).GetMethod(nameof(Atlas.GetAtlasSubtextures))!,
            (Func<Func<Atlas, string, List<MTexture>>, Atlas, string, List<MTexture>>)((_, _, _) => [New<MTexture>()]));
        using var hooks = new HazardVisualHooks(controller);
        var render = typeof(HazardVisualHooks).GetMethod("RenderLava", All)!
            .CreateDelegate<Action<On.Celeste.LavaRect.orig_Render, LavaRect>>(hooks);
        var update = typeof(HazardVisualHooks).GetMethod("UpdateLava", All)!
            .CreateDelegate<Action<On.Celeste.LavaRect.orig_Update, LavaRect>>(hooks);
        var atlas = GFX.Game;
        var random = Calc.Random;
        try
        {
            GFX.Game = new Atlas();
            foreach (Entity owner in new Entity[] { New<RisingLava>(), New<SandwichLava>(), New<Entity>() })
            {
                var surface = new LavaRect(400, 100, 4) { Position = new Vector2(-40, 16) };
                typeof(Component).GetProperty("Entity")!.SetValue(surface, owner);
                foreach (double width in new[] { 420d, 430d, 1280d / 3, 640d, 960d, 320d })
                {
                    var geometry = new ViewGeometry(width);
                    typeof(RenderController).GetProperty("Geometry")!.SetValue(controller, geometry);
                    bool wide = width > 320 && owner.GetType() != typeof(Entity);
                    float side = wide ? (float)geometry.SideExtension : 0;
                    var expectedRandom = new Random(123);
                    Calc.Random = new Random(123);
                    surface.SurfaceColor = Color.Blue;
                    render(drawn =>
                    {
                        Check(ReferenceEquals(drawn, surface) != wide, "Only wide vanilla lava should use a separate surface");
                        Check(drawn.Width == 400 + side * 2 && drawn.Position == new Vector2(-40 - side, 16),
                            $"{owner.GetType().Name} art must preserve its margins at width {width}");
                        Check(drawn.SurfaceColor == surface.SurfaceColor, "Wide lava should follow the native color");
                    }, surface);
                    update(_ => Calc.Random.Next(), surface);
                    expectedRandom.Next();
                    Check(Calc.Random.Next() == expectedRandom.Next(), "Preview resize and animation must preserve the native RNG stream");
                    Check(surface.Width == 400 && surface.Position == new Vector2(-40, 16),
                        "The original lava surface must preserve its geometry");
                }
            }
        }
        finally { GFX.Game = atlas; Calc.Random = random; }
    }

    private static void CheckSpinners(ViewGeometry geometry)
    {
        var visuals = new SpinnerVisuals();
        var spinner = New<CrystalStaticSpinner>();
        spinner.Active = true;
        spinner.Visible = false;
        spinner.Collidable = false;
        spinner.Position = new Vector2(-30, 90);
        bool wide = geometry.Width > 320;
        int prepared = 0;
        using var createSpritesHook = new Hook(typeof(CrystalStaticSpinner).GetMethod("CreateSprites", All)!,
            (Action<Action<CrystalStaticSpinner>, CrystalStaticSpinner>)((_, entity) =>
            {
                prepared++;
                Set(entity, "expanded", true);
            }));
        visuals.Prepare(new[] { spinner }, Vector2.Zero, geometry);
        visuals.Prepare(new[] { spinner }, Vector2.Zero, geometry);
        Check(prepared == (wide ? 1 : 0), "Side sprites must initialize once, without rerunning creation every frame");
        Check(!spinner.Visible && !spinner.Collidable, "Preparing graphics must preserve original activation flags");
        var level = New<Level>();
        level.Camera = new Camera(320, 180);
        typeof(Entity).GetProperty("Scene")!.SetValue(spinner, level);
        Check(SpinnerVisuals.InVisualView(spinner.Position, Vector2.Zero, geometry) == wide,
            "Left extension spinner visibility must follow the selected view");
        visuals.Show(new[] { spinner }, Vector2.Zero, geometry);
        try
        {
            Check(spinner.Visible == wide, "Side spinner must draw only in a sufficiently wide view");
            Check(!spinner.Collidable, "Drawing must not activate a non-collidable spinner");
            Check(!(bool)typeof(CrystalStaticSpinner).GetMethod("InView", All)!.Invoke(spinner, null)!,
                "Native update-time visibility must still use the original camera");
        }
        finally { visuals.Restore(); }
        Check(!spinner.Visible && !spinner.Collidable, "Original flags must be restored before updates");
        spinner.Position = new Vector2(350, 90);
        spinner.Collidable = true;
        visuals.Show(new[] { spinner }, Vector2.Zero, geometry);
        visuals.Restore();
        Check(spinner.Collidable, "Drawing must preserve an already-collidable spinner");
        Check(!SpinnerVisuals.InVisualView(new Vector2(100, 200), Vector2.Zero, geometry),
            "Horizontal extension must not widen vertical culling");
        spinner.Active = false;
        visuals.Show(new[] { spinner }, Vector2.Zero, geometry);
        Check(!spinner.Visible, "Explicitly disabled spinners must remain hidden");
        visuals.Restore();
    }

    private static T New<T>() where T : class => (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
    private static void Set(object target, string field, object value) => target.GetType().GetField(field, All)!.SetValue(target, value);
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        assertions++;
    }
}
