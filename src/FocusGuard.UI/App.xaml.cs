using FocusGuard.Enforcement;
using FocusGuard.Infrastructure.Persistence;
using FocusGuard.Infrastructure.Windows;
using FocusGuard.UI.ViewModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using System.IO;
using System.Text.Json;
using System.Windows;

namespace FocusGuard.UI;

public partial class App : System.Windows.Application
{
    private static readonly string PreferencesPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FocusGuard", "preferences.json");
    private static readonly SemaphoreSlim PreferencesGate = new(1, 1);
    private static string _themePreference = "System";
    private static bool _preventEarlyEnd;
    public static string ThemePreference => _themePreference;
    public static bool PreventEarlyEnd => _preventEarlyEnd;
    public static event Action<bool>? ThemeChanged;
    public static bool IsDarkTheme => Current.Resources["BgBrush"] is System.Windows.Media.SolidColorBrush brush && brush.Color.R < 128;
    private ServiceProvider? _serviceProvider;
    private System.Windows.Forms.NotifyIcon? _trayIcon;
    private Mutex? _singleInstance;
    private bool _isExiting;

    public static void ApplyTheme(string preference)
    {
        _themePreference = preference;
        var dark = preference == "Dark" || (preference == "System" && !IsWindowsLightTheme());
        var uri = new Uri(dark ? "Themes/Dark.xaml" : "Themes/Light.xaml", UriKind.Relative);
        var next = new ResourceDictionary { Source = uri };
        var dictionaries = Current.Resources.MergedDictionaries;
        dictionaries.Clear();
        dictionaries.Add(next);
        ThemeChanged?.Invoke(dark);
    }

    public static void SetPreventEarlyEnd(bool prevent)
    {
        _preventEarlyEnd = prevent;
        _ = SavePreferencesAsync();
    }

    public static async Task SavePreferencesAsync()
    {
        await PreferencesGate.WaitAsync();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PreferencesPath)!);
            await File.WriteAllTextAsync(PreferencesPath, JsonSerializer.Serialize(new { Theme = _themePreference, PreventEarlyEnd = _preventEarlyEnd }));
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        finally { PreferencesGate.Release(); }
    }

    private static bool IsWindowsLightTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return Convert.ToInt32(key?.GetValue("AppsUseLightTheme") ?? 0) != 0;
        }
        catch { return false; }
    }

    private static async Task<(string Theme, bool PreventEarlyEnd)> LoadPreferencesAsync()
    {
        try
        {
            if (!File.Exists(PreferencesPath)) return ("System", false);
            using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(PreferencesPath));
            var root = doc.RootElement;
            var theme = root.TryGetProperty("Theme", out var themeElement) ? themeElement.GetString() : null;
            var prevent = root.TryGetProperty("PreventEarlyEnd", out var preventElement) && preventElement.ValueKind == JsonValueKind.True;
            return (theme is "Light" or "Dark" ? theme : "System", prevent);
        }
        catch { return ("System", false); }
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (e.Args.Contains("--register-native-host", StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                NativeMessagingRegistration.RegisterInstalledHost();
                Shutdown(0);
            }
            catch (Exception exception)
            {
                System.Windows.MessageBox.Show($"Browser integration could not be registered: {exception.Message}", "Whitelist Lock In", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(1);
            }
            return;
        }

        var preferences = await LoadPreferencesAsync();
        _preventEarlyEnd = preferences.PreventEarlyEnd;
        ApplyTheme(preferences.Theme);
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        try { NativeMessagingRegistration.RegisterIfHostIsInstalled(); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }

        _singleInstance = new Mutex(true, "Local\\WhitelistLockIn", out var createdNew);
        if (!createdNew)
        {
            System.Windows.MessageBox.Show("Whitelist Lock In is already running. Use its notification area icon to reopen it.", "Whitelist Lock In", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        var services = new ServiceCollection();
        services.AddDbContext<FocusGuardDbContext>(options => options.UseSqlite($"Data Source={DatabasePaths.DefaultDatabasePath}"));
        services.AddScoped<FocusGuardStore>();
        services.AddSingleton<ApplicationDiscoveryService>();
        services.AddSingleton<ProcessEnforcementService>();
        services.AddSingleton<WebsiteAllowlistServer>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();
        _serviceProvider = services.BuildServiceProvider();

        _serviceProvider.GetRequiredService<WebsiteAllowlistServer>().Start();

        var viewModel = _serviceProvider.GetRequiredService<MainViewModel>();
        await viewModel.InitializeAsync();

        var window = _serviceProvider.GetRequiredService<MainWindow>();
        window.DataContext = viewModel;
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        window.Closing += (_, args) =>
        {
            if (!_isExiting)
            {
                args.Cancel = true;
                window.Hide();
                var text = viewModel.IsSessionActive
                    ? "Focus mode continues in the background. Open the tray icon to return."
                    : "Whitelist Lock In is still running in the notification area.";
                _trayIcon?.ShowBalloonTip(1800, "Whitelist Lock In", text, System.Windows.Forms.ToolTipIcon.Info);
            }
        };

        var menu = new System.Windows.Forms.ContextMenuStrip
        {
            ShowImageMargin = false,
            ShowCheckMargin = false,
            Padding = new System.Windows.Forms.Padding(6)
        };
        var menuHeading = new System.Windows.Forms.ToolStripMenuItem("WHITELIST LOCK IN") { Enabled = false, Padding = new System.Windows.Forms.Padding(10, 7, 10, 7) };
        menu.Items.Add(menuHeading);
        var status = new System.Windows.Forms.ToolStripMenuItem(viewModel.IsSessionActive ? $"Focus active · {viewModel.RemainingText} remaining" : "Ready when you are") { Enabled = false, Padding = new System.Windows.Forms.Padding(10, 6, 10, 6) };
        menu.Items.Add(status);
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        var openItem = new System.Windows.Forms.ToolStripMenuItem("Open Whitelist Lock In", null, (_, _) =>
        {
            window.Show();
            window.WindowState = WindowState.Normal;
            window.Activate();
        }) { Padding = new System.Windows.Forms.Padding(10, 8, 10, 8) };
        menu.Items.Add(openItem);
        var exitItem = new System.Windows.Forms.ToolStripMenuItem("Exit") { ToolTipText = "Available after the focus session ends.", Padding = new System.Windows.Forms.Padding(10, 8, 10, 8) };
        exitItem.Click += async (_, _) =>
        {
            if (viewModel.IsSessionActive) return;
            _isExiting = true;
            Shutdown();
        };
        menu.Items.Add(exitItem);
        menu.Opening += (_, _) =>
        {
            status.Text = viewModel.IsSessionActive ? $"Focus active · {viewModel.RemainingText} remaining" : "Ready when you are";
            exitItem.Enabled = !viewModel.IsSessionActive;
            var background = ((System.Windows.Media.SolidColorBrush)Current.Resources["PanelBrush"]).Color;
            var foreground = ((System.Windows.Media.SolidColorBrush)Current.Resources["InkBrush"]).Color;
            var dark = background.R < 128;
            menu.BackColor = System.Drawing.Color.FromArgb(background.A, background.R, background.G, background.B);
            menu.ForeColor = System.Drawing.Color.FromArgb(foreground.A, foreground.R, foreground.G, foreground.B);
            menu.Renderer = new System.Windows.Forms.ToolStripProfessionalRenderer(new TrayColorTable(dark));
        };
        _trayIcon = new System.Windows.Forms.NotifyIcon
        {
            Text = "Whitelist Lock In",
            Icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? System.Drawing.SystemIcons.Application,
            ContextMenuStrip = menu,
            Visible = true
        };
        _trayIcon.DoubleClick += (_, _) =>
        {
            window.Show();
            window.WindowState = WindowState.Normal;
            window.Activate();
        };
        window.Show();
    }

    private static void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (_themePreference == "System") Current.Dispatcher.BeginInvoke(() => ApplyTheme("System"));
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_trayIcon is not null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
        }
        _serviceProvider?.Dispose();
        _singleInstance?.Dispose();
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        base.OnExit(e);
    }
}
