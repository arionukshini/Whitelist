using System.Buffers.Binary;
using System.Text.Json;
using FocusGuard.Core.Models;
using FocusGuard.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

const int maximumMessageBytes = 1024 * 1024;
var input = Console.OpenStandardInput();
var output = Console.OpenStandardOutput();
var header = new byte[4];

while (await ReadExactlyAsync(input, header))
{
    var length = BinaryPrimitives.ReadInt32LittleEndian(header);
    if (length <= 0 || length > maximumMessageBytes) return 1;

    var message = new byte[length];
    if (!await ReadExactlyAsync(input, message)) return 1;

    byte[] response;
    try
    {
        using var document = JsonDocument.Parse(message);
        var type = document.RootElement.TryGetProperty("type", out var typeElement) ? typeElement.GetString() : null;
        response = type == "get-session"
            ? JsonSerializer.SerializeToUtf8Bytes(await ReadSessionAsync(), new JsonSerializerOptions(JsonSerializerDefaults.Web))
            : JsonSerializer.SerializeToUtf8Bytes(new { error = "unsupported_message" });
    }
    catch (Exception exception)
    {
        response = JsonSerializer.SerializeToUtf8Bytes(new { error = "session_unavailable", detail = exception.Message });
    }

    BinaryPrimitives.WriteInt32LittleEndian(header, response.Length);
    await output.WriteAsync(header);
    await output.WriteAsync(response);
    await output.FlushAsync();
}

return 0;

static async Task<bool> ReadExactlyAsync(Stream stream, Memory<byte> buffer)
{
    var offset = 0;
    while (offset < buffer.Length)
    {
        var read = await stream.ReadAsync(buffer[offset..]);
        if (read == 0) return offset == 0 && buffer.Length == 0;
        offset += read;
    }
    return true;
}

static async Task<SessionPayload> ReadSessionAsync()
{
    var databasePath = DatabasePaths.DefaultDatabasePath;
    if (!File.Exists(databasePath))
    {
        MarkConnected();
        return SessionPayload.Inactive;
    }

    var options = new DbContextOptionsBuilder<FocusGuardDbContext>()
        .UseSqlite($"Data Source={databasePath};Pooling=False;Default Timeout=5")
        .Options;
    await using var db = new FocusGuardDbContext(options);
    var store = new FocusGuardStore(db);
    await store.CompleteExpiredSessionsAsync();
    var session = await store.GetActiveSessionAsync();
    MarkConnected();
    return session is null
        ? SessionPayload.Inactive
        : new SessionPayload(true, session.Name, session.EndTime,
            session.Websites.Select(website => new WebsitePayload(website.WebsiteRule.Domain, website.WebsiteRule.IncludeSubdomains)).ToArray());
}

static void MarkConnected()
{
    try
    {
        Directory.CreateDirectory(Path.GetDirectoryName(DatabasePaths.DefaultExtensionHeartbeatPath)!);
        File.WriteAllText(DatabasePaths.DefaultExtensionHeartbeatPath, DateTimeOffset.UtcNow.ToString("O"));
    }
    catch (IOException) { }
    catch (UnauthorizedAccessException) { }
}

sealed record SessionPayload(bool Active, string? Name, DateTimeOffset? EndTime, IReadOnlyList<WebsitePayload> Websites)
{
    public static readonly SessionPayload Inactive = new(false, null, null, []);
}

sealed record WebsitePayload(string Domain, bool IncludeSubdomains);
