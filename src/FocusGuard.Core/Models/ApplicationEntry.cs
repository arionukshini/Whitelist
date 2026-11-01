namespace FocusGuard.Core.Models;

public enum ApplicationSource
{
    Legacy = 0,
    Discovered = 1,
    UserSelected = 2
}

public sealed class ApplicationEntry
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string ExecutablePath { get; set; } = string.Empty;
    public string? IconPath { get; set; }
    public ApplicationSource Source { get; set; } = ApplicationSource.UserSelected;
}

