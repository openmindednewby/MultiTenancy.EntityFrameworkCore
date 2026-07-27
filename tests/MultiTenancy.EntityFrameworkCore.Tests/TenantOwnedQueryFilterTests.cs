using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MultiTenancy.Abstractions;
using Shouldly;

namespace MultiTenancy.EntityFrameworkCore.Tests;

/// <summary>
/// Read-side isolation for the <see cref="ITenantOwned"/> path — the interface-keyed twin of
/// <see cref="TenantQueryFilterTests"/>. Same security property, same two polarities, proved on an
/// entity that carries a raw Guid <c>TenantId</c> instead of inheriting <c>BaseTenantEntity</c>.
/// </summary>
public sealed class TenantOwnedQueryFilterTests : IDisposable
{
  private static readonly Guid TenantA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
  private static readonly Guid TenantB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

  private readonly SqliteConnection _connection;

  public TenantOwnedQueryFilterTests()
  {
    _connection = new SqliteConnection("Filename=:memory:");
    _connection.Open();
    SeedBothTenants();
  }

  public void Dispose() => _connection.Dispose();

  [Theory]
  [InlineData(TenantFilterMode.FailOpen)]
  [InlineData(TenantFilterMode.FailClosed)]
  public async Task Query_WhenAmbientTenantIsSet_ReturnsOnlyThatTenantsRows(TenantFilterMode mode)
  {
    var tenant = new FakeCurrentTenantService { TenantId = TenantA };
    await using var context = CreateContext(mode, tenant);

    var gadgets = await context.Gadgets.AsNoTracking().ToListAsync();

    gadgets.ShouldNotBeEmpty();
    gadgets.ShouldAllBe(g => g.TenantId == TenantA);
    gadgets.Select(g => g.Name).ShouldBe(["a-one", "a-two"], ignoreOrder: true);
  }

  [Theory]
  [InlineData(TenantFilterMode.FailOpen)]
  [InlineData(TenantFilterMode.FailClosed)]
  public async Task Query_WhenAmbientTenantIsSet_NeverReturnsAnotherTenantsRows(TenantFilterMode mode)
  {
    var tenant = new FakeCurrentTenantService { TenantId = TenantB };
    await using var context = CreateContext(mode, tenant);

    var gadgets = await context.Gadgets.AsNoTracking().ToListAsync();

    gadgets.ShouldNotBeEmpty();
    gadgets.ShouldNotContain(g => g.TenantId == TenantA);
    gadgets.Select(g => g.Name).ShouldBe(["b-one"]);
  }

  /// <summary>Fail-open with no ambient tenant is a PASS-THROUGH — every tenant's rows. By design.</summary>
  [Fact]
  public async Task Query_FailOpen_WhenNoAmbientTenant_ReturnsEveryTenantsRows_ByDesign()
  {
    var tenant = new FakeCurrentTenantService { TenantId = null };
    await using var context = CreateContext(TenantFilterMode.FailOpen, tenant);

    var gadgets = await context.Gadgets.AsNoTracking().ToListAsync();

    gadgets.Count.ShouldBe(3);
    gadgets.Select(g => g.TenantId).Distinct().ShouldBe([TenantA, TenantB], ignoreOrder: true);
  }

  /// <summary>Fail-closed with no ambient tenant reads NOTHING — the polarity AMLService wants.</summary>
  [Fact]
  public async Task Query_FailClosed_WhenNoAmbientTenant_ReturnsNothing_ByDesign()
  {
    var tenant = new FakeCurrentTenantService { TenantId = null };
    await using var context = CreateContext(TenantFilterMode.FailClosed, tenant);

    var gadgets = await context.Gadgets.AsNoTracking().ToListAsync();

    gadgets.ShouldBeEmpty();
  }

  [Theory]
  [InlineData(TenantFilterMode.FailOpen)]
  [InlineData(TenantFilterMode.FailClosed)]
  public async Task IgnoreQueryFilters_PairedWithExplicitPredicate_StillScopesToOneTenant(
      TenantFilterMode mode)
  {
    var tenant = new FakeCurrentTenantService { TenantId = null };
    await using var context = CreateContext(mode, tenant);

    var gadgets = await context.Gadgets
        .IgnoreQueryFilters()
        .Where(g => g.TenantId == TenantA)
        .AsNoTracking()
        .ToListAsync();

    gadgets.Count.ShouldBe(2);
    gadgets.ShouldAllBe(g => g.TenantId == TenantA);
  }

  [Theory]
  [InlineData(TenantFilterMode.FailOpen)]
  [InlineData(TenantFilterMode.FailClosed)]
  public async Task IgnoreQueryFilters_WithoutPredicate_ReadsAcrossEveryTenant(TenantFilterMode mode)
  {
    var tenant = new FakeCurrentTenantService { TenantId = TenantA };
    await using var context = CreateContext(mode, tenant);

    var gadgets = await context.Gadgets.IgnoreQueryFilters().AsNoTracking().ToListAsync();

    gadgets.Count.ShouldBe(3);
  }

  /// <summary>
  /// 🔴 The production shape: two contexts, two DIFFERENT tenant-service instances, ONE cached model —
  /// each context must see only its own tenant. Detects a filter that baked request #1's service into
  /// the model, keyed on the interface path.
  /// </summary>
  [Fact]
  public async Task Filter_IsEvaluatedPerContext_NotBakedIntoTheCachedModel()
  {
    var options = BuildOptions();

    var firstRequestTenant = new FakeCurrentTenantService { TenantId = TenantA };
    await using (var first = new FailOpenOwnedDbContext(options, firstRequestTenant))
    {
      var rows = await first.Gadgets.AsNoTracking().ToListAsync();
      rows.ShouldAllBe(g => g.TenantId == TenantA);
    }

    var secondRequestTenant = new FakeCurrentTenantService { TenantId = TenantB };
    await using (var second = new FailOpenOwnedDbContext(options, secondRequestTenant))
    {
      var rows = await second.Gadgets.AsNoTracking().ToListAsync();

      rows.ShouldNotBeEmpty();
      rows.ShouldAllBe(g => g.TenantId == TenantB);
      rows.ShouldNotContain(g => g.TenantId == TenantA);
    }
  }

  /// <summary>Non-tenant entities in the owned model must be untouched, even fail-closed.</summary>
  [Theory]
  [InlineData(TenantFilterMode.FailOpen)]
  [InlineData(TenantFilterMode.FailClosed)]
  public async Task Query_NonTenantEntity_IsNeverFiltered(TenantFilterMode mode)
  {
    var tenant = new FakeCurrentTenantService { TenantId = null };
    await using var context = CreateContext(mode, tenant);

    var settings = await context.OwnedGlobalSettings.AsNoTracking().ToListAsync();

    settings.Count.ShouldBe(1);
  }

  private DbContextOptions BuildOptions() =>
      new DbContextOptionsBuilder()
          .UseSqlite(_connection)
          .Options;

  private OwnedTestDbContextBase CreateContext(TenantFilterMode mode, FakeCurrentTenantService tenant) =>
      mode == TenantFilterMode.FailClosed
          ? new FailClosedOwnedDbContext(BuildOptions(), tenant)
          : new FailOpenOwnedDbContext(BuildOptions(), tenant);

  private void SeedBothTenants()
  {
    // Seeded with NO ambient tenant and explicit TenantId, so seeding never depends on the stamp.
    var seeder = new FakeCurrentTenantService { TenantId = null };
    using var context = new FailOpenOwnedDbContext(BuildOptions(), seeder);
    context.Database.EnsureCreated();

    context.Gadgets.AddRange(
        new Gadget { Id = Guid.NewGuid(), TenantId = TenantA, Name = "a-one" },
        new Gadget { Id = Guid.NewGuid(), TenantId = TenantA, Name = "a-two" },
        new Gadget { Id = Guid.NewGuid(), TenantId = TenantB, Name = "b-one" });
    context.OwnedGlobalSettings.Add(new OwnedGlobalSetting { Id = Guid.NewGuid(), Key = "global" });
    context.SaveChanges();
  }
}
