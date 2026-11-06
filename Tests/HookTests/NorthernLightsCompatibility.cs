using System.IO.Compression;
using System.Reflection;
using Celeste;
using Celeste.Mod.UltrawideToggle;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;

internal static class NorthernLightsCompatibility
{
    internal static void Run(string gameDirectory)
    {
        string path = Path.Combine(gameDirectory, "Mods", "MaxHelpingHand.zip");
        if (!File.Exists(path))
        {
            Console.WriteLine("SKIP: MaxHelpingHand is unavailable for the Northern Lights compatibility check");
            return;
        }

        using var zip = ZipFile.OpenRead(path);
        using var dll = zip.GetEntry("bin/MaxHelpingHand.dll")!.Open();
        using var bytes = new MemoryStream();
        dll.CopyTo(bytes);
        var type = Assembly.Load(bytes.ToArray())
            .GetType("Celeste.Mod.MaxHelpingHand.Effects.NorthernLightsCustomColors", throwOnError: true)!;
        var edit = type.GetMethod("hookBeforeRender", BindingFlags.Static | BindingFlags.NonPublic)!
            .CreateDelegate<ILContext.Manipulator>();
        var target = typeof(NorthernLights).GetMethod(nameof(NorthernLights.BeforeRender))!;

        using (var ultrawide = new VanillaEffectHooks(new RenderController()))
        using (var helpingHand = new ILHook(target, edit)) { }
        using (var helpingHand = new ILHook(target, edit))
        using (var ultrawide = new VanillaEffectHooks(new RenderController())) { }
        Console.WriteLine("PASS: the installed MaxHelpingHand Northern Lights edit compiles with Ultrawide Toggle in both load orders");
    }
}
