namespace FocusGuard.Core.Models;

public sealed class Profile
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public List<ProfileApplication> Applications { get; set; } = [];
    public List<ProfileWebsite> Websites { get; set; } = [];
}

public sealed class ProfileApplication
{
    public int ProfileId { get; set; }
    public Profile Profile { get; set; } = null!;
    public int ApplicationEntryId { get; set; }
    public ApplicationEntry ApplicationEntry { get; set; } = null!;
}

public sealed class ProfileWebsite
{
    public int ProfileId { get; set; }
    public Profile Profile { get; set; } = null!;
    public int WebsiteRuleId { get; set; }
    public WebsiteRule WebsiteRule { get; set; } = null!;
}

