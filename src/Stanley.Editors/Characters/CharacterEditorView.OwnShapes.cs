using Avalonia.Controls;

namespace Stanley.Editors;

/// <summary>
/// The picked part's own-shape commands on the stage's right-click menu (docs/shape-characters.md
/// §9.3) - Import shape..., Draw shape and Keep in My Assets, the same commands as the Module
/// tab's "Your own" group. They join the point and socket items in the one stage menu
/// (<see cref="StageMenuItems"/>).
/// </summary>
public partial class CharacterEditorView
{
    /// <summary>The own-shape items for the picked part - built when the menu opens, so each item's tooltip and enabled state are current.</summary>
    public static IReadOnlyList<Control> OwnShapeItems(CharacterEditorViewModel vm) =>
    [
        new MenuItem { Header = "Import shape...", Command = vm.ImportShapeCommand, [ToolTip.TipProperty] = vm.ImportShapeTip },
        new MenuItem { Header = "Draw shape", Command = vm.DrawShapeCommand, [ToolTip.TipProperty] = vm.DrawShapeTip },
        new Separator(),
        new MenuItem { Header = "Keep in My Assets", Command = vm.KeepShapeInMyAssetsCommand, [ToolTip.TipProperty] = vm.KeepShapeTip, Name = "KeepShapeMenuItem" },
    ];

    /// <summary>The own-shape items a right-click on <paramref name="vm"/>'s picked part shows; for headless UI tests.</summary>
    public static IReadOnlyList<MenuItem> ModuleMenuItems(CharacterEditorViewModel vm) =>
        OwnShapeItems(vm).OfType<MenuItem>().ToList();
}
