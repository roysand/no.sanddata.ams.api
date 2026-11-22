---
description: "Task list for spec 018: the Norgespris limit status and the spot energy price"
---

# Tasks: The Norgespris Limit and the Spot Energy Price for the App

**Input**: Design documents from `specs/018-norgespris-limit-status/`

**Prerequisites**: [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md), [data-model.md](data-model.md),
[contracts/endpoints.md](contracts/endpoints.md), [quickstart.md](quickstart.md)

**Tests**: Included. The plan's Testing section and quickstart §1 define them: xUnit + NSubstitute in
`tests/Features.Tests`.

**Organization**: by user story. US1 (the status, with FR-004's car in the limit), US2 (the spot energy price) and
US3 (who may read the status) are all P1. US2 is independent of US1. US3 adds its tests to US1's handler.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: The user story the task belongs to (US1, US2, US3)

---

## Phase 1: Setup

No setup: no new project, package, table or migration.

---

## Phase 2: Foundational (blocking prerequisites)

**Purpose**: the calculator's public figures and the shared projection helper.

- [X] T001 In `src/Application/ElectricityCost/CostCalculator.cs` (research R3, R6, R10):
  - Make the cap public as `NorgesPrisCapAsync(Location location, DateTime instantUtc, CancellationToken ct)` (the
    body of today's private `CapForAsync`), and call it from `CalculateForModelAsync`.
  - Add `NorgesPrisPriceWithVatAsync(Location, DateTime instantUtc, CancellationToken)`: the Norgespris rate in effect
    at the instant × (1 + VAT at the instant), VAT 0 when `location.VatExempt`; null when the rate or VAT is missing.
  - Add `SpotEnergyWithVatAsync(Location, DateTime hourStartUtc, CancellationToken)`: `SpotEnergyAsync` × (1 + VAT of
    the hour), VAT 0 when exempt; null when the spot energy or VAT is missing.
- [X] T002 [P] In `src/Features/ElectricityCost/Services/ConsumptionProjection.cs` add
  `ProjectRestOfMonth(IReadOnlyList<HourConsumption> rows, DateTime currentHourStartUtc, DateTime monthEndUtc)`
  (research R5). It keeps the rows of the 168 hours before `currentHourStartUtc`, groups them per hour, builds the
  remaining hours `[currentHourStartUtc, monthEndUtc)` and returns `Project(window, remaining)`. Move the same code out
  of `GetMonthlyBillQueryHandler.ProjectAsync` in `src/Features/ElectricityCost/Handlers/GetMonthlyBillQueryHandler.cs`
  and call the helper there; the bill's results must not change.
- [X] T003 [P] Tests for T001 in `tests/Features.Tests/Pricing/CostCalculatorPriceTests.cs`: the cap for a holiday home
  and a home; the Norgespris price with and without VAT exemption, and a rate starting tomorrow not used today; the
  spot energy with VAT, exempt, missing price (null) and negative price.

**Checkpoint**: `dotnet test` green; the bill tests unchanged.

---

## Phase 3: User Story 1 - The Norgespris limit status of every location (Priority: P1) 🎯 MVP

**Goal**: one request gives each Norgespris location's limit, kWh so far, projection, over/by how much, price and
reset time; and the car counts toward the limit everywhere when counted with the house (FR-004).

**Independent Test**: quickstart §1 (status handler, car in the cap) and §2 (against the local database).

### Car in the Norgespris limit (FR-004, research R9)

- [X] T004 [US1] In `src/Features/ElectricityCost/Services/HourlyCostProvider.cs`: when
  `location.IncludeEvChargerInTotals` and the charging repository is set, read the charger's hours from the month
  start (not `fromUtc`), and add each charger hour of the same Oslo month that starts before the hour being priced to
  the running `monthKwh` (house first, car after, R9). Charger hours with no meter reading count too. Attachments
  (`EvCharger` on hours, unmatched hours) keep using only the hours from `fromUtc`. With the setting off, the read and
  the results are as today.
- [X] T005 [US1] In `src/Features/ElectricityCost/Handlers/GetMonthlyBillQueryHandler.cs`: the projection's starting
  month total is the meter kWh so far plus the charger's kWh so far when included (`EvChargerShare.TotalKwh`).
- [X] T006 [P] [US1] In `src/Features/ElectricityCost/Handlers/GetDayPricesQueryHandler.cs`: when included, add the
  charger kWh of the month before the day to the starting total, and each charger hour of the day after that hour.
- [X] T007 [P] [US1] In `src/Features/ElectricityCost/Handlers/GetCurrentHourCostQueryHandler.cs`: when included, add
  the charger kWh of the month before the current hour to `monthKwhBefore`.
- [X] T008 [US1] Update the comments that say the charger never changes a cost (`HourlyCostProvider`,
  `Application/EvChargers/EvChargerShare.cs`, `EvChargerBillResponse` in `Queries/GetMonthlyBillQuery.cs`): the car is
  never priced, but counts toward the Norgespris limit when included.
- [X] T009 [US1] Tests in `tests/Features.Tests/ElectricityCost/EvChargerCapTests.cs`: for a cabin with a 1 000 kWh
  limit, 990 kWh of meter and 20 kWh of car earlier in the month, an hour is priced on spot terms when included and on
  Norgespris terms when not, in the hourly costs (provider), the prices, the current hour and the bill's projection;
  an hour asked alone has the same price as in a whole-month range. Adjust existing tests in
  `tests/Features.Tests/ElectricityCost/EvChargerCostTests.cs` that assert "never affects a cost" only where FR-004
  changes the outcome.

### The status

- [X] T010 [P] [US1] Create `src/Features/ElectricityCost/Queries/GetNorgesPrisStatusQuery.cs`:
  `GetNorgesPrisStatusQuery(Guid UserId) : IQuery<Result<NorgesPrisStatusResponse>>`, and the records
  `NorgesPrisStatusResponse(DateTime AsOf, IReadOnlyList<NorgesPrisStatusItem> Items)` and `NorgesPrisStatusItem` with
  the fields of data-model.md, with XML docs.
- [X] T011 [P] [US1] Create `src/Features/ElectricityCost/Mappers/NorgesPrisStatusMapper.cs`: `ToItem(...)` computes
  `overLimit` and `overLimitKwh` (research R8).
- [X] T012 [P] [US1] Add `NorgesPrisStatusQueried(ILogger, Guid userId, int count)` with EventId 1505 to
  `src/Features/ElectricityCost/Logging/LogMessages.cs`.
- [X] T013 [US1] Create `src/Features/ElectricityCost/Handlers/GetNorgesPrisStatusQueryHandler.cs` (research R2, R4,
  R5, R7, R11): the user's locations via `GetForUserWithRoleAsync`, filtered to active + Norgespris; per location one
  `GetHourlyAsync` from min(month start, current hour − 168 h) to now and, when included, one charger read for the
  month so far; kWh so far, kWh before the current hour and the projection (`ProjectRestOfMonth`, null when
  `BasedOnHours == 0`) from them; limit and price from the calculator at now; `resetsAt` and `month` from `CostTime`.
  Uses `TimeProvider`.
- [X] T014 [US1] Create `src/Features/ElectricityCost/Endpoints/GetNorgesPrisStatusEndpoint.cs`:
  `GET /api/electricity-cost/norgespris-status`, JWT only, tag `ElectricityCost`, summary from the contract.
- [X] T015 [US1] Tests in `tests/Features.Tests/ElectricityCost/NorgesPrisStatusHandlerTests.cs` (quickstart §1):
  filtering; holiday-home vs home limit; kWh so far with the running hour; car only when included; `projectedKwh`
  equal to the bill's `totalKwh + projection.kwh` on the same substituted data; null projection without readings;
  over at exactly the limit; price with VAT and exempt; `resetsAt`/`month` at a month turn; empty list.

**Checkpoint**: US1 works on its own: the status endpoint answers and agrees with the bill.

---

## Phase 4: User Story 2 - The spot energy price per hour (Priority: P1)

**Goal**: `/prices` gives each hour's spot energy price including VAT.

**Independent Test**: quickstart §1 (prices) and §2 (tomorrow's prices).

- [X] T016 [US2] Add `decimal? SpotEnergyPricePerKwh` as the last field of `HourPriceResponse` in
  `src/Features/ElectricityCost/Queries/GetDayPricesQuery.cs` (with a doc comment), and fill it in
  `src/Features/ElectricityCost/Handlers/GetDayPricesQueryHandler.cs` from `SpotEnergyWithVatAsync`. Update the
  endpoint summary in `src/Features/ElectricityCost/Endpoints/GetDayPricesEndpoint.cs`.
- [X] T017 [US2] Tests in `tests/Features.Tests/ElectricityCost/GetDayPricesQueryHandlerTests.cs`: the field equals
  price × rate × (1 + VAT), is the same for a location over its limit, null without a price, and `ratePerKwh` /
  `spotRatePerKwh` unchanged.

---

## Phase 5: User Story 3 - Only people who may see the location (Priority: P1)

**Goal**: the status lists exactly the caller's locations; no sign-in is refused.

- [X] T018 [US3] Tests in `tests/Features.Tests/ElectricityCost/NorgesPrisStatusHandlerTests.cs`: a viewer gets only
  their location; an owner's inactive location is not listed.
- [X] T019 [US3] HTTP tests in `tests/Features.Tests/Http/NorgesPrisStatusHttpTests.cs`: 401 without a token; 200 with
  the contract's shape.

---

## Phase 6: Polish & Cross-Cutting Concerns

- [X] T020 Add the status endpoint to the endpoint list in `DevelopmentGuide.md` or the doc that lists the
  `electricity-cost` endpoints, if one does; note the 1505 EventId where the 1500 range's messages are listed.
  No doc lists the endpoints or the 1500 range's messages; `NorgesPrisStatusQueried` was added to query 19 of
  `ApplicationInsightsQueries.md`.
- [X] T021 Run `dotnet build` (no new warnings), `dotnet test` and `dotnet format --verify-no-changes`.
- [ ] T022 Quickstart §2 against the local database, if it is running: the status agrees with the bill per location.
  Partly done: the API starts locally, the route is in the OpenAPI document and answers 401 without a token. The
  signed-in comparison needs Roy's login; `TheProjection_IsTheBillsProjectedTotal` checks the same equality in tests.
- [ ] T023 After the deploy: quickstart §4 in production (SC-003, SC-004). Roy does this.

---

## Dependencies & Execution Order

- Phase 2 (T001–T003) blocks the stories: T001 is used by T013 and T016; T002 by T005 and T013.
- US1: T004 → T005; T006, T007 in parallel; T008, T009 after them. T010–T012 in parallel, then T013 → T014 → T015.
- US2 (T016–T017) needs only T001; it can run alongside US1. T006 and T016 touch the same handler: do them one after
  the other.
- US3 (T018–T019) needs T013–T014.
- Polish last.

## Parallel Examples

- After T001: T002 and T003 together.
- In US1: T010, T011 and T012 together; T006 and T007 together.

## Implementation Strategy

MVP: Phase 2 + US1 (the status and the car in the limit). Then US2 (one field), then US3's tests and the polish. One
PR for the whole spec.
