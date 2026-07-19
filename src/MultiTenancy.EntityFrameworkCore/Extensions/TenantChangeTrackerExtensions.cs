using DomainCore.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using MultiTenancy.Abstractions;

namespace MultiTenancy.EntityFrameworkCore;

/// <summary>
/// Write-side tenancy guard: stamps the ambient tenant onto new rows that do not carry one.
/// </summary>
public static class TenantChangeTrackerExtensions
{
  /// <summary>
  /// Stamps TenantId/UserId onto NEW tenant-scoped rows that do not carry one yet, from the ambient
  /// <see cref="ICurrentTenantService"/>. Call from <c>SaveChangesAsync</c>, BEFORE
  /// <c>base.SaveChangesAsync</c>.
  /// </summary>
  /// <param name="changeTracker">The context's change tracker.</param>
  /// <param name="currentTenantService">The ambient tenant service.</param>
  /// <remarks>
  /// <para>
  /// This exists so CHILD rows created inside an aggregate (a ProductVariant added by
  /// Product.AddVariant, an OrderItem added by Order.AddItem) get stamped automatically. The
  /// alternative — every handler looping over every child calling SetTenant — is boilerplate that
  /// eventually gets forgotten, and a row saved with <c>TenantId = Guid.Empty</c> is invisible to the
  /// tenant that owns it while still being returned to a filter-pass-through caller. Stamping once,
  /// here, is the only place it cannot be forgotten.
  /// </para>
  /// <para>
  /// <b>Purely additive.</b> It only fills <c>Guid.Empty</c> and never overwrites a TenantId that is
  /// already set, so adopting it cannot change behaviour that was already correct. It is also a no-op
  /// when there is no ambient tenant — it can never invent a tenant for an anonymous path.
  /// </para>
  /// <para>
  /// Note the asymmetry with the query filter: capturing
  /// <paramref name="currentTenantService"/> as a parameter is safe HERE because this runs at
  /// SaveChanges time, not at model-creation time — nothing is cached. The query filter cannot do the
  /// same; see <see cref="ITenantScopedDbContext"/>.
  /// </para>
  /// </remarks>
  public static void StampTenancy(
      this ChangeTracker changeTracker,
      ICurrentTenantService currentTenantService)
  {
    ArgumentNullException.ThrowIfNull(changeTracker);
    ArgumentNullException.ThrowIfNull(currentTenantService);

    if (currentTenantService.TenantId is not Guid tenantId || tenantId == Guid.Empty)
    {
      return;
    }

    Guid? currentUserId = currentTenantService.UserId;

    var newRows = changeTracker.Entries<BaseTenantEntity>()
        .Where(e => e.State == EntityState.Added)
        .Select(e => e.Entity);

    foreach (var row in newRows)
    {
      if (row.TenantId == Guid.Empty)
      {
        row.SetTenant(tenantId);
      }

      if (row.UserId == Guid.Empty && currentUserId is Guid userId && userId != Guid.Empty)
      {
        row.SetUser(userId);
      }
    }
  }
}
