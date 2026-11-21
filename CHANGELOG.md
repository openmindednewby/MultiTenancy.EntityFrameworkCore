# Changelog

Entries below start at 2.0.0. Releases 1.0.0 - 1.6.1 predate this file and are not
reconstructed here; read `git log` for those.

## 2.1.0 - 2026-11-21

### Security - a tenant realm can no longer grant the tenant-filter bypass (SEC-REALM-1)

`CurrentTenantService.IsSuperUser` used the obsolete `IsSuperUser()`, which trusts a `superUser`
role from ANY realm, so a tenant-realm admin could bypass the tenant filter. It now calls
`IsSuperUser(platformRealm)` (Security.Claims 1.7.0): the role counts only in a token whose `iss`
is the platform realm.

- `AddMultiTenancy()` binds `Authentication:PlatformRealm` (the key Identity.Keycloak 1.13.2
  reads) into the new `SuperUserRealmOptions`.
- **Unset = nobody is a super user (fail closed).** A consumer that relied on superUser seeing all
  tenants must set `Authentication:PlatformRealm` to its platform-only realm.
- The `CurrentTenantService(IHttpContextAccessor)` constructor stays, with no platform realm.
- Security.Claims 1.5.1 -> 1.7.0; new direct reference Microsoft.Extensions.Configuration.Abstractions 9.0.6.

## 2.0.0 - 2026-08-23

### BREAKING — drops the `net8.0` target

The package multi-targeted `net8.0;net10.0`. It now single-targets `net10.0`.

**Why it is a major.** A `net8.0` consumer restoring 2.0.0 fails `NU1202` (no compatible
asset), so the contract genuinely narrowed — even though, measured on 2026-08-23, **no
consumer is affected**: AgoraService, ContentService, IchnosService, KefiService,
NotificationService, OnlineMenuSaaS, PaymentService, TenantService, zygos, PROOViD/AMLService
and the P5 scaffolder's generated backend are all `net10.0`. Zero net8 projects remain in the
fleet. The multi-target was kept for "the net8 straggler (AMLService, net8/EF8)" — AMLService
migrated, and the comment justifying the constraint outlived the constraint.

**Why removing it mattered rather than being tidy-up.** The dead target was silently pinning
two dependencies:

| Dependency | Was | Now | Lag it was holding |
|---|---|---|---|
| `DomainCore` | 1.0.1 | 1.14.1 | thirteen minors |
| `Security.Claims` | 1.0.1 | 1.5.1 | five minors |

Both ship `lib/net10.0` **only** from their current releases (1.0.1 of each ships
`lib/net8.0` only), so with a `net8.0` target present neither could be bumped at all —
`dotnet restore` fails `NU1202`. That was **observed** before the change, not assumed. In the
fleet drift report the two read as ordinary neglect ("thirteen minors behind"); they were
structurally enforced, and a consumer-side "just bump it" would have hit NU1202 and been
reverted as inexplicable.

Neither bumped package has a startup surface (no `IHostedService`, `IStartupFilter`, DI
registration extension or startup-time throw), so the "compiles clean, throws at
`StartAsync`" hazard does not apply. 41/41 unit tests pass on the new dependency set.

### Changed

- One unconditional `Microsoft.EntityFrameworkCore` 9.0.6 reference. The per-TFM
  conditions (`9.0.6` for net10, `8.0.11` for net8) went with the net8 target.

### Migration

Update the version and rebuild — there are no source changes. Consumers on `net8.0` (none
today) must move to `net10.0` first, or stay on 1.6.1.
