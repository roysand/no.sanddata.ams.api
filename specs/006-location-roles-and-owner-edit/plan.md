# Implementation Plan: Location Roles and Owner Editing

**Branch**: `feature/006-location-roles-and-owner-edit` | **Date**: 2026-10-09 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/006-location-roles-and-owner-edit/spec.md`. Consumer: web app feature `no.sanddata.ams.frontend` / `specs/002-location-editing-sharing`.

## Summary

Put a role (`Owner` or `Viewer`) on the existing `UserLocation` join row, then build on it: the admin link
call takes an optional role (default `Owner`), owners get two narrow write endpoints (location details and
meter comment), meter registration becomes owner-only, and the read models for locations and users expose roles.
The skip navigations `Location.Users` / `User.Locations` stay as they are, so every link-based read (dashboard
data, cost, "my locations") keeps working with no change; only the writes and the new read models look at the
role. One EF Core migration adds the column and marks all existing links `Owner`.

## Technical Context

**Language/Version**: C# on .NET 10, ASP.NET Core 10

**Primary Dependencies**: FastEndpoints, FluentValidation, EF Core (PostgreSQL), custom CQRS with `Cqrs.SourceGenerator`. No new packages.

**Storage**: PostgreSQL via EF Core; one migration (`AddUserLocationRole`).

**Testing**: xUnit + NSubstitute in `tests/Features.Tests/` (handler and validator tests, as for features 004 and 005).

**Target Platform**: Linux container behind Caddy (Hetzner), local Docker for development.

**Project Type**: Web service (API only; the web app is a separate repository).

**Performance Goals**: No change; lists stay small (few users and locations).

**Constraints**: Existing callers of `PUT /api/users/{id}/locations/{locationId}` and all existing reads must keep working unchanged; non-owners must get `404` (not `403`) so they learn nothing; the migration must not remove or change any link.

**Scale/Scope**: 3 new/changed write endpoints (owner location edit, meter comment edit, link role), 3 changed reads, 1 migration, about 6 handler changes and 1 new repository surface.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | How the plan complies |
|---|---|---|
| I. Strict layering | Pass | `LocationRole` and `UserLocation.Role` in Domain; repository interfaces in Application; EF configuration and queries in Infrastructure; handlers and endpoints in Features. |
| II. Vertical slices | Pass | Owner location edit lives in `Features/Locations`, meter comment in `Features/Meters`, link role in `Features/Users`. Shared reads go through repositories, not through another slice's internals. |
| III. Result values | Pass | `Error.NotFound("Location.NotFound")`, `Error.Conflict("Location.LastOwner")`; no exceptions for business failures. |
| IV. Validate once | Pass | New FluentValidation validators for the owner edit, the meter comment and the optional link role; handlers do not re-validate. |
| V. CQRS, compile-time dispatch | Pass | New commands only mutate; changed queries only read; handlers are picked up by `Cqrs.SourceGenerator`, not registered by hand. |
| VI. Structured logging | Pass | New `LogMessages` entries for role change and meter comment update; owner edits reuse `LocationUpdated` / `LocationActiveChanged` with the acting user. No secrets logged. |
| VII. Manual mapping | Pass | Extend the static `LocationMapper` / `UserMapper` / `MeterMapper`. |
| VIII. Minimal footprint | Pass | No new roles table, no generic permission framework, no new abstraction beyond a few repository methods. |
| Schema change via migration | Pass | `dotnet ef migrations add AddUserLocationRole`; no out-of-band DB edits. |

**Post-design re-check**: Pass. The one judgement call (a small race on the last-owner check) is recorded
in research item 3.

## Project Structure

### Documentation (this feature)

```text
specs/006-location-roles-and-owner-edit/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── endpoints.md
├── checklists/requirements.md
└── tasks.md             # created by /speckit-tasks
```

### Source Code (repository root)

```text
src/
├── Domain/Common/
│   ├── LocationRole.cs                          # NEW enum: Owner, Viewer
│   └── Entities/
│       ├── UserLocation.cs                      # + Role, ChangeRole
│       ├── Location.cs                          # + UpdateDetails(name, address)
│       └── Meter.cs                             # + SetComment
├── Application/Common/Interfaces/Repositories/
│   ├── IUserLocationRepository.cs               # + IsOwnerAsync, CountOwnersAsync, GetForUsersAsync, GetForLocationsAsync
│   └── ILocationRepository.cs                   # CountForUserAsync -> CountOwnedForUserAsync; + GetForUserWithRoleAsync
├── Infrastructure/Database/
│   ├── Configuration/UserLocationConfiguration.cs   # Role column, string conversion, default Owner
│   ├── Repositories/UserLocationEfRepository.cs
│   ├── Repositories/LocationEfRepository.cs
│   └── Migrations/<timestamp>_AddUserLocationRole.cs
├── Features/
│   ├── Users/
│   │   ├── Commands/UserLocationCommands.cs     # LinkUserLocationCommand + Role
│   │   ├── Endpoints/LinkUserLocationEndpoint.cs    # optional body { role }
│   │   ├── Handlers/LinkUserLocationCommandHandler.cs   # role on insert, role change, last-owner rule
│   │   ├── Handlers/UnlinkUserLocationCommandHandler.cs # last-owner rule
│   │   ├── Validators/LinkUserLocationValidator.cs      # NEW
│   │   ├── Queries/GetUsersQuery.cs + Mappers/UserMapper.cs   # + LocationAccess
│   │   └── Logging/LogMessages.cs               # + UserLocationRoleChanged
│   ├── Locations/
│   │   ├── Commands/UpdateOwnLocationCommand.cs     # NEW
│   │   ├── Endpoints/UpdateOwnLocationEndpoint.cs   # NEW  PUT /api/locations/{id}
│   │   ├── Handlers/UpdateOwnLocationCommandHandler.cs  # NEW
│   │   ├── Validators/UpdateOwnLocationValidator.cs     # NEW
│   │   ├── Handlers/GetMyLocationsQueryHandler.cs   # role, inactive-for-owners
│   │   ├── Handlers/GetAdminLocationsQueryHandler.cs    # + users
│   │   ├── Handlers/CreateLocationCommandHandler.cs     # owner-only limit
│   │   ├── Queries/GetMyLocationsQuery.cs / GetAdminLocationsQuery.cs  # response shapes
│   │   └── Mappers/LocationMapper.cs
│   └── Meters/
│       ├── Commands/UpdateMeterCommentCommand.cs    # NEW
│       ├── Endpoints/UpdateMeterCommentEndpoint.cs  # NEW  PUT /api/meters/{id}
│       ├── Handlers/UpdateMeterCommentCommandHandler.cs # NEW
│       ├── Handlers/CreateMeterCommandHandler.cs    # non-admin must be owner
│       └── Validators/UpdateMeterCommentValidator.cs    # NEW
tests/Features.Tests/
├── Users/UserLocationHandlerTests.cs            # extended
├── Locations/UpdateOwnLocationCommandHandlerTests.cs   # NEW
├── Locations/GetMyLocationsQueryHandlerTests.cs / GetAdminLocationsQueryHandlerTests.cs  # extended
├── Locations/CreateLocationCommandHandlerTests.cs      # limit counts owners
└── Meters/UpdateMeterCommentCommandHandlerTests.cs     # NEW + CreateMeterCommandHandlerTests extended
```

**Structure Decision**: No new project and no new feature folder. The work extends the three existing slices
(`Users`, `Locations`, `Meters`) and adds a role to the shared join entity.

## Delivery order

1. **Role on the link** (US1): domain, configuration, migration, repository methods, link/unlink handlers, validator, logging, tests. Apply the migration to the local database and check that existing links became owners.
2. **Read models** (US4): `GET /api/locations`, `GET /api/admin/locations`, `GET /api/users`. The web app's admin Locations page and Users page can then show roles.
3. **Owner writes** (US2, US3): `PUT /api/locations/{id}`, `PUT /api/meters/{id}`, owner-only meter registration.
4. **Limit** (US5): `CountOwnedForUserAsync`.
5. **Deploy**: merge, let the existing Build, Push & Deploy workflow run; the migration applies at startup (`RunMigrationsAtStartup`).

## Complexity Tracking

| Violation | Why Needed | Simpler Alternative Rejected Because |
|---|---|---|
| `IUserLocationRepository` goes from an empty interface to a few query methods | Role checks and role-carrying reads cannot be expressed through the generic repository | Querying `ApplicationDbContext` from handlers would break the layering rule (Principle I). |
| `CountForUserAsync` is renamed, not kept alongside a new method | Its only caller (the self-service limit) must now count owners only | Keeping both leaves a method with a misleading name that nothing should call. |
