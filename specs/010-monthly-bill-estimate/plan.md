# Implementation Plan: Estimated Bill for the Month

**Branch**: `feature/010-monthly-bill-estimate` | **Date**: 2026-11-18 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/010-monthly-bill-estimate/spec.md`

## Summary

Three changes, each shippable on its own and in this order:

1. **Markup under Norgespris (US1).** One line in `CostCalculator.TermsAsync`: the supplier markup is added on
   Norgespris terms too. Every existing cost figure of a Norgespris location rises by the markup plus VAT; the saving's
   markup now cancels. No schema change.
2. **Supplier monthly fee (US4).** A nullable `Location.SupplierMonthlyFee` (NOK per month excluding VAT, null = not
   set), set through the existing `PUT /api/admin/location-pricing/{locationId}`. One migration. `IsComplete` on the
   pricing list also requires the fee.
3. **The bill (US2, US3).** A new read endpoint `GET /api/electricity-cost/bill?locationId=&month=yyyy-MM`. The handler
   prices the hours that have passed with the existing `HourlyCostProvider`, projects the remaining hours of the
   current month from the hourly profile of the last 7 days and prices them with the same `CostCalculator` (with the
   7-day average spot price as fallback for hours without a published price), and folds every priced hour into
   invoice lines with a pure `MonthlyBillBuilder`. The capacity charge comes from the existing pure `CapacityCharge`.
   The Norgespris credit is derived from the same breakdowns as the hourly cost, so spot energy + credit equals the
   hourly energy part exactly (FR-006).

## Technical Context

**Language/Version**: C# 14 / .NET 10, ASP.NET Core 10

**Primary Dependencies**: FastEndpoints, FluentValidation, EF Core (Npgsql), custom CQRS with `Cqrs.SourceGenerator`.
No new packages.

**Storage**: PostgreSQL. One new nullable column `Location.SupplierMonthlyFee` (`numeric(9,4)`), one migration
`AddSupplierMonthlyFee`. No new tables.

**Testing**: xUnit + NSubstitute in `tests/Features.Tests`, with the fakes and helpers of spec 008
(`FakePriceRateRepository`, `RangeStubs`, `FixedTimeProvider`, `EndpointHost`). The July 2026 invoice is rebuilt as
synthetic hours (research R9) for SC-001; the check against the real readings is a manual quickstart step.

**Target Platform**: Linux container, as today.

**Project Type**: web-service (API). No frontend change (a frontend spec follows).

**Performance Goals**: a bill for a month costs a fixed number of queries (location, dated rates, Norgespris rates,
capacity steps, consumption twice, spot prices twice, exchange rates twice), independent of the number of hours
(FR-017, SC-008).

**Constraints**: Oslo time decides the month, day or night, weekday and the projection profile key. Amounts are
`decimal`, never rounded in the calculation. A missing part is "not set" or "incomplete", never zero.

**Scale/Scope**: 1 column, 1 migration, 1 changed calculator line, 1 optional calculator parameter, 3 new fields on
the internal `PriceBreakdown`, 1 changed Admin endpoint, 1 new read endpoint with query, handler, validator and two
pure services.

## Constitution Check

*GATE: passes before Phase 0 and after Phase 1.*

| Principle | Status |
|---|---|
| I. Clean Architecture layering | Pass. The fee is a Domain property; the price formula change and the breakdown fields stay in Application; the migration in Infrastructure; the bill query, handler, endpoint and pure builders in `Features/ElectricityCost`. |
| II. Vertical slices | Pass. The bill lives in the ElectricityCost slice next to the capacity charge and the saving; the fee setting stays in the Pricing slice. No slice reaches into the other: both use the shared Application calculator and the Domain `Location`. |
| III. Result over exceptions | Pass. Unknown location → `Location.NotFound` (404); a month that has not started → `Bill.MonthNotStarted` (400). |
| IV. Validate once | Pass. The validator checks `locationId` and the `yyyy-MM` format, and the fee range on the pricing request. "Not started" depends on the clock, so the handler decides it, as the capacity handler decides "provisional". |
| V. CQRS, compile-time dispatch | Pass. One new query with a generated handler registration; the fee rides on the existing command. |
| VI. Structured logging | Pass. The bill reuses `CostQueried` (1500 range) and `LocationAccessDenied`; the fee is added as a property of `LocationPricingChanged` (1706). No new EventIds, so the CLAUDE.md table is unchanged. |
| VII. Manual mapping | Pass. Response records are built in a static mapper (`BillMapper`). |
| VIII. Minimal footprint | Pass. No new abstraction: the projection reuses `CostCalculator` through one optional `fallbackSpotEnergy` parameter; the fee is a plain column (not dated), as the markup is. |

Post-design re-check: still passes; nothing in Phase 1 added a table, package or abstraction.

## Project Structure

### Documentation (this feature)

```text
specs/010-monthly-bill-estimate/
├── plan.md              # This file
├── research.md          # Phase 0
├── data-model.md        # Phase 1
├── quickstart.md        # Phase 1
├── contracts/
│   └── endpoints.md     # Phase 1
└── tasks.md             # Phase 2 (/speckit-tasks)
```

### Source Code (repository root)

```text
src/Domain/Common/Entities/Location.cs                      # + SupplierMonthlyFee, SetPricing(..., fee)
src/Application/ElectricityCost/
├── PriceCalculation.cs                                      # PriceBreakdown + ElectricityTax, EnovaFee, VatRate
└── CostCalculator.cs                                        # markup on Norgespris terms; fallbackSpotEnergy
src/Infrastructure/Database/
├── Configuration/LocationConfiguration.cs                   # SupplierMonthlyFee numeric(9,4) null
└── Migrations/<timestamp>_AddSupplierMonthlyFee.cs
src/Features/Pricing/                                        # fee on request, command, response, IsComplete, log
src/Features/ElectricityCost/
├── Queries/GetMonthlyBillQuery.cs                           # query + response records
├── Handlers/GetMonthlyBillQueryHandler.cs
├── Endpoints/GetMonthlyBillEndpoint.cs                      # GET /api/electricity-cost/bill
├── Validators/GetMonthlyBillValidator.cs
├── Mappers/BillMapper.cs
└── Services/
    ├── ConsumptionProjection.cs                             # pure: 7-day profile, fallback
    └── MonthlyBillBuilder.cs                                # pure: priced hours → invoice lines
tests/Features.Tests/
├── ElectricityCost/MonthlyBillBuilderTests.cs               # lines, credit identity, July 2026 synthetic (SC-001..3)
├── ElectricityCost/ConsumptionProjectionTests.cs            # profile, DST, fewer than 7 days, none
├── ElectricityCost/MonthlyBillHandlerTests.cs               # so far + projected, final/estimate, not started, access
├── NorgesPris/CostCalculatorNorgesPrisTests.cs              # updated: markup on Norgespris terms
├── ElectricityCost/NorgesPrisSavingTests.cs                 # updated: markup cancels
├── Pricing/LocationPricingHandlerTests.cs                   # fee, IsComplete
└── Http/MonthlyBillHttpTests.cs                             # 200/400/401/404
```

**Structure Decision**: the existing Clean Architecture layout; the bill is a new query in the ElectricityCost slice,
and its pure builders sit in `Services/` next to `SavingCalculator`, which they resemble.

## Delivery order

1. US1 markup fix (calculator, doc comments, endpoint descriptions, two test files). Ship alone if wanted.
2. US4 fee (domain, migration, pricing slice).
3. US2 finished-month bill (builder, handler without projection, endpoint) — verify SC-001 here.
4. US3 projection (projection service, calculator fallback, handler).

## Complexity Tracking

None: no constitution violations.
