using FocusGuard.Core.Models;
using System.Diagnostics;

namespace FocusGuard.Infrastructure.Windows;

public sealed class ApplicationDiscoveryService
{
    private static readonly HashSet<string> LegacyBackgroundHelperNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "APSDaemon", "AacAmbientLighting", "CTMsgHostChrome", "GameInputRedistService", "MSBuild",
        "Microsoft.ServiceHub.Controller", "asus_framework", "msedgewebview2", "git", "iCloudDrive",
        "iCloudServices", "node", "node_repl", "VBCSCompiler"
    };

    private static readonly HashSet<string> ExcludedProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "ApplicationFrameHost", "SearchHost", "SearchApp", "ShellExperienceHost", "StartMenuExperienceHost",
        "TextInputHost", "Widgets", "WidgetService", "SystemSettings", "Taskmgr", "LockApp",
        "explorer", "dwm", "sihost", "RuntimeBroker", "FocusGuard.UI", "WhitelistLockIn", "devenv", "dotnet"
    };

    public IReadOnlyList<ApplicationEntry> DiscoverRunningApplications()
    {
        var currentSession = Process.GetCurrentProcess().SessionId;

        return Process.GetProcesses()
            .Where(process => process.SessionId == currentSession)
            .Select(TryCreateEntry)
            .Where(x => x is not null)
            .Cast<ApplicationEntry>()
            .GroupBy(x => x.ExecutablePath, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.First())
            .OrderBy(x => x.Name)
            .ToList();
    }

    public IReadOnlyList<ApplicationEntry> FilterUserFacingApplications(IEnumerable<ApplicationEntry> entries)
    {
        var visiblePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var hiddenRunningPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    var path = process.MainModule?.FileName;
                    if (string.IsNullOrWhiteSpace(path)) continue;
                    if (process.MainWindowHandle != IntPtr.Zero && !string.IsNullOrWhiteSpace(process.MainWindowTitle))
                        visiblePaths.Add(path);
                    else
                        hiddenRunningPaths.Add(path);
                }
                catch { }
            }
        }

        return entries
            .Where(entry => IsUserFacingApplication(entry.Name, entry.ExecutablePath))
            .Where(entry => !hiddenRunningPaths.Contains(entry.ExecutablePath) || visiblePaths.Contains(entry.ExecutablePath))
            .ToList();
    }

    public IReadOnlyList<ApplicationEntry> DiscoverStartMenuApplications()
    {
        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu)
        };

        return roots
            .Where(Directory.Exists)
            .SelectMany(root => Directory.EnumerateFiles(root, "*.lnk", SearchOption.AllDirectories))
            .Select(path => new ApplicationEntry
            {
                Name = Path.GetFileNameWithoutExtension(path),
                ExecutablePath = path
            })
            .GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.First())
            .OrderBy(x => x.Name)
            .ToList();
    }

    public static bool IsUserFacingApplication(string processName, string executablePath, string? windowTitle = null)
    {
        if (string.IsNullOrWhiteSpace(processName) || string.IsNullOrWhiteSpace(executablePath))
        {
            return false;
        }

        if (windowTitle is not null && string.IsNullOrWhiteSpace(windowTitle))
        {
            return false;
        }

        var executableName = Path.GetFileNameWithoutExtension(executablePath);
        if (ExcludedProcessNames.Contains(processName) || ExcludedProcessNames.Contains(executableName) || IsWindowsComponent(executablePath))
        {
            return false;
        }

        var normalizedName = executableName.ToLowerInvariant();
        return !normalizedName.Contains("nvidia")
            && !normalizedName.StartsWith("nv", StringComparison.Ordinal)
            && !normalizedName.StartsWith("amd", StringComparison.Ordinal)
            && !normalizedName.Contains("radeon")
            && !normalizedName.StartsWith("igfx", StringComparison.Ordinal)
            && !normalizedName.StartsWith("igcc", StringComparison.Ordinal)
            && !normalizedName.StartsWith("intc", StringComparison.Ordinal)
            && !normalizedName.StartsWith("intelgraphics", StringComparison.Ordinal)
            && !normalizedName.StartsWith("intelaudio", StringComparison.Ordinal)
            && !HasPathSegment(executablePath, "NVIDIA")
            && !HasPathSegment(executablePath, "AMD")
            && !HasPathSegment(executablePath, "Intel");
    }

    public static bool IsLegacyBackgroundHelper(ApplicationEntry entry) =>
        LegacyBackgroundHelperNames.Contains(entry.Name)
        || LegacyBackgroundHelperNames.Contains(Path.GetFileNameWithoutExtension(entry.ExecutablePath));

    public static string GetFriendlyName(string executablePath)
    {
        var description = FileVersionInfo.GetVersionInfo(executablePath).FileDescription?.Trim();
        return !string.IsNullOrWhiteSpace(description) ? description : Path.GetFileNameWithoutExtension(executablePath);
    }

    private static ApplicationEntry? TryCreateEntry(Process process)
    {
        try
        {
            // Services and Windows shell components do not belong in a focus allowlist.
            if (process.MainWindowHandle == IntPtr.Zero || string.IsNullOrWhiteSpace(process.MainWindowTitle))
            {
                return null;
            }

            var path = process.MainModule?.FileName;
            if (string.IsNullOrWhiteSpace(path) || !IsUserFacingApplication(process.ProcessName, path))
            {
                return null;
            }

            return new ApplicationEntry
            {
                Name = GetFriendlyName(path),
                ExecutablePath = path,
                IconPath = path
            };
        }
        catch
        {
            return null;
        }
    }

    private static bool IsWindowsComponent(string path)
    {
        var windowsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var windowsAppsDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "WindowsApps");

        return path.StartsWith(windowsDirectory, StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(windowsAppsDirectory, StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasPathSegment(string path, string segment) =>
        path.Contains($"\\{segment}\\", StringComparison.OrdinalIgnoreCase)
        || path.StartsWith($"{segment}\\", StringComparison.OrdinalIgnoreCase);
}
