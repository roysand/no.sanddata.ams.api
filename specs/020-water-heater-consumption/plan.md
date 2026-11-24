# Implementation Plan: Hot Water Used per Hour, Day and Month

**Branch**: `feature/020-water-heater-consumption` | **Date**: 2026-11-24 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/020-water-heater-consumption/spec.md`

## Summary

The feature has two parts. The first starts collecting the data; the second answers the chart.

1. **Store the litres (story 1, ships first).**
   - The minute sampler reads OSO's `Status` next to `ActualLoad`, in parallel.
   - It stores `availableMix40Volume` in a new nullable column `AvailableLitres40` on `WaterHeaterPowerSample`.
   - If the litres can't be read, the power is still stored, and a rate-limited log line says why (research R1–R3).
   - One migration, with no backfill.
2. **Answer the water used (stories 2 and 3).** A new read-only endpoint:
   `GET /api/locations/{locationId}/water-heater/hot-water?period=day|week|month|year&date=yyyy-MM-dd`.
   - **SQL:** one new raw SQL query pairs consecutive samples (`LAG`, at most 2 minutes apart, both with litres). Each
     pair counts `fall + heated` when the litres fell by at least `MinFallLitres`, and 0 otherwise. `heated` is the
     heater's kWh in the pair ÷ `KwhPerLitre`: the power correction. It sums per UTC hour of the later sample
     (research R4).
   - **Handler:** it rolls the hours up into the period's parts (hours, Oslo days or Oslo months). It gives `null` for
     parts without sampled time, rounds each part to whole litres, and totals the rounded parts (research R7).
   - **Access:** the same rule as spec 017 (research R8).
3. **Calibration between the two.** With 3–5 days of stored litres, the starting values (0.035 kWh/L and a minimum
   fall of 2 L) and the pairing are checked against the OSO app's day totals. The results go into research.md
   (research R5–R6, quickstart §4).

## Technical Context

**Language/Version**: C# / .NET 10

**Primary Dependencies**:
- FastEndpoints, FluentValidation;
- EF Core (Npgsql), with raw SQL through `Database.SqlQuery<T>`;
- the existing typed `HttpClient` for OSO.

**Storage**: PostgreSQL (TimescaleDB image, PG16 locally, PG17 in production).
- One new nullable `integer` column, `WaterHeaterPowerSample.AvailableLitres40`, added by migration
  `AddWaterHeaterLitres`.
- It reads through the existing index `(LocationId, SampledAtUtc)`.

**Testing**: xUnit + NSubstitute in `tests/Features.Tests`:
- client tests with the existing `OsoStubHandler`;
- sampler tests;
- pure tests for the period helper;
- handler tests with substituted repositories;
- HTTP tests through `EndpointHost`.

The SQL has no database test harness, as in specs 012 and 017. Quickstart §3 checks it with seeded samples.

**Target Platform**: Linux container on the Hetzner VPS behind Caddy

**Project Type**: web service (REST API)

**Performance Goals**: a day in < 300 ms p95 and a year in < 1 s p95 in production (SC-004).

**Constraints**:
- No existing endpoint or field changes (FR-010).
- A litres failure never costs a power sample (FR-001).
- OSO gets one extra call per heater per minute. OSO states no rate limit (spec 011).

**Scale/Scope**:
- About 1,440 samples a day and 525,000 a year per heater; at most 8,784 hourly rows from SQL.
- The changes:
  - 1 column and 1 migration;
  - 1 client method (replacing `GetActualLoadAsync`);
  - 1 sampler change and 3 log messages;
  - 1 query, 1 repository method, 1 endpoint, 1 validator, 1 handler and 1 period helper;
  - 1 rule constant class and the mapper additions.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design.*

| Principle | How this plan meets it | Status |
|---|---|---|
| I. Clean layering | The column is on the Domain entity, with no EF attributes; its EF config is in Infrastructure. `WaterHeaterSample`, `HotWaterRuleValues` and `WaterHeaterHotWaterHour` are plain records in Application. The OSO call and the SQL stay in Infrastructure. | ✅ |
| II. Vertical slices | Everything new is in `Features/WaterHeaters`: endpoint, query, handler, validator, mapper, `HotWaterPeriods` and `HotWaterRule`. Oslo time uses the feature's own few lines, in the style of `CostTime`, not a reach into `ElectricityCost`. The access check is copied from spec 017's handler. | ✅ |
| III. Result over exceptions | The client returns `Result<WaterHeaterSample>`, with the litres error as a value. The handler returns `Result<WaterHeaterHotWaterResponse>`, and not found is `Error.NotFound("Location.NotFound", …)`. | ✅ |
| IV. Validate once | `GetWaterHeaterHotWaterValidator` refuses an unknown `period` and a bad `date`. The handler only fills in the defaults (research R7). | ✅ |
| V. CQRS, compile-time dispatch | `GetWaterHeaterHotWaterQuery` reads only. The sampler is a hosted service, not a command, as in spec 011. The source generator picks up the handler. | ✅ |
| VI. Structured logging | Three new `LoggerMessage` delegates, EventIds 1834–1836 inside WaterHeater's 1800–1899. They carry the location id and a reason code, never the device id or key. Refused access uses the existing 1805. | ✅ |
| VII. Manual mapping | `WaterHeaterMapper` gets the roll-up and rounding to items. No automapper. | ✅ |
| VIII. Minimal footprint | No rollup table, no cache, no new index, no config flag: the rule's numbers are a C# constant, changed by calibration. The entity isn't renamed. `GetActualLoadAsync` is replaced, not kept beside the new method. | ✅ |
| Stack: raw SQL behind EF Core | One more raw SQL query in the repository that already holds spec 012's. It's needed for `LAG` over up to a year of samples (research R4). | ⚠️ justified below |
| Workflow: migrations | One migration through `dotnet ef migrations add`, committed with the model snapshot. | ✅ |
| Workflow: format and build | `dotnet format --verify-no-changes` and `dotnet build` with no new warnings, before the work is done. | ✅ |

**Post-design re-check**: the design artifacts (research, data model, contract, quickstart) add no other violations.
The gate passes.

## Project Structure

### Documentation (this feature)

```text
specs/020-water-heater-consumption/
├── spec.md
├── plan.md              # this file
├── research.md          # R1–R9, and the calibration results once checked
├── data-model.md
├── quickstart.md
├── contracts/
│   └── endpoints.md
├── checklists/
│   └── requirements.md
└── tasks.md             # from /speckit-tasks (not created here)
```

### Source Code (repository root)

```text
src/Domain/Common/Entities/
└── WaterHeaterPowerSample.cs                       # + AvailableLitres40 (int?), optional ctor argument

src/Application/
├── WaterHeaters/WaterHeaterSample.cs               # new: PowerKw, Litres40, LitresError
├── WaterHeaters/WaterHeaterHotWater.cs             # new: HotWaterRuleValues, WaterHeaterHotWaterHour
├── Common/Interfaces/External/IWaterHeaterClient.cs            # GetActualLoadAsync → GetSampleAsync
└── Common/Interfaces/Repositories/IWaterHeaterPowerSampleRepository.cs   # + GetHourlyHotWaterAsync

src/Infrastructure/
├── External/OsoWaterHeaterClient.cs                # GetSampleAsync: ActualLoad + Status in parallel
├── Database/Configuration/WaterHeaterPowerSampleConfiguration.cs        # + AvailableLitres40 (optional)
├── Database/Repositories/WaterHeaterPowerSampleEfRepository.cs          # + hot water SQL (LAG pairs, per UTC hour)
└── Database/Migrations/<timestamp>_AddWaterHeaterLitres.cs              # new

src/Features/WaterHeaters/
├── Services/WaterHeaterSamplingService.cs          # stores litres; second tracker for litres failures
├── Services/HotWaterRule.cs                        # new: Current = (0.035 kWh/L, 2 L)
├── Services/HotWaterPeriods.cs                     # new: period → [from, to) UTC and part starts (Oslo)
├── Logging/LogMessages.cs                          # + 1834–1836
├── Endpoints/GetWaterHeaterHotWaterEndpoint.cs     # new: GET …/water-heater/hot-water, request record
├── Queries/WaterHeaterQueries.cs                   # + query, HotWaterPeriod, response and item records
├── Handlers/GetWaterHeaterHotWaterQueryHandler.cs  # new: access, period, repository, roll-up
├── Validators/GetWaterHeaterHotWaterValidator.cs   # new: period and date
└── Mappers/WaterHeaterMapper.cs                    # + roll-up of hours into items, rounding

tests/Features.Tests/
├── WaterHeaters/OsoWaterHeaterClientTests.cs       # GetSampleAsync cases
├── WaterHeaters/WaterHeaterSamplingServiceTests.cs # litres stored, litres failure keeps power, litres log
├── WaterHeaters/NoSecretsInLogsTests.cs            # calls GetSampleAsync instead
├── WaterHeaters/HotWaterPeriodsTests.cs            # new
├── WaterHeaters/GetWaterHeaterHotWaterHandlerTests.cs  # new
└── Http/WaterHeaterHotWaterHttpTests.cs            # new
```

**Structure Decision**: The existing Clean Architecture projects and the `Features/WaterHeaters` slice from specs
011, 012 and 017. There is no new project.

**Delivery order**: story 1 (column, migration, client, sampler, logs) goes in its own PR, so that it can be deployed
and start collecting. Stories 2 and 3 follow in a second PR. After the calibration (quickstart §4), the second PR
updates `HotWaterRule` and research.md, and the frontend backlog note in `specs/_backlog/` comes last.

## Complexity Tracking

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| A second raw SQL query in `WaterHeaterPowerSampleEfRepository` | Each sample has to be paired with the one before it (`LAG`), which LINQ to EF can't express. A year is about 525,000 samples, too many to load for SC-004 | Loading the samples and pairing them in C# is fine for a day but too slow for a year. A stored rollup adds a table, a job and a backfill. Reusing spec 017's `QueryStepsAsync` would mix "power over time" with "change between samples" in the repo's hardest query |
