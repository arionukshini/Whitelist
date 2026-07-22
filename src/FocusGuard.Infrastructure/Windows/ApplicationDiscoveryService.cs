using FocusGuard.Core.Models;
using System.Diagnostics;

namespace FocusGuard.Infrastructure.Windows;

public sealed class ApplicationDiscoveryService
{
    public IReadOnlyList<ApplicationEntry> DiscoverRunningApplications()
    {
        return Process.GetProcesses()
            .Select(TryCreateEntry)
            .Where(x => x is not null)
            .Cast<ApplicationEntry>()
            .GroupBy(x => x.ExecutablePath, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.First())
            .OrderBy(x => x.Name)
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

    private static ApplicationEntry? TryCreateEntry(Process process)
    {
        try
        {
            var path = process.MainModule?.FileName;
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            return new ApplicationEntry
            {
                Name = string.IsNullOrWhiteSpace(process.MainWindowTitle)
                    ? Path.GetFileNameWithoutExtension(path)
                    : process.ProcessName,
                ExecutablePath = path
            };
        }
        catch
        {
            return null;
        }
    }
}

