using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace MultiTenancy.EntityFrameworkCore.Tests;

/// <summary>
/// Write-side guard for the <see cref="MultiTenancy.Abstractions.ITenantOwned"/> path — the twin of
/// <see cref="StampTenancyTests"/>. Pins the same additive guarantees on an entity that keys on the
/// interface and stamps through the change-tracker property entry rather than a domain setter.
/// </summary>
public sealed class StampTenantOwnedTests : IDisposable
{
  private static readonly Guid TenantA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
  private static readonly Guid TenantB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
  private static readonly Guid UserA = Guid.Parse("11111111-1111-1111-1111-111111111111");

  private readonly SqliteConnection _connection;

  public StampTenantOwnedTests()
  {
    _connection = new SqliteConnection("Filename=:memory:");
    _connection.Open();

    using var context = CreateContext(new FakeCurrentTenantService());
    context.Database.EnsureCreated();
  }

  public void Dispose() => _connection.Dispose();

  [Fact]
  public async Task StampTenantOwned_WhenTenantIdIsEmpty_FillsItFromTheAmbientTenant()
  {
    var tenant = new FakeCurrentTenantService { TenantId = TenantA };
    await using var context = CreateContext(tenant);

    var gadget = new Gadget { Id = Guid.NewGuid(), Name = "unstamped" };
    gadget.TenantId.ShouldBe(Guid.Empty, "precondition: the row starts untenanted");

    context.Gadgets.Add(gadget);
    await context.SaveChangesAsync();

    gadget.TenantId.ShouldBe(TenantA);
  }

  /// <summary>Adopting the stamp cannot change behaviour that was already correct.</summary>
  [Fact]
  public async Task StampTenantOwned_WhenTenantIdIsAlreadySet_DoesNotOverwriteIt()
  {
    // Ambient tenant is B; the row was explicitly built for A.
    var tenant = new FakeCurrentTenantService { TenantId = TenantB };
    await using var context = CreateContext(tenant);

    var gadget = new Gadget { Id = Guid.NewGuid(), TenantId = TenantA, Name = "already-stamped" };

    context.Gadgets.Add(gadget);
    await context.SaveChangesAsync();

    gadget.TenantId.ShouldBe(TenantA);
  }

  [Fact]
  public async Task StampTenantOwned_WhenNoAmbientTenant_IsANoOp()
  {
    var tenant = new FakeCurrentTenantService { TenantId = null };
    await using var context = CreateContext(tenant);

    var gadget = new Gadget { Id = Guid.NewGuid(), Name = "anonymous" };
    context.Gadgets.Add(gadget);
    await context.SaveChangesAsync();

    gadget.TenantId.ShouldBe(Guid.Empty);
  }

  /// <summary>
  /// An ambient tenant of <see cref="Guid.Empty"/> must be screened — otherwise the guard stamps the
  /// exact value it exists to prevent.
  /// </summary>
  [Fact]
  public async Task StampTenantOwned_WhenAmbientTenantIsGuidEmpty_StampsNothingAtAll()
  {
    var tenant = new FakeCurrentTenantService { TenantId = Guid.Empty, UserId = UserA };
    await using var context = CreateContext(tenant);

    var gadget = new Gadget { Id = Guid.NewGuid(), Name = "empty-ambient" };
    context.Gadgets.Add(gadget);
    await context.SaveChangesAsync();

    gadget.TenantId.ShouldBe(Guid.Empty);
  }

  /// <summary>Child rows created deep in an aggregate get stamped too, without anyone remembering.</summary>
  [Fact]
  public async Task StampTenantOwned_StampsEveryAddedRow_NotJustTheOneTheHandlerTouched()
  {
    var tenant = new FakeCurrentTenantService { TenantId = TenantA };
    await using var context = CreateContext(tenant);

    var parent = new Gadget { Id = Guid.NewGuid(), Name = "parent" };
    var child = new Gadget { Id = Guid.NewGuid(), Name = "child" };
    context.Gadgets.AddRange(parent, child);

    await context.SaveChangesAsync();

    parent.TenantId.ShouldBe(TenantA);
    child.TenantId.ShouldBe(TenantA);
  }

  /// <summary>Stamping applies to ADDED rows only. An existing orphan is never re-tenanted on update.</summary>
  [Fact]
  public async Task StampTenantOwned_DoesNotStampModifiedRows_OnlyAddedOnes()
  {
    var orphanId = Guid.NewGuid();

    // Seed an ORPHAN: saved with no ambient tenant, so it lands with TenantId = Guid.Empty.
    await using (var context = CreateContext(new FakeCurrentTenantService { TenantId = null }))
    {
      context.Gadgets.Add(new Gadget { Id = orphanId, Name = "orphan" });
      await context.SaveChangesAsync();
    }

    // Re-open WITH an ambient tenant and modify it.
    await using (var context = CreateContext(new FakeCurrentTenantService { TenantId = TenantA }))
    {
      var row = await context.Gadgets.IgnoreQueryFilters().FirstAsync(g => g.Id == orphanId);
      row.TenantId.ShouldBe(Guid.Empty, "precondition: the row is orphaned");

      row.Name = "orphan-modified";
      await context.SaveChangesAsync();

      row.TenantId.ShouldBe(Guid.Empty);
    }
  }

  private FailOpenOwnedDbContext CreateContext(FakeCurrentTenantService tenant) =>
      new(new DbContextOptionsBuilder().UseSqlite(_connection).Options, tenant);
}
