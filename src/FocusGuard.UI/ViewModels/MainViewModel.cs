using FocusGuard.Core.Models;
using FocusGuard.Enforcement;
using FocusGuard.Infrastructure.Persistence;
using FocusGuard.Infrastructure.Windows;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Threading;

namespace FocusGuard.UI.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly FocusGuardStore _store;
    private readonly ApplicationDiscoveryService _applicationDiscovery;
    private readonly ProcessEnforcementService _enforcement;
    private readonly DispatcherTimer _timer;
    private string _newWebsite = string.Empty;
    private int _durationMinutes = 60;
    private FocusSession? _activeSession;
    private string _selectedSection = "Focus";
    private string _noticeText = "Choose the apps you need, then start focus mode.";

    public MainViewModel(FocusGuardStore store, ApplicationDiscoveryService applicationDiscovery, ProcessEnforcementService enforcement)
    {
        _store = store;
        _applicationDiscovery = applicationDiscovery;
        _enforcement = enforcement;
        AddApplicationCommand = new RelayCommand(_ => AddApplicationAsync());
        AddRunningApplicationsCommand = new RelayCommand(_ => AddRunningApplicationsAsync());
        RemoveApplicationCommand = new RelayCommand(item => RemoveApplicationAsync(item as SelectableItem<ApplicationEntry>));
        AddWebsiteCommand = new RelayCommand(_ => AddWebsiteAsync());
        RemoveWebsiteCommand = new RelayCommand(item => RemoveWebsiteAsync(item as SelectableItem<WebsiteRule>));
        StartSessionCommand = new RelayCommand(_ => StartSessionAsync());
        EndSessionCommand = new RelayCommand(_ => EndSessionAsync());
        NavigateCommand = new RelayCommand(parameter =>
        {
            SelectedSection = parameter?.ToString() ?? "Focus";
            return Task.CompletedTask;
        });

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += async (_, _) =>
        {
            OnPropertyChanged(nameof(RemainingText));
            OnPropertyChanged(nameof(SessionStatusText));
            if (_activeSession is not null && _activeSession.EndTime <= DateTimeOffset.Now)
            {
                await RefreshAsync();
            }
        };
    }

    public ObservableCollection<SelectableItem<ApplicationEntry>> SelectedApplications { get; } = [];
    public ObservableCollection<SelectableItem<WebsiteRule>> SelectedWebsites { get; } = [];

    public RelayCommand AddApplicationCommand { get; }
    public RelayCommand AddRunningApplicationsCommand { get; }
    public RelayCommand RemoveApplicationCommand { get; }
    public RelayCommand AddWebsiteCommand { get; }
    public RelayCommand RemoveWebsiteCommand { get; }
    public RelayCommand StartSessionCommand { get; }
    public RelayCommand EndSessionCommand { get; }
    public RelayCommand NavigateCommand { get; }

    public IReadOnlyList<string> Sections { get; } = ["Focus", "Websites", "Settings"];

    public string SelectedSection
    {
        get => _selectedSection;
        set
        {
            if (SetProperty(ref _selectedSection, value))
            {
                OnPropertyChanged(nameof(IsFocusSelected));
                OnPropertyChanged(nameof(IsWebsitesSelected));
                OnPropertyChanged(nameof(IsSettingsSelected));
            }
        }
    }

    public bool IsFocusSelected => SelectedSection == "Focus";
    public bool IsWebsitesSelected => SelectedSection == "Websites";
    public bool IsSettingsSelected => SelectedSection == "Settings";

    public string NewWebsite
    {
        get => _newWebsite;
        set => SetProperty(ref _newWebsite, value);
    }

    public int DurationMinutes
    {
        get => _durationMinutes;
        set => SetProperty(ref _durationMinutes, Math.Clamp(value, 5, 720));
    }

    public string NoticeText
    {
        get => _noticeText;
        private set => SetProperty(ref _noticeText, value);
    }

    public string ActiveSessionName => _activeSession is null ? "Ready to focus" : "Focus mode is on";
    public string SessionStatusText => _activeSession is null
        ? "Only selected desktop apps stay available during focus mode."
        : $"Ends at {_activeSession.EndTime.LocalDateTime:t}";
    public string DatabaseLocation => DatabasePaths.DefaultDatabasePath;

    public string RemainingText
    {
        get
        {
            if (_activeSession is null)
            {
                return "--:--:--";
            }

            var remaining = _activeSession.EndTime - DateTimeOffset.Now;
            return (remaining < TimeSpan.Zero ? TimeSpan.Zero : remaining).ToString(@"hh\:mm\:ss");
        }
    }

    public async Task InitializeAsync()
    {
        await _store.InitializeAsync();
        await RefreshAsync();
        _timer.Start();
    }

    private async Task RefreshAsync()
    {
        await _store.CompleteExpiredSessionsAsync();
        var applications = (await _store.GetApplicationsAsync())
            .Where(app => ApplicationDiscoveryService.IsUserFacingApplication(app.Name, app.ExecutablePath));
        ReplaceSelectable(SelectedApplications, applications, app => app.Name, app => app.ExecutablePath);
        ReplaceSelectable(SelectedWebsites, await _store.GetWebsitesAsync(), site => site.Domain, site => "Includes subdomains");

        _activeSession = await _store.GetActiveSessionAsync();
        if (_activeSession is not null)
        {
            ApplySessionSelection(_activeSession);
            await _enforcement.StartAsync();
        }
        else
        {
            _enforcement.Stop();
        }

        OnPropertyChanged(nameof(ActiveSessionName));
        OnPropertyChanged(nameof(SessionStatusText));
        OnPropertyChanged(nameof(RemainingText));
    }

    private async Task AddApplicationAsync()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Applications (*.exe)|*.exe",
            CheckFileExists = true,
            Title = "Choose an app to allow"
        };

        if (dialog.ShowDialog() == true)
        {
            await _store.AddApplicationAsync(Path.GetFileNameWithoutExtension(dialog.FileName), dialog.FileName);
            NoticeText = "App added. Tick it to allow it during focus mode.";
            await RefreshAsync();
        }
    }

    private async Task AddRunningApplicationsAsync()
    {
        var apps = _applicationDiscovery.DiscoverRunningApplications().Take(40).ToList();
        foreach (var app in apps)
        {
            await _store.AddApplicationAsync(app.Name, app.ExecutablePath);
        }

        NoticeText = apps.Count == 0
            ? "No visible user apps were found. Use Add app to choose an .exe."
            : $"Added {apps.Count} visible user app(s). Tick the ones you need.";
        await RefreshAsync();
    }

    private async Task RemoveApplicationAsync(SelectableItem<ApplicationEntry>? item)
    {
        if (item is null)
        {
            return;
        }

        await _store.RemoveApplicationAsync(item.Value.Id);
        NoticeText = $"Removed {item.DisplayName}.";
        await RefreshAsync();
    }

    private async Task AddWebsiteAsync()
    {
        if (string.IsNullOrWhiteSpace(NewWebsite))
        {
            NoticeText = "Enter a website such as github.com.";
            return;
        }

        try
        {
            await _store.AddWebsiteAsync(NewWebsite, includeSubdomains: true);
            NewWebsite = string.Empty;
            NoticeText = "Website added. Tick it to allow it during focus mode.";
            await RefreshAsync();
        }
        catch (ArgumentException)
        {
            NoticeText = "Enter a valid website such as github.com.";
        }
    }

    private async Task RemoveWebsiteAsync(SelectableItem<WebsiteRule>? item)
    {
        if (item is null)
        {
            return;
        }

        await _store.RemoveWebsiteAsync(item.Value.Id);
        NoticeText = $"Removed {item.DisplayName}.";
        await RefreshAsync();
    }

    private async Task StartSessionAsync()
    {
        var selectedApplicationIds = SelectedApplications.Where(x => x.IsSelected).Select(x => x.Value.Id).ToArray();
        if (selectedApplicationIds.Length == 0)
        {
            NoticeText = "Tick at least one app before starting focus mode.";
            return;
        }

        await _store.StartSessionAsync(
            profileId: null,
            name: "Focus mode",
            TimeSpan.FromMinutes(DurationMinutes),
            selectedApplicationIds,
            SelectedWebsites.Where(x => x.IsSelected).Select(x => x.Value.Id));
        NoticeText = "Focus mode started.";
        await RefreshAsync();
    }

    private async Task EndSessionAsync()
    {
        await _store.EndActiveSessionAsync();
        NoticeText = "Focus mode ended.";
        await RefreshAsync();
    }

    private void ApplySessionSelection(FocusSession session)
    {
        var applicationIds = session.Applications.Select(x => x.ApplicationEntryId).ToHashSet();
        var websiteIds = session.Websites.Select(x => x.WebsiteRuleId).ToHashSet();
        foreach (var app in SelectedApplications)
        {
            app.IsSelected = applicationIds.Contains(app.Value.Id);
        }

        foreach (var site in SelectedWebsites)
        {
            site.IsSelected = websiteIds.Contains(site.Value.Id);
        }
    }

    private static void ReplaceSelectable<T>(ObservableCollection<SelectableItem<T>> target, IEnumerable<T> values, Func<T, string> name, Func<T, string> detail)
    {
        var selected = target.Where(x => x.IsSelected).Select(x => x.Detail).ToHashSet(StringComparer.OrdinalIgnoreCase);
        target.Clear();
        foreach (var value in values)
        {
            target.Add(new SelectableItem<T>(value, name(value), detail(value))
            {
                IsSelected = selected.Contains(detail(value))
            });
        }
    }
}
