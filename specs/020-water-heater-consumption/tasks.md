---
description: "Task list for spec 020: hot water used per hour, day and month"
---

# Tasks: Hot Water Used per Hour, Day and Month

**Input**: Design documents from `specs/020-water-heater-consumption/`

**Prerequisites**: [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md), [data-model.md](data-model.md),
[contracts/endpoints.md](contracts/endpoints.md), [quickstart.md](quickstart.md)

**Tests**: Included. The plan's Testing section and quickstart §1 define them: xUnit + NSubstitute in
`tests/Features.Tests`.

**Organization**: by user story. All three are P1.
- **US1** (store the litres) ships on its own in the first PR, so history starts as early as possible.
- **US2** (the water used per period) and **US3** (who may read it) follow in a second PR. US3 only adds the access
  check and its tests to US2's handler.
- **Calibration** (Phase 6) needs 3–5 days of US1's data in production. It can run while US2/US3 are being built, but
  the second PR is finished only after it.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: The user story the task belongs to (US1, US2, US3)

---

## Phase 1: Setup

**Purpose**: none needed. The projects, the `Features/WaterHeaters` slice, the sample table and the OSO client all
exist (specs 011, 012, 017). Work starts on branch `feature/020-water-heater-consumption`.

---

## Phase 2: Foundational (blocking prerequisites)

**Purpose**: none shared by all stories. US1's column is the data US2 reads, so US1 itself is the prerequisite for
US2. See Dependencies.

---

## Phase 3: User Story 1 - Keep a record of the hot water in the tank (Priority: P1) 🎯 MVP, PR 1

**Goal**: Every heater sample stores the litres of 40 °C water OSO reports (`AvailableLitres40`) next to the power.
If the litres can't be read, the power is still stored, and a rate-limited log line says why (FR-001, FR-002,
research R1–R3).

**Independent Test**: Let the sampler run locally against the real heater for 15 minutes, and run a hot tap for a few
minutes in the middle. `SELECT "SampledAtUtc", "PowerKw", "AvailableLitres40" … ORDER BY 1` shows the litres falling
while the tap ran, with the power next to them. Older rows have `NULL`.

### Tests for User Story 1

- [X] T001 [P] [US1] In `tests/Features.Tests/WaterHeaters/OsoWaterHeaterClientTests.cs`, replace the
  `GetActualLoadAsync` tests with `GetSampleAsync` tests, using `OsoStubHandler` (its `Status` answer is
  `OsoSamples.Status`):
  - **Both calls answer:** `PowerKw` is `ActualLoad`'s value, `Litres40` is the `availableMix40Volume` in
    `OsoSamples.Status`, and `LitresError` is null.
  - **`Status` gives 500:** the sample succeeds, with the power kept, `Litres40` null and `LitresError` =
    `WaterHeaterErrors.Unavailable`.
  - **`Status` gives `{}`, or has `availableMix40Volume` null or negative:** `Litres40` is null and `LitresError` is
    set.
  - **`ActualLoad` gives 401:** the result fails with `WaterHeater.Rejected`, as the old load test did.
  - **Both calls go out:** the stub saw both `ActualLoad` and `Status`.
- [X] T002 [P] [US1] Update `tests/Features.Tests/WaterHeaters/WaterHeaterSamplingServiceTests.cs`:
  - The constructor stubs `_client.GetSampleAsync(...)` to return
    `Result.Success(new WaterHeaterSample(0.011774m, 287, null))`, and `Fail` stubs `GetSampleAsync` to fail.
  - The existing tests keep their meaning.
  - New: the inserted sample has `AvailableLitres40` 287.
  - New: a sample with `Litres40` null and a `LitresError` is still inserted, with its power and `AvailableLitres40`
    null.
- [X] T003 [P] [US1] In `tests/Features.Tests/WaterHeaters/NoSecretsInLogsTests.cs`, call `GetSampleAsync` where
  `GetActualLoadAsync` was called. Add a case where `Status` fails: the captured log text still contains neither the
  device id nor the key.

### Implementation for User Story 1

- [X] T004 [P] [US1] Add `public int? AvailableLitres40 { get; private set; }` to
  `src/Domain/Common/Entities/WaterHeaterPowerSample.cs`.
  - The constructor gets an optional last argument `int? availableLitres40 = null`, which sets it.
  - Update the class XML doc: the sample now also holds the litres of 40 °C water in the tank, which may be missing
    (spec 020).
- [X] T005 [US1] In `src/Infrastructure/Database/Configuration/WaterHeaterPowerSampleConfiguration.cs`, add
  `builder.Property(s => s.AvailableLitres40);`. It is optional, with no default. Then create the migration (depends on
  T004):
  ```bash
  dotnet ef migrations add AddWaterHeaterLitres \
    --project src/Infrastructure/Infrastructure.csproj \
    --startup-project src/api/api.csproj \
    --output-dir Database/Migrations
  ```
  Check the generated `Up`: one `AddColumn<int>("AvailableLitres40", "WaterHeaterPowerSample", nullable: true)`, with
  no default and no data change. `Down` drops it. Commit the migration, its Designer file and the updated
  `ApplicationDbContextModelSnapshot.cs`.
- [X] T006 [P] [US1] Create `src/Application/WaterHeaters/WaterHeaterSample.cs`:
  `public sealed record WaterHeaterSample(decimal PowerKw, int? Litres40, Error? LitresError);`. Its XML doc says:
  - it is what one sampler call reads from OSO (spec 020, research R2);
  - `Litres40` is null when `Status` failed or had no number, and `LitresError` then says why.
- [X] T007 [US1] In `src/Application/Common/Interfaces/External/IWaterHeaterClient.cs`, replace
  `GetActualLoadAsync` with
  `Task<Result<WaterHeaterSample>> GetSampleAsync(WaterHeaterCredentials credentials, CancellationToken cancellationToken);`.
  Its doc says: the power and the litres of 40 °C water, read in parallel. The call fails only if the power can't be
  read. Depends on T006.
- [X] T008 [US1] Implement `GetSampleAsync` in `src/Infrastructure/External/OsoWaterHeaterClient.cs` and remove
  `GetActualLoadAsync`. Depends on T007.
  - Start `GetAsync<ActualLoadDto>(…, "ActualLoad", ct)` and `GetAsync<StatusDto>(…, "Status", ct)` and await both
    with `Task.WhenAll`.
  - If the load failed, return its failure.
  - Otherwise `litres = status.IsSuccess ? status.Value.AvailableMix40Volume : null`, where a negative value counts
    as null.
  - `litresError` is `status.Error` when the status failed, or `WaterHeaterErrors.InvalidResponse` when it succeeded
    without a usable number. Otherwise it is null.
  - Return `new WaterHeaterSample(load.ActualLoad, litres, litresError)`.
  - `GetAsync` already logs request failures without the URL. Add nothing that logs the device id or key.
- [X] T009 [P] [US1] Add three `LoggerMessage` delegates and their public methods to
  `src/Features/WaterHeaters/Logging/LogMessages.cs`, next to 1830–1832 and in the same style (research R3):
  - **1834 `WaterHeaterLitresFailing`** (Warning):
    `"Water heater litres of location {LocationId} could not be read (reason: {Reason}); power is still sampled"`
  - **1835 `WaterHeaterLitresStillFailing`** (Warning):
    `"Water heater litres of location {LocationId} still not read (reason: {Reason}) after {Minutes} minutes"`
  - **1836 `WaterHeaterLitresRecovered`** (Information):
    `"Water heater litres of location {LocationId} read again after {Minutes} minutes of failures"`
- [X] T010 [US1] Update `src/Features/WaterHeaters/Services/WaterHeaterSamplingService.cs`. Depends on T004, T008 and
  T009.
  - `ReadPowerAsync` becomes `ReadSampleAsync`, returning `Result<WaterHeaterSample>` from `client.GetSampleAsync`.
    The key-unreadable path returns `Result.Failure<WaterHeaterSample>(key.Error)`.
  - On success, insert `new WaterHeaterPowerSample(Guid.NewGuid(), heater.LocationId, now, sample.PowerKw,
    sample.Litres40)`.
  - Add a second `SampleFailureTracker _litresTracker`:
    - on success with `Litres40` set, call `_litresTracker.Succeeded`;
    - with `LitresError` set, call `_litresTracker.Failed`;
    - log through a `LogLitres` method that mirrors `Log`, using 1834–1836.
  - When the whole sample fails, the litres tracker is not touched: the power log covers it.
  - Update the class XML doc: it also stores the litres of 40 °C water (spec 020), and losing them never costs the
    power sample.
- [X] T011 [US1] Run `dotnet build` and
  `dotnet test tests/Features.Tests --filter "FullyQualifiedName~WaterHeater"`. Make T001–T003 pass, and fix every
  other compile error from the removed `GetActualLoadAsync` (`grep -rn GetActualLoadAsync src tests` must be empty).
- [X] T012 [US1] Run US1's Independent Test locally (`dotnet ef database update`, then let the sampler run against the
  real heater). Check that the litres change while a hot tap runs, and that `/water-heater/power` and
  `/electricity-cost/hourly` answer as before for the same hours (FR-010).

**Checkpoint**: US1 works. Run `dotnet format --verify-no-changes`, commit, open PR 1, and deploy it. The litres are
now being collected in production. From here, Phase 6's calibration can start once 3–5 days have passed.

---

## Phase 4: User Story 2 - Hot water used per hour, day or month for a period (Priority: P1), PR 2

**Goal**: `GET /api/locations/{locationId}/water-heater/hot-water?period=&date=` gives the period's items (`start`,
`litres`, `sampledMinutes`), `totalLitres` and `sampledMinutes`, with the counting rule of FR-004 and the shapes of
[contracts/endpoints.md](contracts/endpoints.md).

**Independent Test**: With quickstart §3's seeded samples, `?period=day&date=<that day>` shows about 52 litres and
30 sampled minutes in the seeded hour. Hours without samples are `null`, and later hours are left out when the day is
today. `?period=week|month|year` give 7, 28–31 and 12 items (or fewer up to now). `?period=hour` gives 400.

### Tests for User Story 2

- [ ] T013 [P] [US2] Write `tests/Features.Tests/WaterHeaters/HotWaterPeriodsTests.cs` (pure, no substitutes):
  - **`Bounds`:**
    - `Day` 2026-11-21 is 2026-11-20T23:00Z … 2026-11-21T23:00Z;
    - `Week` 2026-12-31 (a Thursday) is Monday 2026-12-28 … Monday 2027-01-04, Oslo midnights in UTC;
    - `Month` 2026-02-10 is 2026-01-31T23:00Z … 2026-02-28T23:00Z;
    - `Year` 2026-06-01 is 2025-12-31T23:00Z … 2026-12-31T23:00Z.
  - **`PartStarts`:**
    - `Day` 2026-03-29 has 23 hours, and `Day` 2026-10-25 has 25;
    - `Week` has 7 Oslo midnights, including the one after the 25 October change;
    - `Month` of November 2026 has 30;
    - `Year` has 12 Oslo firsts of the month.
  - **`TodayInOslo`:** 2026-11-20T23:30Z is 2026-11-21.
- [ ] T014 [P] [US2] Write the mapping cases in `tests/Features.Tests/WaterHeaters/GetWaterHeaterHotWaterHandlerTests.cs`.
  - **Setup:**
    - substitute `ILocationRepository<Location>` with `IsUserAssociatedAsync` → true, and
      `IWaterHeaterPowerSampleRepository<WaterHeaterPowerSample>`;
    - use `FixedTimeProvider` (from `tests/Features.Tests/NorgesPris`) at 2026-11-21T12:30:00Z;
    - use `WaterHeaterTestData.LocationId`/`UserId`.
  - **Cases:**
    - **Day up to now:** `Day` for 2026-11-21 has 14 items (Oslo 00:00 to 13:00, which starts at 12:00Z, before now).
      Hours with rows have `litres` rounded half away from zero (37.5 → 38, 0.4 → 0); the others have `null` and
      `sampledMinutes` 0.
    - **Sampled minutes:** 3599.9 seconds is 59 minutes. 3700 seconds is capped at 60 (data-model note on the
      bucket).
    - **Days:** `Week` rolls several UTC hours into their Oslo day, with the sum before rounding.
    - **Total:** `totalLitres` is the sum of the rounded items. It is `null` when no hour has rows, or when the
      period is wholly in the future (`items` empty).
    - **The repository call:** it gets the period's `from` and `to`, `now`, and `HotWaterRule.Current`.
    - **Defaults:** `Period = Day` with `Date = null` uses today in Oslo.
- [ ] T015 [P] [US2] Write `tests/Features.Tests/Http/WaterHeaterHotWaterHttpTests.cs` with `EndpointHost.StartAsync`
  (pattern: `tests/Features.Tests/Http/WaterHeaterPowerHttpTests.cs`), URL `/api/locations/{id}/water-heater/hot-water`:
  - **The JSON shape** is camelCase: `locationId`, `period`, `from`, `to`, `totalLitres`, `sampledMinutes`, and
    `items[].start`, `litres`, `sampledMinutes`. A `null` litres is written as `null`, not left out.
  - **The query:** `?period=Week&date=2026-11-21` reaches `GetWaterHeaterHotWaterQuery` as `HotWaterPeriod.Week` and
    `DateOnly(2026, 11, 21)`, with `IsAdmin` for `HostCaller.Admin`. With no query string it gets `Day` and `null`.
  - **400 from the validator** for `period=hour`, `date=2026-02-30` and `date=21.11.2026`. The dispatcher is not
    called.
  - **401** for `HostCaller.Anonymous`.

### Implementation for User Story 2

- [ ] T016 [P] [US2] Create `src/Application/WaterHeaters/WaterHeaterHotWater.cs` with two records:
  - `public sealed record HotWaterRuleValues(decimal KwhPerLitre, int MinFallLitres);`
  - `public sealed record WaterHeaterHotWaterHour(DateTime HourStartUtc, decimal Litres, decimal SampledSeconds);`,
    whose property names are the SQL column aliases, as for `WaterHeaterPowerStep`.

  XML docs per data-model.md.
- [ ] T017 [US2] Add `GetHourlyHotWaterAsync` to
  `src/Application/Common/Interfaces/Repositories/IWaterHeaterPowerSampleRepository.cs`, and implement it in
  `src/Infrastructure/Database/Repositories/WaterHeaterPowerSampleEfRepository.cs` (research R4). Depends on T004 and
  T016.
  - **The signature:**
    `Task<IReadOnlyList<WaterHeaterHotWaterHour>> GetHourlyHotWaterAsync(Guid locationId, DateTime fromUtc,
    DateTime toUtc, DateTime nowUtc, HotWaterRuleValues rule, CancellationToken ct);`
  - **The doc:** pairs of consecutive samples, both with litres and at most 2 minutes apart. Each pair counts
    `fall + heated` when the litres fell by at least `MinFallLitres`, otherwise 0, in the UTC hour of the later sample.
    Only hours with counted pairs are returned, ordered.
  - **The SQL,** with every value an interpolated parameter, as in `QueryStepsAsync`:
    ```sql
    WITH s AS (
        SELECT "SampledAtUtc" AS t, "AvailableLitres40" AS l,
               LAG("SampledAtUtc")      OVER (ORDER BY "SampledAtUtc") AS pt,
               LAG("PowerKw")           OVER (ORDER BY "SampledAtUtc") AS pp,
               LAG("AvailableLitres40") OVER (ORDER BY "SampledAtUtc") AS pl
        FROM public."WaterHeaterPowerSample"
        WHERE "LocationId" = {locationId}
          AND "SampledAtUtc" >= {fromUtc} - interval '2 minutes'
          AND "SampledAtUtc" < LEAST({toUtc}, {nowUtc})
    ),
    pairs AS (
        SELECT date_trunc('hour', t, 'UTC') AS h,
               EXTRACT(EPOCH FROM t - pt) AS secs,
               CASE WHEN pl - l >= {rule.MinFallLitres}
                    THEN (pl - l) + pp * EXTRACT(EPOCH FROM t - pt) / 3600 / {rule.KwhPerLitre}
                    ELSE 0 END AS used
        FROM s
        WHERE t >= {fromUtc} AND l IS NOT NULL AND pl IS NOT NULL
          AND t > pt AND t - pt <= interval '2 minutes'
    )
    SELECT h AS "HourStartUtc", SUM(used) AS "Litres", SUM(secs) AS "SampledSeconds"
    FROM pairs GROUP BY h ORDER BY h
    ```
  - **The mapping:** map `HourStartUtc` to `DateTimeKind.Utc`.
  - **The remark:** add a `<remarks>` explaining each CTE, as `QueryStepsAsync` has. Update the class remark: the
    repository now holds two raw SQL queries (spec 020, plan Complexity Tracking).
- [ ] T018 [P] [US2] Create `src/Features/WaterHeaters/Services/HotWaterRule.cs`:
  `public static class HotWaterRule { public static readonly HotWaterRuleValues Current = new(0.035m, 2); }`.
  Its XML doc gives the source of each value (research R5) and says that calibration (research R6, "Calibration
  results") is where they are checked and changed.
- [ ] T019 [US2] Create `src/Features/WaterHeaters/Services/HotWaterPeriods.cs` (research R7). It has its own
  `private static readonly TimeZoneInfo Oslo = TimeZoneInfo.FindSystemTimeZoneById("Europe/Oslo");`, in the style of
  `CostTime`, not a reference to it.
  - `public static DateOnly TodayInOslo(DateTime nowUtc)`.
  - `public static (DateTime FromUtc, DateTime ToUtc) Bounds(HotWaterPeriod period, DateOnly date)`:
    - day: the date;
    - week: back to Monday (`(7 + (int)date.DayOfWeek - 1) % 7` days), plus 7 days;
    - month: the 1st, plus 1 month;
    - year: 1 January, plus 1 year.

    Each local midnight is converted with `TimeZoneInfo.ConvertTimeToUtc` (kind Unspecified), and the result is
    `DateTimeKind.Utc`.
  - `public static IReadOnlyList<DateTime> PartStarts(HotWaterPeriod period, DateOnly date)`:
    - day: every UTC hour from `FromUtc` to `ToUtc`;
    - week and month: every Oslo midnight in the period;
    - year: the 12 Oslo firsts of the month.

    All in UTC.
  - Depends on T020 for the `HotWaterPeriod` enum.
- [ ] T020 [US2] Add to `src/Features/WaterHeaters/Queries/WaterHeaterQueries.cs`:
  - `public enum HotWaterPeriod { Day, Week, Month, Year }`
  - `public sealed record GetWaterHeaterHotWaterQuery(Guid UserId, bool IsAdmin, Guid LocationId, HotWaterPeriod Period,
    DateOnly? Date) : IQuery<Result<WaterHeaterHotWaterResponse>>;`
  - `public sealed record WaterHeaterHotWaterResponse(Guid LocationId, string Period, DateTime From, DateTime To,
    int? TotalLitres, int SampledMinutes, IReadOnlyList<WaterHeaterHotWaterItem> Items);`
  - `public sealed record WaterHeaterHotWaterItem(DateTime Start, int? Litres, int SampledMinutes);`

  Short XML docs pointing to spec 020 and the contract.
- [ ] T021 [US2] Add the roll-up to `src/Features/WaterHeaters/Mappers/WaterHeaterMapper.cs`. Depends on T016 and
  T020.
  - The signature: `public static IReadOnlyList<WaterHeaterHotWaterItem> ToHotWaterItems(IReadOnlyList<DateTime>
    partStarts, DateTime toUtc, DateTime nowUtc, IReadOnlyList<WaterHeaterHotWaterHour> hours)`.
  - **Parts:** for each part start before `nowUtc`, the part ends at the next start, or at `toUtc` for the last.
  - **Litres:** the hours with `HourStartUtc` in `[start, end)` are summed: `Σ Litres` and `Σ SampledSeconds`.
    - When there are none, `Litres` is null and `SampledMinutes` 0.
    - Otherwise `Litres = (int)Math.Round(Σ, MidpointRounding.AwayFromZero)`.
  - **Sampled minutes:** `SampledMinutes = Math.Min((int)(Σ seconds / 60), (int)(end - start).TotalMinutes)`.
  - Add `public static string ToWire(HotWaterPeriod p) => p.ToString().ToLowerInvariant();` for the response's
    `Period`.
- [ ] T022 [US2] Create `src/Features/WaterHeaters/Handlers/GetWaterHeaterHotWaterQueryHandler.cs`. Depends on T017,
  T018, T019 and T021.
  - **The constructor:** primary, taking `ILocationRepository<Location> locations`,
    `IWaterHeaterPowerSampleRepository<WaterHeaterPowerSample> samples`, `TimeProvider clock` and
    `ILogger<GetWaterHeaterHotWaterQueryHandler> logger`.
  - **What `Handle` does:**
    - `now = clock.GetUtcNow().UtcDateTime`;
    - `date = query.Date ?? HotWaterPeriods.TodayInOslo(now)`;
    - get `Bounds` and `PartStarts`;
    - `hours = samples.GetHourlyHotWaterAsync(query.LocationId, from, to, now, HotWaterRule.Current, ct)`;
    - `items = WaterHeaterMapper.ToHotWaterItems(...)`;
    - `total` is the sum of the items' non-null `Litres`, or null if none have litres;
    - `sampledMinutes` is the sum of the items' `SampledMinutes`.
  - **The response:** `WaterHeaterHotWaterResponse`.
  - **The class doc:** it reads only, needs no connected heater, and doesn't re-validate (Principle IV).
  - **Access:** added in T026. Leave a clear spot for it at the top of `Handle`.
- [ ] T023 [US2] Create `src/Features/WaterHeaters/Endpoints/GetWaterHeaterHotWaterEndpoint.cs`. Depends on T020.
  - **The request:** `public record GetWaterHeaterHotWaterRequest(Guid LocationId, string? Period, string? Date);`,
    with `<param>` docs.
  - **Configuration:**
    - `Get("/api/locations/{locationId}/water-heater/hot-water")`;
    - `Tags("WaterHeater")` and `Description(b => b.WithTags("WaterHeater"))`;
    - `AuthSchemes(JwtBearerDefaults.AuthenticationScheme)`.
  - **The summary:** "Litres of 40 °C water used per hour, day or month". The description is condensed from the
    contract: the periods and their items, `null` = not sampled, the total of the items, and owners, viewers and
    Admins.
  - **Responses:** 200, 400, 401 and 404.
  - **`HandleAsync`:**
    - parse `Period` with `Enum.Parse<HotWaterPeriod>(req.Period ?? "Day", ignoreCase: true)`;
    - parse `Date` with `DateOnly.ParseExact(req.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture)` when given;
    - both are safe because the validator already refused bad values;
    - send the query as `GetWaterHeaterPowerEndpoint` does;
    - map a failure with `AddError` + `ThrowIfAnyErrors(WaterHeaterErrorStatus.Of(result.Error, onConnect: false))`.
- [ ] T024 [US2] Create `src/Features/WaterHeaters/Validators/GetWaterHeaterHotWaterValidator.cs`:
  - `Validator<GetWaterHeaterHotWaterRequest>`;
  - `LocationId` is `NotEmpty()`;
  - `Period`, when given, must be one of the four, ignoring case. Message:
    `"period must be day, week, month or year"`, error code `Validation.InvalidPeriod`;
  - `Date`, when given, must parse with `DateOnly.TryParseExact(…, "yyyy-MM-dd", CultureInfo.InvariantCulture, …)`.
    Message: `"date must be a real date as yyyy-MM-dd"`, error code `Validation.InvalidDate`.

  Depends on T023 (the request record).
- [ ] T025 [US2] Run `dotnet test tests/Features.Tests --filter "FullyQualifiedName~HotWater"` and make T013–T015
  pass.

**Checkpoint**: US2 works locally. The endpoint answers for any signed-in caller (access comes in US3). Quickstart §3's
seeded hour gives about 52 litres and 30 sampled minutes.

---

## Phase 5: User Story 3 - Only people who may see the location (Priority: P1), PR 2

**Goal**: The owner, viewers and Admins get the answer. Anyone else, and a location that does not exist, get the same
`404 Location.NotFound`. An inactive location answers only to Admins (research R8).

**Independent Test**: As a viewer of Hjemme, read today and get 200. As a user without access, and with a made-up
location id, get the same 404. Without a token, get 401 (already covered by T015).

### Tests for User Story 3

- [ ] T026 [US3] Add access cases to `tests/Features.Tests/WaterHeaters/GetWaterHeaterHotWaterHandlerTests.cs`, as in
  `GetWaterHeaterPowerHandlerTests`:
  - **Allowed:**
    - a user with `IsUserAssociatedAsync` → true gets 200;
    - an Admin with `GetByIdAsync` → an active location gets 200, and so does one with an inactive location.
  - **Refused:**
    - a user with `IsUserAssociatedAsync` → false gets `Location.NotFound`, and `GetHourlyHotWaterAsync` is not
      called;
    - an Admin with `GetByIdAsync` → null gets `Location.NotFound`;
    - the user's error and the missing location's error are identical.
- [ ] T027 [P] [US3] Add to `tests/Features.Tests/Http/WaterHeaterHotWaterHttpTests.cs`: a handler failure with
  `Error.NotFound("Location.NotFound", "Location not found")` maps to 404 with that code.

### Implementation for User Story 3

- [ ] T028 [US3] Add the access check at the top of `Handle` in
  `src/Features/WaterHeaters/Handlers/GetWaterHeaterHotWaterQueryHandler.cs`. Copy it from
  `GetWaterHeaterPowerQueryHandler`:
  - `mayRead` is `IsAdmin ? GetByIdAsync(...) is not null : IsUserAssociatedAsync(...)`;
  - when false, call `LogMessages.LocationAccessDenied` (EventId 1805) and return
    `Error.NotFound("Location.NotFound", "Location not found")`.

  Update the class doc with the rule. Then make T026–T027 pass.

**Checkpoint**: US2 and US3 work. The whole WaterHeater test filter passes.

---

## Phase 6: Calibration (needs 3–5 days of US1 in production)

**Purpose**: settle the three details the spec left to real data (research R6), and check SC-001 to SC-003, before
PR 2 is finished.

- [X] T029 Follow quickstart §4.1: list the samples around a known shower in production. Decide whether OSO's litres
  change about every minute (keep the pairing) or in steps (switch to runs). If it's runs, add a task to rework the
  `pairs` CTE of T017 as research R6 point 1 describes, before going on.
- [ ] T030 Follow quickstart §4.2: count three quiet nights with `MinFallLitres` 1 and 2. Set
  `HotWaterRule.Current.MinFallLitres` in `src/Features/WaterHeaters/Services/HotWaterRule.cs` to the smallest value
  that keeps every quiet hour at 2 L or less (SC-003).
- [ ] T031 Follow quickstart §4.3: note OSO's `storedEnergyKwh ÷ availableLitres40` at three moments.
  - Compare 5 days' `totalLitres` with the OSO app's "Total v40 used" (SC-001).
  - Adjust `KwhPerLitre` in `src/Features/WaterHeaters/Services/HotWaterRule.cs` only if most days miss in the same
    direction.
- [ ] T032 Follow quickstart §4.4: one shower while heating, against its known litres, with and without the `heated`
  term (SC-002).
- [ ] T033 Write the results under "Calibration results" in `specs/020-water-heater-consumption/research.md`:
  - the dates of the samples used;
  - the values chosen;
  - the day totals against the OSO app;
  - SC-001 to SC-003 met or not.

  Make the XML doc in `HotWaterRule.cs` match.

---

## Phase 7: Polish & Cross-Cutting Concerns

- [ ] T034 [P] If a doc lists the water heater endpoints (`grep -n "water-heater" *.md`), add
  `GET /api/locations/{id}/water-heater/hot-water` with one line and a link to
  `specs/020-water-heater-consumption/contracts/endpoints.md`. Skip it if none does.
- [ ] T035 Run `dotnet build` (no new warnings), `dotnet format --verify-no-changes` and `dotnet test`. Everything must
  pass. Then commit and open PR 2 (US2 + US3 + calibration).
- [ ] T036 After deploying PR 2, follow quickstart §5 in production:
  - SC-004: under 0.3 s for a day and under 1 s for a year;
  - SC-005: 95 % of a day's samples have litres;
  - no 1834–1836 log lines on a normal day;
  - today's items look like the OSO app's Day view.

  Record the results in a short PR note. If a year is slow, apply research R9's covering index as a follow-up.
- [ ] T037 Create `specs/_backlog/water-heater-hot-water-frontend.md`, in the style of
  `specs/_backlog/norgespris-limit-warnings.md`. It is the handover for the frontend's heater tab chart in
  `no.sanddata.ams.frontend`. It says:
  - the API side is built and deployed (spec 020, with the PR numbers);
  - the request is `GET /api/locations/{id}/water-heater/hot-water?period=day|week|month|year&date=yyyy-MM-dd`;
  - the Day/Week/Month/Year switch maps to `period`, and the arrows step `date` back or forward by one period;
  - `totalLitres` is the "Total v40 used" figure;
  - `start` is UTC, so show it in Oslo time;
  - `litres: null` is a gap, never 0;
  - `sampledMinutes` tells how much to trust a bar;
  - history begins on the date US1 was deployed;
  - a link to `specs/020-water-heater-consumption/contracts/endpoints.md`.

  Remove it once the frontend's spec has taken it up.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup / Foundational (Phases 1–2)**: nothing to do.
- **US1 (Phase 3)**:
  - T004 ∥ T006 ∥ T009 first;
  - T005 needs T004; T007 needs T006; T008 needs T007; T010 needs T004, T008 and T009;
  - T001–T003 can be written once T006/T007 define the shapes;
  - T011 then T012 last.
- **US2 (Phase 4)**: needs US1's column (T004, T005), not its deploy.
  - T016 ∥ T018 ∥ T020 first;
  - T017 needs T016; T019 needs T020; T021 needs T016 and T020;
  - T022 needs T017, T018, T019 and T021;
  - T023 needs T020; T024 needs T023;
  - T013–T015 can be written as soon as T019/T020/T023 define the shapes;
  - T025 last.
- **US3 (Phase 5)**: needs US2's handler (T022). T026 → T028. T027 needs T015's file.
- **Calibration (Phase 6)**: needs US1 deployed plus 3–5 days, and US2's endpoint (T022–T024) to read the totals.
  T029 first, because it may change T017's SQL. T030 and T031 can be done together after it. T033 last.
- **Polish (Phase 7)**: T034 any time after T023. T035 needs Phases 4–6. T036 needs PR 2 deployed. T037 last.

### User Story Dependencies

- **US1**: independent. It is the MVP and ships alone, as PR 1.
- **US2**: reads US1's column. It can be built while US1's data collects, and its final numbers come from Phase 6.
- **US3**: adds the access check to US2's handler. It must ship together with US2: an endpoint without it must not
  reach production.

### Parallel Opportunities

- **US1:** T004 ∥ T006 ∥ T009, then T001 ∥ T002 ∥ T003 (tests in different files).
- **US2:** T016 ∥ T018 ∥ T020, then T013 ∥ T014 ∥ T015 (tests), and T019 ∥ T021 ∥ T023.
- **US3:** T027 ∥ T026/T028.
- **Calibration:** T030 ∥ T031 ∥ T032 after T029.
- **Across stories:** all of US2 and US3 can be built while Phase 6 waits for data.

---

## Parallel Example: User Story 2

```text
# After T016 (Application records) and T020 (query, enum and response records) are done:
Task: "T019 [US2] HotWaterPeriods in src/Features/WaterHeaters/Services/HotWaterPeriods.cs"
Task: "T021 [US2] ToHotWaterItems in src/Features/WaterHeaters/Mappers/WaterHeaterMapper.cs"
Task: "T023 [US2] GetWaterHeaterHotWaterEndpoint in src/Features/WaterHeaters/Endpoints/GetWaterHeaterHotWaterEndpoint.cs"
Task: "T013 [US2] HotWaterPeriodsTests in tests/Features.Tests/WaterHeaters/HotWaterPeriodsTests.cs"
Task: "T015 [US2] HTTP tests in tests/Features.Tests/Http/WaterHeaterHotWaterHttpTests.cs"
```

---

## Implementation Strategy

### MVP First (PR 1)

1. Phase 3 (US1): the column, the migration, the client, the sampler and the logs.
2. Stop and validate: T011 tests and T012 locally against the real heater.
3. Merge and deploy at once. Every day without it is history lost.

### Incremental Delivery (PR 2)

1. Build Phases 4 and 5 while the data collects.
2. After 3–5 days, Phase 6: calibrate, and adjust `HotWaterRule` (and the SQL, if T029 says so).
3. Phase 7: tests, PR 2, deploy, the production check, then the frontend backlog note.

---

## Notes

- [P] tasks touch different files and don't depend on incomplete tasks.
- One migration (`AddWaterHeaterLitres`). Three new EventIds (1834–1836). Access refusals reuse
  `LocationAccessDenied` (1805).
- No manual DI registration: the source generator picks up the handler, and the repository and client are already
  registered.
- Commit after each phase. PR 1 ends at Phase 3's checkpoint.
