using FocusGuard.Enforcement;
using FocusGuard.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Net;

namespace FocusGuard.Tests;

public sealed class WebsiteAllowlistServerTests
{
    [Fact]
    public async Task LoopbackApi_ServesSessionOnlyToThePinnedExtensionOrigin()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), "WhitelistLockInTests", Guid.NewGuid().ToString("N"), "test.db");
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
        var services = new ServiceCollection();
        services.AddDbContext<FocusGuardDbContext>(options => options.UseSqlite($"Data Source={databasePath};Pooling=False"));
        services.AddScoped<FocusGuardStore>();
        await using var provider = services.BuildServiceProvider();
        var server = new WebsiteAllowlistServer(provider.GetRequiredService<IServiceScopeFactory>(), port: 0);

        try
        {
            await using (var scope = provider.CreateAsyncScope())
                await scope.ServiceProvider.GetRequiredService<FocusGuardStore>().InitializeAsync();
            server.Start();
            Assert.True(server.IsAvailable);

            using var client = new HttpClient();
            using var trustedRequest = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{server.ListeningPort}/api/session");
            trustedRequest.Headers.TryAddWithoutValidation("Origin", $"chrome-extension://{WebsiteAllowlistServer.ExtensionId}");
            using var trustedResponse = await client.SendAsync(trustedRequest);
            Assert.Equal(HttpStatusCode.OK, trustedResponse.StatusCode);
            Assert.Equal($"chrome-extension://{WebsiteAllowlistServer.ExtensionId}", Assert.Single(trustedResponse.Headers.GetValues("Access-Control-Allow-Origin")));
            Assert.Contains("\"active\":false", await trustedResponse.Content.ReadAsStringAsync());
            Assert.True(server.IsExtensionConnected);

            using var untrustedRequest = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{server.ListeningPort}/api/session");
            untrustedRequest.Headers.TryAddWithoutValidation("Origin", "https://example.com");
            using var untrustedResponse = await client.SendAsync(untrustedRequest);
            Assert.Equal(HttpStatusCode.Forbidden, untrustedResponse.StatusCode);
        }
        finally
        {
            server.Dispose();
            var directory = Path.GetDirectoryName(databasePath)!;
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }
}
