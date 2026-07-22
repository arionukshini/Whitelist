using FocusGuard.Core.Models;

namespace FocusGuard.Core.Services;

public sealed record FocusSessionSnapshot(
    int Id,
    string Name,
    DateTimeOffset StartTime,
    DateTimeOffset EndTime,
    IReadOnlyList<ApplicationEntry> Applications,
    IReadOnlyList<WebsiteRule> Websites)
{
    public TimeSpan Remaining => EndTime - DateTimeOffset.Now;
}

