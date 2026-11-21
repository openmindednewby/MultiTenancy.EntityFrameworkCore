using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MultiTenancy.Abstractions;
using MultiTenancy.Extensions;
using MultiTenancy.Services;
using Security.Claims.Claims;
using Shouldly;

namespace MultiTenancy.EntityFrameworkCore.Tests;

public sealed class CurrentTenantServiceTests
{
    private const string PlatformRealm = "internal";
    private const string TenantRealm = "questioner";
    private static readonly Guid TokenTenant = Guid.NewGuid();

    [Fact]
    public void IsSuperUser_WithTenantRealmSuperUserToken_ReturnsFalse()
    {
        var service = CreateService(PlatformRealm, SuperUserFrom(TenantRealm));

        var isSuperUser = service.IsSuperUser;

        isSuperUser.ShouldBeFalse();
    }

    [Fact]
    public void TenantId_WithTenantRealmSuperUserToken_ReturnsTokenTenant()
    {
        var service = CreateService(PlatformRealm, SuperUserFrom(TenantRealm));

        var tenantId = service.TenantId;

        tenantId.ShouldBe(TokenTenant);
    }

    [Fact]
    public void IsSuperUser_WithPlatformRealmSuperUserToken_ReturnsTrue()
    {
        var service = CreateService(PlatformRealm, SuperUserFrom(PlatformRealm));

        var isSuperUser = service.IsSuperUser;

        isSuperUser.ShouldBeTrue();
    }

    [Fact]
    public void TenantId_WithPlatformRealmSuperUserToken_ReturnsNull()
    {
        var service = CreateService(PlatformRealm, SuperUserFrom(PlatformRealm));

        var tenantId = service.TenantId;

        tenantId.ShouldBeNull();
    }

    [Fact]
    public void IsSuperUser_WithoutPlatformRealmConfigured_ReturnsFalse()
    {
        var service = CreateService(string.Empty, SuperUserFrom(PlatformRealm));

        var isSuperUser = service.IsSuperUser;

        isSuperUser.ShouldBeFalse();
    }

    [Fact]
    public void IsSuperUser_WhenBuiltWithoutOptions_ReturnsFalse()
    {
        var service = new CurrentTenantService(AccessorFor(SuperUserFrom(PlatformRealm)));

        var isSuperUser = service.IsSuperUser;

        isSuperUser.ShouldBeFalse();
    }

    [Fact]
    public void AddMultiTenancy_WithPlatformRealmInConfiguration_TrustsOnlyThatRealm()
    {
        var settings = new Dictionary<string, string?>(StringComparer.Ordinal) { [SuperUserRealmOptions.PlatformRealmKey] = PlatformRealm };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection().AddSingleton<IConfiguration>(configuration).AddMultiTenancy();
        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IHttpContextAccessor>().HttpContext = new DefaultHttpContext { User = SuperUserFrom(PlatformRealm) };
        using var scope = provider.CreateScope();

        var isSuperUser = scope.ServiceProvider.GetRequiredService<ICurrentTenantService>().IsSuperUser;

        isSuperUser.ShouldBeTrue();
    }

    [Fact]
    public void AddMultiTenancy_WithoutPlatformRealmInConfiguration_TrustsNoRealm()
    {
        var configuration = new ConfigurationBuilder().Build();
        var services = new ServiceCollection().AddSingleton<IConfiguration>(configuration).AddMultiTenancy();
        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IHttpContextAccessor>().HttpContext = new DefaultHttpContext { User = SuperUserFrom(PlatformRealm) };
        using var scope = provider.CreateScope();

        var isSuperUser = scope.ServiceProvider.GetRequiredService<ICurrentTenantService>().IsSuperUser;

        isSuperUser.ShouldBeFalse();
    }

    private static CurrentTenantService CreateService(string platformRealm, ClaimsPrincipal user) =>
        new(AccessorFor(user), Options.Create(new SuperUserRealmOptions { PlatformRealm = platformRealm }));

    private static HttpContextAccessor AccessorFor(ClaimsPrincipal user) =>
        new() { HttpContext = new DefaultHttpContext { User = user } };

    private static ClaimsPrincipal SuperUserFrom(string realm)
    {
        var claims = new[]
        {
            new Claim(OnlineMenuClaimTypes.Iss, $"https://auth.example.test/realms/{realm}"),
            new Claim(OnlineMenuClaimTypes.TenantId, TokenTenant.ToString()),
            new Claim(ClaimTypes.Role, OnlineMenuRoles.SuperUser),
        };
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test", ClaimTypes.Name, ClaimTypes.Role));
    }
}
