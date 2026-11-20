# Implementation Plan: The Water Heater's kWh and Cost, Split from the Rest

**Branch**: `feature/012-water-heater-energy` | **Date**: 2026-11-19 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/012-water-heater-energy/spec.md`

## Summary

The heater's energy per hour is computed on read from the power samples spec 011 stores, and added as an optional
`waterHeater` part to the figures the app already reads. Nothing new is stored and no endpoint is added.

1. **Energy per hour (FR-001).** A new repository method,
   `IWaterHeaterPowerSampleRepository.GetHourlyEnergyAsync(locationId, fromUtc, toUtc, nowUtc)`, returns one row per
   UTC hour: kWh and sampled seconds. It is one SQL query. Each sample holds until the next one, for at most 2
   minutes and never past `now`, and a hold that crosses an hour boundary is split between the two hours (research
   R1). That's at most 8,760 rows a year instead of 525,000 samples (research R2).
2. **Attach to the hours (FR-002–FR-004).** `HourlyCostProvider.GetAsync` gets a `withWaterHeater` flag. When it's
   set, the provider makes that one extra query and gives each `HourCost` a `WaterHeaterHour`: kWh capped at the
   hour's kWh, cost = the hour's actual cost × heater kWh / hour kWh, sampled minutes and a capped flag. The pure
   `WaterHeaterShare.ForHour` does the math (research R3). Hours without samples get `null`.
3. **Days and the bill (FR-005, FR-006).** `CostMapper.ToDailyResponse` and the bill sum the hours' parts with the
   pure `WaterHeaterShare.Sum`. As with a day's own cost, the cost is `null` when a sampled hour has no price. The
   bill uses only the hours so far (`soFar`), never the projection, and changes no amount.
4. **Contract (FR-008).** `waterHeater` is added to `HourlyCostItemResponse`, `DailyCostItemResponse` and
   `MonthlyBillResponse`, and is `null` when there are no samples. No existing field changes
   ([contracts/endpoints.md](contracts/endpoints.md)).

Only the hourly, daily and bill handlers pass `withWaterHeater: true`. The saving handler, the only other user of the
provider, keeps its current query count.

## Technical Context

**Language/Version**: C# 14 / .NET 10, ASP.NET Core 10

**Primary Dependencies**: FastEndpoints, EF Core (Npgsql) with one raw SQL query through `Database.SqlQuery<T>`
(research R2), custom CQRS with `Cqrs.SourceGenerator`. **No new packages.**

**Storage**: PostgreSQL (TimescaleDB image). No new table, no migration. It reads `WaterHeaterPowerSample` through
its existing `(LocationId, SampledAtUtc)` index.

**Testing**: xUnit + NSubstitute in `tests/Features.Tests`:
- `WaterHeaterShare` gets pure tests: share, cap, null cost, day and month sums.
- The cost handler tests get a substituted sample repository: an hourly, daily and bill response with and without
  heater rows, checked for null versus zero.
- The SQL itself has no database test harness in this repo, as with the consumption aggregates. It is checked by
  hand against Hjemme's real samples (quickstart §2), including the 2-minute hold and the split at hour boundaries.

**Target Platform**: a Linux container on a Hetzner VPS (`compose.prod.yaml`), as before.

**Project Type**: web-service (API). The frontend follows in its own spec 010.

**Performance Goals**: a year of daily figures takes at most 1,5 × the current time (SC-004). Expect one indexed
range scan of about 525,000 samples a year per location, with one window function, measured in quickstart §3.

**Constraints**:
- Existing response fields don't change (FR-008).
- Water heating plus the rest equals the total, to the øre and to 0,001 kWh (SC-001). So the "rest" is never
  computed in the API: it is total − heater on the client, and the heater's kWh and cost never exceed the total.

**Scale/Scope**: one household, one heater. The work is:
- 1 repository method and its SQL, 1 result record and 1 pure calculator in Application
- 1 flag on `HourlyCostProvider`, and `HourCost` gains one property
- 3 response records gain one property, plus 2 mapper changes and 3 handler call sites.

## Constitution Check

_GATE: Must pass before Phase 0 research. Re-check after Phase 1 design._

| Principle | How the plan meets it | Status |
|---|---|---|
| I. Clean layering | The SQL lives in `Infrastructure/Database/Repositories/WaterHeaterPowerSampleEfRepository`. The interface method and the `WaterHeaterHourEnergy` record are in Application. The pure `WaterHeaterShare` is in Application/WaterHeaters, with no EF and no HTTP. | ✅ |
| II. Vertical slices | `ElectricityCost` reads samples only through the Application repository interface, the same way it reads consumption. Nothing from `Features/WaterHeaters` is used. | ✅ |
| III. Results over exceptions | No new failure paths. Missing samples are `null`, not errors. | ✅ |
| IV. Validate at the boundary | No new inputs: the existing hourly, daily and bill validators still apply. | ✅ |
| V. CQRS | Queries only; no handler added. Existing handlers gain one call. | ✅ |
| VI. Logging | No new log events, and no new EventIds in range 1500–1599. | ✅ |
| VII. Manual mapping | `CostMapper` and `BillMapper` map the new part by hand. | ✅ |
| VIII. Minimal footprint | No new table, job, endpoint or setting. The `withWaterHeater` flag exists only so the saving handler doesn't pay for a query it doesn't use. | ✅ |
| Stack: "provider choice kept cheap to change by staying behind EF Core" | **Deviation, justified:** the first raw SQL in the repo (`SqlQuery<T>` with `LEAD` and `date_trunc(..., 'UTC')`). The hold-until-next-sample rule needs the next row, which LINQ to EF can't express. Loading the samples into memory would move up to about 1 million rows for the year view, which compares two years. The database is already PostgreSQL-specific through the TimescaleDB continuous aggregates. The SQL stays in one repository method, behind the interface. | ⚠️ justified |

**Re-check after Phase 1**: unchanged. The one deviation is recorded in Complexity Tracking.

## Project Structure

### Documentation (this feature)

```text
specs/012-water-heater-energy/
├── plan.md              # This file
├── research.md          # Phase 0: R1–R6
├── data-model.md        # Phase 1: records and rules
├── quickstart.md        # Phase 1: how to check it
├── contracts/
│   └── endpoints.md     # Phase 1: the added fields
├── checklists/
│   └── requirements.md
└── tasks.md             # Phase 2 (/speckit-tasks)
```

### Source Code (repository root)

```text
src/Application/
├── Common/Interfaces/Repositories/IWaterHeaterPowerSampleRepository.cs   # + GetHourlyEnergyAsync
└── WaterHeaters/
    ├── WaterHeaterHourEnergy.cs          # new: (HourStartUtc, Kwh, SampledSeconds) from the query
    └── WaterHeaterShare.cs               # new: ForHour (cap, share of cost), Sum (days, month)

src/Infrastructure/Database/Repositories/
└── WaterHeaterPowerSampleEfRepository.cs # + GetHourlyEnergyAsync (raw SQL, research R1/R2)

src/Features/ElectricityCost/
├── Services/HourlyCostProvider.cs        # + withWaterHeater; HourCost.WaterHeater
├── Queries/GetHourlyCostQuery.cs         # + WaterHeaterHourResponse on HourlyCostItemResponse
├── Queries/GetDailyCostQuery.cs          # + WaterHeaterDayResponse on DailyCostItemResponse
├── Queries/GetMonthlyBillQuery.cs        # + WaterHeaterBillResponse on MonthlyBillResponse
├── Mappers/CostMapper.cs                 # map hour and day parts
├── Mappers/BillMapper.cs                 # map the month part
└── Handlers/GetHourlyCostQueryHandler.cs, GetDailyCostQueryHandler.cs, GetMonthlyBillQueryHandler.cs
                                          # pass withWaterHeater: true

tests/Features.Tests/
├── WaterHeaters/WaterHeaterShareTests.cs # new
└── ElectricityCost/CostHandlerTests.cs, MonthlyBillHandlerTests.cs   # + heater cases
```

**Structure Decision**: Extend the existing ElectricityCost slice and the Application water-heater types. The split is
a property of the cost figures, not a new feature with endpoints of its own.

## Complexity Tracking

| Violation | Why Needed | Simpler Alternative Rejected Because |
|---|---|---|
| Raw SQL (PostgreSQL `LEAD`, `date_trunc(…, 'UTC')`) in one repository method | The hold rule needs each sample's next sample, and a year view must not load about 1 M rows | LINQ can't express `LEAD`. Loading the samples into memory is too slow for the year view. A stored hourly rollup (research R2, alternative) adds a table, a job and a backfill. |
