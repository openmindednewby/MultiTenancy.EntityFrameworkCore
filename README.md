# MultiTenancy.EntityFrameworkCore

[![NuGet](https://img.shields.io/nuget/v/MultiTenancy.EntityFrameworkCore.svg)](https://www.nuget.org/packages/MultiTenancy.EntityFrameworkCore)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

Multi-tenant data isolation support for Entity Framework Core applications

## Installation

```bash
dotnet add package MultiTenancy.EntityFrameworkCore
```

## Description

Multi-tenant data isolation support for Entity Framework Core applications.

Provides the ambient tenant context (`ICurrentTenantService`) **and**, since 1.5.0, the EF Core half:
a global tenant query filter and a write-side stamping guard.

## Usage

Have your `DbContext` implement `ITenantScopedDbContext`, then call the two helpers:

```csharp
using MultiTenancy.Abstractions;
using MultiTenancy.EntityFrameworkCore;

public class AppDbContext : DbContext, ITenantScopedDbContext
{
    private readonly ICurrentTenantService _currentTenantService;

    public ICurrentTenantService CurrentTenantService => _currentTenantService;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        // ... entity configuration ...

        modelBuilder.ApplyTenantFilters(this, TenantFilterMode.FailOpen);
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        ChangeTracker.StampTenancy(_currentTenantService);
        return await base.SaveChangesAsync(cancellationToken);
    }
}
```

### `ApplyTenantFilters(context, mode)`

Adds a global query filter to every entity deriving from `BaseTenantEntity`.

**`mode` is required and has no default** — the two polarities differ by one character at the call
site and by "every tenant's rows" at runtime:

| Mode | With NO ambient tenant | Use when |
|---|---|---|
| `TenantFilterMode.FailOpen` | matches **every** row (pass-through) | migrations, background jobs, MassTransit consumers and unauthenticated webhook/reset paths must still read. **Those paths get isolation from an explicit `TenantId` predicate, not from this filter.** |
| `TenantFilterMode.FailClosed` | matches **no** rows | a forgotten tenant argument should fail loudly ("not found") rather than leak silently. Requires `IgnoreQueryFilters()` + `e.TenantId == tenantId` repository methods for anonymous paths. |

> ⚠️ Pass `this` as `context`. The filter is deliberately rooted at the DbContext instance so EF Core
> substitutes the *executing* context on every query. `ICurrentTenantService` is registered **Scoped**,
> and EF caches the model once per process — so a helper that captured the service instance would pin
> every future request to whichever request happened to build the model. That is a permanent
> cross-tenant leak, and it is why this method takes a context rather than a service.

### `ChangeTracker.StampTenancy(currentTenantService)`

Stamps `TenantId`/`UserId` onto **newly added** tenant-scoped rows that still hold `Guid.Empty`, from
the ambient tenant. Purely additive: it never overwrites a value that is already set, never stamps
non-`Added` rows, and is a no-op when there is no ambient tenant.

It exists because a row saved with `TenantId = Guid.Empty` raises no error and violates no
constraint — it is simply invisible to the tenant that owns it, forever. Child rows created inside an
aggregate are the usual way that happens.

## Documentation

See the [NuGet package page](https://www.nuget.org/packages/MultiTenancy.EntityFrameworkCore) for full documentation.

## Contributing

Contributions are welcome! Please open an issue or submit a pull request.

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## Related Packages

- [DomainCore](https://www.nuget.org/packages/DomainCore)
- [Security.Claims](https://www.nuget.org/packages/Security.Claims)

## Support

- Issues: [GitHub Issues](https://github.com/openmindednewby/MultiTenancy.EntityFrameworkCore/issues)
- Discussions: [GitHub Discussions](https://github.com/openmindednewby/MultiTenancy.EntityFrameworkCore/discussions)

## How to Publish

```bash
cd C:\desktopContents\projects\SaaS\NuGetPackages\MultiTenancy.EntityFrameworkCore
.\publish.ps1 -ApiKey XXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXX -Bump patch  # Bug fixes
.\publish.ps1 -ApiKey XXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXX -Bump minor  # New features
.\publish.ps1 -ApiKey XXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXX -Bump major  # Breaking changes
```
