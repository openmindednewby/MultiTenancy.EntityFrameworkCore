namespace MultiTenancy.Abstractions;

/// <summary>
/// Implemented by a <c>DbContext</c> that participates in ambient tenant scoping, exposing the
/// tenant service so the shared query filter can be rooted at the context instance.
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>This interface exists for a correctness reason, not for tidiness.</b> EF Core builds the
/// model ONCE and caches it. Anything a query-filter lambda closes over is therefore captured at
/// model-creation time and frozen for the lifetime of the process.
/// </para>
/// <para>
/// <c>CurrentTenantService</c> is registered <b>Scoped</b> — a fresh instance per HTTP request. So a
/// helper of the shape <c>ApplyTenantFilters(modelBuilder, currentTenantService)</c> would bake the
/// FIRST request's service instance into the cached model, and every later request would read that
/// request's tenant. Not a subtle skew: a permanent cross-tenant leak, invisible to any test that
/// uses a single context.
/// </para>
/// <para>
/// EF Core special-cases exactly one thing here: a reference to <b>the DbContext instance</b> inside
/// a query filter is replaced, per query, with the context that is actually executing. So the filter
/// must be rooted at the context (<c>ctx.CurrentTenantService.TenantId</c>) and never at a captured
/// service. This interface is how the shared helper gets that root in a type-safe way — the same
/// guarantee the hand-rolled versions got from referencing their private <c>_currentTenantService</c>
/// field.
/// </para>
/// <para>
/// Pinned by <c>TenantQueryFilterTests.Filter_IsEvaluatedPerContext_NotBakedIntoCachedModel</c>.
/// </para>
/// </remarks>
public interface ITenantScopedDbContext
{
  /// <summary>
  /// Gets the ambient tenant service. Typically <c>=> _currentTenantService;</c> over the field the
  /// context already holds.
  /// </summary>
  ICurrentTenantService CurrentTenantService { get; }
}
