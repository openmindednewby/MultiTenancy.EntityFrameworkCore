using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using MultiTenancy.Abstractions;
using Security.Claims.Claims;

namespace MultiTenancy.Services;

/// <summary>Reads the current tenant, user and super user from the HTTP request claims.</summary>
public class CurrentTenantService : ICurrentTenantService
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly string _platformRealm;

    /// <summary>Initializes a new instance with no platform realm, so nobody is a super user.</summary>
    /// <param name="httpContextAccessor">The HTTP context accessor to get current user information.</param>
    public CurrentTenantService(IHttpContextAccessor httpContextAccessor)
        : this(httpContextAccessor, string.Empty)
    {
    }

    /// <summary>Initializes a new instance whose super user must come from the configured platform realm.</summary>
    /// <param name="httpContextAccessor">The HTTP context accessor to get current user information.</param>
    /// <param name="options">The platform realm, bound from <c>Authentication:PlatformRealm</c>.</param>
    public CurrentTenantService(IHttpContextAccessor httpContextAccessor, IOptions<SuperUserRealmOptions> options)
        : this(httpContextAccessor, options.Value.PlatformRealm ?? string.Empty)
    {
    }

    private CurrentTenantService(IHttpContextAccessor httpContextAccessor, string platformRealm)
    {
        _httpContextAccessor = httpContextAccessor;
        _platformRealm = platformRealm;
    }

    /// <inheritdoc />
    public Guid? TenantId
    {
        get
        {
            var user = _httpContextAccessor.HttpContext?.User;
            if (user == null) return null;
            if (IsSuperUser) return null;

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

            if (IsSuperUser) return null;

            var subjectId = user.GetSubjectId();
            if (Guid.TryParse(subjectId, out var userId))
                return userId;

            return null;
        }
    }

    /// <inheritdoc />
    public bool IsSuperUser =>
        _httpContextAccessor.HttpContext?.User?.IsSuperUser(_platformRealm) ?? false;
}
