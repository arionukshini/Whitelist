using FocusGuard.Core.Models;
using FocusGuard.Infrastructure.Persistence;
using System.Diagnostics;

namespace FocusGuard.Enforcement;

public sealed class ProcessEnforcementService : IDisposable
{
    private static readonly HashSet<string> ProtectedProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "System", "Idle", "Registry", "smss", "csrss", "wininit", "winlogon", "services", "lsass",
        "svchost", "fontdrvhost", "dwm", "explorer", "spoolsv", "sihost", "taskhostw", "RuntimeBroker",
        "SearchIndexer", "SecurityHealthService", "MsMpEng", "FocusGuard.UI", "dotnet", "devenv",
        "MSBuild", "VBCSCompiler", "chrome", "msedge", "brave", "firefox", "vivaldi", "opera"
    };

    private readonly FocusGuardStore _store;
    private readonly Timer _timer;
    private HashSet<string> _allowedPaths = new(StringComparer.OrdinalIgnoreCase);
    private bool _running;

    public ProcessEnforcementService(FocusGuardStore store)
    {
        _store = store;
        _timer = new Timer(async _ => await ScanAsync(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    public bool IsRunning => _running;

    public async Task RefreshAsync()
    {
        var active = await _store.GetActiveSessionAsync();
        _allowedPaths = active?.Applications
            .Select(x => x.ApplicationEntry.ExecutablePath)
            .Where(File.Exists)
            .ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];
    }

    public async Task StartAsync()
    {
        await RefreshAsync();
        _running = true;
        _timer.Change(TimeSpan.Zero, TimeSpan.FromSeconds(3));
    }

    public void Stop()
    {
        _running = false;
        _timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    private async Task ScanAsync()
    {
        if (!_running)
        {
            return;
        }

        var active = await _store.GetActiveSessionAsync();
        if (active is null)
        {
            Stop();
            return;
        }

        if (active.EndTime <= DateTimeOffset.Now)
        {
            await _store.CompleteExpiredSessionsAsync();
            Stop();
            return;
        }

        foreach (var process in Process.GetProcesses())
        {
            RestrictIfNeeded(process, active.Applications.Select(x => x.ApplicationEntry));
        }
    }

    private void RestrictIfNeeded(Process process, IEnumerable<ApplicationEntry> allowedApplications)
    {
        try
        {
            if (ProtectedProcessNames.Contains(process.ProcessName))
            {
                return;
            }

            if (process.Id == Environment.ProcessId)
            {
                return;
            }

            var path = process.MainModule?.FileName;
            if (string.IsNullOrWhiteSpace(path) || process.MainWindowHandle == IntPtr.Zero)
            {
                return;
            }

            var isAllowed = allowedApplications.Any(x => string.Equals(x.ExecutablePath, path, StringComparison.OrdinalIgnoreCase));
            if (!isAllowed && !IsWindowsPath(path))
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Process access can fail for elevated or protected processes. Leave those alone.
        }
    }

    private static bool IsWindowsPath(string path)
    {
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        return path.StartsWith(windows, StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose() => _timer.Dispose();
}
