using Avalonia.Controls;

namespace Stanley.Editors;

/// <summary>The character ribbon's contextual Module tab; see ModuleRibbonTab.axaml: the picked part of a body built from shapes.</summary>
public partial class ModuleRibbonTab : UserControl
{
    public ModuleRibbonTab()
    {
        InitializeComponent();
        SliderDrag.Wire(() => DataContext as CharacterEditorViewModel, ModuleWidthSlider, ModuleHeightSlider, ModuleRotationSlider,
            LimbLengthSlider, LimbThicknessSlider, LimbEndSizeSlider, LimbBendSlider, LimbTaperSlider);
    }
}
