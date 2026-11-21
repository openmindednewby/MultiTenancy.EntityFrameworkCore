namespace MultiTenancy.Services;

public sealed class SuperUserRealmOptions
{
    public const string PlatformRealmKey = "Authentication:PlatformRealm";

    public string PlatformRealm { get; set; } = string.Empty;
}
