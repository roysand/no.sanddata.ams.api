# Implementation Plan: Location Management and Hashed Sensor Keys

**Branch**: `feature/004-location-management` | **Date**: 2026-10-03 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/004-location-management/spec.md`

## Summary

Lets an Admin add and manage locations from the web app, and stops storing sensor keys in readable form. The API
gets admin-only endpoints to list all locations, create a location together with its generated sensor key (shown
once), edit it, activate/deactivate it, and rotate or deactivate its key. Keys are stored only as a SHA-256
fingerprint plus a 4-character hint; a migration converts the existing key so the running sensor keeps working.
"Inactive location" gets a real meaning (rejects readings, hidden from regular users) through the existing central
access check. Request logging is hardened so keys cannot leak even with verbose logging. The frontend gets an admin
Locations page and the Users page offers every location for linking. See [research.md](./research.md).

## Technical Context

**Language/Version**: C# / .NET 10 (API); TypeScript, React 19, Vite (frontend repo)

**Primary Dependencies**: ASP.NET Core 10, FastEndpoints 7.1.1, EF Core + Npgsql, FluentValidation (API, all existing); React Query, react-hook-form, zod, shadcn/ui (frontend, all existing). **No new packages** in either repo.

**Storage**: PostgreSQL + TimescaleDB. One migration: `ApiKey.Key` replaced by `KeyHash`/`KeyHint`, unique index on `KeyHash`, unique index on `Location.SerialNumber`

**Testing**: `tests/Features.Tests` (xUnit + NSubstitute) for hashing, handlers, validators, the key-auth handler and the logging redaction; the quickstart (including a migration test on an isolated container with a copy of the real key) and a headless-browser run for the UI

**Target Platform**: Linux server / Docker; Windows for development

**Project Type**: web-service (API) plus single-page app (separate repo)

**Performance Goals**: key check stays one indexed lookup per sensor request; nothing else is on a hot path

**Constraints**: the running forwarder must keep working across the migration with no reconfiguration; keys never in logs, responses (except the two one-time responses) or claims; no new packages

**Scale/Scope**: single owner, a handful of locations; 5 new admin endpoints, 3 changed behaviors, 1 migration, 1 admin page + 4 dialogs in the frontend

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design.*

| Principle | Status | Notes |
|---|---|---|
| I. Strict layering | PASS | Hashing/generation helper in Application (no infrastructure); EF mapping/migration in Infrastructure; zone constants in Domain; handlers/endpoints in Features |
| II. Vertical slices | PASS | Location management extends the Locations slice; the Meters slice gets its own admin flag instead of depending on the Users slice's `Caller` |
| III. Result over exceptions | PASS | `Location.NotFound`, `Location.SerialNumberExists`, validation errors are `Result` errors |
| IV. Validate once | PASS | Request validators for create/update (text lengths, zone list); handlers only enforce business rules (serial uniqueness) |
| V. CQRS + compile-time dispatch | PASS | New commands/queries are picked up by the generator |
| VI. Structured logging | PASS | New events in the Locations range 1400-1499 with ids only; key redaction hardened in the request logger |
| VII. Manual mapping | PASS | Static mappers in the Locations slice |
| VIII. Minimal footprint | PASS | No overlap window for rotation, no custom expiry, no delete, no key-rejection logging, no new packages |
| Migrations only for schema/data | PASS | Hash conversion and indexes ship in one migration, tested on an isolated database first |

No violations; Complexity Tracking not needed.

## Project Structure

### Documentation (this feature)

```text
specs/004-location-management/
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
├── PriceZones.cs                          # NEW: NO1-NO5
└── Entities/
    ├── ApiKey.cs                          # KeyHash + KeyHint instead of Key; Rotate(), SetActive()
    └── Location.cs                        # Update(), SetActive(), AssignApiKey()

src/Application/
├── Common/ApiKeys/ApiKeyCrypto.cs         # NEW: Generate(), Hash(), Hint() (SHA-256 hex, 32 random bytes)
└── Common/Interfaces/Repositories/
    ├── IApiKeyRepository.cs               # FindActiveByKeyHashAsync
    └── ILocationRepository.cs             # GetAllWithKeyAsync, SerialNumberExistsAsync; active filters

src/Infrastructure/
├── Authentication/ApiKeyAuthenticationHandler.cs   # hash the header; drop the plain-key claim; reject inactive location
├── Database/Configuration/{ApiKeyConfiguration,LocationConfiguration}.cs
├── Database/Repositories/{ApiKeyEfRepository,LocationEfRepository}.cs
├── Database/Migrations/<ts>_AddHashedApiKeys.cs    # NEW (with backfill)
└── Middleware/RequestResponseLoggingMiddleware.cs  # always redact Authorization / X-API-Key; mask key-revealing bodies

src/Features/Locations/                    # existing slice, extended
├── Commands/   CreateLocation, UpdateLocation, RotateLocationKey, SetLocationKeyActive
├── Queries/    GetAdminLocations
├── Handlers/   one per command/query
├── Endpoints/  /api/admin/locations (GET, POST), /{id} (PUT), /{id}/api-key/rotate (POST), /{id}/api-key (PUT)
├── Validators/ create + update request validators
├── Mappers/    admin location / key info mapping
└── Logging/LogMessages.cs                 # events 1401-1409

src/Features/Meters/                       # admin may register at any location (IsAdmin on the command)

tests/Features.Tests/Locations/ , tests/Features.Tests/Authentication/

# Frontend repo: no.sanddata.ams.frontend
src/features/admin/locations/              # types, api, hooks, LocationsPage, LocationDialog,
                                           # KeyRevealDialog, RotateKeyDialog, RegisterReaderDialog
src/app/routes.tsx , src/components/Header.tsx   # /admin/locations + "Locations" link (Admin only)
src/features/admin/UsersPage.tsx           # location checkboxes use the admin list
```

**Structure Decision**: extend the existing Locations and Meters vertical slices and the Infrastructure
authentication/persistence code; one small helper class in Application for key crypto. No new project or slice.
The frontend follows the existing `features/<name>` layout next to the admin Users page.

## Phase notes

- **Phase 0** produced [research.md](./research.md): how keys work today, the hashing design and migration, where inactive
  and expiry checks go, the endpoint shape, the logging leak risk, and the frontend approach.
- **Phase 1** produced [data-model.md](./data-model.md), [contracts/endpoints.md](./contracts/endpoints.md) and
  [quickstart.md](./quickstart.md).
- **Re-check after design**: the constitution table above still passes; the design added no packages, tables, projects
  or layers.
- **Delivery order** (for `tasks.md`): hashing + migration first (it protects the running sensor and shapes everything),
  then the admin endpoints, then the frontend. The hashed-key story is built before the create-location story so keys
  are never stored in plain text, not even briefly.
