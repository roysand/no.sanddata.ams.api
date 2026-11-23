# Implementation Plan: What the Car Charging Costs, on the Bill

**Branch**: `feature/019-ev-charger-cost-build` | **Date**: 2026-11-22 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/019-ev-charger-cost/spec.md`

## Summary

The bill's car part (`evCharger`) gets `cost` and `pricedAt`. A new service in the `ElectricityCost` slice,
`EvChargerCost`, prices the car's hours so far:

- **Not counted with the house:** at the Norgespris price including VAT, through spec 018's
  `CostCalculator.NorgesPrisPriceWithVatAsync`. This rule lives in one method (R2).
- **Counted with the house:** by the one calculation, as house kWh used after the house's kWh of the same hour, with
  spec 018's running month total. Car kWh past the Norgespris limit are therefore priced on spot terms (R3).

The bill handler calls it before the projection, so every price lookup is preloaded (R1, R7). No line, part or total
reads it. Nothing is stored, and there is no migration.

## Technical Context

**Language/Version**: C# / .NET 10

**Primary Dependencies**: FastEndpoints, FluentValidation, EF Core (Npgsql)

**Storage**: PostgreSQL (TimescaleDB). No new query: it reuses the bill's charger and meter hours, rates and prices.

**Testing**: xUnit + NSubstitute in `tests/Features.Tests`:
- service tests for `EvChargerCost`;
- bill handler tests;
- an HTTP test of the shape.

**Target Platform**: Linux container on the Hetzner VPS behind Caddy

**Project Type**: web service (REST API)

**Performance Goals**: the bill no slower than today, within 10 % at p95 (SC-004)

**Constraints**:
- Bill lines and totals unchanged to the øre (SC-003).
- New fields only (FR-007).

**Scale/Scope**:
- About 100 car hours a month.
- 1 service, 2 response fields, a mapper parameter and a handler call.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design.*

| Principle | How this plan meets it | Status |
|---|---|---|
| I. Clean layering | `EvChargerCost` is in `Features/ElectricityCost/Services` and uses `CostCalculator` (Application). No EF or HTTP outside Infrastructure and the endpoint. | ✅ |
| II. Vertical slices | Everything is in `ElectricityCost`; it reads the charger's hours the bill already has. | ✅ |
| III. Result over exceptions | A price that can't be worked out is a null `cost`, never an exception. | ✅ |
| IV. Validate once | No new input. | ✅ |
| V. CQRS, compile-time dispatch | No new query or handler: the existing bill query reads only. | ✅ |
| VI. Structured logging | Nothing new to log: the bill already logs `CostQueried`. | ✅ |
| VII. Manual mapping | `BillMapper.ToResponse` takes the cost and maps it. | ✅ |
| VIII. Minimal footprint | No setting for the garages' rule, just one method to change (FR-005); no cache, no new query. | ✅ |
| Workflow: format and build | `dotnet format --verify-no-changes` and `dotnet build` with no new warnings, before the work is done. | ✅ |

**Post-design re-check**: the design artifacts add no violations. The gate passes.

## Project Structure

### Documentation (this feature)

```text
specs/019-ev-charger-cost/
├── spec.md
├── plan.md              # this file
├── research.md          # R1–R7
├── data-model.md
├── quickstart.md
├── contracts/
│   └── endpoints.md
├── checklists/
│   └── requirements.md
└── tasks.md             # from /speckit-tasks
```

### Source Code (repository root)

```text
src/Features/ElectricityCost/
├── Services/EvChargerCost.cs                 # new: PriceAsync, the two rules
├── Handlers/GetMonthlyBillQueryHandler.cs    # calls EvChargerCost before the projection
├── Mappers/BillMapper.cs                     # + the cost and pricedAt on evCharger
└── Queries/GetMonthlyBillQuery.cs            # EvChargerBillResponse + Cost, PricedAt

tests/Features.Tests/
├── ElectricityCost/EvChargerCostBillTests.cs # new
└── Http/MonthlyBillHttpTests.cs              # + the shape of evCharger
```

**Structure Decision**: The existing `Features/ElectricityCost` slice. No new project.

## Complexity Tracking

No violations to justify.
