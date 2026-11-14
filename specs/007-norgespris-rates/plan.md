# Implementation Plan: Norgespris Rates with Start Dates

**Branch**: `feature/007-norgespris-rates` | **Date**: 2026-11-13 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/007-norgespris-rates/spec.md`

## Summary

Replace the fixed `NorgesPris` configuration section with a table of rates, each with a start date. `CostCalculator`
looks up the rate valid for an hour from that table, using the Oslo calendar date of the hour. Admins list, add and
delete rates through three Admin-only endpoints. The first rate is seeded by the migration from today's configuration
values, so no existing figure changes. The calculator is request-scoped and loads the (tiny) rate list once, on first
use, so a month of hours costs one query for rates, not one per hour. `FlatRateOptions` and its config section are
removed. A small Admin page in the separate frontend repository consumes the endpoints; this repository defines the
contract only.

## Technical Context

**Language/Version**: C# 14 / .NET 10, ASP.NET Core 10

**Primary Dependencies**: FastEndpoints, FluentValidation, EF Core (Npgsql in production), custom CQRS with
`Cqrs.SourceGenerator`. No new packages.

**Storage**: PostgreSQL (schema `public`), one new table `NorgesPrisRate`, created and seeded by an EF Core migration.

**Testing**: xUnit + NSubstitute in `tests/Features.Tests` (existing pattern). Calculator, handler and validator unit
tests; a rate-selection test over the DST days. No new test packages (a small `TimeProvider` subclass instead of
`FakeTimeProvider`).

**Target Platform**: Linux container (Hetzner), as today.

**Project Type**: web-service (API), plus a small change in the separate React frontend repo.

**Performance Goals**: rates loaded once per request; a month of hourly costs adds exactly one rate query.

**Constraints**: all existing cost figures identical before any new rate is added (SC-002); Oslo date, not UTC, picks
the rate (FR-002).

**Scale/Scope**: a handful of rows ever (one change per few years). 3 endpoints, 1 entity, 1 migration, ~1 admin page.

## Constitution Check

*GATE: passes before Phase 0 and after Phase 1.*

| Principle | Status |
|---|---|
| I. Clean Architecture layering | Pass. Entity in Domain; repository interface and the pure rate-selection function in Application; EF repository in Infrastructure; handlers and endpoints in Features. Domain gets no dependency. |
| II. Vertical slices | Pass. New slice `Features/NorgesPris/` owns its commands, queries, handlers, endpoints, validator, mapper and log messages. The calculator (shared, in Application) already serves several slices; the slice does not reach into another slice's internals. |
| III. Result over exceptions | Pass. Duplicate date, not-newest, started-rate delete and not-found are `Result` errors. |
| IV. Validate once | Pass. Format rules (non-negative, 4 decimals, date present) in a FluentValidation validator. The date-dependent rules (duplicate, newest-only, started) need stored data, so they live in the handler as business rules, the same split `CreateLocationCommandHandler` uses for the serial number. |
| V. Real CQRS, compile-time dispatch | Pass. `ListNorgesPrisRatesQuery`; `AddNorgesPrisRateCommand`, `DeleteNorgesPrisRateCommand`. Handlers are found by the generator; nothing registered by hand. |
| VI. Structured logging | Pass. `LogMessages` for the slice with a new EventId range 1600-1699, added to the tables in `CLAUDE.md` and `DevelopmentGuide.md`. |
| VII. Manual mapping | Pass. Static `NorgesPrisMapper`. |
| VIII. Minimal footprint | Pass. No edit, no end date, no current flag, no cache layer beyond a field on the scoped calculator, one explicit `confirmPastDate` flag on the add request so the API enforces the past-date rule for every caller. Deliberately dropped: a separate "last rate" delete guard (see research D7). |
| Migrations | Pass. One migration `AddNorgesPrisRate` creates the table and seeds the first row. |

No violations; nothing to justify.

## Project Structure

### Documentation (this feature)

```text
specs/007-norgespris-rates/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── endpoints.md
└── tasks.md             # created by /speckit-tasks
```

### Source Code (repository root)

```text
src/
├── Domain/Common/Entities/
│   └── NorgesPrisRate.cs                          # NEW entity
├── Application/
│   ├── Common/Interfaces/Repositories/
│   │   └── INorgesPrisRateRepository.cs           # NEW: GetAllAsync (ordered, no-tracking), GetByIdAsync, DateExistsAsync, TryInsertAsync, Remove, SaveChangesAsync
│   └── ElectricityCost/
│       ├── NorgesPrisSchedule.cs                  # NEW: pure RateFor(hourStartUtc) over an ordered list, Oslo date
│       ├── CostCalculator.cs                      # CHANGED: uses repository + schedule, loads rates once
│       └── FlatRateOptions.cs                     # REMOVED
├── Infrastructure/
│   ├── Database/Configuration/NorgesPrisRateConfiguration.cs   # NEW (unique index on ValidFrom)
│   ├── Database/Repositories/NorgesPrisRateEfRepository.cs     # NEW
│   ├── Database/ApplicationDbContext.cs           # CHANGED: DbSet<NorgesPrisRate>
│   ├── Database/Migrations/<ts>_AddNorgesPrisRate.cs (+Designer, Snapshot)   # NEW via dotnet ef
│   └── AddInfrastructureToDI.cs                   # CHANGED: register repo + TimeProvider; drop FlatRateOptions
├── Features/NorgesPris/                           # NEW slice
│   ├── Commands/NorgesPrisCommands.cs
│   ├── Queries/ListNorgesPrisRatesQuery.cs
│   ├── Handlers/{ListNorgesPrisRates,AddNorgesPrisRate,DeleteNorgesPrisRate}Handler.cs
│   ├── Endpoints/{ListNorgesPrisRates,AddNorgesPrisRate,DeleteNorgesPrisRate}Endpoint.cs
│   ├── Validators/AddNorgesPrisRateValidator.cs
│   ├── Mappers/NorgesPrisMapper.cs
│   └── Logging/LogMessages.cs                     # 1600-1699
└── api/appsettings.json                           # CHANGED: remove "NorgesPris" section

tests/Features.Tests/
├── ElectricityCost/{CostCalculatorTests,CostHandlerTests,GetDayPricesQueryHandlerTests}.cs   # CHANGED: stub repository instead of FlatRateOptions
└── NorgesPris/                                    # NEW: schedule, calculator-over-time, add/delete handlers, validator

CLAUDE.md, DevelopmentGuide.md                     # CHANGED: EventId table (1600-1699 NorgesPris)
```

**Frontend (separate repo `no.sanddata.ams.frontend`)**: new `src/features/admin/NorgesPrisPage.tsx`, an
`AddNorgesPrisRateDialog.tsx`, api/hook additions in `features/admin/{api,hooks,types}.ts`, an Admin-only route and
menu entry beside `/admin/users`. Specified and built there under its own spec, using
[contracts/endpoints.md](contracts/endpoints.md) as the backend contract, the way 006 was.

**Structure Decision**: single existing .NET solution plus the new `Features/NorgesPris` folder inside the existing
`Features.csproj` (slices are folders in this repo, not projects).

## Complexity Tracking

No constitution violations.
