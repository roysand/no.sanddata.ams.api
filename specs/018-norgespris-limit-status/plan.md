# Implementation Plan: The Norgespris Limit and the Spot Energy Price for the App

**Branch**: `feature/018-norgespris-limit-status-build` | **Date**: 2026-11-22 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/018-norgespris-limit-status/spec.md`

## Summary

Three changes, all in the `ElectricityCost` slice and the cost calculator:

- **The status.** A new read-only endpoint, `GET /api/electricity-cost/norgespris-status`, lists every active
  Norgespris location of the caller's location list. Each entry has the month's limit, the kWh so far, the projected
  kWh, over/by how much, the Norgespris price including VAT and the reset time. The limit and the price come from new
  public methods on `CostCalculator` (R3, R6). The projection is the bill's, through one helper both call (R5). Two
  queries per location, no pricing (R11).
- **The spot energy price.** `/api/electricity-cost/prices` adds `spotEnergyPricePerKwh` to each hour: spot energy ×
  (1 + VAT), from a new `CostCalculator.SpotEnergyWithVatAsync` (R10).
- **The car in the limit (FR-004).** For a location that counts its car with the house, the charger's kWh of earlier
  hours of the month are added to the running month total the cap is applied to, in the four places that keep it:
  `HourlyCostProvider`, the bill's projection, the prices and the current hour (R9). The car is still never priced.

Nothing is stored, and there is no migration.

## Technical Context

**Language/Version**: C# / .NET 10

**Primary Dependencies**: FastEndpoints, FluentValidation, EF Core (Npgsql)

**Storage**: PostgreSQL (TimescaleDB). Reads the hourly consumption aggregate, `ChargingSessionHour`, the price rates
and the Norgespris rates through existing repository methods. No new table, index or migration.

**Testing**: xUnit + NSubstitute in `tests/Features.Tests`:
- handler tests for the status with substituted repositories;
- cost tests for the car in the cap (provider, prices, current hour, bill);
- calculator tests for the new public methods;
- HTTP tests through `EndpointHost`.

**Target Platform**: Linux container on the Hetzner VPS behind Caddy

**Project Type**: web service (REST API)

**Performance Goals**: the status for 3 locations in < 300 ms p95 in production (SC-003). The app reads it on every
page load and every few minutes.

**Constraints**:
- No existing field changes meaning (FR-006); the one change in behaviour is FR-004.
- The status, the bill and the prices must agree (FR-003): shared code, not copies.

**Scale/Scope**:
- 1–3 Norgespris locations per user, at most ~1 100 hourly rows per meter per request.
- 1 endpoint, 1 query, 1 handler, 3 calculator methods, 1 projection helper, 1 log message, 1 added field, and the
  running-total change in 4 places.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design.*

| Principle | How this plan meets it | Status |
|---|---|---|
| I. Clean layering | The new calculator methods are in `Application/ElectricityCost` and use repository interfaces only. Queries stay in Infrastructure; no EF or HTTP in Application or Domain. | ✅ |
| II. Vertical slices | Everything new is in `Features/ElectricityCost`. It reads the location list through `ILocationRepository` (Application), not through the `Locations` slice, and the charger's hours through `IChargingSessionRepository`, as the slice already does. | ✅ |
| III. Result over exceptions | The handler returns `Result<NorgesPrisStatusResponse>`. Missing data is null in the answer, never an exception. | ✅ |
| IV. Validate once | The status has no input, so no validator. `/prices` keeps its validation. | ✅ |
| V. CQRS, compile-time dispatch | `GetNorgesPrisStatusQuery` reads only. The source generator registers the handler. | ✅ |
| VI. Structured logging | `NorgesPrisStatusQueried` (EventId 1505, inside 1500–1599), with the user id and a count. No PII. | ✅ |
| VII. Manual mapping | A static `NorgesPrisStatusMapper` builds an entry from its figures (over/by how much). | ✅ |
| VIII. Minimal footprint | No cache, no new repository method, no setting. The bill's projection code moves into a helper instead of being copied. | ✅ |
| Workflow: format and build | `dotnet format --verify-no-changes` and `dotnet build` with no new warnings, before the work is done. | ✅ |

**Post-design re-check**: the design artifacts (research, data model, contract, quickstart) add no violations. The
gate passes.

## Project Structure

### Documentation (this feature)

```text
specs/018-norgespris-limit-status/
├── spec.md
├── plan.md              # this file
├── research.md          # R1–R12
├── data-model.md
├── quickstart.md
├── contracts/
│   └── endpoints.md
└── tasks.md             # from /speckit-tasks
```

### Source Code (repository root)

```text
src/Application/ElectricityCost/
└── CostCalculator.cs                       # + NorgesPrisCapAsync, NorgesPrisPriceWithVatAsync, SpotEnergyWithVatAsync

src/Features/ElectricityCost/
├── Endpoints/GetNorgesPrisStatusEndpoint.cs        # new: GET /api/electricity-cost/norgespris-status
├── Queries/GetNorgesPrisStatusQuery.cs             # new: query + response records
├── Queries/GetDayPricesQuery.cs                    # HourPriceResponse + SpotEnergyPricePerKwh
├── Handlers/GetNorgesPrisStatusQueryHandler.cs     # new
├── Handlers/GetDayPricesQueryHandler.cs            # spot energy price; car in the running total
├── Handlers/GetCurrentHourCostQueryHandler.cs      # car in the kWh before the hour
├── Handlers/GetMonthlyBillQueryHandler.cs          # projection through the helper; car in its start total
├── Mappers/NorgesPrisStatusMapper.cs               # new
├── Services/HourlyCostProvider.cs                  # car in the running total, read from the month start
├── Services/ConsumptionProjection.cs               # + ProjectRestOfMonth (window + remaining hours)
├── Services/CostTime.cs                            # + month helpers if needed
└── Logging/LogMessages.cs                          # + NorgesPrisStatusQueried (1505)

tests/Features.Tests/
├── ElectricityCost/NorgesPrisStatusHandlerTests.cs # new
├── ElectricityCost/EvChargerCapTests.cs            # new: FR-004 across the cost figures
├── ElectricityCost/GetDayPricesQueryHandlerTests.cs # + spot energy price
└── Http/NorgesPrisStatusHttpTests.cs               # new
```

**Structure Decision**: The existing Clean Architecture projects and the `Features/ElectricityCost` slice. No new
project.

## Complexity Tracking

No violations to justify.
