---
description: "Task list for spec 017: the water heater's power per 5 minutes"
---

# Tasks: The Water Heater's Power over the Last Hours

**Input**: Design documents from `specs/017-water-heater-power/`

**Prerequisites**: [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md), [data-model.md](data-model.md),
[contracts/endpoints.md](contracts/endpoints.md), [quickstart.md](quickstart.md)

**Tests**: Included. The plan's Testing section and quickstart §1 define them: xUnit + NSubstitute in
`tests/Features.Tests`.

**Organization**: by user story. US1 (the 5-minute steps) and US2 (who may read them) are both P1. US2 only adds the
access check and its tests to US1's handler, so it builds on US1.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: The user story the task belongs to (US1, US2)

---

## Phase 1: Setup

**Purpose**: save today's hourly figures before the shared SQL refactor, so FR-006 can be checked afterwards.

- [X] T001 Following quickstart §0, save the hourly response for Hjemme for 2026-11-18 to 2026-11-20 into
  `hourly-before.json`, outside the repo (for example in `/tmp`). Use the current `main` build against the local or
  production database:
  `GET /api/electricity-cost/hourly?locationId=…&from=2026-11-18&to=2026-11-20`, signed in as the owner. Do this
  before T003 changes the SQL. Don't commit the file.

---

## Phase 2: Foundational (blocking prerequisites)

**Purpose**: the step record, the shared step query and the window helper. US1 and US2 both need them.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [X] T002 [P] Create the record `public sealed record WaterHeaterPowerStep(DateTime StepStartUtc, decimal Kwh,
  decimal SampledSeconds);` in `src/Application/WaterHeaters/WaterHeaterPowerStep.cs`.
  - Its XML doc says it is one 5-minute UTC step with any held time: `Kwh` ≥ 0 and 0 < `SampledSeconds` ≤ 300.
  - Like `WaterHeaterHourEnergy`, the property names are the SQL column aliases.
- [X] T003 Refactor `src/Infrastructure/Database/Repositories/WaterHeaterPowerSampleEfRepository.cs` (research R1):
  - Move the SQL of `GetHourlyEnergyAsync` into a private method
    `QueryStepsAsync(Guid locationId, DateTime fromUtc, DateTime toUtc, DateTime nowUtc, TimeSpan step,
    CancellationToken ct)`. It returns rows of a private record `(DateTime StepStartUtc, decimal Kwh,
    decimal SampledSeconds)`, or reuses `WaterHeaterPowerStep` as the row type.
  - In the `parts` CTE, replace every `date_trunc('hour', t, 'UTC')` with
    `date_bin({step}, t, TIMESTAMPTZ '1970-01-01 00:00:00+00')`, and every `+ interval '1 hour'` with `+ {step}`.
    `step` is passed as an interpolated parameter, which Npgsql maps from `TimeSpan` to `interval`.
  - Rename the output alias to `"StepStartUtc"`. Keep the `s` and `held` CTEs unchanged: the 2-minute hold, the
    `LEAST(…, now, to)`, and the 2 minutes read before `from`.
  - Comment on the private method: the split in `parts` assumes `step` ≥ 2 minutes (the longest hold), and
    `date_bin` needs PostgreSQL 14+.
  - `GetHourlyEnergyAsync` calls it with `TimeSpan.FromHours(1)` and maps each row to
    `WaterHeaterHourEnergy(StepStartUtc, Kwh, SampledSeconds)`, with `DateTimeKind.Utc` as today. Its signature,
    XML doc and results stay the same (FR-006).
  - Update the class remark to say the query serves both the hourly and the 5-minute view.
- [X] T004 Add to `src/Application/Common/Interfaces/Repositories/IWaterHeaterPowerSampleRepository.cs`:
  `Task<IReadOnlyList<WaterHeaterPowerStep>> GetPowerStepsAsync(Guid locationId, DateTime fromUtc, DateTime toUtc,
  DateTime nowUtc, CancellationToken ct);`.
  - Its XML doc is the same as `GetHourlyEnergyAsync`, but per 5-minute UTC step ("spec 017, research R1").
  - Implement it in `src/Infrastructure/Database/Repositories/WaterHeaterPowerSampleEfRepository.cs` by calling
    `QueryStepsAsync` with `TimeSpan.FromMinutes(5)`, with `StepStartUtc` as `DateTimeKind.Utc`. Depends on T002
    and T003.
- [X] T005 [P] Create `src/Features/WaterHeaters/Services/PowerWindow.cs` (research R3):
  - `public static class PowerWindow` with `DefaultLength = TimeSpan.FromHours(3)` and
    `MaxLength = TimeSpan.FromHours(48)`.
  - `public static (DateTime FromUtc, DateTime ToUtc) Resolve(DateTime? from, DateTime? to, DateTime nowUtc)`:
    `to` is `to?.ToUniversalTime() ?? nowUtc`, and `from` is `from?.ToUniversalTime() ?? toUtc - DefaultLength`.
  - `public static string? Problem(DateTime fromUtc, DateTime toUtc, DateTime nowUtc)` returns `null` when the window
    is valid, or one of these messages:
    - `"from must be before to"` when `fromUtc >= toUtc`;
    - `"The window can be at most 48 hours"` when `toUtc - fromUtc > MaxLength`;
    - `"from must not be in the future"` when `fromUtc > nowUtc`.
  - It does not round to whole 5 minutes.
- [X] T006 [P] Write `tests/Features.Tests/WaterHeaters/PowerWindowTests.cs` (quickstart §1, `PowerWindow`):
  - defaults: none given gives now−3h…now; only `from` gives `from`…now; only `to` gives `to`−3h…`to`;
  - a `DateTime` with `Kind=Local`, or parsed from `+01:00`, is converted to UTC;
  - `Problem` returns each of the three messages;
  - exactly 48 hours is allowed and 48 h + 1 s is refused;
  - `to` in the future with `from` in the past is allowed.

  Depends on T005.

**Checkpoint**: `dotnet build` passes. The hourly figures still come from the shared query.

---

## Phase 3: User Story 1 - The heater's power per 5 minutes for a time window (Priority: P1) 🎯 MVP

**Goal**: `GET /api/locations/{locationId}/water-heater/power?from=&to=` answers the window's 5-minute steps
(`start`, `powerKw`, `sampledSeconds`), with the default of the last 3 hours and the refusals of FR-004.

**Independent Test**: For Hjemme, read the default window.
- `from`/`to` are now−3h…now.
- Each `start` is on whole 5 minutes, and full steps have `sampledSeconds` 300.
- Heating steps show about the heater's power, and gaps have no item.
- An out-of-range window gives 400.

### Tests for User Story 1

- [X] T007 [P] [US1] Write `tests/Features.Tests/WaterHeaters/GetWaterHeaterPowerHandlerTests.cs`, mapping part:
  - Substitute `ILocationRepository<Location>` with `IsUserAssociatedAsync` → true and
    `IWaterHeaterPowerSampleRepository<WaterHeaterPowerSample>`, and use a fixed `TimeProvider` (e.g. NSubstitute or
    `FakeTimeProvider` if already referenced; otherwise a substitute for `TimeProvider.GetUtcNow()`). Use
    `WaterHeaterTestData.LocationId`/`UserId`. The cases:
  - a step with `Kwh` 0.1693, `SampledSeconds` 300 gives `powerKw` 2.032 and `sampledSeconds` 300;
  - a step with `Kwh` 0.0150, `SampledSeconds` 134.4 gives `powerKw` 0.402 (rounded to 3 decimals) and
    `sampledSeconds` 134;
  - a step with `SampledSeconds` 0.4 is left out;
  - no rows give `200` with `items: []`;
  - the repository is called with the resolved window and now (default: now−3h, now);
  - `from`, `to` and `stepSeconds` 300 are in the response.
- [X] T008 [P] [US1] Write `tests/Features.Tests/Http/WaterHeaterPowerHttpTests.cs` with `EndpointHost.StartAsync`
  (pattern: `tests/Features.Tests/Http/WaterHeaterStateHttpTests.cs`), URL
  `/api/locations/{id}/water-heater/power`:
  - the JSON shape is camelCase `locationId`, `from`, `to`, `stepSeconds`, and `items[].start`, `powerKw`,
    `sampledSeconds`;
  - `from`/`to` query values reach `GetWaterHeaterPowerQuery`, along with `IsAdmin` for `HostCaller.Admin`;
  - 400 from the validator for `from` ≥ `to`, a 49-hour window, and `from` tomorrow (the dispatcher not called);
  - 401 for `HostCaller.Anonymous`.

### Implementation for User Story 1

- [X] T009 [US1] Add to `src/Features/WaterHeaters/Queries/WaterHeaterQueries.cs`:
  - `public sealed record GetWaterHeaterPowerQuery(Guid UserId, bool IsAdmin, Guid LocationId, DateTime? From,
    DateTime? To) : IQuery<Result<WaterHeaterPowerResponse>>;`
  - `public sealed record WaterHeaterPowerResponse(Guid LocationId, DateTime From, DateTime To, int StepSeconds,
    IReadOnlyList<WaterHeaterPowerItem> Items);`
  - `public sealed record WaterHeaterPowerItem(DateTime Start, decimal PowerKw, int SampledSeconds);`

  Each gets a short XML doc pointing to spec 017 and the contract.
- [X] T010 [US1] Add `public static WaterHeaterPowerItem? ToPowerItem(WaterHeaterPowerStep step)` to
  `src/Features/WaterHeaters/Mappers/WaterHeaterMapper.cs` (research R2):
  - `seconds = (int)Math.Round(step.SampledSeconds, MidpointRounding.AwayFromZero)`;
  - return `null` when `seconds` is 0;
  - otherwise `PowerKw = Math.Round(step.Kwh * 3600m / step.SampledSeconds, 3, MidpointRounding.AwayFromZero)`.

  Use the unrounded `SampledSeconds` for the division. Depends on T002 and T009.
- [X] T011 [US1] Create `src/Features/WaterHeaters/Handlers/GetWaterHeaterPowerQueryHandler.cs`:
  - Primary constructor taking `ILocationRepository<Location> locations`,
    `IWaterHeaterPowerSampleRepository<WaterHeaterPowerSample> samples`, `TimeProvider clock` and
    `ILogger<GetWaterHeaterPowerQueryHandler> logger`. It implements
    `IQueryHandler<GetWaterHeaterPowerQuery, Result<WaterHeaterPowerResponse>>`.
  - In this task: `now = clock.GetUtcNow().UtcDateTime`, then `PowerWindow.Resolve(query.From, query.To, now)`, then
    `samples.GetPowerStepsAsync(…, now, ct)`.
  - Map with `WaterHeaterMapper.ToPowerItem` and skip nulls. Return
    `WaterHeaterPowerResponse(locationId, from, to, 300, items)`.
  - The class XML doc says it reads only, it needs no connected heater, and it does not re-validate the window
    (Principle IV).
  - The access check is added in T015. Leave a clear spot for it at the top of `Handle`.

  Depends on T004, T005 and T010.
- [X] T012 [US1] Create `src/Features/WaterHeaters/Validators/GetWaterHeaterPowerValidator.cs`:
  - `Validator<GetWaterHeaterPowerRequest>` with `RuleFor(r => r.LocationId).NotEmpty()`.
  - A rule on the whole request: `now = Resolve<TimeProvider>().GetUtcNow().UtcDateTime`, then
    `PowerWindow.Resolve(r.From, r.To, now)`, then `PowerWindow.Problem(...)`. Add a failure with that message and
    the error code `Validation.InvalidWindow` when it is not null (e.g. `RuleFor(r => r).Custom((r, ctx) => …)`).

  Depends on T005 and T013 (the request record).
- [X] T013 [US1] Create `src/Features/WaterHeaters/Endpoints/GetWaterHeaterPowerEndpoint.cs`:
  - `public record GetWaterHeaterPowerRequest(Guid LocationId, DateTime? From, DateTime? To);`, with `<param>` docs
    as in `GetChargingSessionsRequest`.
  - `Endpoint<GetWaterHeaterPowerRequest, WaterHeaterPowerResponse>`:
    - `Get("/api/locations/{locationId}/water-heater/power")`;
    - `Tags("WaterHeater")` and `Description(b => b.WithTags("WaterHeater"))`;
    - `AuthSchemes(JwtBearerDefaults.AuthenticationScheme)`.
  - The summary says "The water heater's average power per 5 minutes". The description is condensed from
    `contracts/endpoints.md`: default last 3 hours, at most 48 h, a step without samples has no item, the average is
    over the sampled time, and access is for the owner, viewers and Admins.
  - Responses: 200, 400, 401 and 404.
  - `HandleAsync` follows `GetWaterHeaterStateEndpoint`:
    - read `userId` from `ClaimTypes.NameIdentifier`;
    - send `GetWaterHeaterPowerQuery(userId, User.IsInRole(RoleNames.Admin), req.LocationId, req.From, req.To)`;
    - on failure, `AddError(result.Error.Description, result.Error.Code)` and
      `ThrowIfAnyErrors(WaterHeaterErrorStatus.Of(result.Error, onConnect: false))`.

  Depends on T009.
- [X] T014 [US1] Add `builder.Services.AddSingleton(TimeProvider.System);` to `EndpointHost.StartAsync` in
  `tests/Features.Tests/Http/EndpointHost.cs`. The new validator resolves `TimeProvider`, and the host doesn't
  register it today. Then run
  `dotnet test tests/Features.Tests --filter "FullyQualifiedName~WaterHeaterPower|FullyQualifiedName~PowerWindow"`
  and make T006–T008 pass.

**Checkpoint**: US1 works. The endpoint answers 5-minute steps for any signed-in caller (access comes in US2), and
the US1 tests pass.

---

## Phase 4: User Story 2 - Only people who may see the location (Priority: P1)

**Goal**: The owner, viewers and Admins get the window. Anyone else, and a location that does not exist, get the same
`404 Location.NotFound`. An inactive location answers only to Admins (research R4).

**Independent Test**: As a viewer of Hjemme, read the window and get 200. As a user without access, and with a
made-up location id, get the same 404. Without a token, get 401 (already covered by T008).

### Tests for User Story 2

- [X] T015 [US2] Add access cases to `tests/Features.Tests/WaterHeaters/GetWaterHeaterPowerHandlerTests.cs`:
  - a user with `IsUserAssociatedAsync` → true gets 200 (owner or viewer: the repository doesn't tell them apart);
  - a user with `IsUserAssociatedAsync` → false gets `Location.NotFound`, and `GetPowerStepsAsync` is not called;
  - an Admin with `GetByIdAsync` → an active location gets 200;
  - an Admin with `GetByIdAsync` → `WaterHeaterTestData.Location(active: false)` gets 200 (an inactive location
    answers to Admins);
  - an Admin with `GetByIdAsync` → null gets `Location.NotFound`;
  - a user and a missing location give the identical error (same code and description).
- [X] T016 [P] [US2] Add to `tests/Features.Tests/Http/WaterHeaterPowerHttpTests.cs`: a handler failure with
  `Error.NotFound("Location.NotFound", "Location not found")` maps to 404 with that code in the problem body.

### Implementation for User Story 2

- [X] T017 [US2] Add the access check at the top of `Handle` in
  `src/Features/WaterHeaters/Handlers/GetWaterHeaterPowerQueryHandler.cs`:
  ```csharp
  bool mayRead = query.IsAdmin
      ? await locations.GetByIdAsync(query.LocationId, ct) is not null
      : await locations.IsUserAssociatedAsync(query.UserId, query.LocationId, ct);
  ```
  - When false, call `LogMessages.LocationAccessDenied(logger, query.UserId, query.LocationId)` (EventId 1805) and
    return `Result.Failure<WaterHeaterPowerResponse>(Error.NotFound("Location.NotFound", "Location not found"))`.
  - Update the class XML doc with the access rule: owners and viewers of an active location, or an Admin for any
    existing location; everyone else gets the same 404.
  - Then make T015–T016 pass.

**Checkpoint**: US1 and US2 work. The whole WaterHeater test filter passes.

---

## Phase 5: Polish & Cross-Cutting Concerns

- [X] T018 [P] Document the endpoint in `DevelopmentGuide.md` or the water heater section of the docs, if one lists
  the water heater endpoints. Check with `grep -n "water-heater" *.md`. Add the route with one line and a link to
  `specs/017-water-heater-power/contracts/endpoints.md`. Skip it if no doc lists them.
- [X] T019 [P] Update `specs/_backlog/` for the frontend. Create `specs/_backlog/water-heater-power-frontend.md`
  in the style of `specs/_backlog/ev-charger-frontend.md`. It says:
  - the API side is built (spec 017), and the frontend's "Last 3 hours" card should draw water heating under the
    meter's power line;
  - the request is `GET /api/locations/{id}/water-heater/power` with `from`/`to`, twice (today and the same 3 hours
    yesterday);
  - a missing step is a gap, never 0;
  - `sampledSeconds` tells how much to trust a step;
  - link to `specs/017-water-heater-power/contracts/endpoints.md`.
- [X] T020 Run `dotnet build` (no new warnings), `dotnet format --verify-no-changes` and `dotnet test`. Everything
  must pass.
- [X] T021 Run quickstart §2 and §3 against the local database.
  - Compare the hourly response with T001's `hourly-before.json`
    (`diff <(jq -S . hourly-before.json) <(jq -S . hourly-after.json)`). Complete hours must be identical
    (FR-006).
  - Check SC-002 for one complete hour: Σ `powerKw × sampledSeconds / 3600` equals the hour's uncapped heater kWh
    within 0.001 kWh.
- [ ] T022 After deploying, run quickstart §4 in production: SC-003 (p95 < 300 ms for the default window) and SC-004
  (≥ 95 % of yesterday's steps have ≥ 240 sampled seconds). Record the results in `quickstart.md`, or in a short note
  in the PR.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: T001 must run before T003 changes the SQL. It needs the current build and a database with
  samples.
- **Foundational (Phase 2)**: T002 and T005 are independent. T003 → T004 (and T004 needs T002). T006 needs T005.
- **US1 (Phase 3)**: needs Phase 2.
  - T009 first. Then T010 (needs T002 and T009), and T013 (needs T009).
  - T011 needs T004, T005 and T010.
  - T012 needs T005 and T013.
  - T014 last.
  - T007 and T008 can be written as soon as T009/T013 define the shapes.
- **US2 (Phase 4)**: needs US1's handler (T011). T015 → T017. T016 needs T008's file.
- **Polish (Phase 5)**: needs US1 and US2. T021 needs T001's saved file.

### User Story Dependencies

- **US1**: independent once Phase 2 is done. It is the MVP.
- **US2**: adds the access check to US1's handler. It must ship together with US1: the spec requires the rule from
  the first release.

### Parallel Opportunities

- Phase 2: T002 ∥ T005 (∥ T006 once T005 is done).
- US1: T007 ∥ T008 (tests, different files). T010 ∥ T013 once T009 is done.
- US2: T016 ∥ T015/T017.
- Polish: T018 ∥ T019.

---

## Parallel Example: User Story 1

```text
# After T009 (the records) is done:
Task: "T010 [US1] ToPowerItem in src/Features/WaterHeaters/Mappers/WaterHeaterMapper.cs"
Task: "T013 [US1] GetWaterHeaterPowerEndpoint in src/Features/WaterHeaters/Endpoints/GetWaterHeaterPowerEndpoint.cs"
Task: "T007 [US1] Handler mapping tests in tests/Features.Tests/WaterHeaters/GetWaterHeaterPowerHandlerTests.cs"
Task: "T008 [US1] HTTP tests in tests/Features.Tests/Http/WaterHeaterPowerHttpTests.cs"
```

---

## Implementation Strategy

### MVP First

1. T001: save the hourly figures.
2. Phase 2: the record, the shared step query, the repository method and `PowerWindow`.
3. Phase 3 (US1). Stop and validate: the steps are right locally, and the hourly figures are unchanged (T021's diff
   can be run here already).
4. Phase 4 (US2) before any deploy. An endpoint without the access check must not reach production.

### Incremental Delivery

- One PR for the whole feature: US1 + US2 + Polish. Both stories are P1, and US2 is a few lines.
- After merge and deploy: T022 in production, then the frontend spec from the backlog note (T019) in
  `no.sanddata.ams.frontend`.

---

## Notes

- [P] tasks touch different files and don't depend on incomplete tasks.
- No migration and no new EventId. Access refusals reuse `LocationAccessDenied` (1805).
- No manual DI registration: the source generator picks up the handler, and the sample repository is already
  registered.
- Commit after each phase.
