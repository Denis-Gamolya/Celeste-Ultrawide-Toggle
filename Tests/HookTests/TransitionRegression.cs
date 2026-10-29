using System.Reflection;
using System.Runtime.CompilerServices;
using Celeste;
using Celeste.Mod.UltrawideToggle;
using Monocle;
using MonoMod.RuntimeDetour;

internal static class TransitionRegression
{
    private static int assertions;
    private const BindingFlags All = BindingFlags.Instance | BindingFlags.Public |
        BindingFlags.NonPublic | BindingFlags.Static;

    public static void Run()
    {
        assertions = 0;
        foreach (var (source, destination) in new[] { (320d, 430d), (430d, 320d),
            (320d, 640d), (640d, 320d), (430d, 1280d / 3), (430d, 430d) })
        {
            var transition = new MenuAspectTransition();
            transition.Begin(new(source), new(destination));
            Check(transition.Geometry.Width == source, "First exit frame must match outgoing gameplay");
            double previous = source;
            for (int frame = 0; frame < 30; frame++)
            {
                transition.Advance(1d / 60);
                double current = transition.Geometry.Width;
                Check(current >= Math.Min(source, destination) - .001 &&
                    current <= Math.Max(source, destination) + .001, "Ease must not overshoot");
                Check(destination >= source ? current >= previous : current <= previous,
                    "Presentation width must move monotonically");
                previous = current;
            }
            Check(transition.Complete && Math.Abs(transition.Geometry.Width - destination) < .001,
                "Transition must finish at the exact menu aspect");
        }

        // Capture the actual engine assignment; no game loop or GPU is needed.
        Scene? requested = null;
        using var sceneHook = new Hook(typeof(Engine).GetProperty("Scene")!.SetMethod!,
            (Action<Action<Scene>, Scene>)((_, scene) => requested = scene));
        var controller = new RenderController();
        var exit = (LevelExit)RuntimeHelpers.GetUninitializedObject(typeof(LevelExit));
        var menu = (OverworldLoader)RuntimeHelpers.GetUninitializedObject(typeof(OverworldLoader));
        controller.BeginMenuExit(exit, new(320), new(430));
        controller.RequestMenuHandoff(menu, exit);
        Check(requested is null && controller.ExitTransitionPending(exit),
            "Fast saves must queue the handoff rather than cut the ease short");
        controller.AfterExitFrame(MenuAspectTransition.Duration);
        Check(requested is null && controller.ExitTransitionPending(exit),
            "Elapsed time alone must not release an unpresented final frame");
        controller.AfterExitFrame(0);
        Check(ReferenceEquals(requested, menu) && !controller.ExitTransitionPending(exit),
            "A presented destination frame must release the exact native destination");
        requested = null;
        controller.AfterExitFrame(0);
        Check(requested is null, "Handoff must be assigned only once");

        controller.BeginMenuExit(exit, new(430), new(430));
        controller.RequestMenuHandoff(menu, exit);
        Check(ReferenceEquals(requested, menu) && !controller.ExitTransitionPending(exit),
            "Equal aspects must preserve immediate native handoff");

        requested = null;
        controller.BeginMenuExit(exit, new(430), new(320));
        controller.RequestMenuHandoff(menu, exit);
        // Dispose must not strand the player in an exit scene during hot unload.
        controller.Dispose();
        Check(ReferenceEquals(requested, menu), "Unload must release a pending scene safely");
        Console.WriteLine($"PASS: {assertions} animated menu-transition and deferred-handoff assertions");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        assertions++;
    }
}
