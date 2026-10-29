using System.Reflection;
using System.Runtime.CompilerServices;
using Celeste;
using Celeste.Mod.UltrawideToggle;
using Monocle;

internal static class BufferResizeRegression
{
    private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic |
        BindingFlags.Static | BindingFlags.Instance;

    public static void Run()
    {
        var fields = typeof(GameplayBuffers).GetFields(All)
            .Where(field => field.IsStatic && field.FieldType == typeof(VirtualRenderTarget)).ToArray();
        var owners = (List<VirtualRenderTarget>)typeof(GameplayBuffers)
            .GetField("all", All)!.GetValue(null)!;
        var originalOwners = owners.ToArray();
        var originalFields = fields.ToDictionary(field => field, field => field.GetValue(null));
        var destroyed = new HashSet<VirtualRenderTarget>();
        int allocations = 0, assertions = 0;
        void Check(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
            assertions++;
        }
        VirtualRenderTarget Allocate(string name, int width, int height, VirtualRenderTarget previous)
        {
            allocations++;
            return FakeTarget(name, width, height);
        }
        void Release(VirtualRenderTarget target)
        {
            if (!destroyed.Add(target)) throw new Exception("Double-disposed target");
        }

        try
        {
            owners.Clear();
            foreach (var field in fields)
            {
                (int width, int height) = field.Name switch
                {
                    "LightBuffer" => (1024, 1024),
                    "SpeedRings" => (512, 512),
                    "Lightning" => (160, 160),
                    "MirrorSources" or "MirrorMasks" => (384, 244),
                    _ => (320, 180)
                };
                var target = FakeTarget(field.Name, width, height);
                field.SetValue(null, target);
                owners.Add(target);
            }
            var atlas = GameplayBuffers.LightBuffer;
            var rings = GameplayBuffers.SpeedRings;
            var lightning = GameplayBuffers.Lightning;
            int ownerCount = owners.Count;

            // A cached light is clean and indexed: clearing its texture would
            // not automatically redraw it. Atlas identity must survive.
            var light = (VertexLight)RuntimeHelpers.GetUninitializedObject(typeof(VertexLight));
            typeof(VertexLight).GetField("Index", All)!.SetValue(light, 12);
            typeof(VertexLight).GetField("Dirty", All)!.SetValue(light, false);

            foreach (int width in new[] { 430, 320, 430, 320, 427, 430, 320 })
            {
                GameplayBufferResize.Resize(width, Allocate, Release);
                Check(ReferenceEquals(GameplayBuffers.LightBuffer, atlas), "Lighting atlas replaced");
                Check(!destroyed.Contains(atlas), "Lighting atlas destroyed");
                Check(ReferenceEquals(GameplayBuffers.SpeedRings, rings), "Speed-ring cache replaced");
                Check(ReferenceEquals(GameplayBuffers.Lightning, lightning), "Lightning cache replaced");
                Check((int)typeof(VertexLight).GetField("Index", All)!.GetValue(light)! == 12 &&
                    !(bool)typeof(VertexLight).GetField("Dirty", All)!.GetValue(light)!,
                    "Existing light cache state was modified");
                Check(owners.Count == ownerCount, "Ownership list leaked targets");
                Check(owners.All(target => !destroyed.Contains(target)), "Ownership list retains disposed targets");
                foreach (var field in fields)
                {
                    var target = (VirtualRenderTarget)field.GetValue(null)!;
                    Check(owners.Contains(target), $"Unregistered {field.Name}");
                    if (field.Name is "LightBuffer" or "SpeedRings" or "Lightning") continue;
                    int padding = field.Name is "MirrorSources" or "MirrorMasks" ? 64 : 0;
                    Check(target.Width == width + padding, $"Wrong {field.Name} width");
                    Check(target.Height == (padding == 0 ? 180 : 244), $"Wrong {field.Name} height");
                }
            }

            int before = allocations;
            GameplayBufferResize.Resize(320, Allocate, Release);
            Check(allocations == before, "Unchanged width allocated targets");

            var beforeFailure = fields.ToDictionary(field => field, field => field.GetValue(null));
            var ownersBeforeFailure = owners.ToArray();
            int failedAllocations = 0;
            var abandoned = new List<VirtualRenderTarget>();
            try
            {
                GameplayBufferResize.Resize(430, (name, width, height, previous) =>
                {
                    if (++failedAllocations == 4) throw new IOException("Simulated GPU allocation failure");
                    var target = FakeTarget(name, width, height);
                    abandoned.Add(target);
                    return target;
                }, Release);
                throw new Exception("Expected allocation failure");
            }
            catch (IOException)
            {
                Check(fields.All(field => ReferenceEquals(field.GetValue(null), beforeFailure[field])),
                    "Allocation failure partially replaced buffers");
                Check(owners.SequenceEqual(ownersBeforeFailure), "Allocation failure changed ownership");
                Check(abandoned.All(destroyed.Contains), "Failed allocation leaked new targets");
                Check(!destroyed.Contains(atlas), "Failed allocation invalidated the light atlas");
            }
            Console.WriteLine($"PASS: {assertions} buffer-resize regression assertions; persistent light atlas preserved");
        }
        finally
        {
            foreach (var entry in originalFields) entry.Key.SetValue(null, entry.Value);
            owners.Clear();
            owners.AddRange(originalOwners);
        }
    }

    private static VirtualRenderTarget FakeTarget(string name, int width, int height)
    {
        var target = (VirtualRenderTarget)RuntimeHelpers.GetUninitializedObject(typeof(VirtualRenderTarget));
        foreach (var entry in new Dictionary<string, object>
        {
            ["Name"] = name, ["Width"] = width, ["Height"] = height
        })
            typeof(VirtualAsset).GetField($"<{entry.Key}>k__BackingField", All)!.SetValue(target, entry.Value);
        return target;
    }
}
