using System.Text.RegularExpressions;
using Umbraco.RedirectManager.Services;
using Xunit;

namespace Umbraco.RedirectManager.Tests.Services;

public class WildcardPatternBuilderTests
{
    [Theory]
    [InlineData("/blog/hello-world", true)]
    [InlineData("/blog/", true)]
    [InlineData("/blogx/hello", false)]
    public void BuildRegexPattern_SingleWildcard_MatchesExpectedPaths(string path, bool expectedMatch)
    {
        var pattern = WildcardPatternBuilder.BuildRegexPattern("/blog/*");
        var regex = new Regex(pattern, RegexOptions.IgnoreCase);

        Assert.Equal(expectedMatch, regex.IsMatch(path));
    }

    [Fact]
    public void BuildRegexPattern_EscapesLiteralRegexMetacharacters()
    {
        var pattern = WildcardPatternBuilder.BuildRegexPattern("/a.b/*/c+d");
        var regex = new Regex(pattern, RegexOptions.IgnoreCase);

        Assert.True(regex.IsMatch("/a.b/xyz/c+d"));
        Assert.False(regex.IsMatch("/aXb/xyz/c+d")); // '.' must not act as "any character"
        Assert.False(regex.IsMatch("/a.b/xyz/cd"));  // '+' must not act as a quantifier
    }

    [Fact]
    public void BuildRegexPattern_NoWildcard_FallsBackToLiteralExactMatch()
    {
        var pattern = WildcardPatternBuilder.BuildRegexPattern("/exact/path");
        var regex = new Regex(pattern, RegexOptions.IgnoreCase);

        Assert.True(regex.IsMatch("/exact/path"));
        Assert.False(regex.IsMatch("/exact/path/extra"));
    }

    [Fact]
    public void BuildRegexPattern_CapturesTheWildcardSegment()
    {
        var pattern = WildcardPatternBuilder.BuildRegexPattern("/blog/*");
        var regex = new Regex(pattern, RegexOptions.IgnoreCase);

        var match = regex.Match("/blog/hello-world");

        Assert.True(match.Success);
        Assert.Equal("hello-world", match.Groups[1].Value);
    }
}
