# Implementation Plan: Electricity Cost Tracking

**Branch**: `002-electricity-cost-tracking` | **Date**: 2026-09-27 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/002-electricity-cost-tracking/spec.md`

## Summary

Turns raw power measurements into electricity cost in NOK, including a live-updating figure for
the current, still-in-progress hour. Adds TimescaleDB continuous aggregates for minute/hour
energy consumption (activating TimescaleDB in this database for the first time), a background
job that fetches day-ahead spot prices (ENTSO-E) and daily FX rates (Norges Bank), and query
endpoints that compute cost on read — under whichever pricing model (spot price or the flat
government "Norgespris" rate) each location is enrolled in, plus an always-available comparison
against the other model.

## Technical Context

**Language/Version**: C# / .NET 10

**Primary Dependencies**: FastEndpoints, FluentValidation, EF Core via
`Npgsql.EntityFrameworkCore.PostgreSQL`, the project's CQRS abstractions +
`Cqrs.SourceGenerator`, `Microsoft.AspNetCore.Authentication.JwtBearer`. No new NuGet packages —
ENTSO-E's XML is parsed with `System.Xml.Linq` and Norges Bank's SDMX-JSON with
`System.Text.Json`, both already part of the BCL.

**Storage**: PostgreSQL with the TimescaleDB extension — **activated by this feature's first
migration** (not yet enabled anywhere in this database). Two new plain tables
(`ElectricityPrice`, `ExchangeRate`) plus two continuous aggregates over the existing
`Measurement` hypertable (`measurement_minute`, `measurement_hour`), mapped into EF Core as
read-only keyless entities.

**Testing**: `tests/Features.Tests` (xUnit + NSubstitute) now exists in this repo — unit-test
`CostCalculator`'s rate-selection logic and the new handlers' authorization/mapping behavior the
same way `GetMeasurementsQueryHandlerTests` covers 001's date-range defaulting. The ENTSO-E/Norges
Bank HTTP clients and the TimescaleDB continuous aggregates still need manual/integration
verification (curl/Scalar via `quickstart.md`) — not practical to unit-test external API parsing
without live fixtures, same reasoning as the parser tests in `no.sanddata.ams.services`.

**Target Platform**: ASP.NET Core 10 Web API (existing solution) — the price/FX fetch job runs
as a `BackgroundService` inside this same API process, since both ENTSO-E and Norges Bank are
public HTTPS APIs reachable directly from the Azure-hosted API (unlike the MQTT broker, which is
why that ingestion path is a separate service in `no.sanddata.ams.services`).

**Performance Goals**: Current-hour cost query (SC-001) reflects consumption no more than 2
minutes old — satisfied by TimescaleDB's real-time aggregation over the open bucket, not by
polling/refresh tuning.

**Constraints**: Cost figures MUST be computed at query time, never persisted (research.md §7);
every query MUST be authorization-scoped to the caller's own Location associations, matching
001's pattern exactly; the flat government rate is configuration, not data (research.md §8); no
historical backfill — cost tracking applies going forward only (FR-012).

**Scale/Scope**: Same household hobby-project scale as 001 — a handful of locations, each with a
handful of meters; two new small reference tables (one row per price-region-hour and one row per
day respectively) alongside the existing `Measurement` volume.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design — still passes.*

| Principle | Compliance |
|---|---|
| I. Strict Clean Architecture Layering | PASS — new `ElectricityPrice`/`ExchangeRate` Domain entities; continuous-aggregate read models and the ENTSO-E/Norges Bank HTTP clients live in `Infrastructure`, behind `Application`-defined interfaces; `Domain` has no knowledge of TimescaleDB or the external APIs. |
| II. Vertical Slices Over Shared Coupling | PASS — one new `Features/ElectricityCost` slice owns its Queries/Handlers/Endpoints/Mappers plus the background fetch service, matching how `Measurements` already combines ingestion and querying in one slice. |
| III. Explicit Control Flow Over Exceptions | PASS — `Location.NotFound` via `Result`/`Error` exactly as in 001; missing price/FX data is modeled as a nullable field in a successful response (FR-011), not an exception or a failed `Result`. |
| IV. Validate Once, At the Boundary | PASS — FluentValidation validators on each request DTO (e.g. `granularity` enum, date range ordering), reusing the same pattern as `GetMeasurementsValidator`. |
| V. Real CQRS Separation, Compile-Time Dispatch | PASS — new `IQuery<Result<T>>` types per endpoint, dispatched via the generated `IDispatcher`; the background fetch service is a `BackgroundService`, not a CQRS command (it's a scheduled infrastructure process, not a user-initiated action — matches how `no.sanddata.ams.services`'s own background services aren't CQRS commands either). |
| VI. Structured, Low-Allocation Logging | PASS, with follow-up — no `LogMessages` class exists yet for this feature; claims EventId range `1500-1599`. **Action required in tasks.md**: update the EventId table in `CLAUDE.md`/`DevelopmentGuide.md`. |
| VII. Explicit Manual Mapping, No Automappers | PASS — static mappers from the continuous-aggregate read models + price/FX entities into response DTOs. |
| VIII. Minimal-Footprint Changes | PASS — no cost table (computed at read time, research.md §7), no price-region reference table (deferred per spec's own Assumptions, a static 5-entry dictionary suffices for NO1-NO5), flat rate is config not data, no new NuGet packages. |

No violations — Complexity Tracking table intentionally omitted.

## Project Structure

### Documentation (this feature)

```text
specs/002-electricity-cost-tracking/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md         # Phase 1 output
├── quickstart.md         # Phase 1 output
├── contracts/
│   └── endpoints.md      # Phase 1 output
└── tasks.md              # Phase 2 output (/speckit-tasks — not created here)
```

### Source Code (repository root)

```text
src/
├── Domain/
│   └── Common/Entities/
│       ├── ElectricityPrice.cs               # new
│       └── ExchangeRate.cs                    # new
├── Application/
│   └── Common/Interfaces/
│       ├── Repositories/
│       │   ├── IElectricityPriceRepository.cs  # new
│       │   └── IExchangeRateRepository.cs      # new
│       └── External/
│           ├── ISpotPriceClient.cs             # new — ENTSO-E
│           └── IExchangeRateClient.cs          # new — Norges Bank
├── Infrastructure/
│   ├── Database/
│   │   ├── Configuration/
│   │   │   ├── ElectricityPriceConfiguration.cs   # new
│   │   │   └── ExchangeRateConfiguration.cs       # new
│   │   ├── ReadModels/
│   │   │   ├── MinuteConsumption.cs               # new, keyless, .ToView("measurement_minute")
│   │   │   └── HourConsumption.cs                 # new, keyless, .ToView("measurement_hour")
│   │   ├── Repositories/
│   │   │   ├── ElectricityPriceEfRepository.cs    # new
│   │   │   └── ExchangeRateEfRepository.cs        # new
│   │   └── Migrations/
│   │       └── <timestamp>_AddElectricityCostTracking.cs   # enables timescaledb, hypertable, continuous aggregates, new tables
│   └── External/
│       ├── EntsoeSpotPriceClient.cs            # new
│       └── NorgesBankExchangeRateClient.cs     # new
└── Features/
    └── ElectricityCost/
        ├── Queries/
        │   ├── GetConsumptionQuery.cs
        │   ├── GetCurrentHourCostQuery.cs
        │   ├── GetHourlyCostQuery.cs
        │   └── GetDailyCostQuery.cs
        ├── Handlers/                            # one per query above
        ├── Endpoints/                           # one per query above
        ├── Validators/
        │   └── GetConsumptionValidator.cs        # + validators for the other 3 requests
        ├── Mappers/
        │   └── CostMapper.cs
        ├── Services/
        │   └── PriceFetchService.cs              # BackgroundService, registered via AddHostedService
        └── Logging/
            └── LogMessages.cs                    # new, EventId 1500-1599
```

**Structure Decision**: One new vertical slice (`Features/ElectricityCost`), matching the
existing pattern exactly. No changes to `Measurements` or `Locations` slices — this feature reads
`Measurement` data but doesn't modify how it's ingested or queried at the raw level.
