namespace FocusGuard.Core.Models;

public sealed class ApplicationEntry
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string ExecutablePath { get; set; } = string.Empty;
    public string? IconPath { get; set; }
}

