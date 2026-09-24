using Dorksmith.Api.Generation;

namespace Dorksmith.Api.Tests.Unit;

public class QuotingTests
{
    [Theory]
    [InlineData("quarterly roadmap", "\"quarterly roadmap\"")]
    [InlineData("\"already quoted\"", "\"already quoted\"")]
    [InlineData("nested \"inner\" quotes", "\"nested inner quotes\"")]
    [InlineData("  padded  ", "\"padded\"")]
    [InlineData("Ünïcödé 日本", "\"Ünïcödé 日本\"")]
    [InlineData("", "")]
    [InlineData("\"\"", "")]
    public void Quote_is_idempotent_and_strips_nested_quotes(string input, string expected)
        => Assert.Equal(expected, QueryQuoting.Quote(input));

    [Theory]
    [InlineData("roadmap", "roadmap")]
    [InlineData("-secret", "\"-secret\"")]
    [InlineData("site:evil.com", "\"site:evil.com\"")]
    [InlineData("OR", "\"OR\"")]
    [InlineData("AND", "\"AND\"")]
    [InlineData("(group", "\"group\"")]
    [InlineData("AROUND(3)", "\"AROUND(3)\"")]
    [InlineData("10:30", "10:30")]
    [InlineData("C++", "C++")]
    public void SafeTerm_neutralises_user_text_that_looks_like_syntax(string input, string expected)
        => Assert.Equal(expected, QueryQuoting.SafeTerm(input));

    [Theory]
    [InlineData("roadmap", "roadmap")]
    [InlineData("quarterly roadmap", "\"quarterly roadmap\"")]
    [InlineData("\"quoted\"", "quoted")]
    [InlineData("-x", "\"-x\"")]
    public void OperatorValue_quotes_only_when_needed(string input, string expected)
        => Assert.Equal(expected, QueryQuoting.OperatorValue(input));

    [Fact]
    public void OrGroup_handles_zero_one_many()
    {
        Assert.Equal("", QueryQuoting.OrGroup([]));
        Assert.Equal("filetype:pdf", QueryQuoting.OrGroup(["filetype:pdf"]));
        Assert.Equal("(filetype:pdf OR filetype:docx)", QueryQuoting.OrGroup(["filetype:pdf", "filetype:docx", "filetype:pdf"]));
    }

    [Theory]
    [InlineData("jobs", "-jobs")]
    [InlineData("-jobs", "-jobs")]
    [InlineData("privacy policy", "-\"privacy policy\"")]
    [InlineData("site:pinterest.com", "-site:pinterest.com")]
    [InlineData("SITE:pinterest.com", "-site:pinterest.com")]
    [InlineData("cache:example.com", "-\"cache:example.com\"")]
    [InlineData("bogus:thing", "-\"bogus:thing\"")]
    public void Exclusion_passes_generatable_operators_and_quotes_everything_else(string input, string expected)
    {
        var ops = TestServices.GoogleOperators();
        Assert.Equal(expected, QueryQuoting.Exclusion(input, token => ops.ByToken.TryGetValue(token, out var op) && op.Generate));
    }
}
