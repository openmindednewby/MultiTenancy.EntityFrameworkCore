using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MultiTenancy.Abstractions;
using Shouldly;

namespace MultiTenancy.EntityFrameworkCore.Tests;

/// <summary>
/// Read-side isolation. These pin SECURITY behaviour — a cross-tenant leak that passes tests is the
/// worst outcome available here, so each test below was mutation-proved: the implementation was
/// broken, the test was confirmed to fail naming the defect, and the implementation restored.
/// </summary>
public sealed class TenantQueryFilterTests : IDisposable
{
  private static readonly Guid TenantA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
  private static readonly Guid TenantB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

  private readonly SqliteConnection _connection;

  public TenantQueryFilterTests()
  {
    _connection = new SqliteConnection("Filename=:memory:");
    _connection.Open();
    SeedBothTenants();
  }

  public void Dispose() => _connection.Dispose();

  // ---------------------------------------------------------------------------------------------
  // Ambient tenant set -> only that tenant's rows.
  // ---------------------------------------------------------------------------------------------

  [Theory]
  [InlineData(TenantFilterMode.FailOpen)]
  [InlineData(TenantFilterMode.FailClosed)]
  public async Task Query_WhenAmbientTenantIsSet_ReturnsOnlyThatTenantsRows(TenantFilterMode mode)
  {
    var tenant = new FakeCurrentTenantService { TenantId = TenantA };
    await using var context = CreateContext(mode, tenant);

    var widgets = await context.Widgets.AsNoTracking().ToListAsync();

    widgets.ShouldNotBeEmpty();
    widgets.ShouldAllBe(w => w.TenantId == TenantA);
    widgets.Select(w => w.Name).ShouldBe(["a-one", "a-two"], ignoreOrder: true);
  }

  [Theory]
  [InlineData(TenantFilterMode.FailOpen)]
  [InlineData(TenantFilterMode.FailClosed)]
  public async Task Query_WhenAmbientTenantIsSet_NeverReturnsAnotherTenantsRows(TenantFilterMode mode)
  {
    var tenant = new FakeCurrentTenantService { TenantId = TenantB };
    await using var context = CreateContext(mode, tenant);

    var widgets = await context.Widgets.AsNoTracking().ToListAsync();

    widgets.ShouldNotBeEmpty();
    widgets.ShouldNotContain(w => w.TenantId == TenantA);
    widgets.Select(w => w.Name).ShouldBe(["b-one"]);
  }

  // ---------------------------------------------------------------------------------------------
  // No ambient tenant. The two modes deliberately DISAGREE. Both behaviours are pinned as INTENDED.
  // ---------------------------------------------------------------------------------------------

  /// <summary>
  /// 🔴 <b>This asserts a PASS-THROUGH, and that is INTENTIONAL. Do not "fix" it.</b>
  /// </summary>
  /// <remarks>
  /// <para>
  /// With no ambient tenant, <see cref="TenantFilterMode.FailOpen"/> matches EVERY tenant's rows.
  /// This looks exactly like a cross-tenant leak because mechanically it IS one — the filter provides
  /// an unauthenticated caller no isolation at all.
  /// </para>
  /// <para>
  /// It is pinned because five services DEPEND on it. Migrations, background jobs, MassTransit GDPR
  /// consumers and unauthenticated webhook/reset paths all run without an <c>HttpContext</c>, so
  /// their ambient tenant is always null. Those paths get their isolation from an EXPLICIT
  /// <c>TenantId</c> predicate at the call site (pinned by
  /// <see cref="IgnoreQueryFilters_PairedWithExplicitPredicate_StillScopesToOneTenant"/>), not from
  /// this filter. Flipping this polarity would not harden them; it would silently return zero rows
  /// and turn a GDPR export into a no-op and a webhook into a lost payment.
  /// </para>
  /// <para>
  /// If you want isolation-by-default, that is <see cref="TenantFilterMode.FailClosed"/> — an opt-in
  /// per service, alongside the <c>IgnoreQueryFilters()</c> + predicate repository methods it
  /// requires. Zygos did exactly that. Changing THIS test's expectation instead would break the five
  /// services that chose the other trade.
  /// </para>
  /// </remarks>
  [Fact]
  public async Task Query_FailOpen_WhenNoAmbientTenant_ReturnsEveryTenantsRows_ByDesign()
  {
    var tenant = new FakeCurrentTenantService { TenantId = null };
    await using var context = CreateContext(TenantFilterMode.FailOpen, tenant);

    var widgets = await context.Widgets.AsNoTracking().ToListAsync();

    // Every row, across BOTH tenants. Deliberate - see the remarks above before changing this.
    widgets.Count.ShouldBe(3);
    widgets.Select(w => w.TenantId).Distinct().ShouldBe([TenantA, TenantB], ignoreOrder: true);
  }

  /// <summary>
  /// The Zygos polarity: no ambient tenant reads NOTHING, so a forgotten tenant argument fails loudly
  /// instead of leaking silently.
  /// </summary>
  [Fact]
  public async Task Query_FailClosed_WhenNoAmbientTenant_ReturnsNothing_ByDesign()
  {
    var tenant = new FakeCurrentTenantService { TenantId = null };
    await using var context = CreateContext(TenantFilterMode.FailClosed, tenant);

    var widgets = await context.Widgets.AsNoTracking().ToListAsync();

    widgets.ShouldBeEmpty();
  }

  // ---------------------------------------------------------------------------------------------
  // The IgnoreQueryFilters() + explicit predicate combination Zygos documents as load-bearing.
  // ---------------------------------------------------------------------------------------------

  [Theory]
  [InlineData(TenantFilterMode.FailOpen)]
  [InlineData(TenantFilterMode.FailClosed)]
  public async Task IgnoreQueryFilters_PairedWithExplicitPredicate_StillScopesToOneTenant(
      TenantFilterMode mode)
  {
    // The anonymous-path shape: no ambient tenant at all (webhook / statement import / GDPR consumer).
    var tenant = new FakeCurrentTenantService { TenantId = null };
    await using var context = CreateContext(mode, tenant);

    var widgets = await context.Widgets
        .IgnoreQueryFilters()
        .Where(w => w.TenantId == TenantA)
        .AsNoTracking()
        .ToListAsync();

    widgets.Count.ShouldBe(2);
    widgets.ShouldAllBe(w => w.TenantId == TenantA);
  }

  /// <summary>
  /// The companion to the test above: proves WHICH half does the work. Drop the predicate and the
  /// same query reads every tenant — in BOTH modes. That is why the pairing is load-bearing and why
  /// fail-closed is not a substitute for the predicate.
  /// </summary>
  [Theory]
  [InlineData(TenantFilterMode.FailOpen)]
  [InlineData(TenantFilterMode.FailClosed)]
  public async Task IgnoreQueryFilters_WithoutPredicate_ReadsAcrossEveryTenant(TenantFilterMode mode)
  {
    var tenant = new FakeCurrentTenantService { TenantId = TenantA };
    await using var context = CreateContext(mode, tenant);

    var widgets = await context.Widgets.IgnoreQueryFilters().AsNoTracking().ToListAsync();

    widgets.Count.ShouldBe(3);
  }

  // ---------------------------------------------------------------------------------------------
  // Per-query / per-context evaluation. The subtle ones.
  // ---------------------------------------------------------------------------------------------

  /// <summary>
  /// Moving the ambient tenant between two queries on the SAME context changes the results.
  /// </summary>
  /// <remarks>
  /// <para>
  /// ⚠️ This test catches a filter that captured the tenant VALUE (<c>var id = svc.TenantId;</c>) at
  /// model creation, which is the defect the original hand-rolled comments warned about.
  /// </para>
  /// <para>
  /// It ALSO happens to catch a captured SERVICE instance — but only incidentally, because EF caches
  /// the model across this whole test class, so by the time this test runs the model may hold an
  /// earlier test's service. That is an accident of ordering, not a property of this test: run it
  /// alone against a fresh model and a captured service would satisfy it, since the service instance
  /// is shared within one context. The defect is caught BY CONSTRUCTION only by
  /// <see cref="Filter_IsEvaluatedPerContext_NotBakedIntoTheCachedModel"/>.
  /// </para>
  /// </remarks>
  [Fact]
  public async Task Filter_IsEvaluatedPerQuery_NotBakedInAtModelCreation()
  {
    var tenant = new FakeCurrentTenantService { TenantId = TenantA };
    await using var context = CreateContext(TenantFilterMode.FailOpen, tenant);

    var first = await context.Widgets.AsNoTracking().ToListAsync();
    first.ShouldAllBe(w => w.TenantId == TenantA);

    // Same context, same compiled model - only the ambient tenant moved.
    tenant.TenantId = TenantB;

    var second = await context.Widgets.AsNoTracking().ToListAsync();

    second.ShouldNotBeEmpty();
    second.ShouldAllBe(w => w.TenantId == TenantB);
    second.ShouldNotContain(w => w.TenantId == TenantA);
  }

  /// <summary>
  /// 🔴 <b>The most important test in this file.</b> Two contexts, two DIFFERENT tenant-service
  /// instances, ONE cached model — each context must see only its own tenant.
  /// </summary>
  /// <remarks>
  /// <para>
  /// This is the production shape. EF Core builds the model once and caches it per context type;
  /// <c>CurrentTenantService</c> is Scoped, so every HTTP request brings a NEW service instance to
  /// that one cached model.
  /// </para>
  /// <para>
  /// A helper that captured the service instance in the filter lambda — the obvious API,
  /// <c>ApplyTenantFilters(modelBuilder, currentTenantService)</c> — would bake request #1's service
  /// into the model forever. Request #2 would then be filtered by request #1's tenant.
  /// </para>
  /// <para>
  /// This test is the one that detects that deterministically: it constructs the two-requests-one-model
  /// situation directly instead of relying on test ordering to produce it.
  /// </para>
  /// </remarks>
  [Fact]
  public async Task Filter_IsEvaluatedPerContext_NotBakedIntoTheCachedModel()
  {
    var options = BuildOptions();

    // "Request 1" - builds and caches the model.
    var firstRequestTenant = new FakeCurrentTenantService { TenantId = TenantA };
    await using (var first = new FailOpenTestDbContext(options, firstRequestTenant))
    {
      var rows = await first.Widgets.AsNoTracking().ToListAsync();
      rows.ShouldAllBe(w => w.TenantId == TenantA);
    }

    // "Request 2" - a DIFFERENT service instance reusing the model request 1 cached.
    var secondRequestTenant = new FakeCurrentTenantService { TenantId = TenantB };
    await using (var second = new FailOpenTestDbContext(options, secondRequestTenant))
    {
      var rows = await second.Widgets.AsNoTracking().ToListAsync();

      rows.ShouldNotBeEmpty();
      rows.ShouldAllBe(w => w.TenantId == TenantB);

      // If this fails, tenant B is reading tenant A's rows through a model cached on request 1.
      rows.ShouldNotContain(w => w.TenantId == TenantA);
    }
  }

  // ---------------------------------------------------------------------------------------------
  // Blast radius: non-tenant entities must be untouched.
  // ---------------------------------------------------------------------------------------------

  [Theory]
  [InlineData(TenantFilterMode.FailOpen)]
  [InlineData(TenantFilterMode.FailClosed)]
  public async Task Query_NonTenantEntity_IsNeverFiltered(TenantFilterMode mode)
  {
    // Even fail-closed with no ambient tenant must not hide rows that have no tenant concept.
    var tenant = new FakeCurrentTenantService { TenantId = null };
    await using var context = CreateContext(mode, tenant);

    var settings = await context.GlobalSettings.AsNoTracking().ToListAsync();

    settings.Count.ShouldBe(1);
  }

  // ---------------------------------------------------------------------------------------------

  private DbContextOptions BuildOptions() =>
      new DbContextOptionsBuilder()
          .UseSqlite(_connection)
          .Options;

  private TestDbContextBase CreateContext(TenantFilterMode mode, FakeCurrentTenantService tenant) =>
      mode == TenantFilterMode.FailClosed
          ? new FailClosedTestDbContext(BuildOptions(), tenant)
          : new FailOpenTestDbContext(BuildOptions(), tenant);

  private void SeedBothTenants()
  {
    // Seeded with NO ambient tenant and explicit SetTenant, so seeding never depends on the
    // behaviour under test.
    var seeder = new FakeCurrentTenantService { TenantId = null };
    using var context = new FailOpenTestDbContext(BuildOptions(), seeder);
    context.Database.EnsureCreated();

    var aOne = new Widget { Name = "a-one" };
    aOne.SetTenant(TenantA);
    var aTwo = new Widget { Name = "a-two" };
    aTwo.SetTenant(TenantA);
    var bOne = new Widget { Name = "b-one" };
    bOne.SetTenant(TenantB);

    context.Widgets.AddRange(aOne, aTwo, bOne);
    context.GlobalSettings.Add(new GlobalSetting { Key = "global" });
    context.SaveChanges();
  }
}
