using Umbraco.RedirectManager.Services;
using Xunit;

namespace Umbraco.RedirectManager.Tests.Services;

public class DomainNormalizerTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Normalize_NullOrWhitespace_ReturnsNull(string? input)
    {
        Assert.Null(DomainNormalizer.Normalize(input));
    }

    [Fact]
    public void Normalize_LowercasesAndTrims()
    {
        Assert.Equal("example.com", DomainNormalizer.Normalize("  Example.COM  "));
    }

    [Fact]
    public void Normalize_StripsTrailingPort()
    {
        Assert.Equal("example.com", DomainNormalizer.Normalize("example.com:8080"));
    }

    [Fact]
    public void Normalize_StripsBareTrailingColon()
    {
        Assert.Equal("example.com", DomainNormalizer.Normalize("example.com:"));
    }

    [Fact]
    public void Normalize_BareIPv6Literal_IsNotCorruptedByInternalColons()
    {
        // No trailing port here -- the last ':' is inside the brackets, part
        // of the address itself. The guard must recognize this and leave the
        // value untouched rather than truncating at that internal colon.
        Assert.Equal("[::1]", DomainNormalizer.Normalize("[::1]"));
    }

    [Fact]
    public void Normalize_IPv6LiteralWithPort_StripsOnlyThePort()
    {
        // Here the last ':' genuinely is a port separator (it comes after the
        // closing ']'), so it should be stripped, same as any other host:port.
        Assert.Equal("[::1]", DomainNormalizer.Normalize("[::1]:8080"));
    }

    [Fact]
    public void Normalize_DoesNotStripWwwPrefix()
    {
        Assert.Equal("www.example.com", DomainNormalizer.Normalize("www.example.com"));
    }
}
