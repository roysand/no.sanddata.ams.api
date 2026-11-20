# Tasks: Read the Zaptec EV Charger and Optionally Count It with the House

**Input**: Design documents from `/specs/016-zaptec-ev-charger/`

**Prerequisites**: [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md), [data-model.md](data-model.md),
[contracts/endpoints.md](contracts/endpoints.md), [quickstart.md](quickstart.md)

**Tests**: Included. The plan and quickstart §1 name them. They are xUnit + NSubstitute in `tests/Features.Tests`,
with no database. The EF queries are checked by hand in quickstart §2.

**Organization**:
- **Phase 2** builds what every story needs: the entities, the migration, the Zaptec client and the repositories.
- **US1** connects a charger. **US2** reads and stores sessions, and needs US1, because there must be a connection to
  read. **US3** counts the charger with the house; it needs US2's stored hours to show anything, but it can be built
  and unit-tested on Phase 2 alone.
- All three stories are P1.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: US1, US2 or US3 from spec.md

## Facts from the code that the tasks rely on

- **The template to copy:** the water heater slice. Copy its patterns, not its types (Principle II):
  - `src/Features/WaterHeaters/Services/WaterHeaterSamplingService.cs`: `BackgroundService`, `PeriodicTimer` with
    `TimeProvider`, a last-resort catch, and a public `SampleOnceAsync(IServiceProvider, CancellationToken)` for
    tests.
  - `SampleFailureTracker.cs`: logs the start of a failure, then at most hourly, then the recovery.
  - `src/Infrastructure/External/OsoWaterHeaterClient.cs`: `Result` on every failure, logs no URL, body or exception
    message, and its HttpClient is registered with `.RemoveAllLoggers()` in `src/Infrastructure/AddInfrastructureToDI.cs`.
  - The endpoints in `WaterHeaterAdminEndpoints.cs` map errors to statuses through `WaterHeaterErrorStatus.Of(error,
    onConnect)`, which is defined in that file.
- **Hosted services** are registered in `src/Features/ServiceCollectionExtensions.cs`. Handlers are registered by
  the source generator; never by hand.
- **`HourCost`** is declared at the top of `src/Features/ElectricityCost/Services/HourlyCostProvider.cs` as
  `record HourCost(DateTime HourStart, decimal ConsumptionKwh, CostResult Cost, WaterHeaterHour? WaterHeater = null)`.
- **`HourlyCostProvider.GetAsync(location, from, to, ct, withWaterHeater)`** returns `IReadOnlyList<HourCost>`.
  - It is called in `GetHourlyCostQueryHandler`, `GetDailyCostQueryHandler`, `GetMonthlyBillQueryHandler` and
    `GetNorgesPrisSavingQueryHandler`.
  - It is constructed with `new` in `tests/Features.Tests/ElectricityCost/MonthlyBillHandlerTests.cs` (3 places) and
    `NorgesPrisSavingTests.cs`.
- **`GetCurrentHourCostQueryHandler`** does not use the provider. It reads one hour through `IConsumptionRepository`
  and maps it with `CostMapper.ToCurrentHourResponse(hourStart, kwh, cost)`.
- **`LocationMapper.ToResponse(location, role, hasWaterHeater)`** is called in `GetMyLocationsQueryHandler`, which
  uses `IWaterHeaterRepository.GetLocationIdsWithHeaterAsync`, and in `UpdateOwnLocationCommandHandler`.
- **DbSets** are in `src/Infrastructure/Database/ApplicationDbContext.cs`, and the configurations are in
  `src/Infrastructure/Database/Configuration/`.
- **EventIds:** 1900–1999 is free. Infra/Logging is 2000–2099.

---

## Phase 1: Setup

- [X] T001 Create the branch `feature/016-zaptec-ev-charger` from `main`. Then check the baseline is green: run
  `dotnet build`, `dotnet test` and `dotnet format --verify-no-changes` from the repo root, and note the test count.
- [X] T002 **Gate:** run [quickstart.md](quickstart.md) §0 with Roy's Zaptec login. Roy types the password, and it is
  never written to a file. Record the following under "Verified by hand" at the end of R2 in
  `specs/016-zaptec-ev-charger/research.md`:
  - the status of each call;
  - the shape of one session;
  - the number of `energyDetails` points per hour, and whether they are cumulative.

  Save one anonymised session answer as the test fixture in T013: replace ids with new GUIDs, and remove the user
  name and e-mail. **Done 2026-11-20:** the results are in research R2 and R5. The fixture is
  `zaptec-session-fixture.json`, made by the probe script; it has fixed GUIDs, no user, no signature, and generic
  names. **If `/api/chargers` or `/api/sessions/archived` is refused (401 or 403), stop here and report to
  Roy:** the UserGroup route is then needed first.

---

## Phase 2: Foundational (blocks all stories)

**Purpose**: the entities and migration, the Application contracts, the Zaptec client and the repositories.

### Domain

- [X] T003 [P] Create `src/Domain/Common/Entities/EvCharger.cs`, as in data-model.md:
  - Properties: `LocationId`, `ChargerId`, `ChargerName`, `InstallationId`, `InstallationName`, `ReadUpToUtc?`.
  - Constructor and methods: a constructor, `Replace(...)`, which sets `ReadUpToUtc = null`, and `MarkReadUpTo(DateTime)`.
  - `ToString()`: `$"EvCharger(LocationId={LocationId})"`.
  - Use the style of `WaterHeater.cs` (private setters and a parameterless constructor for EF).
- [X] T004 [P] Create `src/Domain/Common/Entities/ChargingSession.cs` and `ChargingSessionHour.cs`, as in data-model.md.
  - **`ChargingSession`:** owns `Hours` as a private `List<ChargingSessionHour>` exposed read-only. Its constructor
    takes the hours.
  - **`ChargingSessionHour`:** has `ChargingSessionId`, `LocationId`, `HourStartUtc`, `Kwh` and `Estimated`.
- [X] T005 [P] In `src/Domain/Common/Entities/Location.cs`, add:
  - `public bool IncludeEvChargerInTotals { get; private set; }`;
  - `public void SetIncludeEvCharger(bool include) => IncludeEvChargerInTotals = include;`, with a one-line doc comment.

### Application

- [X] T006 [P] Create `src/Application/EvChargers/EvChargerModels.cs` with the records and errors listed in
  data-model.md:
  - **Records:** `ZaptecCharger`, `EnergyPoint`, `ZaptecSession` (with `Discard`) and `ChargingHourSplit`.
  - **Errors:** `static class EvChargerErrors` with `NotConfigured`, `SignInRejected`, `Unavailable`,
    `InvalidResponse`, `UnknownCharger`, `ChargerInUse` and `NotConnected`. Use the codes in the data-model error
    table, and follow the style of `WaterHeaterErrors` in `src/Application/WaterHeaters/WaterHeaterModels.cs`.
- [X] T007 [P] Create `src/Application/Common/Interfaces/External/IEvChargerClient.cs`. It is read-only, and its doc
  comment says it never throws for HTTP, timeout or parsing failures, and is read-only by design (FR-017).
  - `bool IsConfigured { get; }`
  - `Task<Result<IReadOnlyList<ZaptecCharger>>> GetChargersAsync(CancellationToken)`
  - `Task<Result<IReadOnlyList<ZaptecSession>>> GetCompletedSessionsAsync(Guid chargerId, DateTime fromUtc, DateTime toUtc, CancellationToken)`
- [X] T008 [P] Create the repository interfaces in `src/Application/Common/Interfaces/Repositories/`:
  - **`IEvChargerRepository<T> : IRepository<T>`** with:
    - `GetByLocationAsync` (tracked);
    - `GetByChargerIdAsync`;
    - `GetLocationIdsWithChargerAsync(IReadOnlyCollection<Guid>, ct)`;
    - `GetForActiveLocationsAsync` (tracked, because the job updates `ReadUpToUtc`).
  - **`IChargingSessionRepository`** with the methods in data-model.md:
    - `UpsertAsync(locationId, chargerId, sessions, ct)`, where each session comes with its split hours;
    - `GetHourlyEnergyAsync(locationId, fromUtc, toUtc, ct) → IReadOnlyList<EvChargerHourEnergy>`;
    - `GetSessionsAsync(locationId, fromUtc, toUtc, ct)`;
    - `AnyForLocationAsync`;
    - `SaveChangesAsync`.

    Add `EvChargerHourEnergy(DateTime HourStartUtc, decimal Kwh, bool Estimated)` to `EvChargerModels.cs`.

### Infrastructure: database

- [X] T009 [P] Create `EvChargerConfiguration.cs`, `ChargingSessionConfiguration.cs` and
  `ChargingSessionHourConfiguration.cs` in `src/Infrastructure/Database/Configuration/`, and add the
  `IncludeEvChargerInTotals` column (default false) in `LocationConfiguration.cs`.
  - **Keys:** unique `EvCharger.LocationId`, unique `EvCharger.ChargerId`, unique `ChargingSession.ZaptecSessionId`,
    and unique `(ChargingSessionId, HourStartUtc)`.
  - **Indexes:** `(LocationId, EndUtc)` and `(LocationId, HourStartUtc)`.
  - **Columns:** names at most 200 characters, and `decimal(12,4)` for kWh.
  - **Foreign keys:**
    - session → hours: cascade;
    - `EvCharger` → Location: cascade;
    - sessions → Location: restrict, since the sessions are kept.
- [X] T010 Add `DbSet<EvCharger> EvCharger`, `DbSet<ChargingSession> ChargingSession` and
  `DbSet<ChargingSessionHour> ChargingSessionHour` to `src/Infrastructure/Database/ApplicationDbContext.cs`. Then
  create the migration:

  ```bash
  dotnet ef migrations add AddEvCharger --project src/Infrastructure/Infrastructure.csproj \
    --startup-project src/api/api.csproj --output-dir Database/Migrations
  ```

  Read the generated migration, and check that it only adds the 3 tables, the indexes and the 1 column.
- [X] T011 [P] Create `src/Infrastructure/Database/Repositories/EvChargerEfRepository.cs`, which implements
  `IEvChargerRepository<EvCharger>` on `GenericEfRepository<EvCharger>`, as `WaterHeaterEfRepository` does.
  `GetForActiveLocationsAsync` joins `Location.IsActive`.
- [X] T012 [P] Create `src/Infrastructure/Database/Repositories/ChargingSessionEfRepository.cs`, using LINQ only:
  - **`UpsertAsync`:** load the existing sessions with those `ZaptecSessionId`s, including their hours. Remove them.
    Then add a new `ChargingSession`, with its hours, for each session that is not `Discard`. Do not save here: the
    caller calls `SaveChangesAsync` once (research R4).
  - **`GetHourlyEnergyAsync`:** `ChargingSessionHour` where `LocationId` matches and `from ≤ HourStartUtc < to`,
    grouped by `HourStartUtc`, with `Kwh = Sum` and `Estimated = Any(Estimated)`, ordered by hour.
  - **`GetSessionsAsync`:** sessions where `from ≤ EndUtc < to`, newest first, read without tracking.

### Infrastructure: Zaptec client

- [X] T013 [P] Create `tests/Features.Tests/EvChargers/ZaptecSamples.cs` and a stub `HttpMessageHandler` in
  `tests/Features.Tests/EvChargers/ZaptecStubHandler.cs`, modelled on `OsoSamples.cs` and `OsoStubHandler.cs`.
  - **Answers:** a token answer, a chargers page, and two archived-sessions pages (`hasMore: true` with a cursor,
    then `hasMore: false`). Use the session fixture from T002.
  - **Sessions to include:** one `voided` session, and one with `replacedBySessionId` set.
- [X] T014 [P] Create `src/Infrastructure/External/ZaptecOptions.cs`:
  - It has `SectionName = "Zaptec"`, `Username` and `Password`, and `IsConfigured` (both set).
  - `ToString()` never prints either value.
- [X] T015 [P] Create `src/Infrastructure/External/ZaptecTokenCache.cs` as a singleton:
  - It holds the token and its expiry, guarded by a `SemaphoreSlim`.
  - `GetAsync(Func<CancellationToken, Task<Result<(string Token, int ExpiresIn)>>> fetch, ct)` returns the cached
    token until 5 minutes before it expires, using `TimeProvider`.
  - `Invalidate()` clears the token.
- [X] T016 Create `src/Infrastructure/External/Logging/ZaptecLogMessages.cs` with EventIds 1920–1924:
  - `RequestFailed(locationId?, call, status)`
  - `Unavailable(locationId?, call)`
  - `InvalidResponse(locationId?, call)`
  - `SignInRejected()`
  - `RateLimited(call)`

  `call` is one of "token", "chargers" or "sessions". There is no URL, header, body or message parameter.
- [X] T017 Create `src/Infrastructure/External/ZaptecEvChargerClient.cs`, which implements `IEvChargerClient` as in
  research R1 and R2.
  - **Sign-in:** form-encoded `POST oauth/token` through `ZaptecTokenCache`. Every request sends
    `User-Agent: AmsApi/1.0`.
  - **Chargers:** `GET api/chargers?PageSize=100`, read from `data[]`.
  - **Sessions:** `GET api/sessions/archived?ChargerId=…&From=…&To=…&PageSize=200`, following `cursor` while
    `hasMore` is true. Dates are formatted `O` in UTC.
  - **Mapping a session:** `Discard = voided || replacedBySessionId != null`. A missing `endDateTime` skips the
    session.
  - **401 on a data call:** call `Invalidate()` and retry once.
  - **429:** wait `Retry-After` (default 1 s) once.
  - **Failures map to errors:**
    - 400/401 from the token call → `SignInRejected`;
    - 5xx, timeout or `HttpRequestException` → `Unavailable`;
    - a JSON error → `InvalidResponse`;
    - not configured → `NotConfigured`, without any HTTP call.
  - **Parsing:** DTOs with case-insensitive matching.
  - **Logging:** never log the URL, the exception or its message (copy the `catch` filters of `OsoWaterHeaterClient`).
- [X] T018 Register in `src/Infrastructure/AddInfrastructureToDI.cs`:
  - `AddOptions<ZaptecOptions>().Bind(configuration.GetSection(ZaptecOptions.SectionName))`;
  - `AddSingleton<ZaptecTokenCache>()`;
  - `AddHttpClient<IEvChargerClient, ZaptecEvChargerClient>` with base address `https://api.zaptec.com/`, a 15 s
    timeout and `.RemoveAllLoggers()`, with a comment: the token request body holds the password, and the headers
    hold the token;
  - the two repositories, scoped.
- [X] T019 [P] Create `tests/Features.Tests/EvChargers/ZaptecEvChargerClientTests.cs`, using T013's stub:
  - **Success:** the token is fetched once for two calls; the chargers parse; both session pages are read through
    the cursor; voided and replaced sessions are `Discard`.
  - **Failures:** 401 from the token call → `SignInRejected`; 401 on data → one retry, then success; 503 →
    `Unavailable`; a timeout → `Unavailable`; bad JSON → `InvalidResponse`; not configured → `NotConfigured`, with
    no request sent.

**Checkpoint**: `dotnet build` and `dotnet test` are green, and the migration is created.

---

## Phase 3: User Story 1 - An Admin connects a Zaptec charger to a location (Priority: P1) 🎯 MVP

**Goal**: An Admin lists the chargers the server's sign-in sees, and connects, shows and removes one charger per
location.

**Independent Test**: With the login in `local.settings.json`:
- As Admin, `GET /api/admin/ev-chargers` lists Roy's charger.
- `PUT /api/admin/locations/{Hjemme}/ev-charger` saves it, and the GET of the same path shows its name and
  installation.
- An unknown id gives `400`, a viewer gets `403`, and with no login on the server the answer is `503 NotConfigured`.

### Tests for User Story 1

- [X] T020 [P] [US1] Create `tests/Features.Tests/EvChargers/EvChargerAdminHandlerTests.cs`, with a substituted
  `IEvChargerClient` and substituted repositories:
  - list chargers: passes the result through, and passes errors through;
  - connect:
    - success saves ids and names;
    - an unknown charger gives `UnknownCharger` and saves nothing;
    - the charger at another location gives `ChargerInUse`;
    - Zaptec unavailable, or the sign-in rejected, saves nothing;
    - connecting again replaces the old charger and resets `ReadUpToUtc`;
  - settings: `NotConnected` when there is none;
  - remove: deletes the connection, keeps the sessions (no call to the session repository), and sets
    `IncludeEvChargerInTotals` to false.

### Implementation for User Story 1

- [X] T021 [P] [US1] Create the commands and queries:
  - `src/Features/EvChargers/Commands/EvChargerCommands.cs`: `ConnectEvChargerCommand(ActingUserId, LocationId,
    ChargerId)` and `RemoveEvChargerCommand(ActingUserId, LocationId)`;
  - `src/Features/EvChargers/Queries/EvChargerQueries.cs`: `ListZaptecChargersQuery()` and
    `GetEvChargerSettingsQuery(LocationId)`, with the response records `ZaptecChargerResponse` and
    `EvChargerSettingsResponse`, shaped as in contracts/endpoints.md.
- [X] T022 [P] [US1] Create `src/Features/EvChargers/Mappers/EvChargerMapper.cs`, with `ToResponse(ZaptecCharger)` and
  `ToSettings(EvCharger, bool includeInTotals)`.
- [X] T023 [P] [US1] Create `src/Features/EvChargers/Logging/LogMessages.cs` with EventIds 1900–1909:
  - `EvChargerConnected(locationId, actingUserId)`
  - `EvChargerReplaced(...)`
  - `EvChargerRemoved(...)`
  - `EvChargerIncludeChanged(locationId, include, actingUserId)`
  - `Reason(Error)`, which maps an error to a stable reason code, as in the water heater `LogMessages`.
- [X] T024 [US1] Create the handlers in `src/Features/EvChargers/Handlers/`, following data-model.md and spec US1:
  - `ListZaptecChargersQueryHandler`
  - `ConnectEvChargerCommandHandler`: location exists → charger is in the list from `GetChargersAsync` → not at
    another location → insert or `Replace` → save.
  - `GetEvChargerSettingsQueryHandler`
  - `RemoveEvChargerCommandHandler`: removes the row, calls `location.SetIncludeEvCharger(false)`, and saves.
- [X] T025 [P] [US1] Create `src/Features/EvChargers/Validators/ConnectEvChargerValidator.cs` (`chargerId` not empty).
- [X] T026 [US1] Create `src/Features/EvChargers/Endpoints/EvChargerAdminEndpoints.cs` with 4 endpoints, all with
  `Roles(RoleNames.Admin)`:
  - `GET /api/admin/ev-chargers`
  - `PUT /api/admin/locations/{locationId}/ev-charger`
  - `GET /api/admin/locations/{locationId}/ev-charger`
  - `DELETE /api/admin/locations/{locationId}/ev-charger`

  Include an `EvChargerErrorStatus.Of(error, onConnect)` that maps errors to the status codes in the contract, with
  `SignInRejected` giving 400 on connect and 503 otherwise. Write Scalar summaries and responses as in
  `WaterHeaterAdminEndpoints.cs`.
- [X] T027 [US1] Add `bool HasEvCharger` to the end of `LocationSummaryResponse` in
  `src/Features/Locations/Queries/GetMyLocationsQuery.cs`, as described in contracts/endpoints.md.
  - Pass it through `LocationMapper.ToResponse` in `src/Features/Locations/Mappers/LocationMapper.cs`.
  - Fill it in `GetMyLocationsQueryHandler` with `IEvChargerRepository.GetLocationIdsWithChargerAsync`, and in
    `UpdateOwnLocationCommandHandler` with `GetByLocationAsync`.
  - Fix any tests in `tests/Features.Tests/Locations/` that construct the response.

**Checkpoint**: US1 runs end to end locally (quickstart §2, step 2).

---

## Phase 4: User Story 2 - The charger's energy is read and stored per hour (Priority: P1)

**Goal**: Every 15 minutes, the completed sessions of each connected charger are read and stored, with their kWh per
clock hour. Gaps are filled, and failures store nothing.

**Independent Test**: After the first read, `GET /api/locations/{Hjemme}/ev-charger/sessions?from=<start of last
month>` matches the Zaptec app at ±0.01 kWh per session (SC-002). With a wrong password, nothing is stored, one
failure is logged, and the gap is filled after the password is fixed.

### Tests for User Story 2

- [X] T028 [P] [US2] Create `tests/Features.Tests/EvChargers/ChargingEnergySplitterTests.cs`, following research R5
  (per-interval points, not cumulative):
  - **The fixture session** from `ZaptecSamples`: its points give the expected kWh per hour, and they add up to 33.982.
  - **Point times:**
    - a first partial hour (start 18:23, point 19:00 = 2.706 → hour 18:00);
    - an end point mid-hour (05:57:48 → hour 05:00);
    - a skewed timestamp (`02:59:59` → hour 02:00);
    - skipped hours (18:00, then 20:00) → no 19:00 hour from the skip;
    - two points landing in one hour are added;
    - the 0 start point is dropped.
  - **No points:** an even spread, marked `Estimated`, split at hour boundaries.
  - **Points that don't add up** to `energy`: the hours are scaled to it.
  - **A negative point** counts as 0.
  - **A session shorter than one hour.**
  - **The 2026-10-25 daylight-saving change:** UTC hours, so no special case.
  - **Totals:** the hours always add up to `totalKwh` (to 0.0001).
- [X] T029 [P] [US2] Create `tests/Features.Tests/EvChargers/EvChargerReadServiceTests.cs`, with a fake client and
  substituted repositories, calling `ReadOnceAsync` directly:
  - a first read uses `From` = the start of the previous Oslo month;
  - later reads use `ReadUpToUtc − 24 h`;
  - success: upsert, then save once, then `ReadUpToUtc = now`;
  - a failure stores nothing and leaves `ReadUpToUtc` unchanged;
  - one charger failing does not stop the next;
  - not configured: no client call;
  - discarded sessions are passed to the upsert so it can delete them.
- [X] T030 [P] [US2] Create `tests/Features.Tests/EvChargers/ReadFailureTrackerTests.cs`, a copy of
  `SampleFailureTrackerTests.cs` adapted to the new class.

### Implementation for User Story 2

- [X] T031 [P] [US2] Create `src/Application/EvChargers/ChargingEnergySplitter.cs`, with
  `public static IReadOnlyList<ChargingHourSplit> Split(DateTime startUtc, DateTime endUtc, decimal totalKwh,
  IReadOnlyList<EnergyPoint> details)`. It follows research R5 exactly, with no rounding.
- [X] T032 [P] [US2] Create `src/Features/EvChargers/Services/ReadFailureTracker.cs`, a copy of
  `src/Features/WaterHeaters/Services/SampleFailureTracker.cs` with the types renamed (`ReadLogKind`,
  `ReadLogDecision`). Add a comment saying it is copied on purpose (Principle II).
- [X] T033 [US2] Add the read log messages to `src/Features/EvChargers/Logging/LogMessages.cs`, as EventIds
  1910–1915:
  - `EvChargerReadFailing(locationId, reason)`
  - `EvChargerReadStillFailing(locationId, reason, minutes)`
  - `EvChargerReadRecovered(locationId, minutes)`
  - `EvChargerReadCycleFailed(exceptionType)`
  - `EvChargerNotConfigured()`, logged once at startup
  - `EvChargerSessionsStored(locationId, count)`, at Debug level
- [X] T034 [US2] Create `src/Features/EvChargers/Services/EvChargerReadService.cs`, following research R3 and modelled
  on `WaterHeaterSamplingService`:
  - **Timer:** `EvCharger:ReadIntervalMinutes`, default 15. The first cycle runs at startup.
  - **Startup when not configured:** log `EvChargerNotConfigured` once, then skip.
  - **`ReadOnceAsync`:** for each connected charger,
    1. compute the window;
    2. call `GetCompletedSessionsAsync`;
    3. split each session that is not discarded with `ChargingEnergySplitter`;
    4. call `UpsertAsync`;
    5. call `charger.MarkReadUpTo(to)`;
    6. save once.
  - **Failures:** each charger's failure is caught and goes to the tracker. A session with `EndUtc < StartUtc` is
    skipped.
  - **Registration:** `services.AddHostedService<Features.EvChargers.Services.EvChargerReadService>()` in
    `src/Features/ServiceCollectionExtensions.cs`.
- [X] T035 [P] [US2] Add `GetChargingSessionsQuery(UserId, IsAdmin, LocationId, From?, To?)` and
  `ChargingSessionsResponse` to `src/Features/EvChargers/Queries/EvChargerQueries.cs`. The response shape is in the
  contract.
- [X] T036 [US2] Create `src/Features/EvChargers/Handlers/GetChargingSessionsQueryHandler.cs`:
  - Access: an Admin, or a user linked to the location (`ILocationRepository.IsUserAssociatedAsync`); otherwise
    `404`.
  - Defaults: `to` is now and `from` is `to − 30 days`. Results are newest first, mapped by `EvChargerMapper`.
- [X] T037 [P] [US2] Create `src/Features/EvChargers/Validators/GetChargingSessionsValidator.cs`: when both are given,
  `from < to`, and the range is at most 366 days.
- [X] T038 [US2] Add `GET /api/locations/{locationId}/ev-charger/sessions` (JWT, any signed-in user) to
  `src/Features/EvChargers/Endpoints/EvChargerEndpoints.cs`.
- [X] T039 [P] [US2] Add handler tests for the sessions list to `tests/Features.Tests/EvChargers/EvChargerHandlerTests.cs`:
  - a stranger gets `404`;
  - a viewer gets `200`;
  - the default range;
  - newest first.

**Checkpoint**: quickstart §2, step 3, matches the Zaptec app.

---

## Phase 5: User Story 3 - A checkbox decides whether the charger counts with the house (Priority: P1)

**Goal**: An owner or an Admin turns "count the charger with the house" on or off.
- The hourly, current, daily and bill figures carry an `evCharger` part and a `totalKwh` that includes the car only
  when the checkbox is on.
- No cost changes.

**Independent Test**: quickstart §2, steps 4–6:
- with the setting off, `totalKwh == consumptionKwh`;
- with it on, `totalKwh` rises by exactly `evCharger.kwh`;
- every other field is byte-identical (SC-004).

### Tests for User Story 3

- [X] T040 [P] [US3] Create `tests/Features.Tests/EvChargers/EvChargerShareTests.cs`:
  - `Sum` gives null for no parts;
  - it sums kWh, ORs `Estimated`, and gives the first and last hour;
  - `HoursWithoutMeterReading` counts the hours it was told had no meter hour;
  - `Included` carries through.
- [X] T041 [P] [US3] Add the include tests to `tests/Features.Tests/EvChargers/EvChargerHandlerTests.cs`:
  - an owner can turn it on and off;
  - an Admin who is not linked can too;
  - a viewer gets `EvChargerErrors.Forbidden`, which the endpoint turns into 403;
  - a stranger gets `404`;
  - turning it on with no connection and no sessions gives `NotConnected`, which becomes 400;
  - turning it on with no connection but stored sessions is allowed;
  - turning it off is always allowed.
- [X] T042 [P] [US3] Add charger cases to `tests/Features.Tests/ElectricityCost/CostHandlerTests.cs` and
  `MonthlyBillHandlerTests.cs`, using a substituted `IChargingSessionRepository`:
  - **Hourly:**
    - a charger hour on a meter hour gives `evCharger` and `totalKwh`;
    - with the setting off, `totalKwh == consumptionKwh` and `included: false`;
    - an hour with no charger data gives `evCharger: null`.
  - **Daily:** the part includes a charger hour that has no meter hour, and `hoursWithoutMeterReading` is 1.
  - **Bill:** `total`, every line and the capacity are identical with the setting on and off; `totalKwh` differs by
    the charger kWh.
  - **Water heater:** the heater part is unchanged by the setting.

### Implementation for User Story 3

- [X] T043 [P] [US3] Create `src/Application/EvChargers/EvChargerShare.cs`:
  - `record EvChargerHour(DateTime HourStartUtc, decimal Kwh, bool Estimated, bool Included)`;
  - `record EvChargerTotal(...)`, as in data-model.md;
  - `static EvChargerTotal? Sum(IEnumerable<EvChargerHour> parts, int hoursWithoutMeterReading)`.
- [X] T044 [US3] Change `src/Features/ElectricityCost/Services/HourlyCostProvider.cs`, following research R8:
  - **`HourCost`:** add `EvChargerHour? EvCharger = null`.
  - **New record:** `HourlyCosts(IReadOnlyList<HourCost> Hours, IReadOnlyList<EvChargerHour> UnmatchedChargerHours)`.
  - **`GetAsync`:** add `bool withEvCharger = false`, and return `HourlyCosts`.
    - With `withEvCharger`, call `IChargingSessionRepository.GetHourlyEnergyAsync(location.Id, fromUtc,
      HourCeiling(toUtc), ct)` once.
    - Attach an `EvChargerHour` with `Included = location.IncludeEvChargerInTotals` to each matching hour.
    - Put charger hours in `[fromUtc, toUtc)` that have no meter hour into `UnmatchedChargerHours`.
  - **Constructor:** inject `IChargingSessionRepository`.
- [X] T045 [US3] Update every caller of `GetAsync` to the new return type.
  **As built:** `GetAsync` kept its return type, and a new `GetWithEvChargerAsync` returns `HourlyCosts`. The hourly,
  daily and bill handlers call it; the saving handler and the existing tests are unchanged. The repository is an
  optional last constructor parameter of `HourlyCostProvider` and `GetCurrentHourCostQueryHandler`; the DI container
  always passes it. The original plan:
  - `GetHourlyCostQueryHandler`, `GetDailyCostQueryHandler` and `GetMonthlyBillQueryHandler` pass
    `withEvCharger: true`;
  - `GetNorgesPrisSavingQueryHandler` uses `.Hours`;
  - in `tests/Features.Tests/ElectricityCost/MonthlyBillHandlerTests.cs` (3 places) and `NorgesPrisSavingTests.cs`,
    add a substituted `IChargingSessionRepository` to the `new HourlyCostProvider(...)` calls and use `.Hours`.
- [X] T046 [P] [US3] Add the response fields to the query files in `src/Features/ElectricityCost/Queries/`, all at the
  end of each record so no existing position moves:
  - **`GetHourlyCostQuery.cs`:** add `decimal TotalKwh` and `EvChargerHourResponse? EvCharger` to
    `HourlyCostItemResponse`, and the new `EvChargerHourResponse(decimal Kwh, bool Estimated, bool Included)`.
  - **`GetCurrentHourCostQuery.cs`:** add `decimal TotalKwhSoFar` and `EvChargerHourResponse? EvCharger` to
    `CurrentHourCostResponse`.
  - **`GetDailyCostQuery.cs`:** add `decimal TotalKwh` and `EvChargerDayResponse? EvCharger`, and the new
    `EvChargerDayResponse(Kwh, Estimated, Included, HoursWithoutMeterReading)`.
  - **`GetMonthlyBillQuery.cs`:** add `decimal TotalKwh` and `EvChargerBillResponse? EvCharger`, which also has
    `FirstHour` and `LastHour`.
- [X] T047 [US3] Map the new fields in `src/Features/ElectricityCost/Mappers/CostMapper.cs` and `BillMapper.cs`:
  - **Hourly:** `totalKwh = ConsumptionKwh + (EvCharger is { Included: true } e ? e.Kwh : 0)`.
  - **Daily:** `ToDailyResponse` gets the day's unmatched charger hours as a parameter. The day's part is
    `EvChargerShare.Sum(hours' parts + unmatched, unmatched.Count)`, and `totalKwh` is the meter kWh plus the part's
    kWh when it is included.
  - **Bill:** `ToResponse` gets an `EvChargerTotal?`, plus the meter kWh so far for `totalKwh`.
  - **Handlers:** the daily and bill handlers group `UnmatchedChargerHours` by Oslo day and month, using the same
    day grouping as the existing daily handler, and pass them in.
- [X] T048 [US3] In `src/Features/ElectricityCost/Handlers/GetCurrentHourCostQueryHandler.cs`, inject
  `IChargingSessionRepository`, read `GetHourlyEnergyAsync(location.Id, hourStart, hourStart.AddHours(1), ct)`, and
  map `evCharger` and `totalKwhSoFar` through `CostMapper.ToCurrentHourResponse`, which gets an `EvChargerHour?`
  parameter.
- [X] T049 [P] [US3] Add `SetIncludeEvChargerCommand(ActingUserId, IsAdmin, LocationId, Include)` to
  `src/Features/EvChargers/Commands/EvChargerCommands.cs`, with the response
  `EvChargerIncludeResponse(LocationId, IncludeInTotals)`. Add `SetIncludeEvChargerValidator` in
  `src/Features/EvChargers/Validators/`, which only checks that `include` is present.
- [X] T050 [US3] Create `src/Features/EvChargers/Handlers/SetIncludeEvChargerCommandHandler.cs`, following research
  R9:
  - **Access:**
    - an Admin, or an owner (`IUserLocationRepository.IsOwnerAsync`), may change it;
    - a linked viewer gets `EvChargerErrors.Forbidden`. `Error` has no forbidden kind, so add
      `Forbidden = Error.Failure("EvCharger.Forbidden", "Only an owner of the location or an Admin may change this")`
      to `EvChargerErrors` in `src/Application/EvChargers/EvChargerModels.cs`. The endpoint maps that code to 403;
    - otherwise `404`.
  - **Turning it on** with no connection (`IEvChargerRepository.GetByLocationAsync`) and no sessions (`AnyForLocationAsync`)
    gives `EvChargerErrors.NotConnected`.
  - **Saving:** `location.SetIncludeEvCharger(...)`, save, log `EvChargerIncludeChanged`.
- [X] T051 [US3] Add `PUT /api/locations/{locationId}/ev-charger/include` to
  `src/Features/EvChargers/Endpoints/EvChargerEndpoints.cs`. Map errors as follows: `NotConnected` → 400, `EvCharger.Forbidden` (by code) →
  403, not found → 404.
- [X] T052 [US3] Add `bool IncludeEvCharger` after `HasEvCharger` in `LocationSummaryResponse`, and map it from
  `location.IncludeEvChargerInTotals` in `LocationMapper.ToResponse`, in
  `src/Features/Locations/Queries/GetMyLocationsQuery.cs` and `src/Features/Locations/Mappers/LocationMapper.cs`.

**Checkpoint**: quickstart §2, steps 4–7, pass.

---

## Phase 6: Polish & Cross-Cutting Concerns

- [X] T053 [P] Add the row `| 1900-1999 | EvCharger |` to the EventId tables in `CLAUDE.md` and `DevelopmentGuide.md`.
- [X] T054 [P] Wire up the Zaptec login variables:
  - In `compose.prod.yaml`, under the api service's `environment`, add `Zaptec__Username: ${ZAPTEC_USERNAME}` and
    `Zaptec__Password: ${ZAPTEC_PASSWORD}`. Add a comment: Roy's own Zaptec login for reading the charger (spec
    016); without it, no charging data is read.
  - Document both variables in `DockerDeployment.md` next to `WATER_HEATER_KEY_ENCRYPTION_KEY`.
  - Add empty `"Zaptec": { "Username": "", "Password": "" }` placeholders to the `local.settings.json` example in
    `CLAUDE.md`.
- [X] T055 [P] Add a test to `tests/Features.Tests/EvChargers/NoSecretsInLogsTests.cs`, modelled on
  `tests/Features.Tests/WaterHeaters/NoSecretsInLogsTests.cs`. It runs the client and the read service through
  success and every failure with a capturing logger, and asserts that the user name, the password and the token
  never appear (SC-006, FR-018).
- [X] T056 Run `dotnet build` (no new warnings), `dotnet test`, and `dotnet format --verify-no-changes`, and fix any
  findings.
- [ ] T057 Run [quickstart.md](quickstart.md) §2 end to end locally against Roy's real charger. Record the results,
  including the session and month kWh compared with the Zaptec app, under a "Verification" heading in
  `specs/016-zaptec-ev-charger/quickstart.md`.
- [ ] T058 After deploying, run [quickstart.md](quickstart.md) §3 in production:
  - add `ZAPTEC_USERNAME` and `ZAPTEC_PASSWORD` to the server's `.env`;
  - check the outage case.

  Record the results in the same Verification section.
- [X] T059 [P] Write `specs/_backlog/ev-charger-frontend.md` for the frontend spec. It covers:
  - the checkbox (`PUT …/ev-charger/include` and `includeEvCharger`);
  - charting `totalKwh` with the `evCharger` part next to the house;
  - the sessions list;
  - that a charge in progress appears only when it ends.

  Link it to contracts/endpoints.md.

---

## Dependencies & Execution Order

- **Setup (T001–T002):** T002 is a gate. If Zaptec refuses the homeowner login, nothing after it starts.
- **Foundational (T003–T019):** blocks every story.
  - The Domain tasks T003–T005 and the Application tasks T006–T008 can all run in parallel.
  - T009 needs T003–T005.
  - T010 needs T009.
  - T011 and T012 need T008 and T010.
  - T017 needs T013–T016.
  - T018 needs T011, T012 and T017.
  - T019 needs T017.
- **US1 (T020–T027):** needs Phase 2.
- **US2 (T028–T039):** needs Phase 2. The read service only does something with a connection, so US1 must be done
  to check it end to end, but its unit tests do not need US1.
- **US3 (T040–T052):** needs Phase 2. It can be built and unit-tested alongside US1 and US2. It shows real data only
  once US2 has stored hours.
  - T044 → T045 → T047.
  - T046 can run before T047.
- **Polish (T053–T059):** T056 needs all the stories. T057 needs T056. T058 needs a deploy.

### Within each story

Tests are written first and fail. Then come types and mappers, then handlers, then endpoints.

## Parallel Examples

```text
# Phase 2, together:
T003 EvCharger.cs | T004 ChargingSession(+Hour).cs | T005 Location column
T006 EvChargerModels.cs | T007 IEvChargerClient.cs | T008 repository interfaces
T013 Zaptec fixtures | T014 ZaptecOptions | T015 ZaptecTokenCache

# US1, together:
T020 handler tests | T021 commands and queries | T022 mapper | T023 log messages | T025 validator

# US2, together:
T028 splitter tests | T029 read service tests | T030 tracker tests | T031 splitter | T032 tracker

# US3, together:
T040 share tests | T041 include tests | T042 cost tests | T043 EvChargerShare | T046 response fields
```

## Implementation Strategy

- **MVP (US1 + US2):** connect the charger, and read and store its sessions. With these alone, the sessions list
  already answers "how many kWh did the car use" (spec US2, "Why this priority"). Check them with quickstart §2, steps
  2–3.
- **Then US3:** the checkbox and the added fields. These are what the frontend needs, and T059 hands them over.
- **Commits:** follow the existing habit of one commit per phase or story. The migration (T010) is committed together
  with the entities it describes.
