namespace FocusGuard.Core.Models;

public enum FocusSessionStatus
{
    Active = 0,
    Completed = 1,
    Cancelled = 2
}

public sealed class FocusSession
{
    public int Id { get; set; }
    public int? ProfileId { get; set; }
    public Profile? Profile { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTimeOffset StartTime { get; set; }
    public DateTimeOffset EndTime { get; set; }
    public FocusSessionStatus Status { get; set; }
    public int BlockedApplicationAttempts { get; set; }
    public int BlockedWebsiteAttempts { get; set; }
    public List<SessionApplication> Applications { get; set; } = [];
    public List<SessionWebsite> Websites { get; set; } = [];
}

public sealed class SessionApplication
{
    public int FocusSessionId { get; set; }
    public FocusSession FocusSession { get; set; } = null!;
    public int ApplicationEntryId { get; set; }
    public ApplicationEntry ApplicationEntry { get; set; } = null!;
}

public sealed class SessionWebsite
{
    public int FocusSessionId { get; set; }
    public FocusSession FocusSession { get; set; } = null!;
    public int WebsiteRuleId { get; set; }
    public WebsiteRule WebsiteRule { get; set; } = null!;
}

