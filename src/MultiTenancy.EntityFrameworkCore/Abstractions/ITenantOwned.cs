namespace MultiTenancy.Abstractions;

/// <summary>
/// The interface-keyed alternative to <c>DomainCore.Entities.BaseTenantEntity</c>: any row that
/// carries a <see cref="TenantId"/> and wants the shared per-tenant query filter + stamp, WITHOUT
/// inheriting <c>BaseTenantEntity</c>.
/// </summary>
/// <remarks>
/// <para>
/// The original tenancy backstop keys on <c>BaseTenantEntity</c> (a base class with a Guid PK,
/// <c>ExternalId</c>, domain events and a private-set <c>TenantId</c>). That is the right shape for the
/// services that were built on DomainCore, but it cannot be adopted cheaply by a context whose entities
/// use a raw Guid PK and a plain <c>TenantId</c> column — e.g. PROOViD AMLService. Forcing those
/// entities under <c>BaseTenantEntity</c> would drag in <c>ExternalId</c>, the events navigation and a
/// PK reshape.
/// </para>
/// <para>
/// This interface exists so such a context can opt into the exact same fail-open / fail-closed filter
/// and the same additive stamp by declaring nothing more than "I own a tenant". The
/// <c>ApplyTenantOwnedFilters</c> / <c>StampTenantOwned</c> overloads key on this interface and behave
/// identically to their <c>BaseTenantEntity</c> counterparts.
/// </para>
/// <para>
/// Only a getter is required. The read side (query filter) needs only to read the value; the write side
/// (stamp) writes through the EF change-tracker property entry, so the concrete type is free to expose a
/// public setter, a private setter or a backing field — the interface does not dictate that.
/// </para>
/// </remarks>
public interface ITenantOwned
{
  /// <summary>
  /// The tenant that owns this row. <see cref="System.Guid.Empty"/> means "not yet stamped" — the
  /// stamp fills it from the ambient tenant, exactly as an unset <c>BaseTenantEntity.TenantId</c> is
  /// filled.
  /// </summary>
  Guid TenantId { get; }
}
