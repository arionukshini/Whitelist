namespace FocusGuard.Core.Models;

public sealed class WebsiteRule
{
    public int Id { get; set; }
    public string Domain { get; set; } = string.Empty;
    public bool IncludeSubdomains { get; set; } = true;
}

