using System.Windows;
using DrawColor = System.Drawing.Color;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;

namespace FocusGuard.UI;

internal sealed class TrayColorTable : System.Windows.Forms.ProfessionalColorTable
{
    private readonly bool _dark;
    public TrayColorTable(bool dark) { _dark = dark; UseSystemColors = false; }
    private DrawColor ColorFrom(string key, DrawColor fallback) => System.Windows.Application.Current.Resources[key] is SolidColorBrush brush
        ? DrawColor.FromArgb(brush.Color.A, brush.Color.R, brush.Color.G, brush.Color.B)
        : fallback;

    public override DrawColor ToolStripDropDownBackground => ColorFrom("PanelBrush", _dark ? DrawColor.FromArgb(32, 37, 34) : DrawColor.FromArgb(251, 252, 250));
    public override DrawColor MenuBorder => ColorFrom("BorderBrush", _dark ? DrawColor.FromArgb(52, 60, 55) : DrawColor.FromArgb(220, 226, 219));
    public override DrawColor MenuItemSelected => ColorFrom("HoverBrush", _dark ? DrawColor.FromArgb(42, 50, 45) : DrawColor.FromArgb(231, 236, 231));
    public override DrawColor MenuItemBorder => ColorFrom("BorderBrush", _dark ? DrawColor.FromArgb(52, 60, 55) : DrawColor.FromArgb(220, 226, 219));
    public override DrawColor ImageMarginGradientBegin => ToolStripDropDownBackground;
    public override DrawColor ImageMarginGradientMiddle => ToolStripDropDownBackground;
    public override DrawColor ImageMarginGradientEnd => ToolStripDropDownBackground;
    public override DrawColor SeparatorDark => MenuBorder;
    public override DrawColor SeparatorLight => ToolStripDropDownBackground;
}
