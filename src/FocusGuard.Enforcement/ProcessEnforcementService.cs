using FocusGuard.Core.Models;
using FocusGuard.Infrastructure.Persistence;
using FocusGuard.Infrastructure.Windows;
using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics;

namespace FocusGuard.Enforcement;

public sealed class ProcessEnforcementService : IDisposable
{
    private static readonly HashSet<string> ProtectedProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "System", "Idle", "Registry", "smss", "csrss", "wininit", "winlogon", "services", "lsass",
        "svchost", "fontdrvhost", "dwm", "explorer", "spoolsv", "sihost", "taskhostw", "RuntimeBroker",
        "SearchIndexer", "SecurityHealthService", "MsMpEng", "FocusGuard.UI", "WhitelistLockIn", "dotnet", "devenv",
        "MSBuild", "VBCSCompiler"
    };

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly Timer _timer;
    private readonly SemaphoreSlim _scanGate = new(1, 1);
    private HashSet<string> _allowedPaths = new(StringComparer.OrdinalIgnoreCase);
    private bool _running;

    public ProcessEnforcementService(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
        _timer = new Timer(async _ => await ScanAsync(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    public bool IsRunning => _running;

    public async Task RefreshAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<FocusGuardStore>();
        var active = await store.GetActiveSessionAsync();
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
        if (!_running || !await _scanGate.WaitAsync(0))
        {
            return;
        }
        try
        {
            if (!_running)
            {
                return;
            }

            FocusSession? active;
            using (var scope = _scopeFactory.CreateScope())
            {
                var store = scope.ServiceProvider.GetRequiredService<FocusGuardStore>();
                active = await store.GetActiveSessionAsync();
            }
            if (active is null)
            {
                Stop();
                return;
            }

            if (active.EndTime <= DateTimeOffset.Now)
            {
                using var scope = _scopeFactory.CreateScope();
                await scope.ServiceProvider.GetRequiredService<FocusGuardStore>().CompleteExpiredSessionsAsync();
                Stop();
                return;
            }

            _allowedPaths = active.Applications
                .Select(x => Path.GetFullPath(x.ApplicationEntry.ExecutablePath))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var allowed = _allowedPaths;
            using var currentProcess = Process.GetCurrentProcess();
            var currentSessionId = currentProcess.SessionId;
            foreach (var process in Process.GetProcesses())
            {
                using (process)
                {
                    RestrictIfNeeded(process, allowed, currentSessionId);
                }
            }
        }
        catch
        {
            // Enforcement is best effort; a transient database or process error must not stop future scans.
        }
        finally
        {
            _scanGate.Release();
        }
    }

    private void RestrictIfNeeded(Process process, HashSet<string> allowedPaths, int currentSessionId)
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

            if (process.SessionId != currentSessionId || process.MainWindowHandle == IntPtr.Zero)
            {
                return;
            }

            var path = process.MainModule?.FileName;
            if (string.IsNullOrWhiteSpace(path) || IsProtectedLocation(path))
            {
                return;
            }

            var fullPath = Path.GetFullPath(path);
            if (!allowedPaths.Contains(fullPath) && ApplicationDiscoveryService.IsUserFacingApplication(process.ProcessName, fullPath, process.MainWindowTitle))
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Process access can fail for elevated or protected processes. Leave those alone.
        }
    }

    private static bool IsProtectedLocation(string path)
    {
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var windowsApps = Path.Combine(programFiles, "WindowsApps");
        return IsWithin(path, windows)
            || IsWithin(path, windowsApps)
            || IsWithin(path, Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData))
            || path.Equals(Environment.ProcessPath, StringComparison.OrdinalIgnoreCase)
            || Path.GetFileName(path).Equals("explorer.exe", StringComparison.OrdinalIgnoreCase)
            || (programFilesX86.Length > 0 && IsWithin(path, Path.Combine(programFilesX86, "WindowsApps")));
    }

    private static bool IsWithin(string path, string root) =>
        !string.IsNullOrWhiteSpace(root) &&
        (path.Equals(root, StringComparison.OrdinalIgnoreCase)
         || path.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));

    public void Dispose()
    {
        Stop();
        _timer.Dispose();
        _scanGate.Dispose();
    }
}
