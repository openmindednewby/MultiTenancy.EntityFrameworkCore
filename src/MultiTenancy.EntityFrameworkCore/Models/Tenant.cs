using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using DomainCore.Interfaces;

namespace MultiTenancy.Models;

/// <summary>
/// Represents a tenant in a multi-tenant application.
/// Each tenant has isolated data and configuration.
/// </summary>
public class Tenant : IAggregateRoot
{
  /// <summary>
  /// Initializes a new instance of the <see cref="Tenant"/> class.
  /// </summary>
  /// <param name="name">The tenant name.</param>
  /// <param name="tenantStatus">The initial status of the tenant.</param>
  /// <param name="logoUrl">Optional logo URL for branding.</param>
  /// <param name="primaryColor">Optional primary color for branding.</param>
  public Tenant(string name, TenantStatus tenantStatus, string? logoUrl = null, string? primaryColor = null)
  {
    if (string.IsNullOrWhiteSpace(name))
      throw new ArgumentException("Name cannot be null or empty.", nameof(name));

    Name = name;
    Slug = name.ToLower().Replace(" ", "-");
    TenantStatus = tenantStatus;
    LogoUrl = logoUrl;
    PrimaryColor = primaryColor;
    CreatedDate = DateTime.UtcNow;
    LastUpdatedDate = DateTime.UtcNow;
  }

  // EF Core requires a parameterless constructor
  private Tenant() { Name = string.Empty; Slug = string.Empty; }

  /// <summary>
  /// Gets the tenant identifier (primary key).
  /// </summary>
  [Key]
  [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
  public Guid TenantId { get; private set; }

  /// <summary>
  /// Gets the tenant name.
  /// </summary>
  [Required]
  [MaxLength(200)]
  public string Name { get; private set; }

  /// <summary>
  /// Gets the URL-friendly slug for the tenant.
  /// </summary>
  [Required]
  [MaxLength(200)]
  public string Slug { get; private set; }

  /// <summary>
  /// Gets the tenant status (Enabled/Disabled).
  /// </summary>
  public TenantStatus TenantStatus { get; private set; }

  /// <summary>
  /// Gets the logo URL for branding purposes.
  /// </summary>
  [MaxLength(500)]
  public string? LogoUrl { get; private set; }

  /// <summary>
  /// Gets the primary brand color.
  /// </summary>
  [MaxLength(50)]
  public string? PrimaryColor { get; private set; }

  /// <summary>
  /// Gets the creation timestamp.
  /// </summary>
  [Required]
  public DateTime CreatedDate { get; private set; }

  /// <summary>
  /// Gets the last update timestamp.
  /// </summary>
  [Required]
  public DateTime LastUpdatedDate { get; private set; }

  // Authentication configuration

  /// <summary>
  /// Gets the primary authentication method (0=Password, 1=PhoneOtp, 2=EmailOtp, 3=Social).
  /// </summary>
  public int PrimaryAuthMethod { get; private set; } = 0;

  /// <summary>
  /// Gets whether phone-based authentication is allowed.
  /// </summary>
  public bool AllowPhoneAuth { get; private set; } = false;

  /// <summary>
  /// Gets whether email-based authentication is allowed.
  /// </summary>
  public bool AllowEmailAuth { get; private set; } = false;

  /// <summary>
  /// Gets the OTP code length (4-10 digits).
  /// </summary>
  public int OtpCodeLength { get; private set; } = 6;

  /// <summary>
  /// Gets the OTP expiry time in minutes.
  /// </summary>
  public int OtpExpiryMinutes { get; private set; } = 5;

  /// <summary>
  /// Gets the SMS provider name (e.g., "Twilio").
  /// </summary>
  [MaxLength(100)]
  public string? SmsProvider { get; private set; }

  /// <summary>
  /// Gets whether SMS verification is required (feature flag).
  /// </summary>
  public bool RequireSmsVerification { get; private set; } = true;

  /// <summary>
  /// Updates the tenant's basic information.
  /// </summary>
  public void Update(string name, TenantStatus tenantStatus, string? logoUrl = null, string? primaryColor = null)
  {
    if (string.IsNullOrWhiteSpace(name))
      throw new ArgumentException("Name cannot be null or empty.", nameof(name));

    Name = name;
    TenantStatus = tenantStatus;
    LogoUrl = logoUrl;
    PrimaryColor = primaryColor;
    LastUpdatedDate = DateTime.UtcNow;
  }

  /// <summary>
  /// Updates the tenant's authentication configuration.
  /// </summary>
  public void UpdateAuthConfiguration(
    int primaryAuthMethod,
    bool allowPhoneAuth,
    bool allowEmailAuth,
    int otpCodeLength = 6,
    int otpExpiryMinutes = 5,
    string? smsProvider = null,
    bool requireSmsVerification = true)
  {
    if (primaryAuthMethod < 0 || primaryAuthMethod > 3)
      throw new ArgumentOutOfRangeException(nameof(primaryAuthMethod), "Must be between 0 and 3.");

    if (otpCodeLength < 4 || otpCodeLength > 10)
      throw new ArgumentOutOfRangeException(nameof(otpCodeLength), "Must be between 4 and 10.");

    if (otpExpiryMinutes <= 0)
      throw new ArgumentOutOfRangeException(nameof(otpExpiryMinutes), "Must be greater than 0.");

    PrimaryAuthMethod = primaryAuthMethod;
    AllowPhoneAuth = allowPhoneAuth;
    AllowEmailAuth = allowEmailAuth;
    OtpCodeLength = otpCodeLength;
    OtpExpiryMinutes = otpExpiryMinutes;
    SmsProvider = smsProvider;
    RequireSmsVerification = requireSmsVerification;
    LastUpdatedDate = DateTime.UtcNow;
  }
}
