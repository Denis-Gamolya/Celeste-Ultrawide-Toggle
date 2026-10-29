using System.Reflection;
using System.Runtime.CompilerServices;
using Celeste;
using Celeste.Mod.UltrawideToggle;
using Microsoft.Xna.Framework;
using Monocle;
using MonoMod.RuntimeDetour;

internal static class HudRegression
{
    private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic |
        BindingFlags.Instance | BindingFlags.Static;

    public static void Run()
    {
        var controller = new RenderController();
        var level = New<Level>();
        var session = New<Session>();
        typeof(Level).GetField("Session", All)!.SetValue(level, session);
        var texture = New<MTexture>();
        typeof(MTexture).GetProperty("Width")!.SetValue(texture, 400);
        var counter = New<TotalStrawberriesDisplay>();
        var digits = New<StrawberriesCounter>();
        Set(counter, "bg", texture);
        Set(counter, "strawberries", digits);
        counter.Y = 96;
        var timer = New<SpeedrunTimerDisplay>();
        Set(timer, "bg", texture);
        Set(timer, "wiggler", New<Wiggler>());
        typeof(Entity).GetProperty("Scene")!.SetValue(timer, level);
        timer.Y = 60;

        var settingsField = typeof(global::Celeste.Settings).GetField("Instance", All)!;
        var savedSettings = settingsField.GetValue(null);
        var savedMatrix = Engine.ScreenMatrix;
        var gameSettings = New<global::Celeste.Settings>();
        gameSettings.SpeedrunClock = SpeedrunType.Chapter;
        Vector2 berryBackground = default;
        Vector2 timerText = default;
        using var sceneHook = new Hook(typeof(Engine).GetProperty("Scene")!.GetMethod!,
            (Func<Func<Scene>, Scene>)(_ => level));
        using var backgroundHook = new Hook(typeof(MTexture).GetMethod("DrawJustified",
            [typeof(Vector2), typeof(Vector2)])!,
            (Action<Action<MTexture, Vector2, Vector2>, MTexture, Vector2, Vector2>)
                ((_, _, position, _) => berryBackground = Vector2.Transform(position, Engine.ScreenMatrix)));
        using var digitsHook = new Hook(typeof(StrawberriesCounter).GetMethod("Render")!,
            (Action<Action<StrawberriesCounter>, StrawberriesCounter>)((_, _) => { }));
        using var timerBackgroundHook = new Hook(typeof(MTexture).GetMethod("Draw", [typeof(Vector2)])!,
            (Action<Action<MTexture, Vector2>, MTexture, Vector2>)((_, _, _) => { }));
        using var timeHook = new Hook(typeof(SpeedrunTimerDisplay).GetMethod("DrawTime")!,
            (Action<Action<Vector2, string, float, bool, bool, bool, float>,
                Vector2, string, float, bool, bool, bool, float>)
                ((_, position, _, _, _, _, _, _) => timerText = Vector2.Transform(position, Engine.ScreenMatrix)));
        try
        {
            settingsField.SetValue(null, gameSettings);
            Engine.ScreenMatrix = Matrix.Identity;
            using var interfaceHooks = new InterfaceRenderHooks(controller);
            var baseline = new List<(Vector2 Background, Vector2 Digits, Vector2 Timer)>();
            int assertions = 0;
            foreach (double width in new[] { 320d, 430d, 1280d / 3, 640d })
            {
                typeof(RenderController).GetProperty("Geometry")!.SetValue(controller, new ViewGeometry(width));
                Set(controller, "rendering", true);
                for (int step = 1; step <= 4; step++)
                {
                    float progress = step / 4f;
                    Set(counter, "DrawLerp", progress);
                    Set(timer, "DrawLerp", progress);
                    controller.RenderCenteredHud(() =>
                    {
                        counter.Render();
                        timer.Render();
                    });
                    Vector2 berryDigits = Vector2.Transform(digits.Position,
                        Matrix.CreateTranslation((float)(width - 320) * 3, 0, 0));
                    var sample = (berryBackground, berryDigits, timerText);
                    if (width == 320) baseline.Add(sample);
                    else
                    {
                        var expected = baseline[step - 1];
                        Check(Vector2.Distance(sample.berryBackground, expected.Background) < 1,
                            "Berry animation must retain baseline screen-edge trajectory");
                        Check(Vector2.Distance(sample.berryDigits, expected.Digits) < 1,
                            "Berry digits must remain attached to the animated panel");
                        Check(Vector2.Distance(sample.timerText, expected.Timer) < .001f,
                            "Timer must retain the same edge anchor and trajectory");
                        assertions += 3;
                    }
                    if (step == 4)
                    {
                        Check(Math.Abs(berryDigits.X - 32) < 1 && Math.Abs(timerText.X - 32) < .001f,
                            "Fully visible timer and berry count must share x = 32");
                        assertions++;
                    }
                }
            }
            Console.WriteLine($"PASS: {assertions} actual berry/timer render-coordinate assertions");
        }
        finally
        {
            settingsField.SetValue(null, savedSettings);
            Engine.ScreenMatrix = savedMatrix;
        }
    }

    private static T New<T>() where T : class
        => (T)RuntimeHelpers.GetUninitializedObject(typeof(T));

    private static void Set(object target, string name, object value)
        => target.GetType().GetField(name, All)!.SetValue(target, value);

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
