# Tasks: The Water Heater's kWh and Cost, Split from the Rest

**Input**: Design documents from `/specs/012-water-heater-energy/`

**Prerequisites**: [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md), [data-model.md](data-model.md),
[contracts/endpoints.md](contracts/endpoints.md), [quickstart.md](quickstart.md)

**Tests**: Included. The plan and quickstart §1 name the test classes. They are xUnit + NSubstitute in
`tests/Features.Tests`, with no database. The SQL is checked by hand (quickstart §2).

**Organization**: Phase 2 builds the per-hour heater part that both stories need. US1 adds it to the hourly and daily
responses; US2 adds it to the bill. Both are P1, and US2 only needs Phase 2, not US1.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: US1 or US2 from spec.md

## Facts from the code that the tasks rely on

- `HourCost` is declared at the top of `src/Features/ElectricityCost/Services/HourlyCostProvider.cs`, as
  `record HourCost(DateTime HourStart, decimal ConsumptionKwh, CostResult Cost)`. `Cost.Actual` is a `RateCost?` with
  a `Cost` amount.
- `HourlyCostProvider(IConsumptionRepository, CostCalculator)` is registered with `AddScoped` in
  `src/Features/ServiceCollectionExtensions.cs`. It is constructed with `new` in
  `tests/Features.Tests/ElectricityCost/MonthlyBillHandlerTests.cs`, `tests/Features.Tests/ElectricityCost/NorgesPrisSavingTests.cs`
  and `tests/Features.Tests/Pricing/CapAndExemptionTests.cs`.
- The sample repository is generic:
  - The interface is `IWaterHeaterPowerSampleRepository<T> : IRepository<T>`, with no methods of its own yet.
  - The implementation is `WaterHeaterPowerSampleEfRepository : GenericEfRepository<WaterHeaterPowerSample>`.
  - It is registered in `src/Infrastructure/AddInfrastructureToDI.cs` as
    `IWaterHeaterPowerSampleRepository<WaterHeaterPowerSample>`.
- `TimeProvider` is already injected into handlers (bill, saving, capacity), so it is registered.
- The bill's `soFar` hours end at the start of the current hour (`GetMonthlyBillQueryHandler`, line ~60). The heater
  part therefore covers whole passed hours only, like the rest of the bill.
- `CostMapper.ToDailyResponse(DateOnly, IReadOnlyList<HourCost>)` and `CostMapper.ToHourlyResponse(HourCost)` map the
  items. `BillMapper.ToResponse(month, ended, bill, hours, projection, capacity)` maps the bill.

---

## Phase 1: Setup

- [X] T001 Check that the baseline is green on `feature/012-water-heater-energy` before any change: `dotnet build`,
  `dotnet test` and `dotnet format --verify-no-changes` from the repo root. Note the test count.

---

## Phase 2: Foundational (blocks US1 and US2)

**Purpose**: the heater's energy per hour from the database, and a `WaterHeaterHour` on each `HourCost` when asked for.

### Application types and pure math

- [X] T002 [P] Create `src/Application/WaterHeaters/WaterHeaterHourEnergy.cs`:
  - `public sealed record WaterHeaterHourEnergy(DateTime HourStartUtc, decimal Kwh, decimal SampledSeconds)`.
  - Doc comment: "One row per UTC hour that has any held time. Kwh ≥ 0. 0 < SampledSeconds ≤ 3600."
  - The property names must equal the SQL column aliases in T005.
- [X] T003 [P] Create `src/Application/WaterHeaters/WaterHeaterShare.cs` with:
  - `public sealed record WaterHeaterHour(DateTime HourStartUtc, decimal Kwh, decimal? Cost, int SampledMinutes, bool Capped)`.
  - `public sealed record WaterHeaterTotal(decimal Kwh, decimal? Cost, int SampledMinutes, bool Capped, DateTime FirstHourUtc, DateTime LastHourUtc)`.
  - `public static class WaterHeaterShare`, with:
    - `ForHour(WaterHeaterHourEnergy energy, decimal hourKwh, decimal? hourActualCost)` → `WaterHeaterHour`:
      - `Kwh` = `min(energy.Kwh, hourKwh)`, and `Capped` = `energy.Kwh > hourKwh`.
      - `Cost` = `null` when `hourActualCost` is `null`; `0` when `hourKwh` is `0`; otherwise
        `hourActualCost × Kwh / hourKwh`. No rounding.
      - `SampledMinutes` = `(int)Math.Floor(energy.SampledSeconds / 60)`, clamped to 0–60.
    - `Sum(IEnumerable<WaterHeaterHour?> hours)` → `WaterHeaterTotal?`:
      - Skip `null`s. Return `null` when none are left: no heater part, never zeros.
      - Kwh and minutes are sums. Cost is the sum, or `null` if any hour's `Cost` is `null`. Capped is any hour capped.
      - First and last hour are the min and max `HourStartUtc`.
  - It takes plain values, not `HourCost`, because Application must not reference Features (constitution I). Follow
    [data-model.md](data-model.md).
- [X] T004 [P] Write `tests/Features.Tests/WaterHeaters/WaterHeaterShareTests.cs` (tests T003). Cover:
  - **Share of cost:** 2 of 4 kWh at cost 4,00 → 2,00.
  - **Norgespris-split hour:** an hour whose actual cost is not kWh × one rate. The share must still be the cost × kWh
    fraction.
  - **Cap:** energy 3,5 vs hour 3,0 → Kwh 3,0, Capped true, Cost equals the whole hour's cost.
  - **No price:** `hourActualCost` null → Cost null, Kwh still set.
  - **Zero hour:** hourKwh 0 → Kwh 0, Cost 0, Capped true when energy > 0.
  - **Minutes:** 3599 s → 59; 3600 s → 60.
  - **Sum:** of three hours, exact (no rounding drift, compare decimals with `Assert.Equal`); cost null if one hour
    lacks a cost; nulls skipped.
  - **First and last hour:** correct.
  - **Empty or all-null:** → `null`.

### The query

- [X] T005 Add the method to `src/Application/Common/Interfaces/Repositories/IWaterHeaterPowerSampleRepository.cs`:
  - Signature: `Task<IReadOnlyList<WaterHeaterHourEnergy>> GetHourlyEnergyAsync(Guid locationId, DateTime fromUtc, DateTime toUtc, DateTime nowUtc, CancellationToken ct);`
  - Doc comment: returns hours in `[fromUtc, toUtc)` ordered by hour; holds never reach past `nowUtc` or `toUtc`.
  - Implement it in `src/Infrastructure/Database/Repositories/WaterHeaterPowerSampleEfRepository.cs` with
    `ApplicationDbContext.Database.SqlQuery<WaterHeaterHourEnergy>($"…")`, an interpolated `FormattableString` so
    every value is a parameter (no string concatenation). Add a comment pointing to research R1/R2 and the
    Complexity Tracking entry in plan.md.
  - The SQL (research R1/R2): every hold is at most 2 minutes, so it crosses at most one hour boundary.
    - **`s`:** read `"WaterHeaterPowerSample"` where `"LocationId" = {locationId}`,
      `"SampledAtUtc" >= {fromUtc} - interval '2 minutes'` and `"SampledAtUtc" < {toUtc}`. Select `t = "SampledAtUtc"`,
      `p = "PowerKw"` and `e = LEAST(COALESCE(LEAD("SampledAtUtc") OVER (ORDER BY "SampledAtUtc"), {nowUtc}),
      "SampledAtUtc" + interval '2 minutes', {nowUtc}, {toUtc})`.
    - **`held`:** `t = GREATEST(t, {fromUtc})`, `e` and `p`, keeping only the rows where `e > GREATEST(t, {fromUtc})`.
      This drops zero-length holds, such as two samples in the same second.
    - **`parts`:** for each hold, the part in its own hour, `date_trunc('hour', t, 'UTC')` from `t` to
      `LEAST(e, that hour + 1 h)`. `UNION ALL` the part in the next hour, from that hour + 1 h to `e`, where `e` is past it.
    - **Result:** `SELECT h AS "HourStartUtc", SUM(p * EXTRACT(EPOCH FROM e - t) / 3600) AS "Kwh",
      SUM(EXTRACT(EPOCH FROM e - t)) AS "SampledSeconds" FROM parts GROUP BY h ORDER BY h`.
  - Return `HourStartUtc` with `DateTimeKind.Utc`. Map the results after `ToListAsync(ct)` if Npgsql returns another kind.
  - It reads through the existing `(LocationId, SampledAtUtc)` index. There is no migration.
- [X] T006 Add the heater part to `HourCost` and `HourlyCostProvider` in `src/Features/ElectricityCost/Services/HourlyCostProvider.cs`:
  - **`HourCost`:** becomes `record HourCost(DateTime HourStart, decimal ConsumptionKwh, CostResult Cost, WaterHeaterHour? WaterHeater = null)`.
  - **Constructor:** add `IWaterHeaterPowerSampleRepository<WaterHeaterPowerSample> waterHeaterSamples` and
    `TimeProvider timeProvider`.
  - **`GetAsync`:** add the parameter `bool withWaterHeater = false`, before `CancellationToken ct`, or as a named
    parameter after it, matching the existing style.
  - **When `withWaterHeater` is set:**
    - Call `GetHourlyEnergyAsync(location.Id, fromUtc, toUtc, timeProvider.GetUtcNow().UtcDateTime, ct)` once, and put
      the rows in a dictionary keyed by `HourStartUtc`.
    - For each hour added to `result` that has a row, set
      `WaterHeater = WaterHeaterShare.ForHour(row, kwh, cost.Actual?.Cost)`. Hours without a row keep `null`.
  - **When the flag is false:** don't query (research R6).
  - Update the doc comment.
- [X] T007 Update the three tests that construct `new HourlyCostProvider(...)`, adding
  `Substitute.For<IWaterHeaterPowerSampleRepository<WaterHeaterPowerSample>>()` and a `TimeProvider`. Use the test
  file's existing `FixedTimeProvider` where there is one, otherwise `TimeProvider.System`. Behaviour is unchanged.
  The files are:
  - `tests/Features.Tests/ElectricityCost/MonthlyBillHandlerTests.cs`
  - `tests/Features.Tests/ElectricityCost/NorgesPrisSavingTests.cs`
  - `tests/Features.Tests/Pricing/CapAndExemptionTests.cs`

  Also search `tests/` for other places that build the provider or substitute it for DI (for example a
  ServiceCollection test), and add the two dependencies there too.
- [X] T008 Add provider tests to `tests/Features.Tests/ElectricityCost/CostHandlerTests.cs`, or to a new
  `HourlyCostProviderWaterHeaterTests.cs` next to it if the provider has no tests yet:
  - **With the flag:** the substitute returns rows for two of three hours. Those two hours get a `WaterHeater` from
    `ForHour`, and the third gets `null`.
  - **Without the flag:** `GetHourlyEnergyAsync` is never received (`DidNotReceive`).
  - **`nowUtc`:** the value passed in is the `TimeProvider`'s time.

**Checkpoint**: `dotnet build && dotnet test` pass. No response has changed yet.

---

## Phase 3: User Story 1, every hour and day says how much was water heating (P1) 🎯 MVP

**Goal**: `waterHeater` on each item of `GET /api/electricity-cost/hourly` and `/daily` (FR-002 to FR-005, FR-007,
FR-008).

**Independent Test**: quickstart §2 and §3.1. For Hjemme on 2026-11-20, the hourly items carry `waterHeater`, and the
daily item's values are the sums of the hourly ones. A location without a heater has `waterHeater: null`.

### Tests for User Story 1

- [X] T009 [US1] Extend `tests/Features.Tests/ElectricityCost/CostHandlerTests.cs`. Construct the handlers' provider
  with a substituted sample repository and set up rows. Then check:
  - **Hourly:** an item with heater rows has `WaterHeater` with `Kwh`, `Cost`, `SampledMinutes` and `Capped` from
    `ForHour`.
  - **Hourly:** an item without rows has `WaterHeater == null`, not an object of zeros.
  - **Daily:** `WaterHeater` holds the sums of the day's hours. Cost is null when one sampled hour has no price.
    A day with no rows is `null`.
  - **Regression (FR-008):** every existing field (`ConsumptionKwh`, `Actual`, `Comparison`, `PricingModel`) is equal
    to the same call with no heater rows.
  - **Not visible:** a location the user can't see still gives `Location.NotFound`, and the sample repository is not
    called (FR-007).

### Implementation for User Story 1

- [X] T010 [P] [US1] In `src/Features/ElectricityCost/Queries/GetHourlyCostQuery.cs`, add
  `public record WaterHeaterHourResponse(decimal Kwh, decimal? Cost, int SampledMinutes, bool Capped);`. Add
  `WaterHeaterHourResponse? WaterHeater` as the **last** parameter of `HourlyCostItemResponse`, so it serializes
  last, as in the contract.
- [X] T011 [P] [US1] In `src/Features/ElectricityCost/Queries/GetDailyCostQuery.cs`, add
  `public record WaterHeaterDayResponse(decimal Kwh, decimal? Cost, int SampledMinutes, bool Capped);`. Add
  `WaterHeaterDayResponse? WaterHeater` as the last parameter of `DailyCostItemResponse`.
- [X] T012 [US1] Map the hourly and daily parts in `src/Features/ElectricityCost/Mappers/CostMapper.cs` (after T010
  and T011):
  - **Hourly:** `ToHourlyResponse` maps `hour.WaterHeater` to a `WaterHeaterHourResponse`, or `null`.
  - **Daily:** `ToDailyResponse` maps `WaterHeaterShare.Sum(hours.Select(h => h.WaterHeater))` to a
    `WaterHeaterDayResponse`, or `null`. First and last hour are not in the day response.
  - Fix every other construction of these two records so the build passes. Search `src/` and `tests/`.
- [X] T013 [US1] Pass `withWaterHeater: true` to `hourlyCostProvider.GetAsync` in
  `src/Features/ElectricityCost/Handlers/GetHourlyCostQueryHandler.cs` (line ~38) and in
  `src/Features/ElectricityCost/Handlers/GetDailyCostQueryHandler.cs` (line ~41). Do **not** change
  `GetNorgesPrisSavingQueryHandler.cs`.

**Checkpoint**: T009 passes, and `/hourly` and `/daily` match [contracts/endpoints.md](contracts/endpoints.md).

---

## Phase 4: User Story 2, the monthly bill says how much was water heating (P1)

**Goal**: `waterHeater` on `GET /api/electricity-cost/bill`, from the hours so far, never projected, and with no other
amount changed (FR-006).

**Independent Test**: quickstart §3.2. `bill?month=2026-11` carries `waterHeater`, its cost is the sum of the month's
hourly heater costs, and `strom`, `nettleie` and `total` are identical to `main`.

### Tests for User Story 2

- [X] T014 [US2] Extend `tests/Features.Tests/ElectricityCost/MonthlyBillHandlerTests.cs`, using the sample substitute
  from T007:
  - **An Estimate month (now mid-month):**
    - `WaterHeater` sums only the `soFar` hours. Hours from the current hour on are not included, even if rows exist
      for them.
    - `Estimate` is `false`.
    - `FirstHourUtc` and `LastHourUtc` are the first and last hours with a part.
    - `Strom`, `Nettleie` and `Total` equal the same call without heater rows.
  - **An ended month (Final):** covers all its hours.
  - **A month without rows:** `WaterHeater == null`.
  - **The projection:** it is never asked for heater rows. `GetHourlyEnergyAsync` is received exactly once, for
    `[monthStart, soFarEnd)`.

### Implementation for User Story 2

- [X] T015 [P] [US2] In `src/Features/ElectricityCost/Queries/GetMonthlyBillQuery.cs`, add
  `public record WaterHeaterBillResponse(decimal Kwh, decimal? Cost, int SampledMinutes, bool Capped, DateTime FirstHourUtc, DateTime LastHourUtc, bool Estimate);`.
  Add `WaterHeaterBillResponse? WaterHeater` as the last parameter of `MonthlyBillResponse`.
- [X] T016 [US2] Pass the heater part to the bill (after T015):
  - In `src/Features/ElectricityCost/Handlers/GetMonthlyBillQueryHandler.cs`, call
    `hourlyCostProvider.GetAsync(location, monthStartUtc, soFarEndUtc, ct, withWaterHeater: true)` for `soFar` only.
    Leave `ProjectAsync` and its provider use unchanged.
  - Compute `WaterHeaterShare.Sum(soFar.Select(h => h.WaterHeater))`.
  - Add a `WaterHeaterTotal? waterHeater` parameter to `BillMapper.ToResponse` in
    `src/Features/ElectricityCost/Mappers/BillMapper.cs`. Map it to a `WaterHeaterBillResponse` with
    `Estimate: false`, or `null`.
  - `MonthlyBillBuilder` is unchanged.
  - Fix other `MonthlyBillResponse` and `ToResponse` call sites in `src/` and `tests/`.

**Checkpoint**: T014 passes, and the bill matches [contracts/endpoints.md](contracts/endpoints.md).

---

## Phase 5: Polish and checks

- [X] T017 Run `dotnet build`, `dotnet test` and `dotnet format --verify-no-changes` from the repo root. The build has
  no new warnings. The test count is the T001 count plus the new tests.
- [X] T018 Check that no unintended change slipped in. Use `git diff main -- src/Features/ElectricityCost` and
  `grep -rn "withWaterHeater" src`. Only the hourly, daily and bill handlers pass `true`. `/current`, `/consumption`,
  `/prices`, `/capacity` and `/saving` are untouched (contracts, "Unchanged").
- [ ] T019 (Partly done on 2026-11-20: the SQL, both as raw SQL and through EF Core, gives the hand-worked values on
  synthetic samples in the local database, in a rolled-back transaction. It covers the clip at `from`, the split at an
  hour boundary, the 2-minute limit on a gap, the clip at now and a duplicate sample. Still to do: the real samples.)
  Manual: quickstart §2. Check the SQL against Hjemme's real samples on a production copy:
  - An hour of steady heating: within 2 % (SC-002).
  - An hour with a gap: `sampledMinutes` below 60.
  - Two hours on either side of a boundary that one hold crosses: the parts add up to the whole hold.
  - `kwh` within 0,001 of the hand sum.
- [ ] T020 Manual: quickstart §3 and §4.
  - **Day:** day sums equal the hourly sums.
  - **Bill:** the bill's cost equals the hourly sum (SC-005), and the other amounts equal `main`.
  - **Speed:** a year of `/daily` takes at most 1,5 × `main` (SC-004). If it misses, stop and choose a research R2
    fallback with Roy before going further.
  - **No heater:** a location without one gives `waterHeater: null` everywhere.
- [ ] T021 [P] Update the API's backlog and notes, if the repo keeps them (`specs/_backlog/`), to say that spec 012 is
  ready for the frontend spec 010. Don't commit unless asked.

---

## Dependencies and execution order

- **Phase 1 → Phase 2 → (US1 ∥ US2) → Phase 5.**
- **Inside Phase 2:**
  - T002, T003 and T004 run in parallel. T004 tests T003.
  - T005 needs T002. T006 needs T002, T003 and T005. T007 and T008 need T006.
- **US1:** T009 first (it fails until T012/T013). T010 and T011 run in parallel. Then T012, then T013.
- **US2:** T014 first. Then T015, then T016. It doesn't depend on US1, but both stories touch `CostHandlerTests.cs` or
  `MonthlyBillHandlerTests.cs` only within their own story.
- **Shared files:** T012 and T016 both may touch test files that build response records. Run them one after the
  other, not in parallel.

## Parallel examples

```text
Phase 2:  T002 (WaterHeaterHourEnergy.cs)  ∥  T003 (WaterHeaterShare.cs)  ∥  T004 (WaterHeaterShareTests.cs)
US1:      T010 (GetHourlyCostQuery.cs)  ∥  T011 (GetDailyCostQuery.cs)
US2:      T015 (GetMonthlyBillQuery.cs) can run alongside US1's T010/T011
```

## Implementation strategy

1. **MVP = Phases 1 to 3:** the hourly and daily split. That is enough for the frontend's usage chart, Heater tab and
   CSV export.
2. **Then Phase 4:** the bill.
3. **Then Phase 5:** T019 and T020 need the deployed API or a production copy with real samples. Do them before the
   frontend spec 010 relies on the numbers.
4. **Commit after each phase only when Roy asks.** Suggested messages: "Compute the water heater's kWh per hour",
   "Split hourly and daily cost into water heating and the rest", "Show water heating on the monthly bill".
