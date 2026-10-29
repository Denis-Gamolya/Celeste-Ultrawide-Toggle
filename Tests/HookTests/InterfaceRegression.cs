using System.Reflection;
using System.Runtime.CompilerServices;
using Celeste;
using Celeste.Mod;
using Celeste.Mod.UltrawideToggle;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Monocle;
using MonoMod.RuntimeDetour;

internal static class InterfaceRegression
{
    private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic |
        BindingFlags.Instance | BindingFlags.Static;
    private static int assertions;

    public static void Run(string dialogFile)
    {
        assertions = 0;
        var scenes = new List<Scene> { New<GameLoader>(), New<OverworldLoader>(), New<Overworld>() };
        foreach (var mode in new[] { LevelExit.Mode.SaveAndQuit, LevelExit.Mode.GiveUp,
            LevelExit.Mode.CompletedInterlude })
        {
            var exit = New<LevelExit>();
            Set(exit, "mode", mode);
            scenes.Add(exit);
        }
        // Both startup and return-to-menu paths must retain one aspect through
        // all intermediate scenes; explicitly selected 16:9 must still work.
        foreach (Scene scene in scenes)
        {
            Check(RenderController.SelectNonLevelGeometry(scene, AspectPreset.Ratio21x9,
                3440d / 1440).Width == 430, "Menu transition must not fall back to 16:9");
            Check(RenderController.SelectNonLevelGeometry(scene, AspectPreset.Original16x9,
                3440d / 1440).Width == 320, "Explicit menu 16:9 must be respected");
        }
        foreach (var mode in new[] { LevelExit.Mode.Restart, LevelExit.Mode.GoldenBerryRestart })
        {
            var exit = New<LevelExit>();
            Set(exit, "mode", mode);
            Check(!RenderController.UsesMenuAspect(exit), "Gameplay retries are not menu exits");
        }
        Check(!RenderController.UsesMenuAspect(null), "Unknown scenes must not widen indiscriminately");
        Check(!RenderController.UsesMenuAspect(New<Scene>()), "Unrelated scenes retain their original policy");

        var language = new Language();
        foreach (string line in File.ReadAllLines(dialogFile))
        {
            string[] pair = line.Split('=', 2);
            if (pair.Length != 2) continue;
            language.Dialog[pair[0].Trim()] = pair[1].Trim();
            language.Cleaned[pair[0].Trim()] = pair[1].Trim();
        }
        foreach (var (property, expected) in new[] { ("View16x9", "Toggle 16:9"),
            ("ViewAuto", "Toggle Auto"), ("View21x9", "Toggle 21:9"),
            ("View32x9", "Toggle 32:9"), ("View48x9", "Toggle 48:9") })
        {
            var name = typeof(UltrawideSettings).GetProperty(property)!
                .GetCustomAttribute<SettingNameAttribute>()?.Name;
            Check(name is not null && Dialog.Has(name, language) &&
                Dialog.Clean(name, language) == expected,
                "Native binding labels must resolve through the actual game dialog lookup");
        }

        CheckReferenceBoundary();
        CheckDrawing();
        CheckWipeProjection();
        Console.WriteLine($"PASS: {assertions} startup/exit, title-fade, wipe-boundary, and binding-label assertions");
    }

    private static void CheckReferenceBoundary()
    {
        var wipe = New<FadeWipe>();
        Check(RenderController.ShouldDrawReferenceFrame(true, null), "Boundary appears during normal gameplay");
        Check(!RenderController.ShouldDrawReferenceFrame(false, null), "Disabled boundary stays hidden");
        wipe.WipeIn = false;
        Check(!RenderController.ShouldDrawReferenceFrame(true, wipe), "Boundary hides on the first outgoing wipe frame");
        wipe.Completed = true;
        Check(!RenderController.ShouldDrawReferenceFrame(true, wipe), "Boundary stays hidden through outgoing completion");
        wipe.WipeIn = true;
        Check(RenderController.ShouldDrawReferenceFrame(true, wipe), "Incoming wipe restores the enabled boundary");
        Check(!RenderController.ShouldDrawReferenceFrame(false, wipe), "Incoming wipe respects the disabled setting");
    }

    private static void CheckDrawing()
    {
        var controller = new RenderController();
        using var hooks = new InterfaceRenderHooks(controller);
        var title = New<OuiTitleScreen>();
        Set(title, "hideConfirmButton", true);
        Set(title, "version", "test");
        Set(title, "fade", .7f);
        var wipe = New<FadeWipe>();
        Set(wipe, "vertexBuffer", new VertexPositionColor[6]);
        wipe.Percent = .5f;
        Vector4 rectangle = default;
        VertexPositionColor[] vertices = [];
        using var rectHook = new Hook(typeof(Draw).GetMethod("Rect",
            [typeof(float), typeof(float), typeof(float), typeof(float), typeof(Color)])!,
            (Action<Action<float, float, float, float, Color>, float, float, float, float, Color>)
                ((_, x, y, width, height, _) =>
                {
                    Vector2 origin = Vector2.Transform(new Vector2(x, y), Engine.ScreenMatrix);
                    rectangle = new Vector4(origin.X, origin.Y, width, height);
                }));
        using var fontHook = new Hook(typeof(ActiveFont).GetMethod("Draw",
            [typeof(string), typeof(Vector2), typeof(Vector2), typeof(Vector2), typeof(Color)])!,
            (Action<Action<string, Vector2, Vector2, Vector2, Color>,
                string, Vector2, Vector2, Vector2, Color>)((_, _, _, _, _, _) => { }));
        using var wipeHook = new Hook(typeof(ScreenWipe).GetMethod("DrawPrimitives", All)!,
            (Action<Action<VertexPositionColor[]>, VertexPositionColor[]>)
                ((_, value) => vertices = value));
        var savedMatrix = Engine.ScreenMatrix;
        try
        {
            Engine.ScreenMatrix = Matrix.Identity;
            foreach (double width in new[] { 320d, 430d, 1280d / 3, 640d })
            {
                typeof(RenderController).GetProperty("Geometry")!.SetValue(controller, new ViewGeometry(width));
                Set(controller, "outputWidth", (float)(width * 6));
                Set(controller, "rendering", true);
                controller.RenderCenteredHud(() => typeof(OuiTitleScreen)
                    .GetMethod("orig_Render", All)!.Invoke(title, null));
                Check(Math.Abs(rectangle.X + 10) < .001f &&
                    Math.Abs(rectangle.X + rectangle.Z - (width * 6 + 10)) < .001,
                    "Actual title fade must cover both ultrawide edges");
                Check(rectangle.Y == -10 && rectangle.W == 1100,
                    "Title fade must retain its vertical bounds");
                wipe.Render(null!);
                Check(vertices.Min(v => v.Position.X) == -10 &&
                    vertices.Max(v => v.Position.X) == 1930,
                    "Native wipe geometry must retain its original canvas before projection");
                Check(vertices.Min(v => v.Position.Y) == -10 &&
                    vertices.Max(v => v.Position.Y) == 1090,
                    "Wipe must retain its vertical extent");
            }
        }
        finally { Engine.ScreenMatrix = savedMatrix; }
    }

    private static void CheckWipeProjection()
    {
        var controller = new RenderController();
        using var hooks = new InterfaceRenderHooks(controller);
        VertexPositionColor[] vertices = [];
        using var capture = new Hook(typeof(ScreenWipe).GetMethod("DrawPrimitives", All)!,
            (Action<Action<VertexPositionColor[]>, VertexPositionColor[]>)
                ((_, value) => vertices = value.ToArray()));
        foreach (var (type, count) in new[] { (typeof(FadeWipe), 6),
            (typeof(MountainWipe), 9), (typeof(AngledWipe), 36) })
        {
            var wipe = (ScreenWipe)RuntimeHelpers.GetUninitializedObject(type);
            type.GetField("vertexBuffer", All)!.SetValue(wipe, new VertexPositionColor[count]);
            foreach (bool incoming in new[] { false, true })
                foreach (float percent in new[] { 0f, .25f, .5f, .75f, 1f })
                {
                    wipe.WipeIn = incoming;
                    wipe.Percent = percent;
                    Set(controller, "outputWidth", 1920f);
                    wipe.Render(null!);
                    var baseline = vertices.ToArray();
                    foreach (int width in new[] { 1920, 2560, 3440, 5120 })
                    {
                        Set(controller, "outputWidth", (float)width);
                        wipe.Render(null!);
                        var viewport = new Viewport(0, 0, width, 1440);
                        Matrix matrix = InterfaceRenderHooks.WipeProjection(width / 1920f, viewport);
                        Check(vertices.Select(vertex => vertex.Position)
                            .SequenceEqual(baseline.Select(vertex => vertex.Position)),
                            "Wipe timing and native geometry must be independent of output aspect");
                        for (int index = 0; index < vertices.Length; index++)
                        {
                            var projected = Vector3.Transform(vertices[index].Position, matrix);
                            var source = baseline[index].Position;
                            if (Math.Abs(projected.X / width - source.X / 1920) > .00001 ||
                                Math.Abs(projected.Y / 1440 - source.Y / 1080) > .00001)
                                throw new InvalidOperationException("Wipe projection must scale both axes independently");
                        }
                        Check(true, "All wipe vertices preserve normalized position across aspects");
                    }
                }
        }
    }

    private static T New<T>() where T : class
        => (T)RuntimeHelpers.GetUninitializedObject(typeof(T));

    private static void Set(object target, string name, object value)
        => target.GetType().GetField(name, All)!.SetValue(target, value);

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        assertions++;
    }
}
