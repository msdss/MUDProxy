namespace MudProxyViewer;

/// <summary>
/// Color table for dark theme menus
/// </summary>
public class DarkColorTable : ProfessionalColorTable
{
    public override Color MenuBorder => Color.FromArgb(70, 70, 70);
    public override Color MenuItemBorder => Color.FromArgb(70, 70, 70);
    public override Color MenuItemSelected => Color.FromArgb(70, 70, 70);
    public override Color MenuStripGradientBegin => Color.FromArgb(45, 45, 45);
    public override Color MenuStripGradientEnd => Color.FromArgb(45, 45, 45);
    public override Color MenuItemSelectedGradientBegin => Color.FromArgb(70, 70, 70);
    public override Color MenuItemSelectedGradientEnd => Color.FromArgb(70, 70, 70);
    public override Color MenuItemPressedGradientBegin => Color.FromArgb(60, 60, 60);
    public override Color MenuItemPressedGradientEnd => Color.FromArgb(60, 60, 60);
    public override Color ToolStripDropDownBackground => Color.FromArgb(45, 45, 45);
    public override Color ImageMarginGradientBegin => Color.FromArgb(45, 45, 45);
    public override Color ImageMarginGradientMiddle => Color.FromArgb(45, 45, 45);
    public override Color ImageMarginGradientEnd => Color.FromArgb(45, 45, 45);
}
