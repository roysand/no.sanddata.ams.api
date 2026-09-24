# Implementation Plan: Measurement Query Endpoints

**Branch**: `001-measurement-query-endpoints` | **Date**: 2026-09-24 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/001-measurement-query-endpoints/spec.md`

## Summary

Add read-only, JWT-authenticated query endpoints so an authenticated user can retrieve their own
power-usage measurement data: recent usage, a chosen historical date range, a single latest
reading, and the list of their locations/meters to query against. No new persisted data — this
is a query-side vertical slice over the existing `Measurement`/`Location`/`Meter` entities, with
authorization strictly scoped to the caller's `Location` associations.

## Technical Context

**Language/Version**: C# / .NET 10

**Primary Dependencies**: FastEndpoints (REST), FluentValidation (request validation), EF Core
via `Npgsql.EntityFrameworkCore.PostgreSQL`, the project's own CQRS abstractions +
`Cqrs.SourceGenerator`-generated dispatcher, `Microsoft.AspNetCore.Authentication.JwtBearer`

**Storage**: PostgreSQL (TimescaleDB image locally via `compose.db.yaml`); no schema changes —
read-only queries over existing tables using existing indexes

**Testing**: Manual end-to-end verification (curl / Scalar UI) — no automated test project exists
in this repo yet; see research.md §5

**Target Platform**: ASP.NET Core 10 Web API (existing solution, Windows/Mac dev via Docker,
Azure-hosted test/prod)

**Project Type**: Web service — new vertical slices within the existing single-solution
Clean Architecture layout (no separate frontend/backend split in this repo; the frontend is a
different repo)

**Performance Goals**: Recent-usage query (SC-001) returns in <1s under normal load

**Constraints**: Results MUST be paginated for large ranges (FR-008); every query MUST be
authorization-scoped to the caller's own `Location` associations (FR-001, SC-002); no
server-side aggregation in this feature (FR-008); single-location-per-query only (FR-010)

**Scale/Scope**: Single-household hobby-project scale — low number of users/locations, one or a
few meters per location, roughly minute-level reading frequency (so up to ~500K rows/meter/year)

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Compliance |
|---|---|
| I. Strict Clean Architecture Layering | PASS — new Queries/DTOs live in `Features`; new repository methods live in `Infrastructure` behind `Application`-defined interfaces; `Domain` entities are untouched. |
| II. Vertical Slices Over Shared Coupling | PASS — two slices: `Features/Measurements` (extended) and `Features/Locations` (new), each owning its own Queries/Handlers/Endpoints/Validators/Mappers. |
| III. Explicit Control Flow Over Exceptions | PASS — handlers return `Result<T>`; `Location.NotFound`/`Meter.NotFound`/`Validation.InvalidRange` are `Error` values, not exceptions. |
| IV. Validate Once, At the Boundary | PASS — FluentValidation validators on each request DTO check range ordering (`to >= from`) and pagination bounds before the handler runs. |
| V. Real CQRS Separation, Compile-Time Dispatch | PASS — three new `IQuery<Result<T>>` types (`GetMyLocationsQuery`, `GetMeasurementsQuery`, `GetLatestMeasurementQuery`), dispatched via the generated `IDispatcher`; new handlers registered in `Infrastructure/AddInfrastructureToDI.cs`. |
| VI. Structured, Low-Allocation Logging | PASS, with follow-up — no `LogMessages` class exists yet for Measurements or Locations; this plan claims EventId ranges `1300-1399` (Measurements) and `1400-1499` (Locations). **Action required in tasks.md**: update the EventId table in `CLAUDE.md` and `DevelopmentGuide.md`. |
| VII. Explicit Manual Mapping, No Automappers | PASS — new static mappers in `Features/Measurements/Mappers` and `Features/Locations/Mappers`. |
| VIII. Minimal-Footprint Changes | PASS — no new entities, no new migration, no new test framework introduced; reuses existing indexes and the existing per-entity custom-repository-method pattern (e.g. `IApiKeyEfRepository.FindActiveByKeyAsync`) instead of growing the generic repository. |

No violations — Complexity Tracking table intentionally omitted.

## Project Structure

### Documentation (this feature)

```text
specs/001-measurement-query-endpoints/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md         # Phase 1 output
├── quickstart.md         # Phase 1 output
├── contracts/
│   └── endpoints.md      # Phase 1 output
└── tasks.md              # Phase 2 output (/speckit-tasks — not created by /speckit-plan)
```

### Source Code (repository root)

```text
src/
├── Domain/                                  # unchanged
├── Application/
│   └── Common/Interfaces/Repositories/
│       ├── ILocationEfRepository.cs          # + IsUserAssociatedAsync, GetForUserAsync
│       └── IMeasurementEfRepository.cs       # + GetPagedAsync, GetLatestAsync
├── Infrastructure/
│   ├── Database/Repositories/
│   │   ├── LocationEfRepository.cs           # implements the new interface methods
│   │   └── MeasurementEfRepository.cs        # implements the new interface methods
│   ├── Logging/                              # or per-feature, per existing convention
│   └── AddInfrastructureToDI.cs              # register 3 new query handlers
└── Features/
    ├── Measurements/
    │   ├── Queries/GetMeasurementsQuery.cs
    │   ├── Queries/GetLatestMeasurementQuery.cs
    │   ├── Handlers/GetMeasurementsQueryHandler.cs
    │   ├── Handlers/GetLatestMeasurementQueryHandler.cs
    │   ├── Endpoints/GetMeasurementsEndpoint.cs
    │   ├── Endpoints/GetLatestMeasurementEndpoint.cs
    │   ├── Validators/GetMeasurementsValidator.cs
    │   ├── Mappers/MeasurementMapper.cs        # extended
    │   └── Logging/LogMessages.cs              # new, EventId 1300-1399
    └── Locations/                              # new feature slice
        ├── Queries/GetMyLocationsQuery.cs
        ├── Handlers/GetMyLocationsQueryHandler.cs
        ├── Endpoints/GetLocationsEndpoint.cs
        ├── Mappers/LocationMapper.cs
        └── Logging/LogMessages.cs              # new, EventId 1400-1499
```

**Structure Decision**: Follows the existing vertical-slice layout exactly — no new projects.
`Measurements` gains query-side files alongside its existing ingestion command; `Locations` is a
new slice since "location" is a distinct domain concept from both measurements and meters (which
already has its own slice), matching Principle II.
