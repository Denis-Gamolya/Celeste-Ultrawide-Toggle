using Celeste.Mod;

namespace Celeste.Mod.UltrawideToggle;

public sealed class UltrawideSettings : EverestModuleSettings
{
    private AspectPreset gameplayAspect = AspectPreset.Original16x9;
    private bool hasGameplayAspect;
    public AspectPreset GameplayAspect
    {
        get => gameplayAspect;
        set
        {
            gameplayAspect = AspectPresets.Normalize(value);
            hasGameplayAspect = true;
        }
    }
    // Retain old fields only so settings from 0.1.4 and earlier can migrate.
    [SettingIgnore] public bool Ultrawide { get; set; }
    private AspectPreset aspect = AspectPreset.Ratio21x9;
    [SettingIgnore] public AspectPreset Aspect
    {
        get => aspect;
        set => aspect = AspectPresets.Normalize(value);
    }
    public AspectPreset MenuAspect { get; set; } = AspectPreset.Auto;
    private bool legacyShowReferenceFrame;
    [SettingIgnore]
    public bool ShowReferenceFrame
    {
        get => hasBoundaryEnabled ? boundaryEnabled : hasBoundaryColor
            ? boundaryColor != BoundaryColorPreset.Off : legacyShowReferenceFrame;
        set => legacyShowReferenceFrame = value;
    }
    private BoundaryColorPreset boundaryColor = BoundaryColorPreset.Yellow;
    private bool hasBoundaryColor;
    public BoundaryColorPreset BoundaryColor
    {
        get => boundaryColor;
        set
        {
            boundaryColor = Enum.IsDefined(value) ? value : BoundaryColorPreset.Off;
            hasBoundaryColor = true;
        }
    }
    private bool boundaryEnabled;
    private bool hasBoundaryEnabled;
    public bool BoundaryEnabled
    {
        get => boundaryEnabled;
        set
        {
            boundaryEnabled = value;
            hasBoundaryEnabled = true;
        }
    }
    private int boundaryThickness = 1;
    public int BoundaryThickness
    {
        get => boundaryThickness;
        set => boundaryThickness = BoundaryAppearance.NormalizeThickness(value);
    }
    private BoundaryOutlinePreset boundaryOutline = BoundaryOutlinePreset.Auto;
    public BoundaryOutlinePreset BoundaryOutline
    {
        get => boundaryOutline;
        set => boundaryOutline = Enum.IsDefined(value) ? value : BoundaryOutlinePreset.Auto;
    }
    private int boundaryOpacity = 100;
    public int BoundaryOpacity
    {
        get => boundaryOpacity;
        set => boundaryOpacity = ((Math.Clamp(value, 25, 100) + 12) / 25) * 25;
    }
    private BoundaryStylePreset boundaryStyle;
    public BoundaryStylePreset BoundaryStyle
    {
        get => boundaryStyle;
        set => boundaryStyle = Enum.IsDefined(value) ? value : BoundaryStylePreset.Solid;
    }
    private int boundaryDim;
    public int BoundaryDim
    {
        get => boundaryDim;
        set => boundaryDim = ((Math.Clamp(value, 0, 100) + 5) / 10) * 10;
    }
    public bool PreserveSnowballs { get; set; } = true;
    public bool PreserveOshiro { get; set; } = true;
    public bool PreserveBossShots { get; set; } = true;
    [SettingName("modoptions_ultrawidetoggle_view16x9")]
    public ButtonBinding View16x9 { get; set; } = new();
    [SettingName("modoptions_ultrawidetoggle_viewauto")]
    public ButtonBinding ViewAuto { get; set; } = new();
    [SettingName("modoptions_ultrawidetoggle_view21x9")]
    public ButtonBinding View21x9 { get; set; } = new();
    [SettingName("modoptions_ultrawidetoggle_view32x9")]
    public ButtonBinding View32x9 { get; set; } = new();
    [SettingName("modoptions_ultrawidetoggle_view48x9")]
    public ButtonBinding View48x9 { get; set; } = new();

    // Read compatibility for 0.1.2 settings; never show or activate these.
    [SettingIgnore] public ButtonBinding ToggleView { get; set; } = new();
    [SettingIgnore] public ButtonBinding View3440x1440 { get; set; } = new();
    [SettingIgnore] public ButtonBinding View2560x1080 { get; set; } = new();
    [SettingIgnore] public ButtonBinding View3840x1600 { get; set; } = new();

    [SettingIgnore]
    public HashSet<string> OriginalViewRooms { get; set; } = new();

    internal void SelectView(AspectPreset? preset)
    {
        GameplayAspect = preset ?? AspectPreset.Original16x9;
    }

    internal void MigrateLegacySettings()
    {
        // An explicitly saved new selector always wins, independent of the
        // order YAML assigns the old properties during deserialization.
        if (!hasGameplayAspect)
            GameplayAspect = Ultrawide ? Aspect : AspectPreset.Original16x9;
        // Explicit new choices, including Off, win regardless of YAML order.
        if (!hasBoundaryColor)
            BoundaryColor = legacyShowReferenceFrame
                ? BoundaryColorPreset.Yellow : BoundaryColorPreset.Off;
        if (!hasBoundaryEnabled)
            BoundaryEnabled = BoundaryColor != BoundaryColorPreset.Off;
        // Off was a combined color/toggle in 0.1.9. Keep a usable color when
        // disabled, so enabling no longer requires changing the color first.
        if (BoundaryColor == BoundaryColorPreset.Off)
            BoundaryColor = BoundaryColorPreset.Yellow;
        MigrateLegacyBindings();
    }

    internal void MigrateLegacyBindings()
    {
        foreach (var old in new[] { View3440x1440, View2560x1080, View3840x1600 })
        {
            foreach (var key in old.Keys)
                if (!View21x9.Keys.Contains(key)) View21x9.Keys.Add(key);
            foreach (var button in old.Buttons)
                if (!View21x9.Buttons.Contains(button)) View21x9.Buttons.Add(button);
            foreach (var mouse in old.MouseButtons)
                if (!View21x9.MouseButtons.Contains(mouse)) View21x9.MouseButtons.Add(mouse);
            old.Keys.Clear();
            old.Buttons.Clear();
            old.MouseButtons.Clear();
        }
        ToggleView.Keys.Clear();
        ToggleView.Buttons.Clear();
        ToggleView.MouseButtons.Clear();
    }
}
