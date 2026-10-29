using Avalonia.Controls;

namespace Stanley.Editors;

/// <summary>The picked part of a body built from shapes. See ModuleSection.axaml.</summary>
public partial class ModuleSection : UserControl
{
    public ModuleSection()
    {
        InitializeComponent();
        SliderDrag.Wire(() => DataContext as CharacterEditorViewModel, ModuleWidthSlider, ModuleHeightSlider, ModuleRotationSlider,
            LimbLengthSlider, LimbThicknessSlider, LimbTaperSlider, LimbBendSlider, LimbEndSizeSlider);
    }
}
