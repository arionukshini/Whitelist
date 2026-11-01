using FocusGuard.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace FocusGuard.Enforcement;

public sealed class WebsiteAllowlistServer : IDisposable
{
    public const string ExtensionId = "injfodeemdicmnockbapnilgjpjifhoj";
    private const int Port = 47837;
    private const int MaximumHeaderBytes = 8192;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TcpListener _listener = new(IPAddress.Loopback, Port);
    private CancellationTokenSource? _cancellation;
    private long _lastExtensionRequestUtcTicks;
    public bool IsAvailable { get; private set; }

    public WebsiteAllowlistServer(IServiceScopeFactory scopeFactory) => _scopeFactory = scopeFactory;

    public bool IsExtensionConnected
    {
        get
        {
            var ticks = Interlocked.Read(ref _lastExtensionRequestUtcTicks);
            return ticks > 0 && DateTimeOffset.UtcNow.UtcTicks - ticks < TimeSpan.FromSeconds(90).Ticks;
        }
    }

    public void Start()
    {
        if (_cancellation is not null || IsAvailable) return;
        try
        {
            _listener.Start(8);
            _cancellation = new CancellationTokenSource();
            IsAvailable = true;
            _ = Task.Run(() => ListenAsync(_cancellation.Token));
        }
        catch (SocketException)
        {
            IsAvailable = false;
            _listener.Stop();
        }
    }

    private async Task ListenAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                using var client = await _listener.AcceptTcpClientAsync(cancellationToken);
                client.ReceiveTimeout = 5000;
                client.SendTimeout = 5000;
                await HandleAsync(client, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return; }
            catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested) { return; }
            catch (SocketException) when (cancellationToken.IsCancellationRequested) { return; }
            catch
            {
                if (!cancellationToken.IsCancellationRequested)
                    await Task.Delay(250, cancellationToken);
            }
        }
    }

    private async Task HandleAsync(TcpClient client, CancellationToken cancellationToken)
    {
        var stream = client.GetStream();
        var request = await ReadHeadersAsync(stream, cancellationToken);
        if (request is null) return;

        var requestLine = request.Split("\r\n", 2, StringSplitOptions.None)[0].Split(' ');
        if (requestLine.Length < 2 || requestLine[0] is not ("GET" or "OPTIONS"))
        {
            await WriteResponseAsync(stream, 405, "Method Not Allowed", "", null, cancellationToken);
            return;
        }

        var origin = request.Split("\r\n", StringSplitOptions.None)
            .FirstOrDefault(line => line.StartsWith("Origin:", StringComparison.OrdinalIgnoreCase))?
            .Split(':', 2)[1].Trim();
        var trustedOrigin = $"chrome-extension://{ExtensionId}";
        if (!string.Equals(origin, trustedOrigin, StringComparison.Ordinal))
        {
            await WriteResponseAsync(stream, 403, "Forbidden", "", null, cancellationToken);
            return;
        }

        if (requestLine[0] == "OPTIONS")
        {
            await WriteResponseAsync(stream, 204, "No Content", "", trustedOrigin, cancellationToken);
            return;
        }

        if (requestLine[1] != "/api/session")
        {
            await WriteResponseAsync(stream, 404, "Not Found", "", trustedOrigin, cancellationToken);
            return;
        }

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var store = scope.ServiceProvider.GetRequiredService<FocusGuardStore>();
            await store.CompleteExpiredSessionsAsync();
            var active = await store.GetActiveSessionAsync();
            Interlocked.Exchange(ref _lastExtensionRequestUtcTicks, DateTimeOffset.UtcNow.UtcTicks);
            var payload = active is null ? SessionResponse.Inactive : new SessionResponse(
                true,
                active.Name,
                active.EndTime,
                active.Websites.Select(x => new WebsiteResponse(x.WebsiteRule.Domain, x.WebsiteRule.IncludeSubdomains)).ToArray());
            var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            await WriteResponseAsync(stream, 200, "OK", json, trustedOrigin, cancellationToken);
        }
        catch
        {
            await WriteResponseAsync(stream, 503, "Service Unavailable", "{\"error\":\"session_unavailable\"}", trustedOrigin, cancellationToken);
        }
    }

    private static async Task<string?> ReadHeadersAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[1024];
        while (buffer.Length < MaximumHeaderBytes)
        {
            var count = await stream.ReadAsync(chunk, cancellationToken);
            if (count == 0) return null;
            buffer.Write(chunk, 0, count);
            var text = Encoding.ASCII.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
            if (text.Contains("\r\n\r\n", StringComparison.Ordinal)) return text;
        }
        return null;
    }

    private static async Task WriteResponseAsync(NetworkStream stream, int status, string reason, string body, string? allowedOrigin, CancellationToken cancellationToken)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        var headers = new StringBuilder()
            .Append("HTTP/1.1 ").Append(status).Append(' ').Append(reason).Append("\r\n")
            .Append("Content-Type: application/json; charset=utf-8\r\n")
            .Append("Cache-Control: no-store\r\nConnection: close\r\nContent-Length: ").Append(bytes.Length).Append("\r\n");
        if (allowedOrigin is not null)
            headers.Append("Access-Control-Allow-Origin: ").Append(allowedOrigin).Append("\r\nAccess-Control-Allow-Methods: GET, OPTIONS\r\n");
        headers.Append("\r\n");
        await stream.WriteAsync(Encoding.ASCII.GetBytes(headers.ToString()), cancellationToken);
        if (bytes.Length > 0) await stream.WriteAsync(bytes, cancellationToken);
    }

    public void Dispose()
    {
        _cancellation?.Cancel();
        _listener.Stop();
        IsAvailable = false;
        _cancellation?.Dispose();
        _cancellation = null;
    }

    private sealed record SessionResponse(bool Active, string? Name, DateTimeOffset? EndTime, IReadOnlyList<WebsiteResponse> Websites)
    {
        public static readonly SessionResponse Inactive = new(false, null, null, []);
    }

    private sealed record WebsiteResponse(string Domain, bool IncludeSubdomains);
}
