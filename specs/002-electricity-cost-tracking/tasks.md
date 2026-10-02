---

description: "Task list for Electricity Cost Tracking"
---

# Tasks: Electricity Cost Tracking

**Input**: Design documents from `/specs/002-electricity-cost-tracking/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/endpoints.md, quickstart.md

**Tests**: Not included — same convention as 001 (research.md documents no automated test project
exists in this repo); validation is the manual `quickstart.md` run in the Polish phase.

**Organization**: Tasks are grouped by user story (spec.md) to enable independent implementation
and testing of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no unmet dependencies)
- **[Story]**: Maps the task to US1/US2/US3/US4 from spec.md
- Paths are relative to the repository root

## Phase 1: Setup

- [X] T001 Confirm no new NuGet packages are needed — ENTSO-E XML parsing uses `System.Xml.Linq`, Norges Bank SDMX-JSON parsing uses `System.Text.Json`, both already in the BCL (verify only, no edit expected)

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: TimescaleDB activation, new entities/read-models, repositories, external API
clients, the background fetch job, and the shared cost calculator every user story depends on.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [X] T002 [P] Create `ElectricityPrice` domain entity (`PriceRegion`, `HourStartUtc`, `PriceEurPerMwh`) in `src/Domain/Common/Entities/ElectricityPrice.cs`
- [X] T003 [P] Create `ExchangeRate` domain entity (`CurrencyPair`, `RateDate`, `Rate`) in `src/Domain/Common/Entities/ExchangeRate.cs`
- [X] T004 [P] Create `ElectricityPriceConfiguration` (unique index on `PriceRegion`+`HourStartUtc`) in `src/Infrastructure/Database/Configuration/ElectricityPriceConfiguration.cs` (depends on T002)
- [X] T005 [P] Create `ExchangeRateConfiguration` (unique index on `CurrencyPair`+`RateDate`) in `src/Infrastructure/Database/Configuration/ExchangeRateConfiguration.cs` (depends on T003)
- [X] T006 Add `DbSet<ElectricityPrice>` and `DbSet<ExchangeRate>` to `ApplicationDbContext` in `src/Infrastructure/Database/ApplicationDbContext.cs` (depends on T002, T003)
- [X] T007 [P] Create keyless read-model `MinuteConsumption` (`LocationId`, `MeterId`, `BucketStart`, `AvgPowerWatts`) in `src/Infrastructure/Database/ReadModels/MinuteConsumption.cs`
- [X] T008 [P] Create keyless read-model `HourConsumption` (same shape) in `src/Infrastructure/Database/ReadModels/HourConsumption.cs`
- [X] T009 Map `MinuteConsumption`→`measurement_minute` and `HourConsumption`→`measurement_hour` as keyless entities (`.ToView(...).HasNoKey()`) in `ApplicationDbContext.OnModelCreating` in `src/Infrastructure/Database/ApplicationDbContext.cs` (depends on T006, T007, T008)
- [X] T010 Create the EF Core migration that enables the `timescaledb` extension, converts `Measurement` into a hypertable (`migrate_data => true`), and creates the `measurement_minute`/`measurement_hour` continuous aggregates plus their refresh policies — all via `migrationBuilder.Sql(...)` per research.md §1-2 — in `src/Infrastructure/Database/Migrations/<timestamp>_AddElectricityCostTracking.cs` (depends on T009)
- [X] T011 [P] Add `IElectricityPriceRepository<T> : IRepository<T>` (`GetByRegionAndHourAsync`, `GetByRegionAndHourRangeAsync`) in `src/Application/Common/Interfaces/Repositories/IElectricityPriceRepository.cs` (depends on T002)
- [X] T012 [P] Add `IExchangeRateRepository<T> : IRepository<T>` (`GetByDateAsync`) in `src/Application/Common/Interfaces/Repositories/IExchangeRateRepository.cs` (depends on T003)
- [X] T013 [P] Add `IConsumptionRepository` — standalone interface, not `IRepository<T>`, since these are read-only queries over materialized views with no CRUD operations — (`GetMinuteAsync`, `GetHourlyAsync`, `GetHourConsumptionKwhAsync(locationId, hourStart)`) in `src/Application/Common/Interfaces/Repositories/IConsumptionRepository.cs` (depends on T007, T008)
- [X] T014 [P] Implement `ElectricityPriceEfRepository` in `src/Infrastructure/Database/Repositories/ElectricityPriceEfRepository.cs` (depends on T011)
- [X] T015 [P] Implement `ExchangeRateEfRepository` in `src/Infrastructure/Database/Repositories/ExchangeRateEfRepository.cs` (depends on T012)
- [X] T016 [P] Implement `ConsumptionEfRepository`, querying the views mapped in T009, computing `ConsumptionKwh` as `AvgPowerWatts / 60_000` (minute) or `/ 1_000` (hour) per data-model.md, in `src/Infrastructure/Database/Repositories/ConsumptionEfRepository.cs` (depends on T013, T009)
- [X] T017 Register `IElectricityPriceRepository<ElectricityPrice>`, `IExchangeRateRepository<ExchangeRate>`, and `IConsumptionRepository` in `src/Infrastructure/AddInfrastructureToDI.cs` (depends on T014, T015, T016)
- [X] T018 [P] Add `ISpotPriceClient` (`GetPricesAsync(string priceRegion, DateTime fromUtc, DateTime toUtc, CancellationToken)`) in `src/Application/Common/Interfaces/External/ISpotPriceClient.cs`
- [X] T019 [P] Add `IExchangeRateClient` (`GetRateAsync(DateOnly date, CancellationToken)`) in `src/Application/Common/Interfaces/External/IExchangeRateClient.cs`
- [X] T020 Implement `EntsoeSpotPriceClient` — `documentType=A44`, `processType=A01`, the static NO1-NO5 EIC code table from research.md §4, XML parsing via `System.Xml.Linq` — in `src/Infrastructure/External/EntsoeSpotPriceClient.cs` (depends on T018)
- [X] T021 Implement `NorgesBankExchangeRateClient` — queries `data.norges-bank.no/api/data/EXR/B.EUR.NOK.SP` per research.md §5, parses SDMX-JSON via `System.Text.Json` — in `src/Infrastructure/External/NorgesBankExchangeRateClient.cs` (depends on T019)
- [X] T022 Add `EntsoE:SecurityToken` (secret — document in `local.settings.json`, never committed), `NorgesPris:RatePerKwh`, `NorgesPris:TaxPerKwh`, and `PriceFetch:IntervalHours` config keys and bind them as typed options in `src/api/Program.cs` / `src/api/appsettings.json`
- [X] T023 Register `EntsoeSpotPriceClient` and `NorgesBankExchangeRateClient` as typed `HttpClient`s (`AddHttpClient<TInterface, TImpl>`) in `src/Infrastructure/AddInfrastructureToDI.cs` (depends on T020, T021)
- [X] T024 Create `ICostCalculator`/`CostCalculator` that computes a location's **actual** rate/cost for an hour — flat `NorgesPris` config rate if `Location.HasNorgesPriceAgreement`, otherwise `ElectricityPrice`×`ExchangeRate` for that location's `Zone` — leaving the comparison model unimplemented for now (added in US4), in `src/Application/ElectricityCost/CostCalculator.cs` (depends on T011, T012, T017, T022)
- [X] T025 Create `PriceFetchService : BackgroundService` — loop on `PriceFetch:IntervalHours`, idempotently upsert tomorrow's + today's prices for every distinct `Location.Zone` in use via `ISpotPriceClient`, and today's rate via `IExchangeRateClient`, per research.md §6 — in `src/Features/ElectricityCost/Services/PriceFetchService.cs` (depends on T020, T021, T014, T015)
- [X] T026 Register `PriceFetchService` via `builder.Services.AddHostedService<PriceFetchService>()` in `src/api/Program.cs` (depends on T025)

**Checkpoint**: Foundational infrastructure ready — user story implementation can begin.

---

## Phase 3: User Story 1 - See cost accrued so far this hour (Priority: P1) 🎯 MVP

**Goal**: A user can see a live, continuously-accurate cost figure for the current, still-open
hour.

**Independent Test**: Check the current-hour cost figure at two points within the same hour and
confirm it reflects consumption received since the top of the hour (via `quickstart.md` US1).

### Implementation for User Story 1

- [X] T027 [P] [US1] Create `GetCurrentHourCostQuery` + response records in `src/Features/ElectricityCost/Queries/GetCurrentHourCostQuery.cs`
- [X] T028 [US1] Implement `GetCurrentHourCostQueryHandler` — authorize via `ILocationRepository.IsUserAssociatedAsync` (→ `Location.NotFound`), get current-hour consumption via `IConsumptionRepository.GetHourConsumptionKwhAsync`, compute actual cost via `ICostCalculator` — in `src/Features/ElectricityCost/Handlers/GetCurrentHourCostQueryHandler.cs` (depends on T024, T016, T027)
- [X] T029 [P] [US1] Create `src/Features/ElectricityCost/Mappers/CostMapper.cs` with `ToCurrentHourResponse(...)`
- [X] T030 [US1] Create `GetCurrentHourCostEndpoint` + request DTO (`GET /api/electricity-cost/current`, JWT auth, `Tags("ElectricityCost")` + `Description(b => b.WithTags("ElectricityCost"))` per the established Scalar-visibility fix) in `src/Features/ElectricityCost/Endpoints/GetCurrentHourCostEndpoint.cs` (depends on T028, T029)
- [X] T031 [US1] Create `src/Features/ElectricityCost/Logging/LogMessages.cs` claiming EventId range `1500-1599` (e.g. `CostQueried` = 1500, `LocationAccessDenied` = 1501) and call it from the handler (depends on T028)

**Checkpoint**: User Story 1 is fully functional and independently testable via `quickstart.md`'s US1 scenario.

---

## Phase 4: User Story 2 - Browse historical cost by hour and by day (Priority: P2)

**Goal**: A user can see completed hourly and daily cost history, with daily always equal to the
sum of its 24 hourly figures.

**Independent Test**: Request cost for a specific completed hour and the containing day, confirm
the day total equals the sum of that day's hourly figures (via `quickstart.md` US2).

### Implementation for User Story 2

- [X] T032 [P] [US2] Create `GetHourlyCostQuery` + response records in `src/Features/ElectricityCost/Queries/GetHourlyCostQuery.cs`
- [X] T033 [P] [US2] Create `GetDailyCostQuery` + response records in `src/Features/ElectricityCost/Queries/GetDailyCostQuery.cs`
- [X] T034 [US2] Implement `GetHourlyCostQueryHandler` — authorize, paged hour consumption via `IConsumptionRepository.GetHourlyAsync`, per-hour cost via `ICostCalculator` — in `src/Features/ElectricityCost/Handlers/GetHourlyCostQueryHandler.cs` (depends on T024, T032)
- [X] T035 [US2] Implement `GetDailyCostQueryHandler` — sums the hourly results for each day in range (reuses the hourly path per FR-004, not an independent calculation) — in `src/Features/ElectricityCost/Handlers/GetDailyCostQueryHandler.cs` (depends on T034, T033)
- [X] T036 [P] [US2] Extend `CostMapper` with `ToHourlyResponse(...)`/`ToDailyResponse(...)` (depends on T029)
- [X] T037 [US2] Create `GetHourlyCostEndpoint` + request DTO (`GET /api/electricity-cost/hourly`) in `src/Features/ElectricityCost/Endpoints/GetHourlyCostEndpoint.cs` (depends on T034, T036)
- [X] T038 [US2] Create `GetDailyCostEndpoint` + request DTO (`GET /api/electricity-cost/daily`) in `src/Features/ElectricityCost/Endpoints/GetDailyCostEndpoint.cs` (depends on T035, T036)
- [X] T039 [US2] Create `GetHourlyCostValidator : Validator<GetHourlyCostRequest>` (page/pageSize bounds, `to >= from`) — **must** target the Request DTO, not the query record — in `src/Features/ElectricityCost/Validators/GetHourlyCostValidator.cs` (depends on T037)
- [X] T040 [P] [US2] Create `GetDailyCostValidator : Validator<GetDailyCostRequest>` (`to >= from`) in `src/Features/ElectricityCost/Validators/GetDailyCostValidator.cs` (depends on T038)

**Checkpoint**: User Stories 1 and 2 both work independently via `quickstart.md`.

---

## Phase 5: User Story 3 - View consumption trends independent of cost (Priority: P3)

**Goal**: A user can chart minute-level energy consumption without needing price data.

**Independent Test**: Request minute-level consumption for the last hour and confirm one value
per minute comes back (via `quickstart.md` US3).

### Implementation for User Story 3

- [X] T041 [P] [US3] Create `GetConsumptionQuery` + response records in `src/Features/ElectricityCost/Queries/GetConsumptionQuery.cs`
- [X] T042 [US3] Implement `GetConsumptionQueryHandler` — authorize, dispatch to `IConsumptionRepository.GetMinuteAsync`/`GetHourlyAsync` based on the requested `granularity` — in `src/Features/ElectricityCost/Handlers/GetConsumptionQueryHandler.cs` (depends on T016, T041)
- [X] T043 [P] [US3] Create `src/Features/ElectricityCost/Mappers/ConsumptionMapper.cs` with `ToConsumptionResponse(...)`
- [X] T044 [US3] Create `GetConsumptionEndpoint` + request DTO (`GET /api/consumption`) in `src/Features/ElectricityCost/Endpoints/GetConsumptionEndpoint.cs` (depends on T042, T043)
- [X] T045 [US3] Create `GetConsumptionValidator : Validator<GetConsumptionRequest>` (`granularity` must be `minute` or `hour`, `to >= from`) in `src/Features/ElectricityCost/Validators/GetConsumptionValidator.cs` (depends on T044)

**Checkpoint**: User Stories 1-3 all work independently via `quickstart.md`.

---

## Phase 6: User Story 4 - Compare cost under both pricing models (Priority: P4)

**Goal**: Every cost response also shows what the same consumption would have cost under the
location's non-enrolled pricing model.

**Independent Test**: Request cost for an hour and confirm both `actual` and `comparison` are
populated (or `comparison` explicitly marked unavailable) (via `quickstart.md` US4).

### Implementation for User Story 4

- [X] T046 [US4] Extend `CostCalculator` to also compute the **comparison** (non-enrolled) model's rate/cost, nullable when its required price/FX data is unavailable, per FR-009/FR-011, in `src/Application/ElectricityCost/CostCalculator.cs` (depends on T024)
- [X] T047 [US4] Wire the now-populated `comparison` value through `CostMapper`'s existing response builders — no new endpoints, every US1/US2 response already has the field in its shape — in `src/Features/ElectricityCost/Mappers/CostMapper.cs` (depends on T046, T029, T036)

**Checkpoint**: All four user stories independently functional via `quickstart.md`.

---

## Final Phase: Polish & Cross-Cutting Concerns

- [X] T048 [P] Update the EventId allocation table in `CLAUDE.md` and `DevelopmentGuide.md` to add `1500-1599` (ElectricityCost) — required by the constitution's logging principle whenever a new range is claimed
- [X] T049 Run `dotnet format --verify-no-changes` and `dotnet build` from the repo root and confirm no new warnings
- [ ] T050 Execute every scenario in `specs/002-electricity-cost-tracking/quickstart.md` against local dev — note: the ENTSO-E-dependent scenarios need a real, approved security token (research.md §4); if it hasn't arrived yet, verify the FR-011 graceful-unavailability scenarios first and revisit the price-dependent ones once the token is approved

---

## Dependencies & Execution Order

- **Setup (Phase 1)**: No dependencies.
- **Foundational (Phase 2)**: Depends on Setup. Blocks every user story — T027-T047 all need the repositories, external clients, and `CostCalculator` from this phase.
- **User Story 1 (Phase 3)**: Depends on Foundational only. This is the MVP slice.
- **User Story 2 (Phase 4)**: Depends on Foundational only — independent of US1, could be built in parallel.
- **User Story 3 (Phase 5)**: Depends on Foundational only — independent of US1/US2.
- **User Story 4 (Phase 6)**: Depends on Foundational's `CostCalculator` (T024) and whichever of US1/US2's mappers already exist (T029/T036) — the last story to build since it enhances what the others already produce, rather than exposing new endpoints.
- **Polish (Final Phase)**: Depends on whichever stories were completed.

## Parallel Example: Foundational phase

```text
Task: "Create ElectricityPrice domain entity in src/Domain/Common/Entities/ElectricityPrice.cs"
Task: "Create ExchangeRate domain entity in src/Domain/Common/Entities/ExchangeRate.cs"
Task: "Create keyless read-model MinuteConsumption in src/Infrastructure/Database/ReadModels/MinuteConsumption.cs"
Task: "Create keyless read-model HourConsumption in src/Infrastructure/Database/ReadModels/HourConsumption.cs"
```
(T011/T012/T013 can each start once their respective entity/read-model task is done; T014/T015/T016 similarly follow their own interface tasks — many of these run in parallel across different files.)

## Parallel Example: User Story 2

```text
Task: "Create GetHourlyCostQuery in src/Features/ElectricityCost/Queries/GetHourlyCostQuery.cs"
Task: "Create GetDailyCostQuery in src/Features/ElectricityCost/Queries/GetDailyCostQuery.cs"
```

---

## Implementation Strategy

### MVP First (User Story 1 only)

1. Phase 1 (Setup) → Phase 2 (Foundational) → Phase 3 (US1).
2. Stop and validate against `quickstart.md`'s US1 scenario.
3. This alone gives a live "what is this costing me right now" figure — the capability that
   motivated the whole feature.

### Incremental Delivery

1. Foundational → US1 (MVP: live current-hour cost).
2. US2 (historical hourly/daily) — independent, can follow immediately or run in parallel with US3.
3. US3 (minute-level consumption charting) — independent of US2.
4. US4 (pricing-model comparison) — last, since it enhances US1/US2's existing responses rather
   than adding new endpoints.
5. Polish — doc updates and the full manual quickstart pass (noting the ENTSO-E token dependency).

## Notes

- `IConsumptionRepository` deliberately does **not** extend `IRepository<T>` like every other
  repository in this codebase — it's a pure read layer over TimescaleDB continuous-aggregate
  views with no insert/update/delete concept, so the generic CRUD interface doesn't fit.
- No manual CQRS handler registration task is included, for the same reason as in 001:
  `Cqrs.SourceGenerator` discovers every handler in `Features` at compile time.
- Commit after each task or logical group; stop at any checkpoint to validate a story
  independently before moving on.
