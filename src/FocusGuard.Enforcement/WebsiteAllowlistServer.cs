using FocusGuard.Core.Models;
using FocusGuard.Infrastructure.Persistence;
using System.Net;
using System.Text.Json;

namespace FocusGuard.Enforcement;

public sealed class WebsiteAllowlistServer : IDisposable
{
    private readonly FocusGuardStore _store;
    private readonly HttpListener _listener = new();
    private CancellationTokenSource? _cancellation;

    public WebsiteAllowlistServer(FocusGuardStore store)
    {
        _store = store;
        _listener.Prefixes.Add("http://127.0.0.1:47837/");
    }

    public void Start()
    {
        if (_listener.IsListening)
        {
            return;
        }

        _cancellation = new CancellationTokenSource();
        _listener.Start();
        _ = Task.Run(() => ListenAsync(_cancellation.Token));
    }

    private async Task ListenAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && _listener.IsListening)
        {
            try
            {
                var context = await _listener.GetContextAsync();
                _ = Task.Run(() => HandleAsync(context), cancellationToken);
            }
            catch when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch
            {
                await Task.Delay(500, cancellationToken);
            }
        }
    }

    private async Task HandleAsync(HttpListenerContext context)
    {
        context.Response.Headers.Add("Access-Control-Allow-Origin", "*");
        context.Response.Headers.Add("Access-Control-Allow-Headers", "content-type");

        if (context.Request.HttpMethod == "OPTIONS")
        {
            context.Response.StatusCode = 204;
            context.Response.Close();
            return;
        }

        if (context.Request.Url?.AbsolutePath != "/api/session")
        {
            context.Response.StatusCode = 404;
            context.Response.Close();
            return;
        }

        await _store.CompleteExpiredSessionsAsync();
        var active = await _store.GetActiveSessionAsync();
        var payload = active is null ? SessionResponse.Inactive : new SessionResponse(
            true,
            active.Name,
            active.EndTime,
            active.Websites.Select(x => new WebsiteResponse(x.WebsiteRule.Domain, x.WebsiteRule.IncludeSubdomains)).ToArray());

        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        context.Response.ContentType = "application/json";
        context.Response.ContentLength64 = bytes.Length;
        await context.Response.OutputStream.WriteAsync(bytes);
        context.Response.Close();
    }

    public void Dispose()
    {
        _cancellation?.Cancel();
        if (_listener.IsListening)
        {
            _listener.Stop();
        }

        _listener.Close();
        _cancellation?.Dispose();
    }

    private sealed record SessionResponse(bool Active, string? Name, DateTimeOffset? EndTime, IReadOnlyList<WebsiteResponse> Websites)
    {
        public static readonly SessionResponse Inactive = new(false, null, null, []);
    }

    private sealed record WebsiteResponse(string Domain, bool IncludeSubdomains);
}

