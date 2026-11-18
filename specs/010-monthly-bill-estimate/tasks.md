---

description: "Task list for the Estimated Bill for the Month"
---

# Tasks: Estimated Bill for the Month

**Input**: Design documents from `/specs/010-monthly-bill-estimate/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/endpoints.md, quickstart.md

**Tests**: Required, as for specs 004 to 009. xUnit + NSubstitute in `tests/Features.Tests/`. `dotnet build` and
`dotnet test` must pass, and `dotnet format --verify-no-changes --include <touched files>` must be clean for the files
this feature touches (a repo-wide check already fails on old files).

**Organization**: Grouped by user story from spec.md: US1 markup under Norgespris, US2 bill for a finished month, US3
estimate for the current month, US4 Admin sets the supplier monthly fee. **Delivery order: US1, US2, US3, US4.** US1
can ship alone (it fixes figures already shown). US4 needs only Phase 2 and can be done at any point after it; do it
before quickstart step 6 so the real fee is set.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependency on an incomplete task)
- **[Story]**: US1-US4 from spec.md

## Path Conventions

Paths are relative to the repository root. Handlers are discovered by `Cqrs.SourceGenerator`; do not register them by
hand. Patterns to follow: the capacity-charge slice (`src/Features/ElectricityCost/{Queries,Handlers,Endpoints,
Validators}/GetCapacityCharge*.cs`) for the new read endpoint, `SavingCalculator.cs` for a pure service over
`HourCost`, and `tests/Features.Tests/Pricing/FakePriceRateRepository.cs`, `RangeStubs.cs`,
`tests/Features.Tests/NorgesPris/NorgesPrisTestDoubles.cs` and `tests/Features.Tests/Http/EndpointHost.cs` for tests.
Files are written with CRLF line endings by `dotnet format`; run it on the touched files at the end. When writing
files with a shell heredoc, avoid apostrophes in the text.

---

## Phase 1: Setup

- [ ] T001 Confirm the data for the July 2026 acceptance check (quickstart step 6) exists in production, read-only:
  hourly readings of the holiday home for 2026-07 (744 hours expected), spot prices of the location's zone
  and EURNOK rates for July, the grid tariff rate valid from 2026-07-01 (day 0.2899, night 0.1699), the capacity step
  table from 2026-07-01, the Norgespris rate for July and the 1,000 kWh holiday-home cap. Record what is missing in
  this task; it does not block the code, only T040.

---

## Phase 2: Foundational (blocks US2, US3, US4)

**Purpose**: the fee column and the extra breakdown fields. No behaviour changes yet.

- [ ] T002 Add `decimal? SupplierMonthlyFee { get; private set; }` to `src/Domain/Common/Entities/Location.cs` with a
  doc comment (NOK per month excluding VAT; null = not set) and an optional trailing parameter
  `decimal? supplierMonthlyFee` on `SetPricing(...)`; keep existing callers compiling (pass the current value through
  where they do not set it).
- [ ] T003 Map it in `src/Infrastructure/Database/Configuration/LocationConfiguration.cs`:
  `builder.Property(l => l.SupplierMonthlyFee).HasPrecision(9, 4).IsRequired(false);`.
- [ ] T004 Create the migration per `DatabaseMigrations.md`: `dotnet ef migrations add AddSupplierMonthlyFee --project
  src/Infrastructure/Infrastructure.csproj --startup-project src/api/api.csproj --output-dir Database/Migrations`.
  Review: one nullable `numeric(9,4)` column on `Location`, nothing else. Do not hand-edit the snapshot.
- [ ] T005 [P] Add `ElectricityTax`, `EnovaFee` and `VatRate` to `PriceBreakdown` in
  `src/Application/ElectricityCost/PriceCalculation.cs` (research R2); `Calculate` fills them (`Taxes` stays
  `electricityTax + enovaFee`, `VatRate` is `vatFraction`). Fix the other constructor call sites
  (`grep -rn "new PriceBreakdown(" src tests`). The API mapping in `CostMapper` is unchanged.
- [ ] T006 [P] Extend `tests/Features.Tests/ElectricityCost/CostCalculatorTests.cs` (or the existing
  `PriceCalculation` tests): the three new fields equal the inputs, and `Energy + Markup + Grid + Taxes + Vat = Total`
  still holds exactly.

**Checkpoint**: builds, tests pass, the migration exists; no endpoint output has changed.

---

## Phase 3: User Story 1 - The supplier markup is charged under Norgespris too (Priority: P1) 🎯 MVP

**Goal**: Norgespris hours include the location's markup; the saving's markup cancels (FR-001, FR-002).

**Independent Test**: a Norgespris location with markup 0.03192: an hour of 1 kWh costs
(0.40 + 0.03192 + grid + 0.0713 + 0.0100) × 1.25; the saving for an hour is (spot − 0.40) × 1.25 × kWh.

### Tests for User Story 1

- [ ] T007 [P] [US1] Update `tests/Features.Tests/NorgesPris/CostCalculatorNorgesPrisTests.cs`: Norgespris-terms
  breakdown has `Markup` = the location markup (was 0); an hour that crosses the cap has the markup on both the
  within-cap and above-cap kWh; markup 0 gives the old figures.
- [ ] T008 [P] [US1] Update `tests/Features.Tests/ElectricityCost/NorgesPrisSavingTests.cs`: with a markup set, the
  saving equals Σ kWh × (spot − Norgespris) × (1 + VAT) for hours below the cap (the markup no longer appears), and 0
  for kWh above the cap.

### Implementation for User Story 1

- [ ] T009 [US1] In `src/Application/ElectricityCost/CostCalculator.cs` `TermsAsync`, replace
  `model == PricingModel.Spot ? location.SupplierMarkupPerKwh : 0m` with `location.SupplierMarkupPerKwh` and remove
  the "No supplier markup under Norgespris" comment.
- [ ] T010 [P] [US1] Fix the doc comments that say the markup is spot-only: `Location.SupplierMarkupPerKwh` in
  `src/Domain/Common/Entities/Location.cs`, the `markup` param of `PriceCalculation.Calculate` in
  `src/Application/ElectricityCost/PriceCalculation.cs`, and the `SetLocationPricingEndpoint` description in
  `src/Features/Pricing/Endpoints/LocationPricingEndpoints.cs` ("added on all kWh, Norgespris included").
- [ ] T011 [US1] Run `dotnet test`; fix any other test that asserted a 0 markup on Norgespris terms
  (`grep -rn "Markup" tests/Features.Tests`), changing the expectation, never the rule.

**Checkpoint**: US1 is done and can be merged and deployed on its own. Every Norgespris figure rises by the markup;
say so in the pull request.

---

## Phase 4: User Story 2 - The bill for a finished month matches the invoice (Priority: P1)

**Goal**: `GET /api/electricity-cost/bill?locationId=&month=` for an ended month, with Strøm and Nettleie lines as on
the invoice, final (FR-003 to FR-007, FR-013 to FR-015, FR-017).

**Independent Test**: the synthetic July 2026 test (T013) gives 673.55 ± 0.10 and every line within the spec's
tolerances.

### Tests for User Story 2

- [ ] T012 [P] [US2] Create `tests/Features.Tests/ElectricityCost/MonthlyBillBuilderTests.cs` with hand-calculated
  cases on `HourCost` lists built through `PriceCalculation.Calculate`: a spot location (no credit line; spot energy
  line = actual energy); a Norgespris location below the cap (credit = −Σ kWh × (spot − NP) × (1 + v), credit VAT 0);
  an hour where spot < NP (positive credit contribution); a month above the cap (no credit on the above kWh); a
  VAT-exempt location (all VAT 0, credit = spot − NP); a tax-exempt location (tax line 0, Enova line present); a
  Norgespris hour without a spot price (research R3: net energy in the spot line, credit 0, both lines
  `Complete = false`); an unpriceable hour (left out, `Priced` lower); fee not set and grid tariff not set
  (`Set = false`, amounts null, left out of the totals, bill incomplete). Every case asserts the invariants of
  data-model.md: `Amount = SoFar + Projected = AmountExclVat + Vat` per line, part and total, and SC-003
  (spot energy + credit + markup = Σ (energy + markup) × (1 + VAT) × kWh of the hours).
- [ ] T013 [P] [US2] In the same file, the July 2026 invoice test (research R9): 744 Oslo hours of July 2026;
  95.58 kWh spread over the weekday 06-22 hours (holidays per `NorwegianHolidays`) and 144.73 kWh over the others,
  with one 6 kWh hour on each of three different days inside those budgets; spot energy 1.24416, Norgespris 0.40,
  markup 0.03192, fee 27.20, grid 0.2899 / 0.1699, tax 0.0713, Enova 0.0100, VAT 0.25, step 5-10 kW at 336. Assert:
  total 673.55 ± 0.10; fee 34.00, markup 9.59, grid day 34.64, grid night 30.74, capacity 420.00, tax 8.51 + 12.90,
  Enova 1.20 + 1.81, each ± 0.02; spot energy 373.74, credit −253.58, Strøm 417.33, Nettleie 256.22, each ± 1.00;
  spot energy + credit 120.16 ± 0.02; quantities 95.58 / 144.73 kWh.
- [ ] T014 [P] [US2] Create `tests/Features.Tests/ElectricityCost/MonthlyBillHandlerTests.cs` (finished-month
  cases): a past month is `Final` with `Projection` null and every `Projected` 0; a user not linked → `Location.NotFound`
  and `LocationAccessDenied` logged; a month after now → `Bill.MonthNotStarted`; omitted month → current Oslo month;
  the capacity line equals `GetCapacityChargeQueryHandler`'s amount for the same data; the dated rates are loaded once
  (FR-017, assert on the fake repository call count).
- [ ] T015 [P] [US2] Create `tests/Features.Tests/Http/MonthlyBillHttpTests.cs` via `EndpointHost`: 200 for a linked
  user, 400 `Validation.InvalidMonth` for `month=2026-13` and `month=07-2026`, 401 without a token, 404 for another
  user's location.

### Implementation for User Story 2

- [ ] T016 [US2] Create `src/Features/ElectricityCost/Queries/GetMonthlyBillQuery.cs`:
  `GetMonthlyBillQuery(Guid UserId, Guid LocationId, DateOnly? Month) : IQuery<Result<MonthlyBillResponse>>` and the
  response records of contracts/endpoints.md: `MonthlyBillResponse`, `BillHoursResponse`, `BillProjectionResponse`,
  `BillCapacityResponse` (reuse `CapacityStepRangeResponse`), `BillPartResponse`, `BillLineResponse`,
  `BillTotalResponse`, with XML doc comments as in `GetCapacityChargeQuery.cs`. `Status` and `Code` are strings
  (`Final`/`Estimate`; the line codes in contract order).
- [ ] T017 [US2] Create the pure `src/Features/ElectricityCost/Services/MonthlyBillBuilder.cs` (research R3, R4, R5,
  data-model "Bill line"): input = the location, the so-far `HourCost` list, an optional projected `HourCost` list,
  the `CapacityChargeResult`, the VAT fraction on the first Oslo day of the month, and the month; output = an internal
  model with the two parts and the total. Per hour: day/night by `GridPeriods.For`; within/above from
  `Actual.Cap` (all within when null); spot terms from `Comparison.Breakdown` on a Norgespris location; accumulate
  so-far and projected amounts excluding VAT and VAT separately per line. Fixed lines (fee, capacity) carry the full
  amount in so-far. Lines not set have null amounts and are left out of the sums. No rounding. No repositories.
- [ ] T018 [US2] Create `src/Features/ElectricityCost/Mappers/BillMapper.cs`: static mapping from the builder model to
  `MonthlyBillResponse` (unit price = amount / quantity, null when quantity is 0 or not set).
- [ ] T019 [US2] Create `src/Features/ElectricityCost/Handlers/GetMonthlyBillQueryHandler.cs` following
  `GetCapacityChargeQueryHandler`: access check and `LocationAccessDenied`; month from the query or the current Oslo
  month via `TimeProvider` and `CostTime`; `Bill.MonthNotStarted` (`Error.BadRequest`) when the month starts after
  now; so-far hours from `HourlyCostProvider.GetAsync(location, monthStart, min(monthEnd, currentHourStart))`; the
  capacity charge with `CapacityCharge.TableFor` / `Calculate` over those hours; VAT via `PriceRateBook.ValueOn`
  (0 when exempt); build, map, log `CostQueried`. For a finished month no projection (US3 adds it).
- [ ] T020 [P] [US2] Create `src/Features/ElectricityCost/Validators/GetMonthlyBillValidator.cs`, a copy of
  `GetCapacityChargeValidator` for `GetMonthlyBillRequest`.
- [ ] T021 [US2] Create `src/Features/ElectricityCost/Endpoints/GetMonthlyBillEndpoint.cs`:
  `GetMonthlyBillRequest(Guid LocationId, string? Month = null)`, `Get("/api/electricity-cost/bill")`, JWT, tag
  `ElectricityCost`, a summary and description from contracts/endpoints.md (the lines, the credit at 0 % VAT, final vs
  estimate, not set vs incomplete), responses 200/400/401/404; error mapping as in `GetCapacityChargeEndpoint`.

**Checkpoint**: past months return the full bill; T012 to T015 pass.

---

## Phase 5: User Story 3 - The current month shows what the bill will probably be (Priority: P1)

**Goal**: the current month returns so far + projected per line, an `Estimate` with the projection basis
(FR-008 to FR-011).

**Independent Test**: a fixed clock on day 11 of a 30-day month with readings for days 1-10: so-far amounts equal the
finished-month calculation of those hours; the projection covers exactly the hours from the current hour to month
end; the total = so far + projected.

### Tests for User Story 3

- [ ] T022 [P] [US3] Create `tests/Features.Tests/ElectricityCost/ConsumptionProjectionTests.cs` (research R6): a full
  7-day window repeats last week hour by hour by (Oslo weekday, hour); the window reaches into the previous month on
  day 1; the 25-hour day averages the two 02:00 hours; the 23-hour day's missing key gets the window's average kWh per
  hour; a window with 3 days of readings uses them and the fallback for the rest; no readings → 0 kWh and
  `BasedOnHours` 0; `BasedOnDays = BasedOnHours / 24`.
- [ ] T023 [P] [US3] Add to `tests/Features.Tests/NorgesPris/CostCalculatorNorgesPrisTests.cs` (or
  `CostCalculatorTests.cs`): `fallbackSpotEnergy` is used only for an hour without a spot price row; with a price row
  it is ignored; without either the spot terms are null as before; `SpotEnergyAsync` returns the converted spot energy.
- [ ] T024 [P] [US3] Add current-month cases to `tests/Features.Tests/ElectricityCost/MonthlyBillHandlerTests.cs`:
  `Estimate` status; so-far = a finished-month calculation of the same hours (SC-004); `Projection.FromUtc` = start
  of the current Oslo hour and `Projection.Hours` = hours to month end; `AssumedSpotPricePerKwh` = plain average of the
  window's spot energy; published prices are used for hours that have them; a Norgespris location whose projection
  passes the cap prices the excess on spot terms with no credit; the Norgespris total does not change when the assumed
  spot price changes (US3 scen. 5); capacity `Provisional` true; day 1 of a month uses last month's window; a location
  without readings → projection 0 kWh, based on 0 days.

### Implementation for User Story 3

- [ ] T025 [US3] In `src/Application/ElectricityCost/CostCalculator.cs`: add the optional parameter
  `decimal? fallbackSpotEnergy = null` to `CalculateAsync` and thread it to `GetSpotEnergyAsync`, used only when
  `GetPriceAsync` returns null (the exchange rate is not needed then); make the spot-energy lookup available as
  `public Task<decimal?> SpotEnergyAsync(Location location, DateTime hourStartUtc, CancellationToken ct)`. Existing
  callers are unchanged.
- [ ] T026 [P] [US3] Create the pure `src/Features/ElectricityCost/Services/ConsumptionProjection.cs`:
  `Project(IReadOnlyList<(DateTime HourStartUtc, decimal Kwh)> window, IReadOnlyList<DateTime> remainingHourStarts)`
  → projected `(HourStartUtc, Kwh)` list and `BasedOnHours`, per research R6 (Oslo weekday and hour through
  `CostTime`/`TimeZoneInfo`).
- [ ] T027 [US3] Extend `GetMonthlyBillQueryHandler` for the current month (research R5, R7): window =
  [currentHourStart − 168 h, currentHourStart) read with one `IConsumptionRepository.GetHourlyAsync`; remaining hour
  starts from `currentHourStart` to month end (real UTC hours, so the clock-change day has 23 or 25);
  `CostCalculator.PreloadAsync(location, windowStart, monthEnd)` after the so-far pass; assumed spot price = plain
  average of `SpotEnergyAsync` over the window hours that have one (null if none); price each projected hour with
  `CalculateAsync(location, hour, kwh, runningMonthKwh, fallbackSpotEnergy)`, continuing the running month kWh from
  the so-far total; pass the projected `HourCost` list and `ProjectionResponse` data to the builder; capacity stays
  so-far and provisional.

**Checkpoint**: the current month returns an estimate; T022 to T024 pass.

---

## Phase 6: User Story 4 - An Admin sets the supplier monthly fee (Priority: P2)

**Goal**: the fee is set and listed through the existing location-pricing endpoints; `IsComplete` requires it
(FR-012, FR-016).

**Independent Test**: set 27.20 → the bill's fee line is 34.00; mark VAT-exempt → 27.20; `-1` → 400; a regular user
→ 403.

### Tests for User Story 4

- [ ] T028 [P] [US4] Extend `tests/Features.Tests/Pricing/LocationPricingHandlerTests.cs`: the fee is saved; an
  omitted fee keeps the current value; `IsComplete` is false without a fee and true with tariff and fee; the
  `LocationPricingChanged` log carries the fee and the acting user.
- [ ] T029 [P] [US4] Extend `tests/Features.Tests/Pricing/PricingValidatorTests.cs`: `-0.01` and `10000.01` refused
  with `Validation.InvalidMonthlyFee`; `0`, `27.20`, `10000` and omitted accepted.
- [ ] T030 [P] [US4] Extend `tests/Features.Tests/Http/PricingHttpTests.cs`: `PUT` with `supplierMonthlyFee` as Admin
  → 200 with the fee in the body; as a regular user → 403; `GET` lists `supplierMonthlyFee`.

### Implementation for User Story 4

- [ ] T031 [US4] Add `decimal? SupplierMonthlyFee = null` to `SetLocationPricingRequest` in
  `src/Features/Pricing/Endpoints/LocationPricingEndpoints.cs` and to `SetLocationPricingCommand` in
  `src/Features/Pricing/Commands/PricingCommands.cs`; pass it through in the endpoint; describe it in the endpoint
  summary (NOK per month excluding VAT, omitted keeps the value, 0 for a supplier without a fee).
- [ ] T032 [US4] In `src/Features/Pricing/Validators/PricingValidators.cs` `SetLocationPricingValidator`: when set,
  0 ≤ fee ≤ 10000, error code `Validation.InvalidMonthlyFee`.
- [ ] T033 [US4] In `src/Features/Pricing/Handlers/LocationPricingHandlers.cs`: pass
  `command.SupplierMonthlyFee ?? location.SupplierMonthlyFee` to `SetPricing`; add the fee to
  `LogMessages.LocationPricingChanged` (EventId 1706) in `src/Features/Pricing/Logging/LogMessages.cs`.
- [ ] T034 [US4] Add `decimal? SupplierMonthlyFee` to `LocationPricingResponse` in
  `src/Features/Pricing/Queries/PricingQueries.cs` (update the `IsComplete` doc comment) and in
  `PricingMapper.ToLocationPricingResponse` (`src/Features/Pricing/Mappers/PricingMapper.cs`):
  `IsComplete = GridTariffId is not null && SupplierMonthlyFee is not null`.

**Checkpoint**: an Admin sets the fee; the bill's fee line shows it.

---

## Phase 7: Polish and cross-cutting

- [ ] T035 Run `dotnet build` (no new warnings), `dotnet test` twice in a row (zero failures), and
  `dotnet format --verify-no-changes --include <every .cs file this feature added or changed>` (from
  `git diff --name-only main` and `git ls-files --others --exclude-standard`, excluding migrations). Revert unrelated
  formatter changes.
- [ ] T036 [P] Add the bill endpoint and the fee to the docs that list the cost endpoints and the pricing settings
  (`grep -rln "electricity-cost/capacity" *.md` and update those files the same way); no EventId table change.
- [ ] T037 [P] Update `specs/_backlog/monthly-bill-estimate.md` for the frontend: the final response shape (link to
  `specs/010-monthly-bill-estimate/contracts/endpoints.md`), that the markup fix raises Norgespris figures, and that
  `isComplete` on location pricing now needs the fee.
- [ ] T038 Verify on a scratch copy of the local database, as in spec 008 T058 (clone with
  `CREATE DATABASE ... TEMPLATE`, run the new code on another port, never touch the real `amsdb`; remove the clone
  afterwards): apply the migration and run quickstart.md sections 2 to 5.
- [ ] T039 Commit on `feature/010-monthly-bill-estimate`, push, and open a pull request against `main` that describes
  the markup fix and its effect on existing figures, the new fee setting (every location is incomplete until it is
  set), the bill endpoint, and the projection rules.
- [ ] T040 After deploying: set the fee (27.20) and confirm the markup (0.03192) of the holiday home in production,
  then run quickstart step 6 (July 2026 against the invoice) if T001 found the data. Record the result here and in
  `specs/010-monthly-bill-estimate/checklists/requirements.md`.

---

## Dependencies & Execution Order

- **Phase 1** (T001) is a read-only check; it blocks only T040.
- **Phase 2** blocks US2, US3, US4. T002 before T003 before T004; T005 and T006 are independent of T002 to T004.
- **US1** (T007 to T011) needs nothing from Phase 2 and can be done first, in parallel with it, and shipped alone.
- **US2** (T012 to T021) needs Phase 2 (fee property, breakdown fields) and US1 (the bill's markup line assumes the
  markup on Norgespris terms). T016 before T017; T017 before T018 and T019; T019 before T021.
- **US3** (T022 to T027) needs US2 (handler, builder). T025 and T026 before T027.
- **US4** (T028 to T034) needs Phase 2 only. T031 before T032 and T033; T034 is independent.
- **Polish** last; T040 after the deploy.

### Parallel opportunities

- Phase 2: T005/T006 with T002 to T004.
- US1: T007, T008, T010 together.
- US2: T012 to T015 together (tests first), T020 with T017.
- US3: T022, T023, T024 together; T026 with T025.
- US4: T028 to T030 together; the whole of US4 alongside US2 or US3.
- Polish: T036 with T037.

## Implementation Strategy

1. **MVP = US1.** One-line fix with tests; ship it first so the existing figures are right.
2. **Phase 2 + US2**: the bill for finished months, proven by the synthetic July test.
3. **US3**: the estimate the frontend card needs.
4. **US4** any time after Phase 2; before T040 at the latest.
5. Deploy US2 to US4 together if possible, then set the fee in production and check July against the invoice.
