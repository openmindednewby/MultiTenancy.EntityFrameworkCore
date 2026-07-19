using DomainCore.Entities;
using DomainCore.Events;
using Microsoft.EntityFrameworkCore;
using MultiTenancy.Abstractions;

namespace MultiTenancy.EntityFrameworkCore.Tests;

/// <summary>
/// A mutable stand-in for the scoped <c>CurrentTenantService</c>. Mutability is the point: the
/// filter must read <see cref="TenantId"/> at QUERY time, so a test can move the ambient tenant
/// between two queries and watch the results follow.
/// </summary>
public sealed class FakeCurrentTenantService : ICurrentTenantService
{
  public Guid? TenantId { get; set; }

  public Guid? UserId { get; set; }

  public bool IsSuperUser { get; set; }
}

/// <summary>A tenant-scoped row.</summary>
public sealed class Widget : BaseTenantEntity
{
  public string Name { get; set; } = string.Empty;
}

/// <summary>A NON-tenant row, to prove the filter touches only BaseTenantEntity types.</summary>
public sealed class GlobalSetting : BaseEntity
{
  public string Key { get; set; } = string.Empty;
}

/// <summary>
/// The fail-open context — the shape Kefi, Questioner, Content, Agora and Payment use.
/// </summary>
public sealed class FailOpenTestDbContext : TestDbContextBase
{
  public FailOpenTestDbContext(DbContextOptions options, ICurrentTenantService tenantService)
      : base(options, tenantService)
  {
  }

  protected override TenantFilterMode Mode => TenantFilterMode.FailOpen;
}

/// <summary>
/// The fail-closed context — the shape Zygos uses.
/// </summary>
public sealed class FailClosedTestDbContext : TestDbContextBase
{
  public FailClosedTestDbContext(DbContextOptions options, ICurrentTenantService tenantService)
      : base(options, tenantService)
  {
  }

  protected override TenantFilterMode Mode => TenantFilterMode.FailClosed;
}

/// <summary>
/// Mirrors the real contexts: holds the tenant service in a field, exposes it via
/// <see cref="ITenantScopedDbContext"/>, calls the shared helpers from the same two places the
/// hand-rolled versions did.
/// </summary>
public abstract class TestDbContextBase : DbContext, ITenantScopedDbContext
{
  private readonly ICurrentTenantService _currentTenantService;

  protected TestDbContextBase(DbContextOptions options, ICurrentTenantService tenantService)
      : base(options)
  {
    _currentTenantService = tenantService;
  }

  public ICurrentTenantService CurrentTenantService => _currentTenantService;

  public DbSet<Widget> Widgets => Set<Widget>();

  public DbSet<GlobalSetting> GlobalSettings => Set<GlobalSetting>();

  protected abstract TenantFilterMode Mode { get; }

  protected override void OnModelCreating(ModelBuilder modelBuilder)
  {
    base.OnModelCreating(modelBuilder);

    // Same as every real context: BaseEntity exposes a DomainEvents navigation, so the event base
    // would otherwise be discovered as a keyless entity type.
    modelBuilder.Ignore<DomainEventBase>();

    modelBuilder.Entity<Widget>().HasKey(e => e.Id);
    modelBuilder.Entity<GlobalSetting>().HasKey(e => e.Id);

    // ExternalId is declared DatabaseGenerated(Identity); SQLite cannot generate a Guid, so the
    // test model generates it client-side. Not related to tenancy.
    modelBuilder.Entity<Widget>().Property(e => e.ExternalId).ValueGeneratedNever();
    modelBuilder.Entity<GlobalSetting>().Property(e => e.ExternalId).ValueGeneratedNever();

    modelBuilder.ApplyTenantFilters(this, Mode);
  }

  public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
  {
    ChangeTracker.StampTenancy(_currentTenantService);

    return await base.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
  }
}
