using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OnlineMenu.MultiTenancy.Abstractions;
using OnlineMenu.MultiTenancy.Services;

namespace OnlineMenu.MultiTenancy.Extensions;

/// <summary>
/// Extension methods for registering multi-tenancy services.
/// </summary>
public static class ServiceCollectionExtensions
{
  /// <summary>
  /// Adds multi-tenancy services to the dependency injection container.
  /// </summary>
  /// <param name="services">The service collection.</param>
  /// <returns>The service collection for chaining.</returns>
  public static IServiceCollection AddMultiTenancy(this IServiceCollection services)
  {
    services.TryAddSingleton<IHttpContextAccessor, HttpContextAccessor>();
    services.AddScoped<ICurrentTenantService, CurrentTenantService>();

    return services;
  }
}
