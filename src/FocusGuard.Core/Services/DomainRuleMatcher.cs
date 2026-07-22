namespace FocusGuard.Core.Services;

public static class DomainRuleMatcher
{
    public static string NormalizeDomain(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            throw new ArgumentException("Domain is required.", nameof(input));
        }

        var trimmed = input.Trim();
        if (!trimmed.Contains("://", StringComparison.Ordinal))
        {
            trimmed = "https://" + trimmed;
        }

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) || string.IsNullOrWhiteSpace(uri.Host))
        {
            throw new ArgumentException("Enter a valid domain or URL.", nameof(input));
        }

        var host = uri.IdnHost.Trim('.').ToLowerInvariant();
        if (host.StartsWith("www.", StringComparison.Ordinal))
        {
            host = host[4..];
        }

        if (host.Length == 0 || host.Contains("..", StringComparison.Ordinal))
        {
            throw new ArgumentException("Enter a valid domain or URL.", nameof(input));
        }

        return host;
    }

    public static bool IsAllowed(string hostname, string ruleDomain, bool includeSubdomains)
    {
        var host = NormalizeHost(hostname);
        var rule = NormalizeDomain(ruleDomain);

        if (host.Equals(rule, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return includeSubdomains && host.EndsWith("." + rule, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeHost(string hostname)
    {
        var host = hostname.Trim().Trim('.').ToLowerInvariant();
        if (host.StartsWith("www.", StringComparison.Ordinal))
        {
            host = host[4..];
        }

        return host;
    }
}

