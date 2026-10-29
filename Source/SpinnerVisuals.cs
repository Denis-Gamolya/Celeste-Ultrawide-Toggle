using System.Reflection;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.UltrawideToggle;

/// <summary>Expands vanilla spinner drawing without changing update-time activation.</summary>
internal sealed class SpinnerVisuals
{
    private static readonly MethodInfo CreateSprites = typeof(CrystalStaticSpinner).GetMethod(
        "CreateSprites", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;
    private static readonly FieldInfo Expanded = typeof(CrystalStaticSpinner).GetField(
        "expanded", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private readonly List<Entity> temporarilyVisible = new();

    internal static bool InVisualView(Vector2 point, Vector2 camera, ViewGeometry geometry)
        => point.X > camera.X - geometry.SideExtension - 16 &&
            point.X < camera.X + 320 + geometry.SideExtension + 16 &&
            point.Y > camera.Y - 16 && point.Y < camera.Y + 180 + 16;

    internal void Prepare(IEnumerable<Entity> entities, Vector2 camera, ViewGeometry geometry)
    {
        if (geometry.Width <= 320) return;
        foreach (Entity entity in entities)
            if (entity.GetType() == typeof(CrystalStaticSpinner) && entity.Active && !entity.Visible &&
                InVisualView(entity.Position, camera, geometry) && !(bool)Expanded.GetValue(entity)!)
                CreateSprites.Invoke(entity, null);
    }

    internal void Show(IEnumerable<Entity> entities, Vector2 camera, ViewGeometry geometry)
    {
        if (geometry.Width <= 320) return;
        foreach (Entity entity in entities)
            if (entity.GetType() == typeof(CrystalStaticSpinner) && entity.Active && !entity.Visible &&
                InVisualView(entity.Position, camera, geometry))
            {
                temporarilyVisible.Add(entity);
                entity.Visible = true;
            }
    }

    internal void Restore()
    {
        foreach (Entity entity in temporarilyVisible) entity.Visible = false;
        temporarilyVisible.Clear();
    }
}
