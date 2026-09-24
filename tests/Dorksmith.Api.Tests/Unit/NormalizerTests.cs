using Dorksmith.Api.Generation;

namespace Dorksmith.Api.Tests.Unit;

public class NormalizerTests
{
    [Theory]
    [InlineData("  hello   world  ", "hello world")]
    [InlineData("“quarterly roadmap”", "\"quarterly roadmap\"")]
    [InlineData("it’s", "it's")]
    [InlineData("tab\tseparated", "tab separated")]
    [InlineData("Keep Case", "Keep Case")]
    [InlineData("ctrl\u0001char", "ctrlchar")]
    public void NormalizeText_collapses_whitespace_and_maps_quotes(string input, string expected)
        => Assert.Equal(expected, QueryNormalizer.NormalizeText(input));

    [Fact]
    public void NormalizeText_does_not_lowercase()
        => Assert.Equal("Alice Smith", QueryNormalizer.NormalizeText("Alice Smith"));

    [Fact]
    public void Words_unwraps_fully_quoted_phrase()
    {
        var (words, quoted) = QueryNormalizer.Words("\"quarterly roadmap\"");
        Assert.True(quoted);
        Assert.Equal(["quarterly", "roadmap"], words);
    }

    [Theory]
    [InlineData("example.com", "example.com")]
    [InlineData("https://www.example.com/path?q=1", "example.com")]
    [InlineData("EXAMPLE.COM.", "example.com")]
    [InlineData("sub.example.co.uk:8443", "sub.example.co.uk")]
    [InlineData("*.example.com", "example.com")]
    [InlineData("www.example.com", "example.com")]
    [InlineData("192.168.1.10", "192.168.1.10")]
    [InlineData("xn--bcher-kva.example", "xn--bcher-kva.example")]
    public void TryNormalizeDomain_accepts_hostnames(string input, string expected)
    {
        Assert.True(QueryNormalizer.TryNormalizeDomain(input, out var d));
        Assert.Equal(expected, d);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a domain")]
    [InlineData("localhost")]
    [InlineData("example")]
    [InlineData("-bad.example.com")]
    [InlineData("exa mple.com")]
    [InlineData("example.c")]
    public void TryNormalizeDomain_rejects_invalid(string input)
        => Assert.False(QueryNormalizer.TryNormalizeDomain(input, out _));

    [Theory]
    [InlineData("example.com", "example")]
    [InlineData("www.example.co.uk", "example")]
    [InlineData("docs.example.org", "example")]
    [InlineData("example.io", "example")]
    public void DomainLabel_picks_organisation_label(string domain, string expected)
        => Assert.Equal(expected, QueryNormalizer.DomainLabel(domain));

    [Theory]
    [InlineData("https://Example.com/Path/", "https://example.com/Path")]
    [InlineData("example.com/docs/api", "https://example.com/docs/api")]
    [InlineData("http://example.com:80/x#frag", "http://example.com/x")]
    public void TryNormalizeUrl_normalises_http_urls(string input, string expected)
    {
        Assert.True(QueryNormalizer.TryNormalizeUrl(input, out var url));
        Assert.Equal(expected, url.ToString());
    }

    [Theory]
    [InlineData("ftp://example.com/file")]
    [InlineData("javascript:alert(1)")]
    [InlineData("not a url")]
    public void TryNormalizeUrl_rejects_non_http(string input)
        => Assert.False(QueryNormalizer.TryNormalizeUrl(input, out _));

    [Fact]
    public void BareUrl_strips_scheme_and_trailing_slash()
    {
        QueryNormalizer.TryNormalizeUrl("https://example.com/a/b/", out var url);
        Assert.Equal("example.com/a/b", QueryNormalizer.BareUrl(url));
    }

    [Theory]
    [InlineData("@alice42", "alice42")]
    [InlineData("@@alice", "alice")]
    [InlineData("  alice.b_c-d ", "alice.b_c-d")]
    [InlineData("Ünïcode", "Ünïcode")]
    public void TryNormalizeUsername_strips_at_and_keeps_case(string input, string expected)
    {
        Assert.True(QueryNormalizer.TryNormalizeUsername(input, 100, out var u));
        Assert.Equal(expected, u);
    }

    [Theory]
    [InlineData("")]
    [InlineData("@")]
    [InlineData("has space")]
    [InlineData("   ")]
    public void TryNormalizeUsername_rejects_empty_and_whitespace(string input)
        => Assert.False(QueryNormalizer.TryNormalizeUsername(input, 100, out _));

    [Fact]
    public void TryNormalizeUsername_enforces_max_length()
        => Assert.False(QueryNormalizer.TryNormalizeUsername(new string('a', 101), 100, out _));

    [Theory]
    [InlineData("Alice.Smith@Example.COM", "Alice.Smith@example.com", "Alice.Smith", "example.com")]
    [InlineData("<bob@example.org>", "bob@example.org", "bob", "example.org")]
    public void TryNormalizeEmail_lowercases_domain_only(string input, string email, string user, string domain)
    {
        Assert.True(QueryNormalizer.TryNormalizeEmail(input, out var e, out var u, out var d));
        Assert.Equal(email, e);
        Assert.Equal(user, u);
        Assert.Equal(domain, d);
    }

    [Theory]
    [InlineData("alice@")]
    [InlineData("alice@localhost")]
    [InlineData("alice example.com")]
    [InlineData("alice@@example.com")]
    public void TryNormalizeEmail_rejects_invalid(string input)
        => Assert.False(QueryNormalizer.TryNormalizeEmail(input, out _, out _, out _));

    [Fact]
    public void TryParseFilename_splits_stem_and_extension()
    {
        Assert.True(QueryNormalizer.TryParseFilename("Annual Report 2024.PDF", out var stem, out var ext));
        Assert.Equal("Annual Report 2024", stem);
        Assert.Equal("pdf", ext);
        Assert.False(QueryNormalizer.TryParseFilename("noext", out _, out _));
    }

    [Theory]
    [InlineData("2024-01-31", true)]
    [InlineData("2024-13-01", false)]
    [InlineData("31/01/2024", false)]
    [InlineData("2024-1-1", false)]
    public void TryParseIsoDate_is_strict(string input, bool ok)
        => Assert.Equal(ok, QueryNormalizer.TryParseIsoDate(input, out _));

    [Theory]
    [InlineData("SITE:example.com   FileType:pdf  or  x", "site:example.com filetype:pdf OR x")]
    [InlineData("(intitle:\"Index Of\" OR intext:x)", "(intitle:\"Index Of\" OR intext:x)")]
    [InlineData("-Site:www.example.com", "-site:www.example.com")]
    public void Canonicalize_normalises_operator_casing_and_whitespace(string input, string expected)
        => Assert.Equal(expected, QueryNormalizer.Canonicalize(input));
}
