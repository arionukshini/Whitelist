using FocusGuard.Enforcement;
using FocusGuard.Infrastructure.Persistence;
using FocusGuard.Infrastructure.Windows;
using FocusGuard.UI.ViewModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using System.Text.Json;
using System.Windows;

namespace FocusGuard.UI;

public partial class App : System.Windows.Application
{
    private static readonly string PreferencesPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FocusGuard", "preferences.json");
    private static ResourceDictionary? _activeTheme;
    private static string _themePreference = "System";
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
        if (_activeTheme is not null) dictionaries.Remove(_activeTheme);
        dictionaries.Insert(0, next);
        _activeTheme = next;
    }

    public static async Task SaveThemePreferenceAsync(string preference)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(PreferencesPath)!);
        await File.WriteAllTextAsync(PreferencesPath, JsonSerializer.Serialize(new { Theme = preference }));
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

    private static async Task<string> LoadThemePreferenceAsync()
    {
        try
        {
            if (!File.Exists(PreferencesPath)) return "System";
            using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(PreferencesPath));
            var theme = doc.RootElement.GetProperty("Theme").GetString();
            return theme is "Light" or "Dark" ? theme : "System";
        }
        catch { return "System"; }
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        ApplyTheme(await LoadThemePreferenceAsync());
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;

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
            Renderer = new System.Windows.Forms.ToolStripProfessionalRenderer(new System.Windows.Forms.ProfessionalColorTable())
        };
        menu.Items.Add("WHITELIST LOCK IN", null, null).Enabled = false;
        var status = new System.Windows.Forms.ToolStripMenuItem(viewModel.IsSessionActive ? $"Focus active · {viewModel.RemainingText} remaining" : "Ready when you are") { Enabled = false };
        menu.Items.Add(status);
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add("Open Whitelist Lock In", null, (_, _) =>
        {
            window.Show();
            window.WindowState = WindowState.Normal;
            window.Activate();
        });
        menu.Items.Add("Exit and end focus", null, async (_, _) =>
        {
            await _serviceProvider.GetRequiredService<FocusGuardStore>().EndActiveSessionAsync();
            _isExiting = true;
            Shutdown();
        });
        _trayIcon = new System.Windows.Forms.NotifyIcon
        {
            Text = "Whitelist Lock In",
            Icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? System.Drawing.SystemIcons.Application,
            ContextMenuStrip = menu,
            Visible = true
        };
        var trayStatusTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        trayStatusTimer.Tick += (_, _) => status.Text = viewModel.IsSessionActive ? $"Focus active · {viewModel.RemainingText} remaining" : "Ready when you are";
        trayStatusTimer.Start();
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
