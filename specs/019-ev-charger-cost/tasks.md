---
description: "Task list for spec 019: the car charging cost on the bill"
---

# Tasks: What the Car Charging Costs, on the Bill

**Input**: Design documents from `specs/019-ev-charger-cost/`

**Prerequisites**: [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md), [data-model.md](data-model.md),
[contracts/endpoints.md](contracts/endpoints.md), [quickstart.md](quickstart.md)

**Tests**: Included. The plan's Testing section and quickstart §1 define them: xUnit + NSubstitute in
`tests/Features.Tests`.

**Organization**: by user story. US1 (Norgespris price, setting off), US2 (house price, setting on) and US4 (access)
are P1; US3 (`pricedAt`) is P2. US1 and US2 share the service and the response fields of Phase 2. US4 needs no code:
the bill's access check is unchanged, and one test confirms that a viewer gets the cost.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: The user story the task belongs to (US1–US4)

---

## Phase 1: Setup

No setup: no new project, package, table or migration.

---

## Phase 2: Foundational (blocking prerequisites)

- [X] T001 Add `decimal? Cost` and `string PricedAt` as the last two fields of `EvChargerBillResponse` in
  `src/Features/ElectricityCost/Queries/GetMonthlyBillQuery.cs`, with doc comments from data-model.md. Update its
  summary: the car is priced on its own, and the cost is added to no line or total.
- [X] T002 Create `src/Features/ElectricityCost/Services/EvChargerCost.cs`:
  - `public sealed record EvChargerCostResult(decimal? Cost, string PricedAt)`, with the constants `HousePrice` and
    `NorgesPrisPrice`;
  - `static Task<EvChargerCostResult> PriceAsync(Location, IReadOnlyList<HourCost> soFar,
    IReadOnlyList<EvChargerHour> unmatched, CostCalculator, CancellationToken)`. It takes the car's hours from
    `soFar[i].EvCharger` and `unmatched`, and dispatches on `location.IncludeEvChargerInTotals`. The rules are in
    T004 and T007; the dispatch can come first.
- [X] T003 In `src/Features/ElectricityCost/Mappers/BillMapper.cs`, take an `EvChargerCostResult?` and map it onto
  `EvChargerBillResponse`. In `src/Features/ElectricityCost/Handlers/GetMonthlyBillQueryHandler.cs`, call
  `EvChargerCost.PriceAsync` right after the hours so far and before `ProjectAsync` (research R1), only when the car
  part is set. Fix the compile errors in existing tests that build `EvChargerBillResponse` directly.

**Checkpoint**: builds; the existing bill tests pass.

---

## Phase 3: User Story 1 - The car's cost when it is not counted with the house (Priority: P1) 🎯 MVP

- [X] T004 [US1] In `EvChargerCost`, the private `SeparateAgreementCostAsync`: Σ car kWh ×
  `NorgesPrisPriceWithVatAsync(location, hour)`, null when any hour's price is null. Its comment says this is the
  garages' agreement and the one place to change it (FR-005).
- [X] T005 [US1] Tests in `tests/Features.Tests/ElectricityCost/EvChargerCostBillTests.cs`:
  - kWh × 0,40 × 1,25 summed over the hours;
  - no VAT when exempt;
  - a rate change mid-month prices each hour at its own rate;
  - no rate in effect gives null;
  - a spot house and a Norgespris house over its limit give the same cost;
  - a charging hour without a house reading is included.

---

## Phase 4: User Story 2 - The car's cost when it is counted with the house (Priority: P1)

- [X] T006 [US2] In `EvChargerCost`, the private `HousePriceCostAsync` (research R3): walk the union of the hours so
  far and the car hours in hour order, keeping the running month kWh (house + car before the hour). For each car
  hour, add `CalculateAsync(location, h, carKwh, runningBefore + houseKwh(h)).Actual?.Cost`; null when any is null.
- [X] T007 [US2] Tests in `EvChargerCostBillTests.cs`:
  - within the limit, the cost equals Σ car kWh × the prices handler's `ratePerKwh` for each hour (SC-002);
  - car kWh past the limit are priced on spot terms, and an hour crossing it is split;
  - the car of an hour comes after the house of that hour;
  - an unmatched hour is priced;
  - a missing spot price gives null;
  - the capacity charge and the monthly fees are not in it.

---

## Phase 5: User Story 3 - The bill says how the cost was worked out (Priority: P2)

- [X] T008 [US3] Tests in `EvChargerCostBillTests.cs`: `pricedAt` is `HousePrice` with the setting on and
  `NorgesPrisPrice` with it off. It is also set when `cost` is null.

---

## Phase 6: User Story 4 - Only people who may see the location (Priority: P1)

- [X] T009 [US4] Test in `EvChargerCostBillTests.cs`: a viewer (associated, not owner) gets the cost; no access still
  gives the bill's 404.
- [X] T010 [P] [US4] In `tests/Features.Tests/Http/MonthlyBillHttpTests.cs`, check that `evCharger.cost` and
  `evCharger.pricedAt` are in the JSON.

---

## Phase 7: Polish & Cross-Cutting Concerns

- [X] T011 Test that the bill's lines, parts, total and projection are the same with the car's cost as without it
  (SC-003), in `EvChargerCostBillTests.cs`. Add a pointer in spec 016's contract
  (`specs/016-zaptec-ev-charger/contracts/endpoints.md`) to the two new fields.
- [X] T012 Run `dotnet build` (no new warnings), `dotnet test` and `dotnet format --verify-no-changes`.
- [ ] T013 After the deploy: quickstart §2 (SC-001, SC-002, SC-003, SC-004). Roy does this.

---

## Dependencies & Execution Order

- Phase 2 (T001 → T002 → T003) blocks every story.
- US1 (T004–T005) and US2 (T006–T007) both edit `EvChargerCost.cs`, so do them one after the other.
- US3 and US4 tests can follow either story. T010 is a separate file.
- Polish last.

## Implementation Strategy

MVP: Phase 2 + US1 (Hjemme's case today). Then US2, US3 and US4's tests, and the polish. One PR for the whole spec.
