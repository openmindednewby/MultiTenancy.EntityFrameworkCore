namespace MultiTenancy.Models;

/// <summary>
/// Represents the status of a tenant in the system.
/// </summary>
public enum TenantStatus
{
  /// <summary>
  /// The tenant is disabled and cannot access the system.
  /// </summary>
  Disabled = 0,

  /// <summary>
  /// The tenant is enabled and can access the system.
  /// </summary>
  Enabled = 1
}
