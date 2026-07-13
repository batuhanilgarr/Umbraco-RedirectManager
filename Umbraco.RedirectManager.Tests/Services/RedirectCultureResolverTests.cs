using Microsoft.Extensions.Caching.Memory;
using NSubstitute;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;
using Umbraco.RedirectManager.Services;
using Xunit;

namespace Umbraco.RedirectManager.Tests.Services;

public class RedirectCultureResolverTests
{
    private static IDomain CreateDomain(string domainName, string? languageIsoCode)
    {
        var domain = Substitute.For<IDomain>();
        domain.DomainName.Returns(domainName);
        domain.LanguageIsoCode.Returns(languageIsoCode);
        return domain;
    }

    [Fact]
    public async Task ResolveCulture_RegisteredDomain_ReturnsItsCultureLowercased()
    {
        var domainService = Substitute.For<IDomainService>();
        var registeredDomains = new[] { CreateDomain("tr.example.com", "tr-TR") };
        domainService.GetAllAsync(false).Returns(Task.FromResult<IEnumerable<IDomain>>(registeredDomains));
        var resolver = new RedirectCultureResolver(domainService, new MemoryCache(new MemoryCacheOptions()));

        Assert.Equal("tr-tr", await resolver.ResolveCultureAsync("tr.example.com"));
    }

    [Fact]
    public async Task ResolveCulture_UnregisteredDomain_ReturnsNull()
    {
        var domainService = Substitute.For<IDomainService>();
        var registeredDomains = new[] { CreateDomain("tr.example.com", "tr-TR") };
        domainService.GetAllAsync(false).Returns(Task.FromResult<IEnumerable<IDomain>>(registeredDomains));
        var resolver = new RedirectCultureResolver(domainService, new MemoryCache(new MemoryCacheOptions()));

        Assert.Null(await resolver.ResolveCultureAsync("other.example.com"));
    }

    [Fact]
    public async Task ResolveCulture_NullDomain_ReturnsNull()
    {
        var domainService = Substitute.For<IDomainService>();
        var registeredDomains = new[] { CreateDomain("tr.example.com", "tr-TR") };
        domainService.GetAllAsync(false).Returns(Task.FromResult<IEnumerable<IDomain>>(registeredDomains));
        var resolver = new RedirectCultureResolver(domainService, new MemoryCache(new MemoryCacheOptions()));

        Assert.Null(await resolver.ResolveCultureAsync(null));
    }

    [Fact]
    public async Task ResolveCulture_QueriesDomainServiceExcludingWildcards()
    {
        var domainService = Substitute.For<IDomainService>();
        var registeredDomains = new[] { CreateDomain("tr.example.com", "tr-TR") };
        domainService.GetAllAsync(false).Returns(Task.FromResult<IEnumerable<IDomain>>(registeredDomains));
        var resolver = new RedirectCultureResolver(domainService, new MemoryCache(new MemoryCacheOptions()));

        await resolver.ResolveCultureAsync("tr.example.com");

        await domainService.Received(1).GetAllAsync(false);
    }
}
