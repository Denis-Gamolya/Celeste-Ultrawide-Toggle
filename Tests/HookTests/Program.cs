using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;

string gameDirectory = Path.GetFullPath(args[0]);
AssemblyLoadContext.Default.Resolving += (_, name) =>
{
    foreach (string directory in new[] { gameDirectory, Path.Combine(gameDirectory, "everest-lib") })
    {
        string path = Path.Combine(directory, name.Name + ".dll");
        if (File.Exists(path)) return AssemblyLoadContext.Default.LoadFromAssemblyPath(path);
    }
    return null;
};
Run(gameDirectory);

[MethodImpl(MethodImplOptions.NoInlining)]
static void Run(string gameDirectory)
{
    var module = new Celeste.Mod.UltrawideToggle.UltrawideModule();
    module.Load();
    module.Unload();
    module.Load();
    module.Unload();
    Console.WriteLine("PASS: all render and vanilla-entity hooks installed/unloaded twice on local Everest assemblies");
    BufferResizeRegression.Run();
    SettingsRegression.Run();
    HudRegression.Run();
    InterfaceRegression.Run(Path.Combine(gameDirectory,
        "ModDevelopment", "UltrawideToggle", "Dialog", "English.txt"));
    TransitionRegression.Run();
    VisualRegression.Run();
    BoundaryRegression.Run();
    BlackholeRegression.Run();
    RoomTransitionRegression.Run(gameDirectory);
}
