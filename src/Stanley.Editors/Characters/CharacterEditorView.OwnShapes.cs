using Avalonia.Controls;
using Avalonia.Input;

namespace Stanley.Editors;

/// <summary>
/// The stage's right-click menu on a part of a body built from shapes (docs/shape-characters.md
/// §9.3): the part is picked, and its menu offers a shape of your own for it - Import shape...,
/// Draw shape - and Keep in My Assets, the same commands as the Module tab's "Your own" group.
/// </summary>
public partial class CharacterEditorView
{
    private void WireOwnShapes()
    {
        Stage.ContextRequested += OnStageContextRequested;
    }

    private void OnStageContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (DataContext is not CharacterEditorViewModel { IsModular: true } vm || vm.IsDrawingShape || !e.TryGetPosition(Stage, out var p))
            return;
        if (Stage.ModuleAt(p) is not { } part)
            return;
        vm.SelectModule(part);
        if (vm.SelectedModule != part)
            return;
        e.Handled = true;
        ModuleMenu(vm).Open(Stage);
    }

    /// <summary>The picked part's menu - built when it opens, so each item's tooltip and enabled state are current.</summary>
    public static ContextMenu ModuleMenu(CharacterEditorViewModel vm) => new()
    {
        ItemsSource = new object[]
        {
            new MenuItem { Header = "Import shape...", Command = vm.ImportShapeCommand, [ToolTip.TipProperty] = vm.ImportShapeTip },
            new MenuItem { Header = "Draw shape", Command = vm.DrawShapeCommand, [ToolTip.TipProperty] = vm.DrawShapeTip },
            new Separator(),
            new MenuItem { Header = "Keep in My Assets", Command = vm.KeepShapeInMyAssetsCommand, [ToolTip.TipProperty] = vm.KeepShapeTip, Name = "KeepShapeMenuItem" },
        },
    };

    /// <summary>The menu as a right-click on <paramref name="vm"/>'s picked part shows it; for headless UI tests.</summary>
    public static IReadOnlyList<MenuItem> ModuleMenuItems(CharacterEditorViewModel vm) =>
        ModuleMenu(vm).ItemsSource!.OfType<MenuItem>().ToList();
}
