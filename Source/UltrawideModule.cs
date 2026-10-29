using System.Reflection;
using FMOD.Studio;
using Microsoft.Xna.Framework;
using Monocle;
using MonoMod.RuntimeDetour;
using SubHudRenderer = global::Celeste.Mod.UI.SubHudRenderer;

namespace Celeste.Mod.UltrawideToggle;

public sealed class UltrawideModule : EverestModule
{
    public static UltrawideModule Instance { get; private set; } = null!;
    public static UltrawideSettings Settings => (UltrawideSettings)Instance._Settings;
    public override Type SettingsType => typeof(UltrawideSettings);
    internal RenderController Renderer { get; private set; } = null!;
    private RenderHooks? renderHooks;
    private EntityBoundsHooks? entityHooks;
    private InterfaceRenderHooks? interfaceHooks;
    private BlackholeBackgroundHooks? backgroundHooks;
    private Hook? subHudHook;

    public UltrawideModule() => Instance = this;

    public override void LoadSettings()
    {
        base.LoadSettings();
        Settings.MigrateLegacySettings();
    }

    public override void Load()
    {
        Renderer = new RenderController();
        try
        {
            renderHooks = new RenderHooks(Renderer);
            entityHooks = new EntityBoundsHooks(Renderer);
            interfaceHooks = new InterfaceRenderHooks(Renderer);
            backgroundHooks = new BlackholeBackgroundHooks(Renderer);
            On.Monocle.Engine.RenderCore += Render;
            On.Monocle.Engine.Update += Update;
            On.Celeste.HiresRenderer.Render += RenderHires;
            On.Celeste.LevelExit.Begin += BeginExit;
            On.Celeste.Level.Update += UpdateLevel;
            subHudHook = new Hook(typeof(SubHudRenderer).GetMethod("Render")!,
                (Action<Action<SubHudRenderer, Scene>, SubHudRenderer, Scene>)RenderSubHud);
            Logger.Log("UltrawideToggle", "Loaded 1.0.2; unified gameplay aspect and animated menu handoff.");
        }
        catch (Exception error)
        {
            Unload();
            Logger.LogDetailed(error);
            throw;
        }
    }

    public override void Unload()
    {
        On.Monocle.Engine.RenderCore -= Render;
        On.Monocle.Engine.Update -= Update;
        On.Celeste.HiresRenderer.Render -= RenderHires;
        On.Celeste.LevelExit.Begin -= BeginExit;
        On.Celeste.Level.Update -= UpdateLevel;
        subHudHook?.Dispose();
        entityHooks?.Dispose();
        backgroundHooks?.Dispose();
        interfaceHooks?.Dispose();
        renderHooks?.Dispose();
        Renderer?.Dispose();
    }

    private void Update(On.Monocle.Engine.orig_Update orig, Engine engine,
        GameTime time)
    {
        orig(engine, time);
#if SMOKE
        SmokeProbe.Update(this);
#endif
        if (Engine.Scene is not Level level || level.Paused || level.Frozen)
            return;

        // The first direct selection wins if users assign overlapping keys.
        if (Settings.View16x9.Pressed) Settings.SelectView(null);
        else if (Settings.ViewAuto.Pressed) Settings.SelectView(AspectPreset.Auto);
        else if (Settings.View21x9.Pressed) Settings.SelectView(AspectPreset.Ratio21x9);
        else if (Settings.View32x9.Pressed) Settings.SelectView(AspectPreset.Ratio32x9);
        else if (Settings.View48x9.Pressed) Settings.SelectView(AspectPreset.Ratio48x9);
        else return;
        SaveSettings();
    }

    private void Render(On.Monocle.Engine.orig_RenderCore orig, Engine engine)
    {
#if SMOKE
        SmokeProbe.BeforeRender();
#endif
        Renderer.Render(() => orig(engine));
#if SMOKE
        SmokeProbe.AfterRender(Renderer);
#endif
    }

    private void RenderHires(On.Celeste.HiresRenderer.orig_Render orig,
        HiresRenderer renderer, Scene scene)
        => Renderer.RenderCenteredHud(() => orig(renderer, scene));

    private void BeginExit(On.Celeste.LevelExit.orig_Begin orig, LevelExit exit)
    {
        Renderer.BeginMenuExit(exit);
        orig(exit);
    }

    private void UpdateLevel(On.Celeste.Level.orig_Update original, Level level)
    {
        original(level);
        if (ReferenceEquals(Engine.Scene, level)) Renderer.PrepareSpinnerSprites(level);
    }

    private void RenderSubHud(Action<SubHudRenderer, Scene> orig,
        SubHudRenderer renderer, Scene scene)
        => Renderer.RenderCenteredHud(() => orig(renderer, scene));

    public override void CreateModMenuSection(TextMenu menu, bool inGame,
        EventInstance snapshot)
    {
        menu.Add(new TextMenu.SubHeader("Ultrawide Toggle"));
        AddAspectSlider(menu, "Gameplay Aspect Ratio", AspectPresets.Menus,
            Settings.GameplayAspect, value => Settings.GameplayAspect = value);
        AddAspectSlider(menu, "Main Menu Aspect Ratio", AspectPresets.Menus,
            Settings.MenuAspect, value => Settings.MenuAspect = value);
        menu.Add(BoundaryMenu.Create(Settings));

        var snowballs = new TextMenu.OnOff("Original Snowball Spawn/Reset", Settings.PreserveSnowballs);
        var oshiro = new TextMenu.OnOff("Original Oshiro Attack Boundaries", Settings.PreserveOshiro);
        var shots = new TextMenu.OnOff("Original Boss-Shot Lifetime", Settings.PreserveBossShots);
        menu.Add(snowballs.Change(value => Settings.PreserveSnowballs = value));
        menu.Add(oshiro.Change(value => Settings.PreserveOshiro = value));
        menu.Add(shots.Change(value => Settings.PreserveBossShots = value));
        menu.Add(new TextMenu.Button("Restore Original Entity Rules").Pressed(() =>
        {
            RestoreEntityRules(Settings, snowballs, oshiro, shots);
            SaveSettings();
        }));
        if (Engine.Scene is Level level)
        {
            var key = RoomKey(level);
            menu.Add(new TextMenu.OnOff("Always Use 16:9 in This Room",
                Settings.OriginalViewRooms.Contains(key)).Change(value =>
            {
                if (value) Settings.OriginalViewRooms.Add(key);
                else Settings.OriginalViewRooms.Remove(key);
            }));
        }
        menu.Add(new TextMenu.Button("Keyboard Config").Pressed(() =>
            OpenViewBindings(menu, controller: false)));
        menu.Add(new TextMenu.Button("Controller Config").Pressed(() =>
            OpenViewBindings(menu, controller: true)));
    }

    private static void AddAspectSlider(TextMenu menu, string label,
        AspectPreset[] choices, AspectPreset current, Action<AspectPreset> changed)
    {
        int selected = Math.Max(0, Array.IndexOf(choices, AspectPresets.Normalize(current)));
        menu.Add(new TextMenu.Slider(label, index => AspectPresets.Label(choices[index]),
            0, choices.Length - 1, selected).Change(index => changed(choices[index])));
    }

    private void OpenViewBindings(TextMenu parent, bool controller)
    {
        parent.Focused = false;
        TextMenu bindings = controller
            ? new ModuleSettingsButtonConfigUI(this)
            : new ModuleSettingsKeyboardConfigUI(this);
        // Keep Everest's native capture, reset, navigation, and input lifecycle.
        bindings.OnClose = () =>
        {
            parent.Focused = true;
            SaveSettings();
        };
        Engine.Scene.Add(bindings);
        Engine.Scene.OnEndOfFrame += () => Engine.Scene.Entities.UpdateLists();
    }

    internal static void RestoreEntityRules(UltrawideSettings settings,
        TextMenu.OnOff snowballs, TextMenu.OnOff oshiro, TextMenu.OnOff shots)
    {
        settings.PreserveSnowballs = true;
        settings.PreserveOshiro = true;
        settings.PreserveBossShots = true;
        // OnOff stores its displayed value independently from the settings.
        snowballs.Index = 1;
        oshiro.Index = 1;
        shots.Index = 1;
    }

    internal static string RoomKey(Level level)
        => $"{level.Session.Area.GetSID()}|{level.Session.Area.Mode}|{level.Session.Level}";
}
