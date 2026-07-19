using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace MultiTenancy.EntityFrameworkCore.Tests;

/// <summary>
/// Write-side guard. A row saved with <c>TenantId = Guid.Empty</c> raises no error and violates no
/// constraint — it is simply invisible to the tenant that owns it, while still being returned to a
/// fail-open pass-through caller. These pin that it cannot happen when a tenant is ambient.
/// </summary>
public sealed class StampTenancyTests : IDisposable
{
  private static readonly Guid TenantA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
  private static readonly Guid TenantB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
  private static readonly Guid UserA = Guid.Parse("11111111-1111-1111-1111-111111111111");
  private static readonly Guid UserB = Guid.Parse("22222222-2222-2222-2222-222222222222");

  private readonly SqliteConnection _connection;

  public StampTenancyTests()
  {
    _connection = new SqliteConnection("Filename=:memory:");
    _connection.Open();

    using var context = CreateContext(new FakeCurrentTenantService());
    context.Database.EnsureCreated();
  }

  public void Dispose() => _connection.Dispose();

  [Fact]
  public async Task StampTenancy_WhenTenantIdIsEmpty_FillsItFromTheAmbientTenant()
  {
    var tenant = new FakeCurrentTenantService { TenantId = TenantA };
    await using var context = CreateContext(tenant);

    var widget = new Widget { Name = "unstamped" };
    widget.TenantId.ShouldBe(Guid.Empty, "precondition: the row starts untenanted");

    context.Widgets.Add(widget);
    await context.SaveChangesAsync();

    widget.TenantId.ShouldBe(TenantA);
  }

  /// <summary>
  /// The additive guarantee: adopting StampTenancy cannot change behaviour that was already correct.
  /// </summary>
  [Fact]
  public async Task StampTenancy_WhenTenantIdIsAlreadySet_DoesNotOverwriteIt()
  {
    // Ambient tenant is B; the row was explicitly built for A.
    var tenant = new FakeCurrentTenantService { TenantId = TenantB };
    await using var context = CreateContext(tenant);

    var widget = new Widget { Name = "already-stamped" };
    widget.SetTenant(TenantA);

    context.Widgets.Add(widget);
    await context.SaveChangesAsync();

    // Untouched. (BaseTenantEntity.SetTenant would THROW on a change, so an overwrite attempt would
    // surface as an InvalidOperationException rather than a silent re-tenanting - either way this
    // test fails if the guard is removed.)
    widget.TenantId.ShouldBe(TenantA);
  }

  [Fact]
  public async Task StampTenancy_WhenNoAmbientTenant_IsANoOp()
  {
    // The anonymous path. Stamping must never INVENT a tenant.
    var tenant = new FakeCurrentTenantService { TenantId = null };
    await using var context = CreateContext(tenant);

    var widget = new Widget { Name = "anonymous" };
    context.Widgets.Add(widget);
    await context.SaveChangesAsync();

    widget.TenantId.ShouldBe(Guid.Empty);
  }

  /// <summary>
  /// Guid.Empty is a perfectly valid Guid, so an ambient tenant of Guid.Empty must be screened
  /// explicitly — otherwise the guard stamps the exact value it exists to prevent.
  /// </summary>
  /// <remarks>
  /// ⚠️ The TenantId assertion alone CANNOT detect a missing screen: stamping Guid.Empty onto a row
  /// that already holds Guid.Empty is invisible. An ambient UserId is what makes the omission
  /// observable — without the screen the method runs on and stamps the user. Found by mutation
  /// testing: deleting <c>|| tenantId == Guid.Empty</c> left the earlier version of this test green.
  /// </remarks>
  [Fact]
  public async Task StampTenancy_WhenAmbientTenantIsGuidEmpty_StampsNothingAtAll()
  {
    var tenant = new FakeCurrentTenantService { TenantId = Guid.Empty, UserId = UserA };
    await using var context = CreateContext(tenant);

    var widget = new Widget { Name = "empty-ambient" };
    context.Widgets.Add(widget);
    await context.SaveChangesAsync();

    widget.TenantId.ShouldBe(Guid.Empty);

    // The load-bearing assertion: an unscreened Guid.Empty tenant would fall through to the user
    // stamp and set this.
    widget.UserId.ShouldBe(Guid.Empty);
  }

  [Fact]
  public async Task StampTenancy_WhenUserIdIsEmpty_FillsItFromTheAmbientUser()
  {
    var tenant = new FakeCurrentTenantService { TenantId = TenantA, UserId = UserA };
    await using var context = CreateContext(tenant);

    var widget = new Widget { Name = "unstamped-user" };
    context.Widgets.Add(widget);
    await context.SaveChangesAsync();

    widget.TenantId.ShouldBe(TenantA);
    widget.UserId.ShouldBe(UserA);
  }

  [Fact]
  public async Task StampTenancy_WhenUserIdIsAlreadySet_DoesNotOverwriteIt()
  {
    var tenant = new FakeCurrentTenantService { TenantId = TenantA, UserId = UserB };
    await using var context = CreateContext(tenant);

    var widget = new Widget { Name = "already-stamped-user" };
    widget.SetUser(UserA);

    context.Widgets.Add(widget);
    await context.SaveChangesAsync();

    widget.UserId.ShouldBe(UserA);
  }

  /// <summary>
  /// The reason the guard lives in SaveChanges rather than in each handler: CHILD rows added deep
  /// inside an aggregate get stamped too, without anyone remembering to do it.
  /// </summary>
  [Fact]
  public async Task StampTenancy_StampsEveryAddedRow_NotJustTheOneTheHandlerTouched()
  {
    var tenant = new FakeCurrentTenantService { TenantId = TenantA };
    await using var context = CreateContext(tenant);

    var parent = new Widget { Name = "parent" };
    var child = new Widget { Name = "child" };
    context.Widgets.AddRange(parent, child);

    await context.SaveChangesAsync();

    parent.TenantId.ShouldBe(TenantA);
    child.TenantId.ShouldBe(TenantA);
  }

  /// <summary>
  /// Stamping applies to ADDED rows only. An existing row is never re-tenanted on update.
  /// </summary>
  /// <remarks>
  /// <para>
  /// The subject is an ALREADY-ORPHANED row (TenantId = Guid.Empty) — the exact legacy row this guard
  /// exists to prevent. Updating it under an ambient tenant must NOT quietly adopt it: a row's tenant
  /// is decided when it is created, and silently moving persisted data between tenants on an
  /// unrelated update is a worse failure than leaving an orphan visible.
  /// </para>
  /// <para>
  /// ⚠️ Using an already-correct row here cannot detect a missing state filter (the
  /// <c>TenantId != Guid.Empty</c> guard short-circuits it anyway). Found by mutation testing:
  /// widening <c>State == Added</c> to every state left the earlier version of this test green.
  /// </para>
  /// </remarks>
  [Fact]
  public async Task StampTenancy_DoesNotStampModifiedRows_OnlyAddedOnes()
  {
    // Seed an ORPHAN: saved with no ambient tenant, so it lands with TenantId = Guid.Empty.
    await using (var context = CreateContext(new FakeCurrentTenantService { TenantId = null }))
    {
      context.Widgets.Add(new Widget { Name = "orphan" });
      await context.SaveChangesAsync();
    }

    // Re-open WITH an ambient tenant and modify it.
    await using (var context = CreateContext(new FakeCurrentTenantService { TenantId = TenantA }))
    {
      var row = await context.Widgets.IgnoreQueryFilters().FirstAsync(w => w.Name == "orphan");
      row.TenantId.ShouldBe(Guid.Empty, "precondition: the row is orphaned");

      row.Name = "orphan-modified";
      await context.SaveChangesAsync();

      // Modified, not Added - so it stays orphaned rather than being adopted by tenant A.
      row.TenantId.ShouldBe(Guid.Empty);
    }
  }

  private FailOpenTestDbContext CreateContext(FakeCurrentTenantService tenant) =>
      new(new DbContextOptionsBuilder().UseSqlite(_connection).Options, tenant);
}
