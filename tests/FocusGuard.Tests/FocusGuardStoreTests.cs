using FocusGuard.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FocusGuard.Tests;

public sealed class FocusGuardStoreTests
{
    [Fact]
    public async Task AddApplicationAsync_DeduplicatesWindowsPathsWithoutCaseSensitivity()
    {
        var databasePath = CreateDatabasePath();
        try
        {
            await using (var context = CreateContext(databasePath))
            {
                var store = new FocusGuardStore(context);
                await store.InitializeAsync();

                var first = await store.AddApplicationAsync("Editor", @"C:\Program Files\Editor\Editor.exe");
                var duplicate = await store.AddApplicationAsync("Editor Copy", @"c:\program files\editor\editor.exe");

                Assert.Equal(first.Id, duplicate.Id);
                Assert.Single(await store.GetApplicationsAsync());
            }
        }
        finally
        {
            DeleteDatabase(databasePath);
        }
    }

    [Fact]
    public async Task ActiveSession_IsRestoredAfterReopeningTheDatabase()
    {
        var databasePath = CreateDatabasePath();
        try
        {
            int applicationId;
            await using (var context = CreateContext(databasePath))
            {
                var store = new FocusGuardStore(context);
                await store.InitializeAsync();
                var app = await store.AddApplicationAsync("Editor", @"C:\Program Files\Editor\Editor.exe");
                applicationId = app.Id;
                await store.StartSessionAsync(null, "Focus mode", TimeSpan.FromMinutes(20), [applicationId], []);
            }

            await using (var reopenedContext = CreateContext(databasePath))
            {
                var reopenedStore = new FocusGuardStore(reopenedContext);
                await reopenedStore.InitializeAsync();
                var session = await reopenedStore.GetActiveSessionAsync();

                Assert.NotNull(session);
                Assert.True(session.EndTime > DateTimeOffset.Now);
                Assert.Equal(applicationId, Assert.Single(session.Applications).ApplicationEntryId);
            }
        }
        finally
        {
            DeleteDatabase(databasePath);
        }
    }

    [Fact]
    public async Task SavedProfile_RestoresAndUpdatesItsAppAndWebsiteSelection()
    {
        var databasePath = CreateDatabasePath();
        try
        {
            await using (var context = CreateContext(databasePath))
            {
                var store = new FocusGuardStore(context);
                await store.InitializeAsync();
                var app = await store.AddApplicationAsync("Editor", @"C:\Program Files\Editor\Editor.exe");
                var website = await store.AddWebsiteAsync("github.com", includeSubdomains: true);
                var profile = await store.CreateProfileAsync("Writing", [app.Id], [website.Id]);

                var loaded = Assert.Single(await store.GetProfilesAsync());
                Assert.Equal(profile.Id, loaded.Id);
                Assert.Equal(app.Id, Assert.Single(loaded.Applications).ApplicationEntryId);
                Assert.Equal(website.Id, Assert.Single(loaded.Websites).WebsiteRuleId);

                await store.UpdateProfileAsync(profile.Id, "Reading", [], [website.Id]);
                loaded = Assert.Single(await store.GetProfilesAsync());
                Assert.Equal("Reading", loaded.Name);
                Assert.Empty(loaded.Applications);
                Assert.Equal(website.Id, Assert.Single(loaded.Websites).WebsiteRuleId);

                await store.UpdateProfileAsync(profile.Id, "Writing", [app.Id], [website.Id]);
                loaded = Assert.Single(await store.GetProfilesAsync());
                Assert.Equal(app.Id, Assert.Single(loaded.Applications).ApplicationEntryId);
            }
        }
        finally
        {
            DeleteDatabase(databasePath);
        }
    }

    [Fact]
    public async Task InitializeAsync_AddsTheAppSourceColumnToAnExistingDatabase()
    {
        var databasePath = CreateDatabasePath();
        try
        {
            await using (var oldContext = CreateContext(databasePath))
            {
                var oldStore = new FocusGuardStore(oldContext);
                await oldStore.InitializeAsync();
                await oldStore.AddApplicationAsync("Editor", @"C:\Program Files\Editor\Editor.exe");
                await oldContext.Database.ExecuteSqlRawAsync("ALTER TABLE Applications DROP COLUMN Source");
            }

            await using (var migratedContext = CreateContext(databasePath))
            {
                var migratedStore = new FocusGuardStore(migratedContext);
                await migratedStore.InitializeAsync();
                var application = Assert.Single(await migratedStore.GetApplicationsAsync());
                Assert.Equal(FocusGuard.Core.Models.ApplicationSource.Legacy, application.Source);
            }
        }
        finally
        {
            DeleteDatabase(databasePath);
        }
    }

    private static FocusGuardDbContext CreateContext(string path) => new(
        new DbContextOptionsBuilder<FocusGuardDbContext>()
            .UseSqlite($"Data Source={path};Pooling=False")
            .Options);

    private static string CreateDatabasePath()
    {
        var directory = Path.Combine(Path.GetTempPath(), "WhitelistLockInTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, "test.db");
    }

    private static void DeleteDatabase(string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (directory is not null && Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }
}
