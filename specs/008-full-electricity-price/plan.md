# Implementation Plan: Full Electricity Price per kWh

**Branch**: `feature/008-full-electricity-price` | **Date**: 2026-11-16 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/008-full-electricity-price/spec.md`

## Summary

Replace the energy-only hourly price in `CostCalculator` with the full price a household pays:
`(1 + VAT) x (energy + markup + grid + taxes)`. The grid charge depends on the Oslo hour and weekday (day rate on
weekdays 06-22 that are not public holidays, night and weekend rate otherwise). Taxes, VAT and the grid tariff rates are
dated rows an Admin manages with the same rules as the Norgespris rates (spec 007). Each location gets a grid tariff and
a supplier markup, set through a new Admin endpoint. The calculator returns a breakdown with every price, and the
existing rate and cost fields become the totals including VAT, so the other handlers and the frontend keep working. A new
endpoint gives the Norgespris saving per period. No UI change is in this feature (see `specs/_backlog`).

**Addendum 2026-11-17 (user stories 6 and 7, after checking against an Elvia bill).** Stories 1-5 are built and merged;
their design below is unchanged. Story 6 adds three Admin-set location flags (electricity-tax exempt, VAT exempt,
holiday home) and two new `PriceRate` kinds for the Norgespris monthly cap (permanent home, holiday home, in kWh). The
calculator applies the exemptions, and for Norgespris terms prices the kWh of an Oslo calendar month up to the cap at
the Norgespris rate and the rest on spot terms; every caller passes the kWh the location used earlier in the month
before the hour, so the crossing hour is split. Story 7 (phase 2) adds dated capacity-step tables per grid tariff and a
pure calculation of the monthly capacity charge from the three highest daily peaks, with a new read endpoint. Two new
migrations, one per story: `AddExemptionsAndNorgesPrisCap` (story 6, also widens `PriceRate.Kind` to 40 characters)
and one for the capacity steps (story 7).

## Technical Context

**Language/Version**: C# 14 / .NET 10, ASP.NET Core 10

**Primary Dependencies**: FastEndpoints, FluentValidation, EF Core (Npgsql), custom CQRS with `Cqrs.SourceGenerator`. No new packages.

**Storage**: PostgreSQL (schema `public`). New tables `PriceRate`, `GridTariff`, `GridTariffRate`; two new columns on `Location`. One EF Core migration with seed rows.

**Testing**: xUnit + NSubstitute in `tests/Features.Tests`, the same helpers as spec 007 (`FixedTimeProvider`, in-memory fakes, `EndpointHost` with a caller option). Hand-calculated examples are the main test input (SC-001).

**Target Platform**: Linux container (Hetzner), as today.

**Project Type**: web-service (API). No frontend change.

**Performance Goals**: a month of hourly costs does one query for the dated rates and one query each for the spot prices and the exchange rates of the range, not one per hour (FR-017, SC-006).

**Constraints**: Oslo time decides day or night, weekday, holidays and which dated value applies. Amounts are `decimal`, never rounded in the calculation. A missing price part is unavailable, never zero.

**Scale/Scope**: a handful of dated rows per kind. 3 new tables, 2 location columns, about 10 endpoints, one calculator change.
Addendum: 1 new table (`GridCapacityStep`), 3 location columns, 2 new `PriceRateKind` values, 1 changed endpoint
(location pricing), 3 new endpoints (capacity steps list/add/delete) and 1 new read endpoint (capacity charge).

**Addendum performance**: the cap needs the kWh used earlier in the month. `HourlyCostProvider` reads the hourly
consumption from the Oslo start of the month of the range (one query, as today, with an earlier start); the current-hour
and day-price handlers get that sum from one new aggregate query. The capacity charge reads one month of hourly
consumption (at most 744 rows) once.

## Constitution Check

*GATE: passes before Phase 0 and after Phase 1.*

| Principle | Status |
|---|---|
| I. Clean Architecture layering | Pass. Entities in Domain; the pure price formula, the grid period rule, the holiday calendar, and the repository interfaces in Application; EF repositories in Infrastructure; handlers and endpoints in Features. |
| II. Vertical slices | Pass. A new slice `Features/Pricing` owns the Admin endpoints; the saving query lives in the existing `Features/ElectricityCost` slice. The dated-rate rules are shared inside `Pricing` only; the Norgespris slice is left untouched and the same rules are not imported from it. The Locations slice is not changed: location pricing settings use their own endpoint in `Pricing`. |
| III. Result over exceptions | Pass. Duplicate date, not-newest, confirmation required, already started, not found, tariff name taken are `Result` errors. |
| IV. Validate once | Pass. Format rules (non-negative, 4 decimals, VAT 0..1) in FluentValidation validators; date-dependent rules need stored data and live in the handlers, as in spec 007. |
| V. Real CQRS, compile-time dispatch | Pass. Commands mutate, queries read (the saving query reads only). Handlers are found by the generator. |
| VI. Structured logging | Pass. `LogMessages` for `Pricing` with a new range 1700-1799, added to `CLAUDE.md` and `DevelopmentGuide.md`. |
| VII. Manual mapping | Pass. Static mappers per slice. |
| VIII. Minimal footprint | Pass. One generic `PriceRate` table for the three national values instead of three tables; no tariff delete or rename; no cache beyond a lazily loaded field on the scoped calculator; phase 2 items (monthly fees, capacity, cap) are not built. |
| Migrations | Pass. One migration `AddFullElectricityPrice`; never edited after it has been applied. The addendum adds new migrations (one per story) instead of editing the applied one. |
| Addendum (stories 6-7) | Pass. Flags on `Location` (Domain); the cap split and the capacity calculation are pure classes in Application; the step tables live with the grid tariff in `Pricing`; the capacity read endpoint sits in `ElectricityCost` next to the saving. Cap values reuse the `PriceRate` table and its endpoints (two new kinds, no new table). Capacity steps are a new table because a step table has several rows per date. No new packages. |

No violations; nothing to justify.

## Project Structure

### Documentation (this feature)

```text
specs/008-full-electricity-price/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/endpoints.md
└── tasks.md             # created by /speckit-tasks
```

### Source Code (repository root)

```text
src/
├── Domain/Common/Entities/
│   ├── PriceRate.cs                         # NEW: national dated value (kind, valid from, value)
│   ├── GridTariff.cs                        # NEW: name
│   ├── GridTariffRate.cs                    # NEW: dated day and night rate of a tariff
│   └── Location.cs                          # CHANGED: GridTariffId (nullable), SupplierMarkupPerKwh, SetPricing(...)
├── Application/
│   ├── Common/Interfaces/Repositories/
│   │   ├── IPriceRateRepository.cs          # NEW: dated rates and tariffs (reads for the calculator, writes for Pricing)
│   │   └── IExchangeRateRepository.cs       # CHANGED: GetByDateRangeAsync
│   └── ElectricityCost/
│       ├── PriceCalculation.cs              # NEW: pure formula and PriceBreakdown
│       ├── GridPeriod.cs                    # NEW: day or night for an hour (Oslo time, weekday, holiday)
│       ├── NorwegianHolidays.cs             # NEW: public holidays computed from the calendar
│       ├── PriceRateBook.cs                 # NEW: the dated values loaded once, lookup by hour
│       └── CostCalculator.cs                # CHANGED: uses the rate book, adds the breakdown, preloads price ranges
├── Infrastructure/
│   ├── Database/Configuration/{PriceRate,GridTariff,GridTariffRate}Configuration.cs, LocationConfiguration.cs
│   ├── Database/Repositories/{PriceRateEfRepository,ExchangeRateEfRepository}.cs
│   ├── Database/Migrations/<ts>_AddFullElectricityPrice.cs (+Designer, Snapshot)
│   └── AddInfrastructureToDI.cs
└── Features/
    ├── Pricing/                             # NEW slice (Admin only)
    │   ├── Commands/ Queries/ Handlers/ Endpoints/ Validators/ Mappers/ Logging/
    └── ElectricityCost/
        ├── Queries/GetNorgesPrisSavingQuery.cs, Handlers/GetNorgesPrisSavingQueryHandler.cs, Endpoints/GetNorgesPrisSavingEndpoint.cs
        ├── Mappers/CostMapper.cs            # CHANGED: parts in the hourly and current-hour responses
        ├── Queries/{GetHourlyCostQuery,GetCurrentHourCostQuery}.cs   # CHANGED: additive parts field
        └── Services/HourlyCostProvider.cs   # CHANGED: preload the range before pricing the hours

tests/Features.Tests/
├── ElectricityCost/                         # CHANGED: existing calculator tests keep their numbers via a zero-tax, zero-VAT stub
└── Pricing/                                 # NEW: formula, grid period, holidays, dated rules, handlers, endpoints, saving

CLAUDE.md, DevelopmentGuide.md               # CHANGED: EventId table (1700-1799 Pricing)
```

**Structure Decision**: the existing solution; a new `Features/Pricing` folder inside `Features.csproj`.

### Addendum source changes (stories 6 and 7)

```text
src/
├── Domain/Common/
│   ├── PriceRateKind.cs                     # CHANGED: + NorgesPrisCapHome, NorgesPrisCapHolidayHome (kWh per month)
│   └── Entities/
│       ├── Location.cs                      # CHANGED: ElectricityTaxExempt, VatExempt, IsHolidayHome; SetPricing(...) takes them
│       └── GridCapacityStep.cs              # NEW: tariff, valid from, lower kW, upper kW (null = top), monthly amount excl. VAT
├── Application/
│   ├── Common/Interfaces/Repositories/
│   │   ├── IPriceRateRepository.cs          # CHANGED: capacity steps read/insert/delete
│   │   └── IConsumptionRepository.cs        # CHANGED: GetConsumptionKwhAsync(locationId, fromUtc, toUtc) (sum)
│   └── ElectricityCost/
│       ├── NorgesPrisCap.cs                 # NEW: pure split of an hour's kWh into within / above the cap
│       ├── CapacityCharge.cs                # NEW: pure daily peaks -> top 3 -> average -> step -> amount
│       ├── PriceRateBook.cs                 # CHANGED: ValueOn(kind, DateOnly) for the cap on the 1st of the month
│       └── CostCalculator.cs                # CHANGED: exemptions; monthKwhBefore parameter; RateCost.CapSplit
├── Infrastructure/
│   ├── Database/Configuration/{GridCapacityStep,Location}Configuration.cs
│   ├── Database/Repositories/{PriceRateEfRepository,ConsumptionRepository}.cs
│   └── Database/Migrations/<ts>_AddExemptionsAndNorgesPrisCap.cs, <ts>_<story 7>.cs (+Designer, Snapshot)
└── Features/
    ├── Pricing/                             # CHANGED: location pricing flags; capacity-step endpoints; cap kinds in validator
    └── ElectricityCost/
        ├── Services/HourlyCostProvider.cs   # CHANGED: reads from the month start, passes the running month kWh
        ├── Handlers/{GetCurrentHourCost,GetDayPrices}QueryHandler.cs   # CHANGED: month kWh before the hour
        ├── Mappers/CostMapper.cs            # CHANGED: additive cap object
        └── {Queries,Handlers,Endpoints}/GetCapacityCharge*.cs          # NEW (phase 2)

tests/Features.Tests/
├── ElectricityCost/                         # NorgesPrisCap, CapacityCharge, exemptions, Elvia bill October 2026
└── Pricing/                                 # flags, cap kinds, capacity-step rules and endpoints
```

## Complexity Tracking

No constitution violations.
