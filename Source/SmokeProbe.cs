#if SMOKE
using System.Globalization;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.UltrawideToggle;

/// <summary>Isolated development harness; excluded from release builds.</summary>
internal static class SmokeProbe
{
    private static readonly string? DirectoryPath =
        Environment.GetEnvironmentVariable("ULTRAWIDE_SMOKE_DIRECTORY");
    private static int frame;
    private static int renderChecks;
    private static bool started;
    private static Vector2 before;
    private static int beforeWidth;
    private static Snowball? snowball;
    private static readonly List<string> samples = new();

    public static void Update(UltrawideModule module)
    {
        if (string.IsNullOrEmpty(DirectoryPath)) return;
        System.IO.Directory.CreateDirectory(DirectoryPath);
        if (!started && Engine.Scene is Overworld && AreaData.Areas.Count > 4)
        {
            started = true;
            SaveData.InitializeDebugMode(false);
            UltrawideModule.Settings.BoundaryEnabled = true;
            UltrawideModule.Settings.BoundaryColor = BoundaryColorPreset.Yellow;
            Engine.Scene = new LevelLoader(new Session(new AreaKey(4)), null);
            File.WriteAllText(Path.Combine(DirectoryPath, "started.txt"), "Loading vanilla chapter 4");
        }
        if (Engine.Scene is not Level level || level.Transitioning) return;
        frame++;
        var player = level.Tracker.GetEntity<Player>();
        if (player is not null) player.Active = false;
        if (frame == 30)
        {
            snowball = new Snowball();
            level.Add(snowball);
        }
        if (snowball is not null) snowball.Collidable = false;
        // Multiple toggles exercise disposal and camera restoration without
        // restarting the room or its snowball lifecycle.
        UltrawideModule.Settings.GameplayAspect = frame >= 90 && frame < 210
            ? AspectPreset.Ratio21x9 : AspectPreset.Original16x9;
        if (frame >= 35 && snowball is not null)
            samples.Add(string.Create(CultureInfo.InvariantCulture,
                $"{frame},{level.Camera.X},{level.Camera.Y},{snowball.X},{snowball.Y}"));
        if (frame == 270)
        {
            File.WriteAllLines(Path.Combine(DirectoryPath, "trajectory.csv"),
                new[] { "frame,cameraX,cameraY,snowballX,snowballY" }.Concat(samples));
            File.WriteAllText(Path.Combine(DirectoryPath, "result.txt"),
                $"PASS: {renderChecks} camera restoration checks; room={level.Session.Level}; rendered aspect sequence 16:9/21:9/16:9");
            Engine.Instance.Exit();
        }
    }

    public static void BeforeRender()
    {
        if (string.IsNullOrEmpty(DirectoryPath) || Engine.Scene is not Level level) return;
        before = level.Camera.Position;
        beforeWidth = level.Camera.Viewport.Width;
    }

    public static void AfterRender(RenderController renderer)
    {
        if (string.IsNullOrEmpty(DirectoryPath) || Engine.Scene is not Level level) return;
        if (level.Camera.Position != before || level.Camera.Viewport.Width != beforeWidth)
            throw new InvalidOperationException("Render leaked visual camera into simulation");
        renderChecks++;
        if (frame is 60 or 120 or 165 or 240)
        {
            using var image = File.Create(Path.Combine(DirectoryPath, $"gameplay-{frame}.png"));
            var target = (Microsoft.Xna.Framework.Graphics.RenderTarget2D)GameplayBuffers.Level;
            target.SaveAsPng(image, target.Width, target.Height);
            File.AppendAllText(Path.Combine(DirectoryPath, "frames.txt"),
                $"{frame}: {renderer.Status}; buffer {target.Width}x{target.Height}\n");
        }
    }
}
#endif
