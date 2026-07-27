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

  /// <summary>
  /// The <see cref="ITenantOwned"/> counterpart of
  /// <see cref="StampTenancy(ChangeTracker, ICurrentTenantService)"/>. Stamps the ambient tenant onto
  /// NEW <see cref="ITenantOwned"/> rows whose <see cref="ITenantOwned.TenantId"/> is still
  /// <see cref="Guid.Empty"/>. Call from <c>SaveChangesAsync</c>, BEFORE <c>base.SaveChangesAsync</c>.
  /// </summary>
  /// <param name="changeTracker">The context's change tracker.</param>
  /// <param name="currentTenantService">The ambient tenant service.</param>
  /// <remarks>
  /// <para>
  /// Same guarantees as the <c>BaseTenantEntity</c> path: <b>purely additive</b> (only fills
  /// <see cref="Guid.Empty"/>, never overwrites a TenantId already set), a <b>no-op</b> when there is no
  /// ambient tenant, and it screens an ambient <see cref="Guid.Empty"/> so it can never stamp the exact
  /// value it exists to prevent.
  /// </para>
  /// <para>
  /// It writes through the change-tracker's <c>PropertyEntry.CurrentValue</c>
  /// rather than a setter, because <see cref="ITenantOwned"/> guarantees only a getter — this lets the
  /// concrete entity keep a public setter, a private setter or a backing field. Note the asymmetry with
  /// the query filter: reading the ambient service as a parameter is safe HERE because this runs at
  /// SaveChanges time, not model-creation time.
  /// </para>
  /// <para>
  /// Unlike the <c>BaseTenantEntity</c> path this does NOT stamp a UserId: <see cref="ITenantOwned"/>
  /// deliberately models tenant ownership only.
  /// </para>
  /// </remarks>
  public static void StampTenantOwned(
      this ChangeTracker changeTracker,
      ICurrentTenantService currentTenantService)
  {
    ArgumentNullException.ThrowIfNull(changeTracker);
    ArgumentNullException.ThrowIfNull(currentTenantService);

    if (currentTenantService.TenantId is not Guid tenantId || tenantId == Guid.Empty)
    {
      return;
    }

    var newRows = changeTracker.Entries<ITenantOwned>()
        .Where(e => e.State == EntityState.Added);

    foreach (var entry in newRows)
    {
      if (entry.Entity.TenantId == Guid.Empty)
      {
        entry.Property(nameof(ITenantOwned.TenantId)).CurrentValue = tenantId;
      }
    }
  }
}
