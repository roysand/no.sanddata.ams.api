# Implementation Plan: User Roles and Endpoint Security

**Branch**: `feature/003-user-roles-and-endpoint-security` | **Date**: 2026-10-03 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/003-user-roles-and-endpoint-security/spec.md`

## Summary

Closes the anonymous user-management endpoints and introduces two roles, `Admin` and `User`. Roles are
seeded by a migration (with a backfill so existing users stay `User`), loaded at login/refresh so tokens
actually carry them (today they do not), and enforced with `Roles("Admin")` on admin-only endpoints and a
self-or-admin check in handlers for own-account endpoints. A startup service promotes the configured
owner to Admin when no Admin exists. New Admin endpoints grant/revoke the Admin role and link/unlink users
and locations; the meter endpoints gain a location-membership check. A "last active Admin" rule keeps the
system from locking itself out. See [research.md](./research.md) for the reasoning.

## Technical Context

**Language/Version**: C# / .NET 10

**Primary Dependencies**: ASP.NET Core 10, FastEndpoints 7.1.1, EF Core + Npgsql, FluentValidation, BCrypt.Net-Next (all existing; **no new packages**)

**Storage**: PostgreSQL + TimescaleDB (existing). No new tables; one migration for seed roles, the `User`-role backfill and model settings

**Testing**: `tests/Features.Tests` (xUnit + NSubstitute): handler, validator and bootstrap-service tests; the quickstart covers the end-to-end auth checks (role claim reaching `IsInRole`, 401/403/404 behavior)

**Target Platform**: Linux server / Docker (Windows for development)

**Project Type**: web-service (REST API), vertical slices

**Performance Goals**: not a concern at one owner and a handful of users; no extra per-request lookups (roles ride in the token)

**Constraints**: tokens are not revoked (role/deletion changes apply at next sign-in/refresh, up to 6 h); no new packages; no schema tables added

**Scale/Scope**: single owner, a few users and locations; 4 new endpoints, 8 changed endpoints, 1 hosted service, 1 migration

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design.*

| Principle | Status | Notes |
|---|---|---|
| I. Strict layering | PASS | Role names are Domain constants; handlers/endpoints in Features; EF seed + `AutoInclude` in Infrastructure; Application unchanged |
| II. Vertical slices | PASS | User/role/location-link admin lives in the Users slice; the membership check in Meters is duplicated, not shared |
| III. Result over exceptions | PASS | `User.LastAdmin`, `User.NotFound`, `User.IsActiveAdminOnly` are `Result` errors; endpoints translate with `AddError`/`ThrowIfAnyErrors` |
| IV. Validate once | PASS | Request shape in validators; authorization (own-or-admin, last-admin) is a business rule in handlers, not re-validation |
| V. CQRS + compile-time dispatch | PASS | New commands/queries are picked up by `Cqrs.SourceGenerator`. (The constitution text still says to register handlers manually; that is stale vs. the generator, noted in research §9) |
| VI. Structured logging | PASS | Compiled `LoggerMessage` delegates in the Users range 1000-1099 (no new range); no passwords/tokens logged |
| VII. Manual mapping | PASS | Existing static `UserMapper` extended |
| VIII. Minimal footprint | PASS | No generic roles CRUD, no policy framework, no revocation list, no new package; Admin grant is one `PUT`/`DELETE` pair |
| Migrations only for schema/data | PASS | Role seed and backfill ship in the migration |

No violations; Complexity Tracking not needed.

## Project Structure

### Documentation (this feature)

```text
specs/003-user-roles-and-endpoint-security/
├── plan.md              # This file
├── research.md          # Phase 0
├── data-model.md        # Phase 1
├── quickstart.md        # Phase 1
├── contracts/
│   └── endpoints.md     # Phase 1
├── checklists/
│   └── requirements.md
└── tasks.md             # Phase 2 (/speckit-tasks - not created here)
```

### Source Code (repository root)

```text
src/Domain/Common/
└── RoleNames.cs                                  # NEW: Admin / User constants

src/Infrastructure/
├── Database/Configuration/RoleConfiguration.cs   # HasData seed (fixed GUIDs)
├── Database/Configuration/UserConfiguration.cs   # AutoInclude Roles + Locations
└── Database/Migrations/<ts>_AddRolesAndSeed.cs   # NEW: seed + User-role backfill

src/Features/Users/                               # existing slice, extended
├── Commands/   ChangePassword, UpdateUser, DeleteUser (+CallerId/CallerIsAdmin); NEW GrantAdminCommand, RevokeAdminCommand, LinkUserLocationCommand, UnlinkUserLocationCommand
├── Queries/    GetUser (+caller)
├── Handlers/   self-or-admin + last-Admin rules; Create assigns the User role; NEW handlers for the four commands
├── Endpoints/  AllowAnonymous removed; Roles("Admin") on admin-only; NEW four endpoints
├── Validators/ NEW validators for the new requests
├── Services/   NEW AdminBootstrapService (hosted, startup-only)
└── Logging/LogMessages.cs                        # new events in 1000-1099

src/Features/Meters/                              # Commands/Queries/Handlers/Endpoints: add UserId + membership check
src/Features/Auth/Handlers/RefreshTokenCommandHandler.cs  # FindAsync instead of GetByIdAsync so roles load
src/api/appsettings.json                          # Bootstrap:OwnerEmail
CLAUDE.md / DevelopmentGuide.md / UserCrudEndpoints.md    # docs: auth requirements per endpoint

tests/Features.Tests/Users/ , tests/Features.Tests/Meters/   # NEW tests
```

**Structure Decision**: extend the existing Users and Meters vertical slices and the Infrastructure
configuration; no new project or slice. Role administration is part of user administration, so it stays in
`Features/Users`.

## Phase notes

- **Phase 0** produced [research.md](./research.md): the roles-not-loaded finding, the 401/403/404 model, the
  last-Admin rule, bootstrap design and its limitation, the meter and location-link decisions.
- **Phase 1** produced [data-model.md](./data-model.md), [contracts/endpoints.md](./contracts/endpoints.md) and
  [quickstart.md](./quickstart.md).
- **Re-check after design**: constitution table above still passes; the design added no packages, tables or
  layers.
