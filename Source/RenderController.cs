using System.Reflection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Monocle;

namespace Celeste.Mod.UltrawideToggle;

/// <summary>
/// Owns the render transaction. Camera position and viewport are restored even
/// when a render hook throws, so simulation never inherits the visual camera.
/// </summary>
internal sealed class RenderController : IDisposable
{
    private static readonly MethodInfo UpdateView = typeof(Engine).GetMethod(
        "UpdateView", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!;
    private static readonly FieldInfo ExitMode = typeof(LevelExit).GetField(
        "mode", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;
    private bool rendering;
    private bool failed;
    private int lastBufferWidth = 320;
    private float outputWidth = 1920;
    private Scene? lastRenderedScene;
    private LevelExit? exitOwner;
    private readonly MenuAspectTransition exitTransition = new();
    private bool exitTargetPresented;
    private Scene? pendingMenuScene;
    private readonly SpinnerVisuals spinnerVisuals = new();
    public ViewGeometry Geometry { get; private set; } = new(320);
    public int BufferWidth => Geometry.BufferWidth;
    public float VisualWidth => (float)Geometry.Width;
    public bool RenderingWide => rendering && VisualWidth > 320;
    public float HudLeftOffset => RenderingWide && Engine.Scene is Level
        ? -(float)Geometry.SideExtension * 6 : 0;
    public float OutputWidth => outputWidth;
    public string Status { get; private set; } = "Original 16:9";

    public void Render(Action original)
    {
        Scene scene = Engine.Scene;
        if (exitOwner is not null && !ReferenceEquals(exitOwner, scene))
        {
            exitOwner = null;
            pendingMenuScene = null;
        }
        RenderFrame(original);
        lastRenderedScene = scene;
        if (ReferenceEquals(exitOwner, scene))
            AfterExitFrame(Math.Clamp(Engine.RawDeltaTime, 0, .1));
    }

    private void RenderFrame(Action original)
    {
        var level = Engine.Scene as Level;
        Geometry = SelectGeometry(level);
        outputWidth = (float)Geometry.HiresWidth;
        UpdateView.Invoke(Engine.Instance, null);

        if (VisualWidth <= 320)
        {
            EnsureBuffers(level);
            original();
            return;
        }

        if (level is null)
        {
            rendering = true;
            try { original(); }
            finally { rendering = false; }
            return;
        }

        EnsureBuffers(level);
        Camera camera = level.Camera;
        var position = camera.Position;
        var viewport = camera.Viewport;
        rendering = true;
        try
        {
            var spinners = level.Tracker.GetEntities<CrystalStaticSpinner>();
            spinnerVisuals.Prepare(spinners, position, Geometry);
            spinnerVisuals.Show(spinners, position, Geometry);
            // Viewport width includes a possible fractional-pixel guard column.
            // Composition clips it against the exact output aspect.
            camera.Viewport = new Viewport(0, 0, BufferWidth, 180);
            camera.Position = position - new Vector2((float)Geometry.SideExtension, 0);
            original();
            var boundary = UltrawideModule.Settings;
            if (ShouldDrawReferenceFrame(boundary.BoundaryEnabled, level.Wipe))
                DrawReferenceFrame(boundary);
        }
        catch (Exception error)
        {
            failed = true;
            Status = "Render failed: using 16:9 next frame; see log";
            Logger.LogDetailed(error);
        }
        finally
        {
            spinnerVisuals.Restore();
            camera.Viewport = viewport;
            camera.Position = position;
            rendering = false;
        }
    }

    internal ViewGeometry SelectGeometry(Level? level)
    {
        if (failed) return new(320);
        if (level is null)
        {
            if (exitOwner is not null && ReferenceEquals(exitOwner, Engine.Scene))
                return exitTransition.Geometry;
            var display = Engine.Instance.GraphicsDevice.PresentationParameters;
            return SelectNonLevelGeometry(Engine.Scene, UltrawideModule.Settings.MenuAspect,
                display.BackBufferWidth / (double)display.BackBufferHeight);
        }
        if (UltrawideModule.Settings.GameplayAspect == AspectPreset.Original16x9)
        {
            if (!failed) Status = "Original 16:9";
            return new(320);
        }
        if (UltrawideModule.Settings.OriginalViewRooms.Contains(UltrawideModule.RoomKey(level)))
        {
            Status = "Room override: original 16:9";
            return new(320);
        }
        // Ordinary room scrolls change only position. They can keep the wide
        // render transaction while the native transition camera stays intact.
        if (level.InCutscene || level.Zoom != 1 ||
            level.ZoomTarget != 1 || level.Camera.Zoom != 1 || level.Camera.Angle != 0 ||
            level.Camera.Viewport.Width != 320 || level.Camera.Viewport.Height != 180 ||
            level.Camera.Origin != Vector2.Zero || level.ScreenPadding != 0 ||
            (SaveData.Instance is not null && SaveData.Instance.Assists.MirrorMode))
        {
            Status = "Unsupported camera/cutscene fallback: original 16:9";
            return new(320);
        }
        var geometry = ResolveAspect(UltrawideModule.Settings.GameplayAspect);
        Status = $"Visual {geometry.Width:0.###}x180; gameplay 320x180";
        return geometry;
    }

    internal void PrepareSpinnerSprites(Level level)
    {
        var geometry = SelectGeometry(level);
        spinnerVisuals.Prepare(level.Tracker.GetEntities<CrystalStaticSpinner>(), level.Camera.Position, geometry);
    }

    // Loading and exit scenes are part of the menu presentation, even though
    // they are not Overworld instances. Keep the same aspect through them.
    internal static bool UsesMenuAspect(Scene? scene)
        => scene is GameLoader or OverworldLoader or Overworld ||
            scene is LevelExit exit && (LevelExit.Mode)ExitMode.GetValue(exit)! is
                LevelExit.Mode.SaveAndQuit or LevelExit.Mode.GiveUp or LevelExit.Mode.CompletedInterlude;

    internal static ViewGeometry SelectNonLevelGeometry(Scene? scene,
        AspectPreset menuAspect, double displayAspect)
        => UsesMenuAspect(scene) ? AspectPresets.Resolve(menuAspect, displayAspect) : new(320);

    internal void BeginMenuExit(LevelExit exit)
    {
        if (!UsesMenuAspect(exit) || lastRenderedScene is not Level) return;
        BeginMenuExit(exit, Geometry, ResolveAspect(UltrawideModule.Settings.MenuAspect));
    }

    internal void BeginMenuExit(LevelExit exit, ViewGeometry source, ViewGeometry destination)
    {
        exitOwner = exit;
        pendingMenuScene = null;
        exitTransition.Begin(source, destination);
        exitTargetPresented = exitTransition.Complete;
    }

    internal bool ExitTransitionPending(LevelExit exit)
        => ReferenceEquals(exitOwner, exit) && !exitTargetPresented;

    internal void RequestMenuHandoff(Scene destination, LevelExit exit)
    {
        // Intercept the actual scene assignment, not the coroutine's return:
        // the native iterator assigns NextScene before it finishes.
        if (ExitTransitionPending(exit)) pendingMenuScene = destination;
        else Engine.Scene = destination;
    }

    internal void AfterExitFrame(double seconds)
    {
        // Present an exact destination frame before releasing a fast save.
        exitTargetPresented = exitTransition.Complete;
        exitTransition.Advance(seconds);
        if (exitTargetPresented && pendingMenuScene is { } destination)
        {
            pendingMenuScene = null;
            Engine.Scene = destination;
        }
    }

    private static ViewGeometry ResolveAspect(AspectPreset preset)
    {
        var display = Engine.Instance.GraphicsDevice.PresentationParameters;
        return AspectPresets.Resolve(preset,
            display.BackBufferWidth / (double)display.BackBufferHeight);
    }

    private void EnsureBuffers(Level? level)
    {
        if (level is null || GameplayBuffers.Gameplay is null) return;
        if (lastBufferWidth == BufferWidth && GameplayBuffers.Gameplay.Width == BufferWidth)
            return;
        // Full scene initialization also clears LightBuffer's cached atlas while
        // VertexLight indices remain valid. Preserve that atlas across toggles.
        GameplayBufferResize.Resize(BufferWidth);
        lastBufferWidth = BufferWidth;
        Logger.Log("UltrawideToggle", $"Resized screen buffers to {BufferWidth}x180; lighting atlas preserved");
    }

    public void RenderCenteredHud(Action original)
    {
        if (!RenderingWide) { original(); return; }
        var matrix = Engine.ScreenMatrix;
        try
        {
            Engine.ScreenMatrix = Matrix.CreateTranslation(
                (float)Geometry.SideExtension * 6, 0, 0) * matrix;
            original();
        }
        finally { Engine.ScreenMatrix = matrix; }
    }

    // This overlay renders after the scene, so the native wipe cannot cover it.
    // Hide it from the first outgoing-wipe frame, including its completion hold.
    internal static bool ShouldDrawReferenceFrame(bool enabled, ScreenWipe? wipe)
        => enabled && (wipe is null || wipe.WipeIn);

    private void DrawReferenceFrame(UltrawideSettings settings)
    {
        Engine.Instance.GraphicsDevice.Viewport = Engine.Viewport;
        Draw.SpriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend,
            SamplerState.PointClamp, DepthStencilState.None,
            RasterizerState.CullNone, null, Engine.ScreenMatrix);
        try
        {
            BoundaryOverlay.Draw(Geometry, settings, Draw.Rect);
        }
        finally { Draw.SpriteBatch.End(); }
    }

    public void Dispose()
    {
        exitOwner = null;
        if (pendingMenuScene is { } destination)
        {
            pendingMenuScene = null;
            Engine.Scene = destination;
        }
        Geometry = new(320);
        outputWidth = 1920;
        if (Engine.Instance?.GraphicsDevice is not null)
        {
            if (Engine.Scene is Level && GameplayBuffers.Gameplay is not null &&
                GameplayBuffers.Gameplay.Width != 320)
                GameplayBufferResize.Resize(320);
            UpdateView.Invoke(Engine.Instance, null);
        }
    }
}
