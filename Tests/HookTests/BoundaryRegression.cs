using System.Reflection;
using System.Runtime.CompilerServices;
using Celeste;
using Celeste.Mod.UltrawideToggle;
using Microsoft.Xna.Framework;
using Monocle;
using MonoMod.RuntimeDetour;

internal static class BoundaryRegression
{
    private static int assertions;
    private readonly record struct Box(float X, float Y, float Width, float Height, Color Color)
    {
        internal float Right => X + Width;
        internal float Bottom => Y + Height;
        internal bool Intersects(Box other) => X < other.Right - .001f &&
            Right > other.X + .001f && Y < other.Bottom - .001f && Bottom > other.Y + .001f;
    }

    internal static void Run()
    {
        var settings = new UltrawideSettings { BoundaryColor = BoundaryColorPreset.Cyan };
        var boxes = new List<Box>();
        void Capture(float x, float y, float width, float height, Color color)
            => boxes.Add(new(x, y, width, height, color));
        settings.BoundaryDim = 100;
        BoundaryOverlay.Draw(new(430), settings, Capture);
        Check(boxes.Count == 0, "Disabled boundary must suppress both strokes and dimming");
        settings.BoundaryEnabled = true;
        BoundaryOverlay.Draw(new(320), settings, Capture);
        Check(boxes.Count == 0, "16:9 gameplay must not receive a boundary or dimming overlay");
        settings.BoundaryDim = 0;
        settings.BoundaryOpacity = 50;
        settings.BoundaryThickness = 4;
        foreach (double width in new[] { 430d, 1280d / 3, 640d, 960d })
            foreach (var style in Enum.GetValues<BoundaryStylePreset>())
                foreach (var outline in new[] { BoundaryOutlinePreset.Off, BoundaryOutlinePreset.Auto })
                {
                    settings.BoundaryStyle = style;
                    settings.BoundaryOutline = outline;
                    boxes.Clear();
                    BoundaryOverlay.Draw(new(width), settings, Capture);
                    float left = (float)((width - 320) * 3);
                    float right = left + 1920;
                    Check(boxes.Count > 0 && boxes.All(box => box.Width > 0 && box.Height > 0 &&
                        box.Y >= 0 && box.Bottom <= 1080), "All styles must produce valid visible-height geometry");
                    Check(boxes.All(box => box.Color.A == (Color.Cyan * .5f).A),
                        "Opacity must apply equally to color and outline");
                    var interior = new Box(left + 20, 20, 1880, 1040, Color.Transparent);
                    Check(boxes.All(box => !box.Intersects(interior)), "Border must not cover the reference interior");
                    Check(boxes.Where(box => box.Height > 10 && box.Width <= 6)
                        .All(box => box.Right <= left + .001f || box.X >= right - .001f),
                        "Vertical thickness must grow outside the original frame");
                    bool overlaps = false;
                    for (int i = 0; i < boxes.Count && !overlaps; i++)
                        for (int j = i + 1; j < boxes.Count; j++)
                            if (boxes[i].Intersects(boxes[j])) { overlaps = true; break; }
                    Check(!overlaps, "Stroke bands must not double opacity by overlapping at corners");
                    if (style == BoundaryStylePreset.CornersOnly)
                        Check(boxes.All(box => box.Width <= 72 && box.Height <= 72),
                            "Corners Only must leave the long middle edge clear");
                }

        settings.BoundaryStyle = BoundaryStylePreset.Solid;
        settings.BoundaryOutline = BoundaryOutlinePreset.Off;
        settings.BoundaryOpacity = 100;
        foreach (int thickness in BoundaryAppearance.Thicknesses)
        {
            settings.BoundaryThickness = thickness;
            boxes.Clear();
            BoundaryOverlay.Draw(new(430), settings, Capture);
            Check(boxes[0].Width == thickness && boxes[2].Height == thickness,
                "Selected thickness must drive both vertical and horizontal strokes");
        }
        settings.BoundaryDim = 30;
        boxes.Clear();
        BoundaryOverlay.Draw(new(430), settings, Capture);
        Check(boxes[0] == new Box(0, 0, 330, 1080, Color.Black * .3f) &&
            boxes[1] == new Box(2250, 0, 330, 1080, Color.Black * .3f),
            "Percentage dimming must cover only the two ultrawide side regions");
        Check(BoundaryAppearance.OutlineColor(BoundaryOutlinePreset.Auto, Color.White) == Color.Black &&
            BoundaryAppearance.OutlineColor(BoundaryOutlinePreset.Auto, Color.Black) == Color.White,
            "Auto outline must contrast with light and dark colors");
        Check(BoundaryAppearance.Colors.Length == 5 && !BoundaryAppearance.Colors.Contains(BoundaryColorPreset.Off),
            "Color row must exclude Off now that Enabled is separate");
        foreach (bool enabledFirst in new[] { false, true })
        {
            var saved = new UltrawideSettings();
            if (enabledFirst) saved.BoundaryEnabled = false;
            saved.BoundaryColor = BoundaryColorPreset.Black;
            saved.ShowReferenceFrame = true;
            if (!enabledFirst) saved.BoundaryEnabled = false;
            saved.MigrateLegacySettings();
            Check(!saved.BoundaryEnabled && saved.BoundaryColor == BoundaryColorPreset.Black,
                "Explicit disabled state must retain its saved color across legacy load order");
        }
        settings.BoundaryThickness = int.MaxValue;
        settings.BoundaryOpacity = int.MinValue;
        settings.BoundaryDim = int.MaxValue;
        settings.BoundaryOutline = (BoundaryOutlinePreset)99;
        settings.BoundaryStyle = (BoundaryStylePreset)99;
        Check(settings.BoundaryThickness == 12 && settings.BoundaryOpacity == 25 &&
            settings.BoundaryDim == 100 && settings.BoundaryOutline == BoundaryOutlinePreset.Auto &&
            settings.BoundaryStyle == BoundaryStylePreset.Solid, "Invalid saved values must normalize safely");

        CheckMenu(settings);
        CheckSavedSettings();
        CheckCornerJoins();
        Console.WriteLine($"PASS: {assertions} boundary geometry, styles, percentages, migration, and menu assertions");
    }

    private static void CheckCornerJoins()
    {
        var settings = new UltrawideSettings
        {
            BoundaryEnabled = true, BoundaryColor = BoundaryColorPreset.Yellow
        };
        var boxes = new List<Box>();
        foreach (int thickness in BoundaryAppearance.Thicknesses)
            foreach (var style in Enum.GetValues<BoundaryStylePreset>())
                foreach (bool outlined in new[] { false, true })
                    foreach (int opacity in new[] { 25, 50, 100 })
                    {
                        settings.BoundaryThickness = thickness;
                        settings.BoundaryStyle = style;
                        settings.BoundaryOutline = outlined ? BoundaryOutlinePreset.Black : BoundaryOutlinePreset.Off;
                        settings.BoundaryOpacity = opacity;
                        boxes.Clear();
                        BoundaryOverlay.Draw(new(430), settings,
                            (x, y, width, height, color) => boxes.Add(new(x, y, width, height, color)));
                        int stroke = thickness + (outlined ? 2 : 0);
                        float outerLeft = 330 - stroke;
                        float outerRight = 2250 + stroke;
                        bool clean = true;
                        foreach (bool right in new[] { false, true })
                            foreach (bool bottom in new[] { false, true })
                                for (int y = 0; y < stroke + 2; y++)
                                    for (int x = 0; x < stroke + 2; x++)
                                    {
                                        float sampleX = right ? outerRight - x - .5f : outerLeft + x + .5f;
                                        float sampleY = bottom ? 1080 - y - .5f : y + .5f;
                                        int edge = Math.Min(x, y);
                                        int hits = 0;
                                        Color actual = default;
                                        foreach (var box in boxes)
                                            if (sampleX >= box.X && sampleX < box.Right &&
                                                sampleY >= box.Y && sampleY < box.Bottom)
                                            {
                                                hits++;
                                                actual = box.Color;
                                            }
                                        Color expected = (outlined && (edge == 0 || edge == stroke - 1)
                                            ? Color.Black : Color.Yellow) * (opacity / 100f);
                                        clean &= edge < stroke ? hits == 1 && actual == expected : hits == 0;
                                    }
                        Check(clean, $"All corners must form one continuous outlined join: {style}, {thickness}px, {opacity}%, outline={outlined}");
                        Check(boxes.All(box => box.X == MathF.Floor(box.X) && box.Y == MathF.Floor(box.Y) &&
                            box.Width == MathF.Floor(box.Width) && box.Height == MathF.Floor(box.Height)),
                            "Rectangle endpoints must already align to Draw.Rect's integer coordinates");
                    }
    }

    private static void CheckMenu(UltrawideSettings settings)
    {
        var atlas = GFX.Gui;
        using var dialogHook = new Hook(typeof(Dialog).GetMethod("Clean", [typeof(string), typeof(Language)])!,
            (Func<Func<string, Language, string>, string, Language, string>)((_, key, _) => key));
        using var textureHook = new Hook(typeof(Atlas).GetProperty("Item")!.GetMethod!,
            (Func<Func<Atlas, string, MTexture>, Atlas, string, MTexture>)
                ((_, _, _) => (MTexture)RuntimeHelpers.GetUninitializedObject(typeof(MTexture))));
        using var heightHook = new Hook(typeof(ActiveFont).GetProperty("LineHeight")!.GetMethod!,
            (Func<Func<float>, float>)(_ => 64));
        using var measureHook = new Hook(typeof(ActiveFont).GetMethod("Measure", [typeof(string)])!,
            (Func<Func<string, Vector2>, string, Vector2>)((_, text) => new(text.Length * 16, 64)));
        try
        {
            GFX.Gui = (Atlas)RuntimeHelpers.GetUninitializedObject(typeof(Atlas));
            var menu = BoundaryMenu.Create(settings);
            var items = (List<TextMenu.Item>)typeof(TextMenuExt.SubMenu).GetField("delayedAddItems",
                BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(menu)!;
            Check(items.Count == 7 && items[0] is TextMenu.OnOff &&
                items.Skip(1).All(item => item is TextMenu.Slider),
                "Native dropdown must contain Enabled first, followed by six arrow selectors");
            CheckFocusColors(menu, items[0]);
            var labels = items.Select(item => (string)item.GetType().GetField("Label")!.GetValue(item)!).ToArray();
            Check(labels.SequenceEqual(new[] { "Enabled", "Color", "Thickness", "Outline",
                "Opacity", "Border Style", "Dim Outside 16:9" }), "Dropdown order must match the requested controls");
        }
        finally { GFX.Gui = atlas; }
    }


    private static void CheckFocusColors(TextMenuExt.SubMenu menu, TextMenu.Item child)
    {
        using var hooks = new InterfaceRenderHooks(new RenderController());
        Color arrow = default, heading = default;
        using var iconHook = new Hook(typeof(MTexture).GetMethod("DrawOutlineCentered",
            [typeof(Vector2), typeof(Color)])!,
            (Action<Action<MTexture, Vector2, Color>, MTexture, Vector2, Color>)
                ((_, _, _, color) => arrow = color));
        using var fontHook = new Hook(typeof(ActiveFont).GetMethod("DrawOutline",
            [typeof(string), typeof(Vector2), typeof(Vector2), typeof(Vector2),
                typeof(Color), typeof(float), typeof(Color)])!,
            (Action<Action<string, Vector2, Vector2, Vector2, Color, float, Color>,
                string, Vector2, Vector2, Vector2, Color, float, Color>)
                ((_, _, _, _, _, color, _, _) => heading = color));
        using var widthHook = new Hook(typeof(MTexture).GetProperty("Width")!.GetMethod!,
            (Func<Func<MTexture, int>, MTexture, int>)((_, _) => 16));
        var container = (TextMenu)RuntimeHelpers.GetUninitializedObject(typeof(TextMenu));
        container.Alpha = 1;
        container.HighlightColor = Color.Lime;
        menu.Container = container;
        menu.Items.Add(child);
        foreach (bool selected in new[] { false, true })
            foreach (bool focused in new[] { false, true })
            {
                menu.Focused = focused;
                menu.Render(Vector2.Zero, selected);
                Color expected = selected && !focused ? Color.Lime : Color.White;
                Check(heading == expected && arrow == expected,
                    "Native boundary heading and arrow must share header focus, including child navigation");
            }
    }

    private static void CheckSavedSettings()
    {
        // Exercise Everest's actual YAML configuration, including saved false.
        var yaml = typeof(Level).Assembly.GetType("Celeste.Mod.YamlHelper")!;
        object serializer = yaml.GetField("Serializer")!.GetValue(null)!;
        object deserializer = yaml.GetField("Deserializer")!.GetValue(null)!;
        var serialize = serializer.GetType().GetMethod("Serialize", [typeof(object)])!;
        var deserialize = deserializer.GetType().GetMethods().Single(method =>
            method.Name == "Deserialize" && method.IsGenericMethodDefinition &&
            method.GetParameters().Length == 1 && method.GetParameters()[0].ParameterType == typeof(string))
            .MakeGenericMethod(typeof(UltrawideSettings));
        foreach (bool enabled in new[] { false, true })
            foreach (var color in new[] { BoundaryColorPreset.Black, BoundaryColorPreset.Cyan })
            {
                var saved = new UltrawideSettings
                {
                    BoundaryEnabled = enabled, BoundaryColor = color,
                    BoundaryThickness = 8, BoundaryOpacity = 25, BoundaryDim = 70,
                    BoundaryStyle = BoundaryStylePreset.Dashed,
                    BoundaryOutline = BoundaryOutlinePreset.White
                };
                string text = (string)serialize.Invoke(serializer, [saved])!;
                var loaded = (UltrawideSettings)deserialize.Invoke(deserializer, [text])!;
                loaded.MigrateLegacySettings();
                Check(loaded.BoundaryEnabled == enabled && loaded.BoundaryColor == color &&
                    loaded.BoundaryThickness == 8 && loaded.BoundaryOpacity == 25 &&
                    loaded.BoundaryDim == 70 && loaded.BoundaryStyle == BoundaryStylePreset.Dashed &&
                    loaded.BoundaryOutline == BoundaryOutlinePreset.White,
                    "Everest YAML round-trip must retain all controls, including disabled with a selected color");
            }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        assertions++;
    }
}
