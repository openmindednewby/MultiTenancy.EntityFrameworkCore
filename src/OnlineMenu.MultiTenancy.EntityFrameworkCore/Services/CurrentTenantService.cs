using Microsoft.AspNetCore.Http;
using OnlineMenu.MultiTenancy.Abstractions;
using Security.Claims.Claims;

namespace OnlineMenu.MultiTenancy.Services;

/// <summary>
/// Implementation of <see cref="ICurrentTenantService"/> that extracts tenant information
/// from the HTTP context and user claims.
/// </summary>
public class CurrentTenantService : ICurrentTenantService
{
  private readonly IHttpContextAccessor _httpContextAccessor;

  /// <summary>
  /// Initializes a new instance of the <see cref="CurrentTenantService"/> class.
  /// </summary>
  /// <param name="httpContextAccessor">The HTTP context accessor to get current user information.</param>
  public CurrentTenantService(IHttpContextAccessor httpContextAccessor)
  {
    _httpContextAccessor = httpContextAccessor;
  }

  /// <inheritdoc />
  public Guid? TenantId
  {
    get
    {
      var user = _httpContextAccessor.HttpContext?.User;
      if (user == null) return null;
      if (IsSuperUser) return null; // Skip filtering for superUser

      return user.GetTenantId();
    }
  }

  /// <inheritdoc />
  public Guid? UserId
  {
    get
    {
      var user = _httpContextAccessor.HttpContext?.User;
      if (user == null) return null;

      if (IsSuperUser) return null; // Skip filtering for superUser

      var subjectId = user.GetSubjectId();
      if (Guid.TryParse(subjectId, out var userId))
        return userId;

      return null;
    }
  }

  /// <inheritdoc />
  public bool IsSuperUser =>
      _httpContextAccessor.HttpContext?.User?.IsSuperUser() ?? false;
}
