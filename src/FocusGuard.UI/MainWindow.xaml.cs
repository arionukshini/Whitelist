using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace FocusGuard.UI;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => UpdateTitleBarTheme();
        App.ThemeChanged += OnThemeChanged;
        Closed += (_, _) => App.ThemeChanged -= OnThemeChanged;
    }

    private void OnThemeChanged(bool isDark) => UpdateTitleBarTheme();

    private void UpdateTitleBarTheme()
    {
        if (!IsLoaded && PresentationSource.FromVisual(this) is null) return;
        try
        {
            var handle = new WindowInteropHelper(this).Handle;
            var dark = App.IsDarkTheme ? 1 : 0;
            var attribute = Environment.OSVersion.Version.Build >= 18985 ? 20 : 19;
            _ = DwmSetWindowAttribute(handle, attribute, ref dark, sizeof(int));
        }
        catch (DllNotFoundException) { }
        catch (EntryPointNotFoundException) { }
    }

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int attributeValue, int attributeSize);
}
