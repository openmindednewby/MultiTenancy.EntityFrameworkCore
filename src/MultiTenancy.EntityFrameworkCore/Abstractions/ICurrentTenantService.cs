namespace MultiTenancy.Abstractions;

/// <summary>
/// Service for accessing the current tenant context from the HTTP request.
/// This is typically used to filter data by tenant ID in multi-tenant applications.
/// </summary>
public interface ICurrentTenantService
{
  /// <summary>
  /// Gets the current tenant identifier from the user's claims.
  /// Returns null for super users to bypass tenant filtering.
  /// </summary>
  Guid? TenantId { get; }

  /// <summary>
  /// Gets the current user identifier from the user's claims.
  /// Returns null for super users or when no user is authenticated.
  /// </summary>
  Guid? UserId { get; }

  /// <summary>
  /// Indicates whether the current user is a super user with elevated privileges.
  /// Super users can see data across all tenants.
  /// </summary>
  bool IsSuperUser { get; }
}
