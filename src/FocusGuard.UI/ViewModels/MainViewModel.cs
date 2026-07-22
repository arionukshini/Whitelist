using FocusGuard.Core.Models;
using FocusGuard.Enforcement;
using FocusGuard.Infrastructure.Persistence;
using FocusGuard.Infrastructure.Windows;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace FocusGuard.UI.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly FocusGuardStore _store;
    private readonly ApplicationDiscoveryService _applicationDiscovery;
    private readonly ProcessEnforcementService _enforcement;
    private readonly DispatcherTimer _timer;
    private Profile? _selectedProfile;
    private string _newWebsite = string.Empty;
    private string _newProfileName = "New Profile";
    private int _durationMinutes = 60;
    private FocusSession? _activeSession;
    private string _selectedSection = "Dashboard";
    private bool _isDarkMode = true;

    public MainViewModel(FocusGuardStore store, ApplicationDiscoveryService applicationDiscovery, ProcessEnforcementService enforcement)
    {
        _store = store;
        _applicationDiscovery = applicationDiscovery;
        _enforcement = enforcement;
        AddApplicationCommand = new RelayCommand(_ => AddApplicationAsync());
        AddRunningApplicationsCommand = new RelayCommand(_ => AddRunningApplicationsAsync());
        AddWebsiteCommand = new RelayCommand(_ => AddWebsiteAsync());
        CreateProfileCommand = new RelayCommand(_ => CreateProfileAsync());
        StartSessionCommand = new RelayCommand(_ => StartSessionAsync());
        EndSessionCommand = new RelayCommand(_ => EndSessionAsync());
        NavigateCommand = new RelayCommand(parameter =>
        {
            SelectedSection = parameter?.ToString() ?? "Dashboard";
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
    public ObservableCollection<Profile> Profiles { get; } = [];
    public ObservableCollection<FocusSession> History { get; } = [];

    public RelayCommand AddApplicationCommand { get; }
    public RelayCommand AddRunningApplicationsCommand { get; }
    public RelayCommand AddWebsiteCommand { get; }
    public RelayCommand CreateProfileCommand { get; }
    public RelayCommand StartSessionCommand { get; }
    public RelayCommand EndSessionCommand { get; }
    public RelayCommand NavigateCommand { get; }

    public IReadOnlyList<string> Sections { get; } = ["Dashboard", "Applications", "Websites", "Profiles", "History", "Settings"];

    public string SelectedSection
    {
        get => _selectedSection;
        set
        {
            if (SetProperty(ref _selectedSection, value))
            {
                OnPropertyChanged(nameof(IsDashboardSelected));
                OnPropertyChanged(nameof(IsApplicationsSelected));
                OnPropertyChanged(nameof(IsWebsitesSelected));
                OnPropertyChanged(nameof(IsProfilesSelected));
                OnPropertyChanged(nameof(IsHistorySelected));
                OnPropertyChanged(nameof(IsSettingsSelected));
            }
        }
    }

    public bool IsDashboardSelected => SelectedSection == "Dashboard";
    public bool IsApplicationsSelected => SelectedSection == "Applications";
    public bool IsWebsitesSelected => SelectedSection == "Websites";
    public bool IsProfilesSelected => SelectedSection == "Profiles";
    public bool IsHistorySelected => SelectedSection == "History";
    public bool IsSettingsSelected => SelectedSection == "Settings";

    public bool IsDarkMode
    {
        get => _isDarkMode;
        set
        {
            if (SetProperty(ref _isDarkMode, value))
            {
                ApplyTheme();
            }
        }
    }

    public Profile? SelectedProfile
    {
        get => _selectedProfile;
        set
        {
            if (SetProperty(ref _selectedProfile, value))
            {
                ApplyProfileSelection(value);
            }
        }
    }

    public string NewWebsite
    {
        get => _newWebsite;
        set => SetProperty(ref _newWebsite, value);
    }

    public string NewProfileName
    {
        get => _newProfileName;
        set => SetProperty(ref _newProfileName, value);
    }

    public int DurationMinutes
    {
        get => _durationMinutes;
        set => SetProperty(ref _durationMinutes, Math.Clamp(value, 5, 720));
    }

    public string ActiveSessionName => _activeSession?.Name ?? "No active focus session";
    public string SessionStatusText => _activeSession is null ? "Choose allowed apps and sites, then start." : $"Ends at {_activeSession.EndTime.LocalDateTime:t}";
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
        ApplyTheme();
        await _store.InitializeAsync();
        await RefreshAsync();
        _timer.Start();
    }

    private void ApplyTheme()
    {
        SetBrush("BgBrush", IsDarkMode ? "#101418" : "#F4F6F8");
        SetBrush("PanelBrush", IsDarkMode ? "#171D23" : "#FFFFFF");
        SetBrush("InkBrush", IsDarkMode ? "#EAF0F6" : "#17202A");
        SetBrush("MutedBrush", IsDarkMode ? "#9AA7B5" : "#657383");
        SetBrush("AccentBrush", IsDarkMode ? "#2A9DB0" : "#1D7A8C");
        SetBrush("AccentDarkBrush", IsDarkMode ? "#7ED6E3" : "#135967");
        SetBrush("SidebarBrush", IsDarkMode ? "#0B0F13" : "#17202A");
        SetBrush("BorderBrush", IsDarkMode ? "#28323C" : "#E2E7EC");
        SetBrush("InputBrush", IsDarkMode ? "#10161C" : "#FFFFFF");
    }

    private static void SetBrush(string key, string color)
    {
        if (Application.Current.Resources[key] is SolidColorBrush brush)
        {
            brush.Color = (Color)ColorConverter.ConvertFromString(color);
        }
    }

    private async Task RefreshAsync()
    {
        await _store.CompleteExpiredSessionsAsync();
        ReplaceSelectable(SelectedApplications, await _store.GetApplicationsAsync(), app => app.Name, app => app.ExecutablePath);
        ReplaceSelectable(SelectedWebsites, await _store.GetWebsitesAsync(), site => site.Domain, site => site.IncludeSubdomains ? "Includes subdomains" : "Exact domain only");
        Replace(Profiles, await _store.GetProfilesAsync());
        Replace(History, await _store.GetHistoryAsync());

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
            Title = "Add allowed application"
        };

        if (dialog.ShowDialog() == true)
        {
            await _store.AddApplicationAsync(Path.GetFileNameWithoutExtension(dialog.FileName), dialog.FileName);
            await RefreshAsync();
        }
    }

    private async Task AddRunningApplicationsAsync()
    {
        foreach (var app in _applicationDiscovery.DiscoverRunningApplications().Take(40))
        {
            await _store.AddApplicationAsync(app.Name, app.ExecutablePath);
        }

        await RefreshAsync();
    }

    private async Task AddWebsiteAsync()
    {
        if (string.IsNullOrWhiteSpace(NewWebsite))
        {
            return;
        }

        await _store.AddWebsiteAsync(NewWebsite, includeSubdomains: true);
        NewWebsite = string.Empty;
        await RefreshAsync();
    }

    private async Task CreateProfileAsync()
    {
        await _store.CreateProfileAsync(
            NewProfileName,
            SelectedApplications.Where(x => x.IsSelected).Select(x => x.Value.Id),
            SelectedWebsites.Where(x => x.IsSelected).Select(x => x.Value.Id));
        await RefreshAsync();
    }

    private async Task StartSessionAsync()
    {
        var selectedApplicationIds = SelectedApplications.Where(x => x.IsSelected).Select(x => x.Value.Id).ToArray();
        if (selectedApplicationIds.Length == 0)
        {
            return;
        }

        await _store.StartSessionAsync(
            SelectedProfile?.Id,
            SelectedProfile?.Name ?? "Custom Focus",
            TimeSpan.FromMinutes(DurationMinutes),
            selectedApplicationIds,
            SelectedWebsites.Where(x => x.IsSelected).Select(x => x.Value.Id));
        await RefreshAsync();
    }

    private async Task EndSessionAsync()
    {
        await _store.EndActiveSessionAsync();
        await RefreshAsync();
    }

    private void ApplyProfileSelection(Profile? profile)
    {
        if (profile is null)
        {
            return;
        }

        var applicationIds = profile.Applications.Select(x => x.ApplicationEntryId).ToHashSet();
        var websiteIds = profile.Websites.Select(x => x.WebsiteRuleId).ToHashSet();
        foreach (var app in SelectedApplications)
        {
            app.IsSelected = applicationIds.Contains(app.Value.Id);
        }

        foreach (var site in SelectedWebsites)
        {
            site.IsSelected = websiteIds.Contains(site.Value.Id);
        }
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

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> values)
    {
        target.Clear();
        foreach (var value in values)
        {
            target.Add(value);
        }
    }

    private static void ReplaceSelectable<T>(ObservableCollection<SelectableItem<T>> target, IEnumerable<T> values, Func<T, string> name, Func<T, string> detail)
    {
        var selected = target.Where(x => x.IsSelected).Select(x => x.Detail).ToHashSet(StringComparer.OrdinalIgnoreCase);
        target.Clear();
        foreach (var value in values)
        {
            var item = new SelectableItem<T>(value, name(value), detail(value))
            {
                IsSelected = selected.Contains(detail(value))
            };
            target.Add(item);
        }
    }
}
