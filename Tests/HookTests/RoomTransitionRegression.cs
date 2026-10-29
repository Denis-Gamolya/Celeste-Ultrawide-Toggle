using System.Reflection;
using System.Runtime.CompilerServices;
using Celeste;
using Celeste.Mod.UltrawideToggle;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Monocle;
using MonoMod.RuntimeDetour;

internal static class RoomTransitionRegression
{
    private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic |
        BindingFlags.Instance | BindingFlags.Static;
    private static int assertions;

    internal static void Run(string gameDirectory)
    {
        // Supply local file IO without requiring Everest's global content mount.
        using var fileHook = new Hook(typeof(Level).Assembly.GetType("Celeste.Mod.Helpers.FileProxy")!
            .GetMethod("OpenRead", All)!,
            (Func<Func<string, FileStream>, string, FileStream>)((_, path) => File.OpenRead(path)));
        var map = BinaryPacker.FromBinary(Path.Combine(gameDirectory, "Content", "Maps", "LostLevels.bin"));
        var rooms = map.Children.Single(child => child.Name == "levels").Children;
        var source = rooms.Single(room => room.Attr("name", "").Replace("lvl_", "") == "g-05");
        var destination = rooms.Single(room => room.Attr("name", "").Replace("lvl_", "") == "g-06");
        Console.WriteLine($"Farewell map evidence: g-05 ({source.Attributes["x"]},{source.Attributes["y"]}), " +
            $"g-06 ({destination.Attributes["x"]},{destination.Attributes["y"]})");
        var from = new Vector2(Convert.ToSingle(source.Attributes["x"]), Convert.ToSingle(source.Attributes["y"]));
        var to = new Vector2(Convert.ToSingle(destination.Attributes["x"]), Convert.ToSingle(destination.Attributes["y"]));
        var level = New<Level>();
        level.Camera = new Camera(320, 180);
        level.Zoom = level.ZoomTarget = 1;
        level.Session = New<Session>();
        var controller = new RenderController();
        var settingsProperty = typeof(Celeste.Mod.EverestModule).GetProperty("_Settings", All)!;
        object? previousSettings = settingsProperty.GetValue(UltrawideModule.Instance);
        var settings = new UltrawideSettings();
        settingsProperty.SetValue(UltrawideModule.Instance, settings);
        var savedAspect = settings.GameplayAspect;
        using var aspectHook = new Hook(typeof(RenderController).GetMethod("ResolveAspect", All)!,
            (Func<Func<AspectPreset, ViewGeometry>, AspectPreset, ViewGeometry>)((_, _) => new(430)));
        using var roomHook = new Hook(typeof(UltrawideModule).GetMethod("RoomKey", All)!,
            (Func<Func<Level, string>, Level, string>)((_, current) => "regression|" + current.Session.Level));
        try
        {
            settings.GameplayAspect = AspectPreset.Ratio21x9;
            foreach (bool beginning in new[] { false, true })
            {
                level.Session.StartedFromBeginning = beginning;
                foreach (string room in new[] { "g-05", "g-06", "g-05" })
                {
                    level.Session.Level = room;
                    SetTransition(level, null);
                    Check(controller.SelectGeometry(level).BufferWidth == 430, "Ordinary room must retain ultrawide");
                    SetTransition(level, New<Coroutine>());
                    Check(level.Transitioning, "Fixture must exercise the game's real transition flag");
                    for (int frame = 0; frame <= 30; frame++)
                    {
                        level.Camera.Position = room == "g-06"
                            ? Vector2.Lerp(from, to, frame / 30f)
                            : Vector2.Lerp(to, from, frame / 30f);
                        var camera = level.Camera.Position;
                        var viewport = level.Camera.Viewport;
                        Check(controller.SelectGeometry(level).Width == 430,
                            "Room scroll must never fall back to 16:9, including checkpoint entry");
                        Check(level.Camera.Position == camera && level.Camera.Viewport.Equals(viewport),
                            "Aspect selection must preserve native transition camera state");
                    }
                }
            }
            level.Zoom = 1.1f;
            Check(controller.SelectGeometry(level).Width == 320, "Zoom fallback must still apply during transitions");
            level.Zoom = 1;
            level.Camera.Angle = .1f;
            Check(controller.SelectGeometry(level).Width == 320, "Rotation fallback must still apply during transitions");
            level.Camera.Angle = 0;
            level.InCutscene = true;
            Check(controller.SelectGeometry(level).Width == 320, "Cutscene fallback remains explicit");
            level.InCutscene = false;
            level.Camera.Viewport = new Viewport(0, 0, 400, 180);
            Check(controller.SelectGeometry(level).Width == 320, "Custom viewport remains unsupported");
            level.Camera.Viewport = new Viewport(0, 0, 320, 180);
            settings.GameplayAspect = AspectPreset.Original16x9;
            Check(controller.SelectGeometry(level).Width == 320, "Explicit gameplay 16:9 must win during transitions");
        }
        finally
        {
            settings.GameplayAspect = savedAspect;
            settingsProperty.SetValue(UltrawideModule.Instance, previousSettings);
        }
        Console.WriteLine($"PASS: {assertions} Farewell room/checkpoint transition-policy assertions");
    }

    private static T New<T>() where T : class => (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
    private static void SetTransition(Level level, Coroutine? transition)
        => typeof(Level).GetField("transition", All)!.SetValue(level, transition);
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        assertions++;
    }
}
