using System.Reflection;
using Monocle;

namespace Celeste.Mod.UltrawideToggle;

/// <summary>
/// Resizes screen-sized targets without invalidating persistent GPU caches.
/// The lighting atlas, speed-ring target, and lightning target retain identity.
/// </summary>
internal static class GameplayBufferResize
{
    private sealed record Slot(FieldInfo Field, int WidthPadding, int Height);
    private sealed record Replacement(
        Slot Slot, VirtualRenderTarget Previous, VirtualRenderTarget Next, int OwnerIndex);

    private static readonly FieldInfo OwnersField = typeof(GameplayBuffers).GetField(
        "all", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public) ??
        throw new MissingFieldException("GameplayBuffers.all");
    private static readonly Slot[] Slots =
    [
        Screen(nameof(GameplayBuffers.Gameplay)),
        Screen(nameof(GameplayBuffers.Level)),
        Screen(nameof(GameplayBuffers.ResortDust)),
        Screen(nameof(GameplayBuffers.Light)),
        Screen(nameof(GameplayBuffers.Displacement)),
        Screen(nameof(GameplayBuffers.TempA)),
        Screen(nameof(GameplayBuffers.TempB)),
        Padded(nameof(GameplayBuffers.MirrorSources)),
        Padded(nameof(GameplayBuffers.MirrorMasks))
    ];

    public static void Resize(int width) => Resize(width,
        (name, targetWidth, targetHeight, previous) => VirtualContent.CreateRenderTarget(
            name, targetWidth, targetHeight, previous.Depth, previous.Preserve,
            previous.MultiSampleCount), target => target.Dispose());

    /// <summary>
    /// Allocates every replacement before committing. Failed allocations leave
    /// the current targets and ownership list intact. Delegates permit a GPU-free
    /// regression check of resource ownership and persistent-cache identity.
    /// </summary>
    internal static void Resize(int width,
        Func<string, int, int, VirtualRenderTarget, VirtualRenderTarget> allocate,
        Action<VirtualRenderTarget> release)
    {
        if (width is < 320 or > 960)
            throw new ArgumentOutOfRangeException(nameof(width));
        var owners = (List<VirtualRenderTarget>)OwnersField.GetValue(null)!;
        var replacements = new List<Replacement>();
        int committed = 0;
        try
        {
            foreach (var slot in Slots)
            {
                var previous = (VirtualRenderTarget?)slot.Field.GetValue(null) ??
                    throw new InvalidOperationException($"Uninitialized buffer: {slot.Field.Name}");
                int targetWidth = width + slot.WidthPadding;
                if (previous.Width == targetWidth && previous.Height == slot.Height) continue;
                int ownerIndex = owners.IndexOf(previous);
                if (ownerIndex < 0)
                    throw new InvalidOperationException($"Unowned buffer: {slot.Field.Name}");
                var next = allocate($"ultrawide-{slot.Field.Name}",
                    targetWidth, slot.Height, previous);
                replacements.Add(new(slot, previous, next, ownerIndex));
            }

            foreach (var replacement in replacements)
                if (!ReferenceEquals(owners[replacement.OwnerIndex], replacement.Previous) ||
                    !ReferenceEquals(replacement.Slot.Field.GetValue(null), replacement.Previous))
                    throw new InvalidOperationException("Gameplay buffers changed during allocation");

            foreach (var replacement in replacements)
            {
                replacement.Slot.Field.SetValue(null, replacement.Next);
                owners[replacement.OwnerIndex] = replacement.Next;
                committed++;
            }
        }
        catch
        {
            for (int index = committed - 1; index >= 0; index--)
            {
                var replacement = replacements[index];
                replacement.Slot.Field.SetValue(null, replacement.Previous);
                owners[replacement.OwnerIndex] = replacement.Previous;
            }
            foreach (var replacement in replacements) release(replacement.Next);
            throw;
        }

        foreach (var replacement in replacements) release(replacement.Previous);
    }

    private static Slot Screen(string name) => new(Find(name), 0, 180);
    private static Slot Padded(string name) => new(Find(name), 64, 244);
    private static FieldInfo Find(string name) => typeof(GameplayBuffers).GetField(
        name, BindingFlags.Static | BindingFlags.Public) ??
        throw new MissingFieldException($"GameplayBuffers.{name}");
}
