using Microsoft.Xna.Framework;
using Mono.Cecil.Cil;
using MonoMod.Cil;

namespace Celeste.Mod.UltrawideToggle;

/// <summary>Uses Everest's native expandable menu and input navigation.</summary>
internal static class BoundaryMenu
{
    private sealed class BoundarySection() : TextMenuExt.SubMenu("16:9 Boundary", false)
    {
        public override void Render(Vector2 position, bool selected)
            => base.Render(position, selected && !Focused);
    }

    // Everest colors its arrow from child focus rather than header selection.
    // Restrict the correction to this section; retain other mods' menu behavior.
    internal static void PatchArrow(ILContext il)
    {
        var cursor = new ILCursor(il);
        if (!cursor.TryGotoNext(MoveType.After,
            instruction => instruction.MatchLdfld<TextMenuExt.SubMenu>("Focused")))
            throw new InvalidOperationException("Submenu arrow focus site changed");
        cursor.Emit(OpCodes.Ldarg_0);
        cursor.Emit(OpCodes.Ldarg_2);
        cursor.EmitDelegate<Func<bool, TextMenuExt.SubMenu, bool, bool>>(
            (focused, menu, selected) => menu is BoundarySection ? selected : focused);
    }

    internal static TextMenuExt.SubMenu Create(UltrawideSettings settings)
    {
        var section = new BoundarySection();
        section.Add(new TextMenu.OnOff("Enabled", settings.BoundaryEnabled)
            .Change(value => settings.BoundaryEnabled = value));
        section.Add(new TextMenu.Slider("Color",
            index => BoundaryAppearance.Colors[index].ToString(),
            0, BoundaryAppearance.Colors.Length - 1,
            Math.Max(0, Array.IndexOf(BoundaryAppearance.Colors, settings.BoundaryColor)))
            .Change(index => settings.BoundaryColor = BoundaryAppearance.Colors[index]));
        section.Add(new TextMenu.Slider("Thickness",
            index => $"{BoundaryAppearance.Thicknesses[index]} px",
            0, BoundaryAppearance.Thicknesses.Length - 1,
            Array.IndexOf(BoundaryAppearance.Thicknesses, settings.BoundaryThickness))
            .Change(index => settings.BoundaryThickness = BoundaryAppearance.Thicknesses[index]));
        section.Add(new TextMenu.Slider("Outline",
            index => ((BoundaryOutlinePreset)index).ToString(),
            0, 3, (int)settings.BoundaryOutline)
            .Change(index => settings.BoundaryOutline = (BoundaryOutlinePreset)index));
        section.Add(new TextMenu.Slider("Opacity",
            index => $"{index * 25}%", 1, 4, settings.BoundaryOpacity / 25)
            .Change(index => settings.BoundaryOpacity = index * 25));
        section.Add(new TextMenu.Slider("Border Style",
            index => index == 2 ? "Corners Only" : ((BoundaryStylePreset)index).ToString(),
            0, 2, (int)settings.BoundaryStyle)
            .Change(index => settings.BoundaryStyle = (BoundaryStylePreset)index));
        section.Add(new TextMenu.Slider("Dim Outside 16:9",
            index => $"{index * 10}%", 0, 10, settings.BoundaryDim / 10)
            .Change(index => settings.BoundaryDim = index * 10));
        return section;
    }
}
