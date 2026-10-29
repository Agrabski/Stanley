using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace Stanley.Editors;

/// <summary>A part's shape gallery as a dropdown; see ModuleShapeDropdown.axaml.</summary>
public partial class ModuleShapeDropdown : UserControl
{
    public ModuleShapeDropdown()
    {
        InitializeComponent();
    }

    /// <summary>Where the choices open: below the button on the ribbon; beside the inspector, over the stage, in the canvas layout.</summary>
    public PlacementMode Placement
    {
        get => Button.Flyout is PopupFlyoutBase flyout ? flyout.Placement : PlacementMode.BottomEdgeAlignedLeft;
        set
        {
            if (Button.Flyout is PopupFlyoutBase flyout)
                flyout.Placement = value;
        }
    }

    /// <summary>Exposed for headless UI tests.</summary>
    public DropDownButton DropDown => Button;
}
