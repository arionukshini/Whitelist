using FocusGuard.Core.Services;

namespace FocusGuard.Tests;

public sealed class DomainRuleMatcherTests
{
    [Theory]
    [InlineData("https://www.quran.com/surah/1", "quran.com")]
    [InlineData("www.github.com", "github.com")]
    [InlineData("learn.microsoft.com/docs", "learn.microsoft.com")]
    public void NormalizeDomain_ReturnsCleanHost(string input, string expected)
    {
        Assert.Equal(expected, DomainRuleMatcher.NormalizeDomain(input));
    }

    [Theory]
    [InlineData("quran.com", "quran.com", true)]
    [InlineData("api.quran.com", "quran.com", true)]
    [InlineData("fakequran.com", "quran.com", false)]
    [InlineData("quran.com.evil.com", "quran.com", false)]
    public void IsAllowed_UsesDomainBoundaries(string host, string rule, bool expected)
    {
        Assert.Equal(expected, DomainRuleMatcher.IsAllowed(host, rule, includeSubdomains: true));
    }

    [Fact]
    public void IsAllowed_CanDisableSubdomains()
    {
        Assert.False(DomainRuleMatcher.IsAllowed("api.quran.com", "quran.com", includeSubdomains: false));
    }
}

