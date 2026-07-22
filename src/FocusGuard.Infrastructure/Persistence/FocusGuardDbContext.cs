using FocusGuard.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace FocusGuard.Infrastructure.Persistence;

public sealed class FocusGuardDbContext(DbContextOptions<FocusGuardDbContext> options) : DbContext(options)
{
    public DbSet<ApplicationEntry> Applications => Set<ApplicationEntry>();
    public DbSet<WebsiteRule> Websites => Set<WebsiteRule>();
    public DbSet<Profile> Profiles => Set<Profile>();
    public DbSet<FocusSession> FocusSessions => Set<FocusSession>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ApplicationEntry>(entity =>
        {
            entity.HasIndex(x => x.ExecutablePath).IsUnique();
            entity.Property(x => x.Name).HasMaxLength(200);
            entity.Property(x => x.ExecutablePath).HasMaxLength(1000);
        });

        modelBuilder.Entity<WebsiteRule>(entity =>
        {
            entity.HasIndex(x => x.Domain).IsUnique();
            entity.Property(x => x.Domain).HasMaxLength(255);
        });

        modelBuilder.Entity<Profile>(entity => entity.Property(x => x.Name).HasMaxLength(120));

        modelBuilder.Entity<ProfileApplication>().HasKey(x => new { x.ProfileId, x.ApplicationEntryId });
        modelBuilder.Entity<ProfileWebsite>().HasKey(x => new { x.ProfileId, x.WebsiteRuleId });
        modelBuilder.Entity<SessionApplication>().HasKey(x => new { x.FocusSessionId, x.ApplicationEntryId });
        modelBuilder.Entity<SessionWebsite>().HasKey(x => new { x.FocusSessionId, x.WebsiteRuleId });
    }
}

