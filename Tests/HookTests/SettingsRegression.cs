using System.Runtime.CompilerServices;
using Celeste;
using Celeste.Mod.UltrawideToggle;

internal static class SettingsRegression
{
    private static int assertions;
    public static void Run()
    {
        assertions = 0;
        var settings = new UltrawideSettings();
        Check(settings.GameplayAspect == AspectPreset.Original16x9,
            "New settings must default gameplay to 16:9");
        foreach (AspectPreset preset in AspectPresets.Menus)
        {
            settings.SelectView(preset);
            Check(settings.GameplayAspect == preset,
                "Direct shortcut must select its gameplay aspect");
            settings.SelectView(null);
            Check(settings.GameplayAspect == AspectPreset.Original16x9,
                "16:9 shortcut must select the same setting as the slider");
        }
        var oldDisabled = new UltrawideSettings { Ultrawide = false, Aspect = AspectPreset.Ratio32x9 };
        oldDisabled.MigrateLegacySettings();
        Check(oldDisabled.GameplayAspect == AspectPreset.Original16x9,
            "Old disabled gameplay must migrate to 16:9");
        var oldEnabled = new UltrawideSettings { Ultrawide = true, Aspect = AspectPreset.Resolution3440x1440 };
        oldEnabled.MigrateLegacySettings();
        Check(oldEnabled.GameplayAspect == AspectPreset.Ratio21x9,
            "Old enabled resolution preset must migrate to adaptive 21:9");
        foreach (bool newSettingFirst in new[] { true, false })
        {
            var explicitSetting = new UltrawideSettings();
            if (newSettingFirst) explicitSetting.GameplayAspect = AspectPreset.Original16x9;
            explicitSetting.Ultrawide = true;
            explicitSetting.Aspect = AspectPreset.Ratio48x9;
            if (!newSettingFirst) explicitSetting.GameplayAspect = AspectPreset.Original16x9;
            explicitSetting.MigrateLegacySettings();
            explicitSetting.MigrateLegacySettings();
            Check(explicitSetting.GameplayAspect == AspectPreset.Original16x9,
                "New setting must win regardless of legacy-property load order");
        }

        // Construct only the state needed for this GPU/font-independent check.
        var snowballs = (TextMenu.OnOff)RuntimeHelpers.GetUninitializedObject(typeof(TextMenu.OnOff));
        var oshiro = (TextMenu.OnOff)RuntimeHelpers.GetUninitializedObject(typeof(TextMenu.OnOff));
        var shots = (TextMenu.OnOff)RuntimeHelpers.GetUninitializedObject(typeof(TextMenu.OnOff));
        settings.PreserveSnowballs = false;
        settings.PreserveOshiro = false;
        settings.PreserveBossShots = false;
        UltrawideModule.RestoreEntityRules(settings, snowballs, oshiro, shots);
        Check(settings.PreserveSnowballs && settings.PreserveOshiro && settings.PreserveBossShots,
            "Restore must update the underlying entity policies");
        Check(snowballs.Index == 1 && oshiro.Index == 1 && shots.Index == 1,
            "Restore must immediately update all three displayed switches");
        settings.View2560x1080.Keys.Add(Microsoft.Xna.Framework.Input.Keys.F8);
        settings.View3440x1440.Keys.Add(Microsoft.Xna.Framework.Input.Keys.F8);
        settings.View3840x1600.Buttons.Add(Microsoft.Xna.Framework.Input.Buttons.LeftShoulder);
        settings.ToggleView.Keys.Add(Microsoft.Xna.Framework.Input.Keys.F7);
        settings.MigrateLegacyBindings();
        Check(settings.View21x9.Keys.Count == 1 && settings.View21x9.Buttons.Count == 1,
            "Legacy 21:9 bindings must merge without duplicates");
        Check(settings.View2560x1080.Keys.Count == 0 && settings.ToggleView.Keys.Count == 0,
            "Obsolete bindings must be cleared after migration");
        settings.MigrateLegacyBindings();
        Check(settings.View21x9.Keys.Count == 1 && settings.View21x9.Buttons.Count == 1,
            "Migration must be idempotent");
        var bindings = typeof(UltrawideSettings).GetProperties().Where(property =>
            property.PropertyType == typeof(Celeste.Mod.ButtonBinding) &&
            !Attribute.IsDefined(property, typeof(Celeste.Mod.SettingIgnoreAttribute))).ToArray();
        Check(bindings.Length == 5 && bindings.All(property => property.Name != "ToggleView"),
            "Native config menus must expose exactly five direct-view bindings");
        CheckBoundaryColors();
        Console.WriteLine($"PASS: {assertions} aspect-selection, settings/binding-migration, and menu-reset assertions");
    }


    private static void CheckBoundaryColors()
    {
        Check(!new UltrawideSettings().BoundaryEnabled,
            "New boundary settings must default to Off");
        foreach (bool enabled in new[] { false, true })
        {
            var legacy = new UltrawideSettings { ShowReferenceFrame = enabled };
            legacy.MigrateLegacySettings();
            Check(legacy.BoundaryEnabled == enabled && legacy.BoundaryColor == BoundaryColorPreset.Yellow,
                "Legacy boundary switch must migrate to Yellow or Off");
        }
        foreach (var preset in new[] { BoundaryColorPreset.Off, BoundaryColorPreset.Black, BoundaryColorPreset.Cyan })
            foreach (bool newFirst in new[] { false, true })
            {
                var saved = new UltrawideSettings();
                if (newFirst) saved.BoundaryColor = preset;
                saved.ShowReferenceFrame = true;
                if (!newFirst) saved.BoundaryColor = preset;
                saved.MigrateLegacySettings();
                saved.MigrateLegacySettings();
                Check(saved.BoundaryEnabled == (preset != BoundaryColorPreset.Off) &&
                    saved.BoundaryColor == (preset == BoundaryColorPreset.Off ? BoundaryColorPreset.Yellow : preset),
                    "Saved boundary color must win over legacy flags and repeated migration");
            }
        Check(!new UltrawideSettings { ShowReferenceFrame = true,
            BoundaryColor = BoundaryColorPreset.Off }.ShowReferenceFrame,
            "Saving Off must also clear the serialized legacy enabled flag");
        var invalid = new UltrawideSettings { BoundaryColor = (BoundaryColorPreset)999 };
        Check(invalid.BoundaryColor == BoundaryColorPreset.Off,
            "Invalid boundary values must safely select Off");
        var expected = new[] { Microsoft.Xna.Framework.Color.Transparent,
            Microsoft.Xna.Framework.Color.Yellow, Microsoft.Xna.Framework.Color.White,
            Microsoft.Xna.Framework.Color.Black, Microsoft.Xna.Framework.Color.Cyan,
            Microsoft.Xna.Framework.Color.Magenta };
        Check(BoundaryColors.Choices.Length == expected.Length,
            "Boundary selector must expose all six choices");
        for (int index = 0; index < expected.Length; index++)
            Check(BoundaryColors.Resolve(BoundaryColors.Choices[index]) == expected[index],
                "Each boundary choice must resolve to its intended full-opacity color");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        assertions++;
    }
}
