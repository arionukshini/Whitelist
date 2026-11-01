using FocusGuard.Core.Models;
using FocusGuard.Enforcement;
using FocusGuard.Infrastructure.Persistence;
using FocusGuard.Infrastructure.Windows;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Threading;
using System.Windows.Data;
using System.ComponentModel;

namespace FocusGuard.UI.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly FocusGuardStore _store;
    private readonly ApplicationDiscoveryService _applicationDiscovery;
    private readonly ProcessEnforcementService _enforcement;
    private readonly WebsiteAllowlistServer _websiteServer;
    private readonly DispatcherTimer _timer;
    private string _newWebsite = string.Empty;
    private int _durationMinutes = 60;
    private FocusSession? _activeSession;
    private Profile? _selectedProfile;
    private string _selectedSection = "Focus";
    private string _applicationSearch = string.Empty;
    private string _profileName = string.Empty;
    private string _noticeText = "Choose the apps you need, then start focus mode.";
    private string _themePreference = "System";
    private bool _preventEarlyEnd;

    public MainViewModel(FocusGuardStore store, ApplicationDiscoveryService applicationDiscovery, ProcessEnforcementService enforcement, WebsiteAllowlistServer websiteServer)
    {
        _themePreference = App.ThemePreference;
        _preventEarlyEnd = App.PreventEarlyEnd;
        _store = store;
        _applicationDiscovery = applicationDiscovery;
        _enforcement = enforcement;
        _websiteServer = websiteServer;
        ApplicationView = CollectionViewSource.GetDefaultView(SelectedApplications);
        ApplicationView.Filter = value => value is SelectableItem<ApplicationEntry> app &&
            (string.IsNullOrWhiteSpace(_applicationSearch)
             || app.DisplayName.Contains(_applicationSearch, StringComparison.OrdinalIgnoreCase)
             || app.Detail.Contains(_applicationSearch, StringComparison.OrdinalIgnoreCase));
        AddApplicationCommand = new RelayCommand(_ => AddApplicationAsync());
        AddRunningApplicationsCommand = new RelayCommand(_ => AddRunningApplicationsAsync());
        RemoveApplicationCommand = new RelayCommand(item => RemoveApplicationAsync(item as SelectableItem<ApplicationEntry>));
        AddWebsiteCommand = new RelayCommand(_ => AddWebsiteAsync());
        RemoveWebsiteCommand = new RelayCommand(item => RemoveWebsiteAsync(item as SelectableItem<WebsiteRule>));
        StartSessionCommand = new RelayCommand(_ => StartSessionAsync(), _ => _activeSession is null);
        EndSessionCommand = new RelayCommand(_ => EndSessionAsync(), _ => _activeSession is not null && !App.PreventEarlyEnd);
        SaveConfigurationCommand = new RelayCommand(_ => SaveConfigurationAsync());
        NavigateCommand = new RelayCommand(parameter =>
        {
            SelectedSection = parameter?.ToString() ?? "Focus";
            return Task.CompletedTask;
        });
        OpenExtensionFolderCommand = new RelayCommand(_ =>
        {
            var path = Path.Combine(AppContext.BaseDirectory, "BrowserExtension");
            if (Directory.Exists(path))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
                NoticeText = "In your browser, enable Developer mode and choose Load unpacked, then select the opened folder.";
            }
            else
            {
                NoticeText = "The browser extension files are missing from this installation.";
            }
            return Task.CompletedTask;
        });

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += async (_, _) =>
        {
            OnPropertyChanged(nameof(RemainingText));
            OnPropertyChanged(nameof(SessionStatusText));
            OnPropertyChanged(nameof(ExtensionStatusText));
            OnPropertyChanged(nameof(IsExtensionConnected));
            if (_activeSession is not null && _activeSession.EndTime <= DateTimeOffset.Now)
            {
                await RefreshAsync();
            }
        };
    }

    public ObservableCollection<SelectableItem<ApplicationEntry>> SelectedApplications { get; } = [];
    public ICollectionView ApplicationView { get; }
    public ObservableCollection<SelectableItem<WebsiteRule>> SelectedWebsites { get; } = [];
    public ObservableCollection<Profile> Profiles { get; } = [];

    public RelayCommand AddApplicationCommand { get; }
    public RelayCommand AddRunningApplicationsCommand { get; }
    public RelayCommand RemoveApplicationCommand { get; }
    public RelayCommand AddWebsiteCommand { get; }
    public RelayCommand RemoveWebsiteCommand { get; }
    public RelayCommand StartSessionCommand { get; }
    public RelayCommand EndSessionCommand { get; }
    public RelayCommand NavigateCommand { get; }
    public RelayCommand OpenExtensionFolderCommand { get; }
    public RelayCommand SaveConfigurationCommand { get; }

    public IReadOnlyList<string> Sections { get; } = ["Focus", "Websites", "Settings"];
    public IReadOnlyList<string> ThemeOptions { get; } = ["System", "Light", "Dark"];

    public string ThemePreference
    {
        get => _themePreference;
        set
        {
            if (value is not ("System" or "Light" or "Dark") || !SetProperty(ref _themePreference, value)) return;
            App.ApplyTheme(value);
            _ = App.SavePreferencesAsync();
        }
    }

    public bool PreventEarlyEnd
    {
        get => _preventEarlyEnd;
        set
        {
            if (IsSessionActive || !SetProperty(ref _preventEarlyEnd, value)) return;
            App.SetPreventEarlyEnd(value);
            EndSessionCommand.RaiseCanExecuteChanged();
        }
    }

    public bool CanChangeSessionLock => !IsSessionActive;

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

    public string ApplicationSearch
    {
        get => _applicationSearch;
        set
        {
            if (SetProperty(ref _applicationSearch, value)) ApplicationView.Refresh();
        }
    }

    public string ProfileName
    {
        get => _profileName;
        set => SetProperty(ref _profileName, value);
    }

    public Profile? SelectedProfile
    {
        get => _selectedProfile;
        set
        {
            if (SetProperty(ref _selectedProfile, value))
            {
                ProfileName = value?.Name ?? string.Empty;
                if (value is not null) ApplyProfileSelection(value);
            }
        }
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
    public bool IsSessionActive => _activeSession is not null;
    public string SessionStatusText => _activeSession is null
        ? "Best-effort checks keep unselected visible apps closed."
        : $"Best-effort app checks · ends at {_activeSession.EndTime.LocalDateTime:t}";
    public string DatabaseLocation => DatabasePaths.DefaultDatabasePath;
    public string ExtensionStatusText => !_websiteServer.IsAvailable
        ? "Browser bridge unavailable (port 47837 is busy)"
        : _websiteServer.IsExtensionConnected ? "Browser connected" : "Waiting for extension connection";
    public bool IsExtensionConnected => _websiteServer.IsExtensionConnected;

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
        var selectedProfileId = _selectedProfile?.Id;
        var savedApplications = await _store.GetApplicationsAsync();
        var applications = _applicationDiscovery.FilterUserFacingApplications(
            savedApplications.Where(app => app.Source != ApplicationSource.Legacy || !ApplicationDiscoveryService.IsLegacyBackgroundHelper(app)));
        ReplaceSelectable(SelectedApplications, applications, app => app.Name, app => app.ExecutablePath);
        ReplaceSelectable(SelectedWebsites, await _store.GetWebsitesAsync(), site => site.Domain, site => "Includes subdomains");

        Profiles.Clear();
        foreach (var profile in await _store.GetProfilesAsync()) Profiles.Add(profile);

        _activeSession = await _store.GetActiveSessionAsync();
        _selectedProfile = Profiles.FirstOrDefault(profile => profile.Id == (_activeSession?.ProfileId ?? selectedProfileId));
        OnPropertyChanged(nameof(SelectedProfile));
        ProfileName = _selectedProfile?.Name ?? string.Empty;
        if (_activeSession is not null)
        {
            ApplySessionSelection(_activeSession);
            await _enforcement.StartAsync();
        }
        else
        {
            _enforcement.Stop();
            if (_selectedProfile is not null) ApplyProfileSelection(_selectedProfile);
        }

        OnPropertyChanged(nameof(ActiveSessionName));
        OnPropertyChanged(nameof(IsSessionActive));
        OnPropertyChanged(nameof(CanChangeSessionLock));
        OnPropertyChanged(nameof(SessionStatusText));
        OnPropertyChanged(nameof(RemainingText));
        StartSessionCommand.RaiseCanExecuteChanged();
        EndSessionCommand.RaiseCanExecuteChanged();
    }

    private async Task AddApplicationAsync()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Applications (*.exe)|*.exe",
            CheckFileExists = true,
            Title = "Choose an app to allow"
        };

        if (dialog.ShowDialog() == true)
        {
            await _store.AddApplicationAsync(ApplicationDiscoveryService.GetFriendlyName(dialog.FileName), dialog.FileName);
            NoticeText = "App added. Tick it to allow it during focus mode.";
            await RefreshAsync();
        }
    }

    private async Task AddRunningApplicationsAsync()
    {
        var apps = _applicationDiscovery.DiscoverRunningApplications().Take(40).ToList();
        var before = (await _store.GetApplicationsAsync()).Select(app => app.ExecutablePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var app in apps)
        {
            await _store.AddApplicationAsync(app.Name, app.ExecutablePath, ApplicationSource.Discovered);
        }

        var added = apps.Count(app => !before.Contains(app.ExecutablePath));
        NoticeText = apps.Count == 0
            ? "No visible user apps were found. Use Add app to choose an .exe."
            : added == 0 ? "Your visible apps are already in the list." : $"Added {added} visible app(s). Tick the ones you need.";
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
            profileId: _selectedProfile?.Id,
            name: "Focus mode",
            TimeSpan.FromMinutes(DurationMinutes),
            selectedApplicationIds,
            SelectedWebsites.Where(x => x.IsSelected).Select(x => x.Value.Id));
        NoticeText = "Focus mode started.";
        await RefreshAsync();
    }

    private async Task EndSessionAsync()
    {
        if (App.PreventEarlyEnd)
        {
            NoticeText = "Early ending is locked in Settings for this session.";
            return;
        }
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

    private void ApplyProfileSelection(Profile profile)
    {
        var applicationIds = profile.Applications.Select(x => x.ApplicationEntryId).ToHashSet();
        var websiteIds = profile.Websites.Select(x => x.WebsiteRuleId).ToHashSet();
        foreach (var app in SelectedApplications) app.IsSelected = applicationIds.Contains(app.Value.Id);
        foreach (var site in SelectedWebsites) site.IsSelected = websiteIds.Contains(site.Value.Id);
    }

    private async Task SaveConfigurationAsync()
    {
        var name = ProfileName.Trim();
        if (name.Length == 0)
        {
            NoticeText = "Give this configuration a name before saving.";
            return;
        }

        var duplicate = Profiles.FirstOrDefault(profile => profile.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (duplicate is not null && duplicate.Id != _selectedProfile?.Id)
        {
            NoticeText = "A configuration already has that name. Select it before saving changes.";
            return;
        }

        var appIds = SelectedApplications.Where(x => x.IsSelected).Select(x => x.Value.Id).ToArray();
        var websiteIds = SelectedWebsites.Where(x => x.IsSelected).Select(x => x.Value.Id).ToArray();
        _selectedProfile = _selectedProfile is null
            ? await _store.CreateProfileAsync(name, appIds, websiteIds)
            : await _store.UpdateProfileAsync(_selectedProfile.Id, name, appIds, websiteIds);
        NoticeText = $"Saved configuration: {name}.";
        await RefreshAsync();
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
