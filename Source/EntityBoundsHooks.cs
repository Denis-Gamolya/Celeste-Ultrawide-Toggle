using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Xna.Framework;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;
using Monocle;

namespace Celeste.Mod.UltrawideToggle;

/// <summary>
/// Vanilla simulation already has original bounds. Only opt-out policies need
/// narrow visual-edge substitutions, pinned for an entity's current lifecycle.
/// </summary>
internal sealed class EntityBoundsHooks : IDisposable
{
    private sealed class Policy { public bool Original = true; }
    private readonly ConditionalWeakTable<Entity, Policy> policies = new();
    private readonly ConditionalWeakTable<object, Level> skippedBirdZooms = new();
    private readonly List<ILHook> hooks = new();
    private readonly RenderController renderer;
    private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic |
        BindingFlags.Instance | BindingFlags.Static;

    public EntityBoundsHooks(RenderController renderer)
    {
        this.renderer = renderer;
        try
        {
            On.Celeste.Snowball.ResetPosition += ResetSnowball;
            On.Celeste.AngryOshiro.Added += AddOshiro;
            On.Celeste.FinalBossShot.Added += AddShot;
            PatchEdges(typeof(Snowball), "ResetPosition", 1, 0);
            PatchEdges(typeof(Snowball), "Update", 0, 1);
            PatchEdges(typeof(AngryOshiro), "Added", 0, 1);
            PatchEdges(typeof(AngryOshiro), "ChaseUpdate", 0, 1);
            PatchEdges(typeof(AngryOshiro), "ChargeUpUpdate", 0, 1);
            PatchEdges(typeof(AngryOshiro), "AttackUpdate", 1, 1);
            PatchEdges(typeof(AngryOshiro), "HurtUpdate", 0, 1);
            hooks.Add(new ILHook(typeof(FinalBossShot).GetMethod("Update", All)!, il =>
            {
                var cursor = new ILCursor(il);
                int count = 0;
                while (cursor.TryGotoNext(MoveType.Before,
                    instruction => instruction.MatchCallvirt<Level>("IsInCamera")))
                {
                    cursor.Remove();
                    cursor.Emit(OpCodes.Ldarg_0);
                    cursor.EmitDelegate<Func<Level, Vector2, float, FinalBossShot, bool>>(
                        CheckShotVisibility);
                    count++;
                }
                if (count != 1) throw new InvalidOperationException("Boss-shot visibility shape changed");
            }));
            PatchBirdZoom();
        }
        catch { Dispose(); throw; }
    }

    private void ResetSnowball(On.Celeste.Snowball.orig_ResetPosition orig, Snowball snowball)
    {
        policies.GetValue(snowball, _ => new Policy()).Original = UltrawideModule.Settings.PreserveSnowballs;
        orig(snowball);
    }

    private void AddOshiro(On.Celeste.AngryOshiro.orig_Added orig, AngryOshiro oshiro, Scene scene)
    {
        policies.GetValue(oshiro, _ => new Policy()).Original = UltrawideModule.Settings.PreserveOshiro;
        orig(oshiro, scene);
    }

    private void AddShot(On.Celeste.FinalBossShot.orig_Added orig, FinalBossShot shot, Scene scene)
    {
        policies.GetValue(shot, _ => new Policy()).Original = UltrawideModule.Settings.PreserveBossShots;
        orig(shot, scene);
    }

    private float Extension(Entity entity)
    {
        if (!policies.TryGetValue(entity, out var policy) || policy.Original ||
            Engine.Scene is not Level level) return 0;
        return (float)renderer.SelectGeometry(level).SideExtension;
    }

    private void PatchEdges(Type type, string method, int expectedRight, int expectedLeft)
    {
        hooks.Add(new ILHook(type.GetMethod(method, All)!, il =>
        {
            int right = 0, left = 0;
            var cursor = new ILCursor(il);
            while (cursor.TryGotoNext(MoveType.Before, instruction =>
                instruction.MatchCallvirt<Camera>("get_Right") ||
                instruction.MatchCallvirt<Camera>("get_Left")))
            {
                bool isRight = cursor.Next!.MatchCallvirt<Camera>("get_Right");
                if (isRight) right++; else left++;
                cursor.Remove();
                cursor.Emit(OpCodes.Ldarg_0);
                cursor.EmitDelegate<Func<Camera, Entity, float>>((camera, entity) =>
                    isRight ? camera.Right + Extension(entity) : camera.Left - Extension(entity));
            }
            if (right != expectedRight || left != expectedLeft)
                throw new InvalidOperationException($"{type.Name}.{method} edge shape changed");
        }));
    }

    private bool CheckShotVisibility(Level level, Vector2 point, float padding, FinalBossShot shot)
    {
        float extra = Extension(shot);
        if (extra == 0) return level.IsInCamera(point, padding);
        return point.X >= level.Camera.X - extra - padding &&
            point.X <= level.Camera.X + 320 + extra + padding &&
            point.Y >= level.Camera.Y - padding && point.Y <= level.Camera.Y + 180 + padding;
    }

    private void PatchBirdZoom()
    {
        var routine = typeof(FlingBird).GetMethod("DoFlingRoutine", All)!;
        var stateMachine = routine.GetCustomAttribute<IteratorStateMachineAttribute>()!.StateMachineType;
        hooks.Add(new ILHook(stateMachine.GetMethod("MoveNext", All)!, il =>
        {
            var cursor = new ILCursor(il);
            if (!cursor.TryGotoNext(MoveType.After, instruction => instruction.MatchCallvirt<Level>(nameof(Level.ZoomTo))))
                throw new InvalidOperationException("Farewell bird zoom-in site changed");
            cursor.Emit(OpCodes.Ldarg_0);
            cursor.EmitDelegate<Func<IEnumerator, object, IEnumerator>>(BirdZoomIn);
            if (!cursor.TryGotoNext(MoveType.After, instruction => instruction.MatchCallvirt<Level>(nameof(Level.ZoomBack))))
                throw new InvalidOperationException("Farewell bird zoom-out site changed");
            cursor.Emit(OpCodes.Ldarg_0);
            cursor.EmitDelegate<Func<IEnumerator, object, IEnumerator>>(BirdZoomOut);
        }));
    }

    private IEnumerator BirdZoomIn(IEnumerator zoom, object fling)
    {
        if (!UltrawideModule.Settings.SkipFarewellBirdZoom || Engine.Scene is not Level level ||
            level.Session.Area.ID != 10 || renderer.SelectGeometry(level).Width <= ViewGeometry.ReferenceWidth)
            return zoom;

        // Keep both ends of the zoom paired even if the player changes aspect mid-fling.
        skippedBirdZooms.Add(fling, level);
        return Array.Empty<object>().GetEnumerator();
    }

    private IEnumerator BirdZoomOut(IEnumerator zoom, object fling)
        => skippedBirdZooms.Remove(fling) ? Array.Empty<object>().GetEnumerator() : zoom;

    public void Dispose()
    {
        On.Celeste.Snowball.ResetPosition -= ResetSnowball;
        On.Celeste.AngryOshiro.Added -= AddOshiro;
        On.Celeste.FinalBossShot.Added -= AddShot;
        for (int index = hooks.Count - 1; index >= 0; index--) hooks[index].Dispose();
        hooks.Clear();
    }
}
