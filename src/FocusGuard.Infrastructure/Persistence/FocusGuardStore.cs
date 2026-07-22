using FocusGuard.Core.Models;
using FocusGuard.Core.Services;
using Microsoft.EntityFrameworkCore;

namespace FocusGuard.Infrastructure.Persistence;

public sealed class FocusGuardStore
{
    private readonly FocusGuardDbContext _db;

    public FocusGuardStore(FocusGuardDbContext db)
    {
        _db = db;
    }

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_db.Database.GetDbConnection().DataSource)!);
        await _db.Database.EnsureCreatedAsync();
        await CompleteExpiredSessionsAsync();
    }

    public Task<List<ApplicationEntry>> GetApplicationsAsync() =>
        _db.Applications.OrderBy(x => x.Name).ToListAsync();

    public Task<List<WebsiteRule>> GetWebsitesAsync() =>
        _db.Websites.OrderBy(x => x.Domain).ToListAsync();

    public Task<List<Profile>> GetProfilesAsync() =>
        _db.Profiles
            .Include(x => x.Applications).ThenInclude(x => x.ApplicationEntry)
            .Include(x => x.Websites).ThenInclude(x => x.WebsiteRule)
            .OrderBy(x => x.Name)
            .ToListAsync();

    public async Task<List<FocusSession>> GetHistoryAsync()
    {
        var sessions = await _db.FocusSessions
            .Where(x => x.Status != FocusSessionStatus.Active)
            .ToListAsync();

        return sessions
            .OrderByDescending(x => x.StartTime)
            .Take(50)
            .ToList();
    }

    public Task<FocusSession?> GetActiveSessionAsync() =>
        _db.FocusSessions
            .Include(x => x.Applications).ThenInclude(x => x.ApplicationEntry)
            .Include(x => x.Websites).ThenInclude(x => x.WebsiteRule)
            .FirstOrDefaultAsync(x => x.Status == FocusSessionStatus.Active);

    public async Task<ApplicationEntry> AddApplicationAsync(string name, string executablePath)
    {
        var path = Path.GetFullPath(executablePath);
        var existing = await _db.Applications.FirstOrDefaultAsync(x => x.ExecutablePath == path);
        if (existing is not null)
        {
            return existing;
        }

        var app = new ApplicationEntry { Name = name, ExecutablePath = path };
        _db.Applications.Add(app);
        await _db.SaveChangesAsync();
        return app;
    }

    public async Task<WebsiteRule> AddWebsiteAsync(string input, bool includeSubdomains)
    {
        var domain = DomainRuleMatcher.NormalizeDomain(input);
        var existing = await _db.Websites.FirstOrDefaultAsync(x => x.Domain == domain);
        if (existing is not null)
        {
            existing.IncludeSubdomains = includeSubdomains;
            await _db.SaveChangesAsync();
            return existing;
        }

        var website = new WebsiteRule { Domain = domain, IncludeSubdomains = includeSubdomains };
        _db.Websites.Add(website);
        await _db.SaveChangesAsync();
        return website;
    }

    public async Task<Profile> CreateProfileAsync(string name, IEnumerable<int> applicationIds, IEnumerable<int> websiteIds)
    {
        var profile = new Profile { Name = name.Trim() };
        profile.Applications.AddRange(applicationIds.Distinct().Select(id => new ProfileApplication { ApplicationEntryId = id }));
        profile.Websites.AddRange(websiteIds.Distinct().Select(id => new ProfileWebsite { WebsiteRuleId = id }));
        _db.Profiles.Add(profile);
        await _db.SaveChangesAsync();
        return profile;
    }

    public async Task<FocusSession> StartSessionAsync(int? profileId, string name, TimeSpan duration, IEnumerable<int> applicationIds, IEnumerable<int> websiteIds)
    {
        await CompleteExpiredSessionsAsync();

        var active = await _db.FocusSessions.FirstOrDefaultAsync(x => x.Status == FocusSessionStatus.Active);
        if (active is not null)
        {
            active.Status = FocusSessionStatus.Cancelled;
        }

        var session = new FocusSession
        {
            ProfileId = profileId,
            Name = name.Trim(),
            StartTime = DateTimeOffset.Now,
            EndTime = DateTimeOffset.Now.Add(duration),
            Status = FocusSessionStatus.Active
        };

        session.Applications.AddRange(applicationIds.Distinct().Select(id => new SessionApplication { ApplicationEntryId = id }));
        session.Websites.AddRange(websiteIds.Distinct().Select(id => new SessionWebsite { WebsiteRuleId = id }));
        _db.FocusSessions.Add(session);
        await _db.SaveChangesAsync();
        return session;
    }

    public async Task EndActiveSessionAsync(FocusSessionStatus status = FocusSessionStatus.Cancelled)
    {
        var active = await _db.FocusSessions.FirstOrDefaultAsync(x => x.Status == FocusSessionStatus.Active);
        if (active is null)
        {
            return;
        }

        active.Status = status;
        await _db.SaveChangesAsync();
    }

    public async Task CompleteExpiredSessionsAsync()
    {
        var now = DateTimeOffset.Now;
        var activeSessions = await _db.FocusSessions
            .Where(x => x.Status == FocusSessionStatus.Active)
            .ToListAsync();
        var expired = activeSessions
            .Where(x => x.EndTime <= now)
            .ToList();

        foreach (var session in expired)
        {
            session.Status = FocusSessionStatus.Completed;
        }

        if (expired.Count > 0)
        {
            await _db.SaveChangesAsync();
        }
    }
}
