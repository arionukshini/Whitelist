using FocusGuard.Enforcement;
using FocusGuard.Infrastructure.Persistence;
using FocusGuard.Infrastructure.Windows;
using FocusGuard.UI.ViewModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Windows;

namespace FocusGuard.UI;

public partial class App : Application
{
    private ServiceProvider? _serviceProvider;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

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
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _serviceProvider?.Dispose();
        base.OnExit(e);
    }
}
