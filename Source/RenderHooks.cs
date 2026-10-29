using System.Reflection;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;
using Monocle;

namespace Celeste.Mod.UltrawideToggle;

internal sealed record ConstantPatch(float Value, int Count);
internal sealed record RenderPatch(string Type, string Method, ConstantPatch[] Constants);

/// <summary>Checked, allowlisted render edits; never modifies gameplay literals.</summary>
internal sealed class RenderHooks : IDisposable
{
    private readonly List<ILHook> hooks = new();
    private readonly RenderController controller;
    private const BindingFlags AllMethods = BindingFlags.Public |
        BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;

    public RenderHooks(RenderController controller)
    {
        this.controller = controller;
        try
        {
            Add(typeof(Engine).GetMethod("UpdateView", AllMethods)!, il =>
            {
                int count = 0;
                var cursor = new ILCursor(il);
                while (cursor.TryGotoNext(MoveType.After,
                    instruction => instruction.MatchCall<Engine>("get_Width")))
                {
                    cursor.EmitDelegate<Func<int, int>>(_ => (int)Math.Round(controller.OutputWidth));
                    count++;
                }
                if (count != 4) throw new InvalidOperationException(
                    $"Engine.UpdateView width sites: expected 4, found {count}");
            });
            Add(typeof(GameplayBuffers).GetMethod("Create", AllMethods,
                null, Type.EmptyTypes, null)!, il =>
            {
                int count = ReplaceInteger(il, 320, () => controller.BufferWidth);
                if (count != 7) throw new InvalidOperationException(
                    $"GameplayBuffers width sites: expected 7, found {count}");
                // Mirror targets carry a 32-pixel border on both sides.
                int mirrors = ReplaceInteger(il, 384, () => controller.BufferWidth + 64);
                if (mirrors != 2) throw new InvalidOperationException("Mirror buffer shape changed");
            });
            foreach (var patch in RenderManifest.Patches)
            {
                var type = typeof(Level).Assembly.GetType(patch.Type, true)!;
                var methods = type.GetMethods(AllMethods | BindingFlags.DeclaredOnly)
                    .Where(method => method.Name == patch.Method).ToArray();
                if (methods.Length != 1) throw new InvalidOperationException(
                    $"Ambiguous render target: {patch.Type}.{patch.Method}");
                Add(methods[0], il => PatchConstants(il, patch));
            }
        }
        catch { Dispose(); throw; }
    }

    private void PatchConstants(ILContext il, RenderPatch patch)
    {
        foreach (var constant in patch.Constants)
        {
            var cursor = new ILCursor(il);
            int count = 0;
            while (cursor.TryGotoNext(MoveType.After,
                instruction => instruction.MatchLdcR4(constant.Value)))
            {
                cursor.EmitDelegate<Func<float, float>>(value => ExpandWidth(value));
                count++;
            }
            if (count != constant.Count) throw new InvalidOperationException(
                $"{patch.Type}.{patch.Method}: {constant.Value} expected {constant.Count}, got {count}");
        }
    }

    private float ExpandWidth(float value)
    {
        float extra = controller.VisualWidth - 320;
        return value switch
        {
            160 or 960 => value + extra * (value == 160 ? .5f : 3),
            320 or 322 or 340 => value + extra,
            1920 or 1922 or 1940 => value + extra * 6,
            _ => throw new InvalidOperationException($"Unsupported render constant {value}")
        };
    }

    private static int ReplaceInteger(ILContext il, int value, Func<int> replacement)
    {
        var cursor = new ILCursor(il);
        int count = 0;
        while (cursor.TryGotoNext(MoveType.After, instruction => instruction.MatchLdcI4(value)))
        {
            cursor.EmitDelegate<Func<int, int>>(_ => replacement());
            count++;
        }
        return count;
    }

    private void Add(MethodBase method, ILContext.Manipulator edit)
        => hooks.Add(new ILHook(method, edit));

    public void Dispose()
    {
        for (int index = hooks.Count - 1; index >= 0; index--) hooks[index].Dispose();
        hooks.Clear();
    }
}
