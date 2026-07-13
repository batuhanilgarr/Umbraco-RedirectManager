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
    public void ResolveCulture_RegisteredDomain_ReturnsItsCultureLowercased()
    {
        var domainService = Substitute.For<IDomainService>();
        var registeredDomains = new[] { CreateDomain("tr.example.com", "tr-TR") };
        domainService.GetAll(false).Returns(registeredDomains);
        var resolver = new RedirectCultureResolver(domainService, new MemoryCache(new MemoryCacheOptions()));

        Assert.Equal("tr-tr", resolver.ResolveCulture("tr.example.com"));
    }

    [Fact]
    public void ResolveCulture_UnregisteredDomain_ReturnsNull()
    {
        var domainService = Substitute.For<IDomainService>();
        var registeredDomains = new[] { CreateDomain("tr.example.com", "tr-TR") };
        domainService.GetAll(false).Returns(registeredDomains);
        var resolver = new RedirectCultureResolver(domainService, new MemoryCache(new MemoryCacheOptions()));

        Assert.Null(resolver.ResolveCulture("other.example.com"));
    }

    [Fact]
    public void ResolveCulture_NullDomain_ReturnsNull()
    {
        var domainService = Substitute.For<IDomainService>();
        var registeredDomains = new[] { CreateDomain("tr.example.com", "tr-TR") };
        domainService.GetAll(false).Returns(registeredDomains);
        var resolver = new RedirectCultureResolver(domainService, new MemoryCache(new MemoryCacheOptions()));

        Assert.Null(resolver.ResolveCulture(null));
    }

    [Fact]
    public void ResolveCulture_QueriesDomainServiceExcludingWildcards()
    {
        var domainService = Substitute.For<IDomainService>();
        var registeredDomains = new[] { CreateDomain("tr.example.com", "tr-TR") };
        domainService.GetAll(false).Returns(registeredDomains);
        var resolver = new RedirectCultureResolver(domainService, new MemoryCache(new MemoryCacheOptions()));

        resolver.ResolveCulture("tr.example.com");

        domainService.Received(1).GetAll(false);
    }
}
