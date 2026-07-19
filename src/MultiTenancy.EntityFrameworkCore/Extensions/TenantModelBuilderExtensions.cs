using System.Reflection;
using DomainCore.Entities;
using Microsoft.EntityFrameworkCore;
using MultiTenancy.Abstractions;

namespace MultiTenancy.EntityFrameworkCore;

/// <summary>
/// Applies the shared per-tenant global query filter to every <see cref="BaseTenantEntity"/> in a
/// model. Replaces the hand-rolled <c>ApplyTenantFilter</c>/<c>SetTenantFilter</c> pair that was
/// duplicated across six DbContexts.
/// </summary>
public static class TenantModelBuilderExtensions
{
  /// <summary>
  /// Adds a tenant query filter to every entity type in the model deriving from
  /// <see cref="BaseTenantEntity"/>.
  /// </summary>
  /// <typeparam name="TContext">The DbContext type; must expose the ambient tenant service.</typeparam>
  /// <param name="modelBuilder">The model builder, from <c>OnModelCreating</c>.</param>
  /// <param name="context">
  /// The context itself — pass <c>this</c>. The filter is rooted at this instance so EF Core swaps in
  /// the executing context per query; see <see cref="ITenantScopedDbContext"/> for why that matters.
  /// </param>
  /// <param name="mode">
  /// What happens when there is no ambient tenant. Required — see <see cref="TenantFilterMode"/>.
  /// </param>
  /// <returns>The same <paramref name="modelBuilder"/>, for chaining.</returns>
  /// <remarks>
  /// Call AFTER the entity configurations, exactly where the hand-rolled <c>ApplyTenantFilter(modelBuilder)</c>
  /// was called.
  /// </remarks>
  public static ModelBuilder ApplyTenantFilters<TContext>(
      this ModelBuilder modelBuilder,
      TContext context,
      TenantFilterMode mode)
      where TContext : DbContext, ITenantScopedDbContext
  {
    ArgumentNullException.ThrowIfNull(modelBuilder);
    ArgumentNullException.ThrowIfNull(context);

    // Entity-type selection is lifted UNCHANGED from the six hand-rolled copies: every model entity
    // assignable to BaseTenantEntity, with no IsAbstract screen. Adding one here would be a
    // behaviour change disguised as a tidy-up, so it is deliberately absent.
    var tenantEntityTypes = modelBuilder.Model.GetEntityTypes()
        .Where(et => typeof(BaseTenantEntity).IsAssignableFrom(et.ClrType))
        .Select(et => et.ClrType);

    foreach (var clrType in tenantEntityTypes)
    {
      // Using reflection to dynamically apply query filters is intentional for EF Core.
      // S3011: this accessibility bypass is safe - we are calling our own private method.
#pragma warning disable S3011
      var method = typeof(TenantModelBuilderExtensions)
          .GetMethod(nameof(SetTenantFilter), BindingFlags.NonPublic | BindingFlags.Static)!
          .MakeGenericMethod(clrType, typeof(TContext));
#pragma warning restore S3011

      method.Invoke(null, [modelBuilder, context, mode]);
    }

    return modelBuilder;
  }

  /// <summary>
  /// Applies <c>HasQueryFilter</c> for one entity type, in the requested polarity.
  /// </summary>
  /// <remarks>
  /// 🔴 Both lambdas reference <paramref name="context"/> (a DbContext) and read
  /// <c>.CurrentTenantService.TenantId</c> THROUGH it. That indirection is the whole point: EF Core
  /// replaces the context reference with the executing context on every query, so the tenant is read
  /// at query time. Hoisting <c>context.CurrentTenantService</c> — let alone its <c>.TenantId</c> —
  /// into a local before the lambda would capture it as a model-creation-time constant and silently
  /// pin every future query to whichever request built the model.
  /// </remarks>
  private static void SetTenantFilter<TEntity, TContext>(
      ModelBuilder modelBuilder,
      TContext context,
      TenantFilterMode mode)
      where TEntity : BaseTenantEntity
      where TContext : DbContext, ITenantScopedDbContext
  {
    if (mode == TenantFilterMode.FailClosed)
    {
      // `!= null &&`, never `== null ||`. One character between isolation and a leak.
      modelBuilder.Entity<TEntity>().HasQueryFilter(e =>
          context.CurrentTenantService.TenantId != null &&
          e.TenantId == context.CurrentTenantService.TenantId);

      return;
    }

    modelBuilder.Entity<TEntity>().HasQueryFilter(e =>
        context.CurrentTenantService.TenantId == null || // SuperUser or no tenant context - show all
        e.TenantId == context.CurrentTenantService.TenantId);
  }
}
