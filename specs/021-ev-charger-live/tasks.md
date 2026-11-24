# Tasks: The Car Charger's State Right Now

**Input**: Design documents from `/specs/021-ev-charger-live/`

**Prerequisites**: [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md), [data-model.md](data-model.md),
[contracts/endpoints.md](contracts/endpoints.md), [quickstart.md](quickstart.md)

**Tests**: Included, as named in the plan and quickstart §1. They use xUnit + NSubstitute in
`tests/Features.Tests/EvChargers`, with no database and no HTTP host.

**Organization**:
- **Phase 2** adds what every story needs: the Application types, the client method, and its tests.
- **US1** works out the state and answers it.
- **US2** puts the shared answer between the handler and Zaptec.
- **US3** proves the access rules and the "no charger" answer. The access check is built in US1's handler, because a
  handler without it must never exist. US3 adds its tests and the endpoint's documented statuses.
- All three stories are P1 and ship in one PR.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: US1, US2 or US3 from spec.md

## Facts from the code that the tasks rely on

- **Branch and worktree:** `feature/021-ev-charger-live`, checked out in the worktree
  `../no.sanddata.ams.api-021`. Speckit scripts there need `SPECIFY_FEATURE_DIRECTORY=specs/021-ev-charger-live`.
- **The Zaptec client:** `src/Infrastructure/External/ZaptecEvChargerClient.cs`.
  - It is a primary-constructor class with `(HttpClient, IOptions<ZaptecOptions>, ZaptecTokenCache, ILogger<…>)`.
    The constructor doesn't change.
  - `GetJsonAsync<T>(path, call, ct)` calls `SendWithTokenAsync(path, call, retrySignIn: true, retryBusy: true, ct)`.
    `SendWithTokenAsync` already takes `retryBusy`, so only `GetJsonAsync` needs the new argument.
  - `SendAsync` catches `HttpRequestException or TaskCanceledException` only when the caller's token is not
    cancelled. When a linked timeout fires, the `OperationCanceledException` gets through, so `GetStateAsync` must
    catch it.
  - It deserializes with `JsonSerializerOptions { PropertyNameCaseInsensitive = true }` (`Json`), into private
    records at the end of the class.
  - Its log calls are `ZaptecLogMessages.RequestFailed/Unavailable/InvalidResponse/SignInRejected/RateLimited(logger,
    call)` (1920–1924).
- **`EvChargerErrors`** are in `src/Application/EvChargers/EvChargerModels.cs`. `EvChargerErrorStatus.Of(error,
  onConnect: false)` in `src/Features/EvChargers/Endpoints/EvChargerAdminEndpoints.cs` maps them to `503`/`502`/`404`.
- **The access rule to copy:** `GetChargingSessionsQueryHandler`, using `query.IsAdmin || await
  locations.IsUserAssociatedAsync(userId, locationId, ct)`, then `locations.GetByIdAsync(...) is null`. Either
  failing gives `Error.NotFound("Location.NotFound", "Location not found")`.
- **The charger lookup:** `IEvChargerRepository<EvCharger>.GetByLocationAsync(locationId, ct)`. `EvCharger.ChargerId`
  is the Zaptec charger id.
- **The endpoint pattern:** `GetChargingSessionsEndpoint` in `src/Features/EvChargers/Endpoints/EvChargerEndpoints.cs`.
  - It uses `AuthSchemes(JwtBearerDefaults.AuthenticationScheme)`, `Tags("EvCharger")` and a `Summary` with
    `s.Response(...)` lines.
  - It reads the user id from `ClaimTypes.NameIdentifier`, and Admin from `User.IsInRole(RoleNames.Admin)`.
  - On failure it calls `AddError(result.Error.Description, result.Error.Code)` and then
    `ThrowIfAnyErrors(EvChargerErrorStatus.Of(...))`.
- **Singletons** for Features go in `src/Features/ServiceCollectionExtensions.cs`. Handlers are registered by the
  source generator, never by hand. `TimeProvider` is already registered as a singleton (`AddInfrastructureToDI.cs`).
- **Test helpers in `tests/Features.Tests/EvChargers/`:**
  - `ZaptecStubHandler` answers by `AbsolutePath`, from `_answers[key]`. It has `Delay`, `Throw`, `Count(path)` and
    `Answer(key, …)`, and it sets `Retry-After` on a 429.
  - `ZaptecSamples` holds `ChargerId`, `Username`, `Password`, `Token` and the JSON samples.
  - `ZaptecEvChargerClientTests.Client(handler, configured, cache, timeout)` builds the client, and that file has a
    private `ManualTimeProvider` with `Advance`.
  - `EvChargerHandlerTests` uses a substituted `ILocationRepository<Location>`, `IEvChargerRepository<EvCharger>` and
    `IChargingSessionRepository`, and `FixedTimeProvider` from `tests/Features.Tests/NorgesPris/NorgesPrisTestDoubles.cs`.
  - `NoSecretsInLogsTests` runs scenarios `ok`, `token-401`, `data-401`, `data-503`, `bad-json` and `network`, with a
    `CapturingLoggerProvider`.
- **EventIds:** the EvCharger range is 1900–1999. 1900–1903, 1910–1915 and 1920–1924 are used, and 1930–1939 is free
  for the live state.

---

## Phase 1: Setup

- [X] T001 In the worktree `../no.sanddata.ams.api-021` on `feature/021-ev-charger-live`, check that the baseline is
  green: run `dotnet build`, `dotnet test` and `dotnet format --verify-no-changes` from the repo root, and note the
  test count.
- [X] T002 **Gate:** Roy runs [quickstart.md](quickstart.md) §0 with his Zaptec login, typed in the shell and never
  written to a file. He reads the filtered state while charging, just after plugging in, and with no car. Record the
  following under a new "Verified by hand" block at the end of R1 in `specs/021-ev-charger-live/research.md`:
  - the exact field names and the `Timestamp` format, including whether it has fractions of a second;
  - what 553 holds just after plugging in;
  - what 710, 513 and 553 hold with no car.

  If 553 is reset to 0 at once when a car is plugged in, note that R2 rule 4 (kWh is 0 when 553 is older than 721)
  is still kept, as a guard. If the answer isn't an array of `{StateId, Timestamp, ValueAsString}`, stop and update
  R1 and data-model.md first.
  - **Done in part (2026-11-24):** the charging read is recorded in research R1, and the samples now use its null
    check-in. The other two reads were left out, on Roy's word. The code doesn't depend on them (R1, "Not read by
    hand").

---

## Phase 2: Foundational (blocks all stories)

### Application

- [X] T003 [P] Add to `src/Application/EvChargers/EvChargerModels.cs`:
  - `public sealed record ZaptecStateValue<T>(T Value, DateTime TimestampUtc);`
  - `public sealed record ZaptecChargerState(ZaptecStateValue<bool>? Online, DateTime? LastCheckInUtc,
    ZaptecStateValue<decimal>? PowerW, ZaptecStateValue<decimal>? SessionKwh, ZaptecStateValue<int>? OperationMode,
    DateTime? SessionSetUtc);`

  Give each a doc comment with the Zaptec state id of each field (-2, -1, 513, 553, 710, 721; data-model.md), and
  say that no other value is kept (FR-006).
- [X] T004 [P] Add `Task<Result<ZaptecChargerState>> GetStateAsync(Guid chargerId, CancellationToken
  cancellationToken);` to `src/Application/Common/Interfaces/External/IEvChargerClient.cs`. Its doc comment should
  say: the charger's latest reported values; at most 3 seconds; no wait-and-retry after a 429; `NotConfigured`
  without a login; never throws for HTTP, timeout or parsing failures (research R4).

### Test fixtures

- [X] T005 [P] In `tests/Features.Tests/EvChargers/ZaptecSamples.cs`, add the samples below. Use the field names and
  timestamp format from T002, with no `Z` on timestamps.
  - `StateCharging`: the filtered "charging" answer from T002. Use `ChargerId` from the samples, and a made-up
    session id for 721. Add three extra entries with other ids (for example 908 with a fake serial-like value, 960
    with a fake SIM-like value, and 554 with a long fake signed-meter string), so the tests can check that they are
    dropped.
  - `StateNoCar`: mode 1, online 1, with a recent check-in.
  - `StateUnreadable`: 710 with value `"x"` and 513 with value `""`.
  - **Done provisionally (2026-11-24):** the samples use the shape and figures in the spec's input (Roy's read during a
    charge), because T002 is still open. When T002 is run, check the field names and timestamp format against them
    and update `ZaptecSamples` if they differ.
- [X] T006 [P] In `tests/Features.Tests/EvChargers/ZaptecStubHandler.cs`, add `public static string StatePath =>
  $"/api/chargers/{ZaptecSamples.ChargerId}/state";` and a default answer `[StatePath] = (OK,
  ZaptecSamples.StateCharging)`. The stub already keys by `AbsolutePath`, so no other change is needed.

### Infrastructure: the client

- [X] T007 Write the failing client tests in `tests/Features.Tests/EvChargers/ZaptecEvChargerClientTests.cs`, using
  the existing `Client(...)` helper:
  - **`GetStateAsync_KeepsTheSixValues`:** `StateCharging` gives:
    - `OperationMode.Value == 3`, with the timestamp from the sample, in UTC and `DateTimeKind.Utc`;
    - `PowerW.Value == 4414.704m`;
    - `SessionKwh` as in the sample;
    - `Online.Value == true`;
    - `LastCheckInUtc` and `SessionSetUtc` set.
  - **`GetStateAsync_DropsOtherValues`:** the result serialized with `JsonSerializer` contains none of the fake
    serial, SIM or meter strings, and not the session id.
  - **`GetStateAsync_UnreadableValuesAreNull`:** `StateUnreadable` is a success, with `OperationMode` and `PowerW`
    null.
  - **`GetStateAsync_NotAnArray_IsInvalidResponse`:** the body `{}` gives `EvChargerErrors.InvalidResponse`.
  - **`GetStateAsync_RateLimited_FailsAtOnce`:** a 429 on `StatePath` gives `Unavailable`, and
    `handler.Count(StatePath) == 1`.
  - **`GetStateAsync_SlowZaptec_GivesUpAfter3Seconds`:** with `handler.Delay = 5 s`, the result is `Unavailable`
    within 4.5 s, measured with a `Stopwatch`.
  - **`GetStateAsync_401_SignsInAgainOnce`:** a 401 then a 200 on `StatePath` succeeds, with 2 token calls.
  - **`GetStateAsync_NotConfigured`:** gives `NotConfigured`, with no request recorded.
  - **`GetCompletedSessionsAsync_StillRetriesAfterA429`:** a 429 then a 200 on `SessionsPath` succeeds, with
    `Count(SessionsPath) == 2`. This shows spec 016 is unaffected (FR-003).
- [X] T008 Implement `GetStateAsync` in `src/Infrastructure/External/ZaptecEvChargerClient.cs` so that T007 passes:
  - **Constants:** `public static readonly TimeSpan StateTimeout = TimeSpan.FromSeconds(3);`, and the six ids as
    `private const int` (`OnlineId = -2`, `CheckInId = -1`, `PowerId = 513`, `SessionKwhId = 553`, `ModeId = 710`,
    `SessionId = 721`).
  - **`GetJsonAsync`:** add the argument `bool retryBusy = true` and pass it through to `SendWithTokenAsync`.
  - **The read:**
    1. Create `using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);` and call
       `timeout.CancelAfter(StateTimeout)`.
    2. Call `GetJsonAsync<List<StateDto>>($"api/chargers/{chargerId}/state", "state", timeout.Token,
       retryBusy: false)`.
    3. Wrap it in `try`/`catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)`. In
       the catch, call `ZaptecLogMessages.Unavailable(logger, "state")` and return `EvChargerErrors.Unavailable`.
  - **The DTO:** `private sealed record StateDto(int StateId, string? Timestamp, string? ValueAsString);`.
  - **Parsing:** a timestamp is parsed with `DateTime.TryParse(…, CultureInfo.InvariantCulture,
    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out …)`. A value that can't be parsed is
    null: int for -2 and 710, where online is `== 1`, and decimal with `NumberStyles.Float` and invariant culture
    for 513 and 553. For -1 and 721, only the timestamp is kept.
  - **Duplicates:** if an id appears twice, take the entry with the latest timestamp.
  - **Comment:** add to the class's doc comment that the state answer was checked on Roy's Go on 2026-11-24
    (spec 021).

**Checkpoint**: `dotnet test --filter "FullyQualifiedName~ZaptecEvChargerClient"` is green.

---

## Phase 3: User Story 1 - See the charger's state right now (Priority: P1) 🎯 MVP

**Goal**: The endpoint gives the state, power, kWh and times for a location's charger (FR-001).

**Independent Test**: Quickstart §2 step 2. While the car charges at Hjemme, the state is `charging`, and the power
and kWh match the Zaptec app.

### Tests for User Story 1

- [X] T009 [P] [US1] Write `tests/Features.Tests/EvChargers/ChargerLiveStateTests.cs` (pure). Use a fixed `now` and a
  `values` builder, one test per rule in data-model.md "Rules":
  - **Mode mapping:** 1 → `noCar`, 2 → `connected`, 3 → `charging`, 5 → `finished`, and 4 or null → `unknown`.
  - **Offline from the flag:** `Online` false → `offline`, with only `LastReportedAtUtc` and `ReadAtUtc` set.
  - **Offline from a late check-in:** a check-in 10 min 1 s ago with the flag true → `offline`. At exactly 10 min
    → not offline.
  - **Missing check-in:** `LastCheckInUtc` null → `offline`.
  - **Missing flag:** `Online` null with a recent check-in → online.
  - **Power:** only while `charging`. 4414.704 W → 4.41 kW. `connected` or `finished`, with 513 still at 4414.704 →
    null.
  - **kWh:** 12.9624 → 12.962. 553 older than 721 → 0. With `noCar` → null. With `unknown` → given.
  - **Times:** `PluggedInAtUtc` is 721's timestamp with `connected`, `charging` or `finished`, and null with
    `noCar`. `ChargingSinceUtc` is 710's timestamp only with `charging`.
  - **No charger:** `NoCharger()` has `State == noCharger` and every other field null.
- [X] T010 [P] [US1] Write `tests/Features.Tests/EvChargers/GetChargerStateHandlerTests.cs`, following
  `EvChargerHandlerTests` (substitutes, `FixedTimeProvider`):
  - a linked user with a charger, and a client returning a "charging" `ZaptecChargerState`, gets success with
    `state == "charging"` and `readAt` set;
  - a client failure `EvChargerErrors.Unavailable` gives that error.

  In US1 the handler calls the substituted client directly. T019 switches it, and these tests, to the cache.

### Implementation for User Story 1

- [X] T011 [P] [US1] Create `src/Application/EvChargers/ChargerLiveState.cs`, a `public sealed record` with the fields
  in data-model.md:
  - a `public static class States` with the seven string constants;
  - `public static readonly TimeSpan OfflineAfter = TimeSpan.FromMinutes(10);`
  - `public static ChargerLiveState From(ZaptecChargerState values, DateTime readAtUtc, DateTime nowUtc)`, which
    applies rules 1–6 in order;
  - `public static ChargerLiveState NoCharger()`.

  Round with `Math.Round(…, MidpointRounding.AwayFromZero)`: 2 decimals for kW and 3 for kWh. Add doc comments citing
  spec 021 and research R2, including that "charging since" is when charging last resumed.
- [X] T012 [P] [US1] In `src/Features/EvChargers/Queries/EvChargerQueries.cs`, add:
  - `public sealed record GetChargerStateQuery(Guid UserId, bool IsAdmin, Guid LocationId) :
    IQuery<Result<ChargerStateResponse>>;`
  - `public sealed record ChargerStateResponse(string State, bool? Online, decimal? PowerKw, decimal? SessionKwh,
    DateTime? PluggedInAt, DateTime? ChargingSince, DateTime? LastReportedAt, DateTime? ReadAt);`

  Add `<param>` docs that match contracts/endpoints.md, including that these figures are never stored or counted
  (FR-004).
- [X] T013 [P] [US1] Add `public static ChargerStateResponse ToStateResponse(ChargerLiveState state)` to
  `src/Features/EvChargers/Mappers/EvChargerMapper.cs`. Every `DateTime?` goes through `DateTime.SpecifyKind(…,
  DateTimeKind.Utc)`, so the JSON has `Z`.
- [X] T014 [US1] Create `src/Features/EvChargers/Handlers/GetChargerStateQueryHandler.cs`:
  - **Dependencies:** `ILocationRepository<Location> locations`, `IEvChargerRepository<EvCharger> chargers`,
    `IEvChargerClient client`, `TimeProvider timeProvider` and `ILogger<GetChargerStateQueryHandler> logger`.
  - **Order:**
    1. The access check, copied from `GetChargingSessionsQueryHandler`. It gives `Location.NotFound`.
    2. `chargers.GetByLocationAsync`. When there's none, return
       `Result.Success(EvChargerMapper.ToStateResponse(ChargerLiveState.NoCharger()))`.
    3. When `!client.IsConfigured`, return `EvChargerErrors.NotConfigured`.
    4. Read the state, then `ChargerLiveState.From(values, readAtUtc, timeProvider.GetUtcNow().UtcDateTime)`, then
       map it.
  - **Doc comment:** reads only, nothing is stored or counted (FR-004), and access is the same as for the session
    list (FR-005).

  It needs T009–T013.
- [X] T015 [US1] Add `GetChargerStateEndpoint : Endpoint<GetChargerStateRequest, ChargerStateResponse>` to
  `src/Features/EvChargers/Endpoints/EvChargerEndpoints.cs`, with `public record GetChargerStateRequest(Guid
  LocationId);`:
  - **Route and auth:** `Get("/api/locations/{locationId}/ev-charger/state")`, JWT only, tag `EvCharger`.
  - **Summary:** "The location's car charger state right now". The description says that Zaptec is read at most
    every 3 minutes and the answer is shared, and that the figures are never stored or counted.
  - **Responses:** list `200`, `401`, `404`, `502` and `503` as in the contract.
  - **Failure:** `ThrowIfAnyErrors(EvChargerErrorStatus.Of(result.Error, onConnect: false))`.
- [X] T016 [US1] Add `GetChargerStateValidator : Validator<GetChargerStateRequest>` with
  `RuleFor(r => r.LocationId).NotEmpty();` to `src/Features/EvChargers/Validators/EvChargerValidators.cs`, next to
  `GetChargingSessionsValidator` (Principle IV). The handler doesn't check the id again.

**Checkpoint**: T009 and T010 pass, and quickstart §2 step 2 gives `charging` against the real charger.

---

## Phase 4: User Story 2 - Kind to Zaptec (Priority: P1)

**Goal**: At most one Zaptec read per charger per 3 minutes (1 minute after a failure), shared by everyone, and none
when nobody asks (FR-002, FR-003).

**Independent Test**: Quickstart §2 step 3. Three calls give the same `readAt` and one `EvChargerStateRead` log line.

### Tests for User Story 2

- [X] T017 [P] [US2] Write `tests/Features.Tests/EvChargers/ChargerStateCacheTests.cs`:
  - **Setup:** a substituted `IEvChargerClient`, registered in a `ServiceCollection` whose provider's
    `IServiceScopeFactory` goes to the cache, and a manual `TimeProvider`. Copy the `ManualTimeProvider` pattern
    from `ZaptecEvChargerClientTests` into this file, since it is private there.
  - **Two calls within 3 min:** 1 client call, with the same `ReadAtUtc`.
  - **3 min after a good read:** advancing 3 min 1 s gives a new call.
  - **A failure:** it is returned to every caller for 59 s with no new call, and a new call is made after 1 min 1 s.
  - **Single flight:** while the client's task is held open with a `TaskCompletionSource`, two `GetAsync` calls make
    1 client call. Both get the result once it completes.
  - **Caller cancellation:** cancelling caller A's token makes A throw `OperationCanceledException` (or
    `TaskCanceledException`). Caller B still gets the result, and the client was called with
    `CancellationToken.None`.
  - **Separate chargers:** two different charger ids make two calls.
  - **Nobody asks:** creating the cache and advancing time makes no client call (SC-003).
- [X] T018 [US2] Create `src/Features/EvChargers/Services/ChargerStateCache.cs`, a singleton class
  `ChargerStateCache(IServiceScopeFactory scopes, TimeProvider timeProvider, ILogger<ChargerStateCache> logger)`:
  - **Constants:** `public static readonly TimeSpan GoodFor = TimeSpan.FromMinutes(3)` and `FailedFor =
    TimeSpan.FromMinutes(1)`.
  - **State:** a `private readonly object _lock` and a `Dictionary<Guid, Entry>`, where `Entry` is a
    `private sealed class` with `Task<Result<ZaptecChargerState>> Read`, `DateTime ReadAtUtc` and
    `DateTime ExpiresAtUtc`.
  - **`public async Task<Result<(ZaptecChargerState Values, DateTime ReadAtUtc)>> GetAsync(Guid chargerId, Guid
    locationId, CancellationToken ct)`:**
    1. Under the lock, reuse the entry if its task isn't completed, or it is completed and `now < ExpiresAtUtc`.
       Otherwise start `ReadAsync(chargerId, locationId)` and store it.
    2. Outside the lock, `await entry.Read.WaitAsync(ct)`.
  - **`ReadAsync`:**
    1. Take `long started = timeProvider.GetTimestamp()`. Create a scope, resolve `IEvChargerClient` from it (as
       `EvChargerReadService` does), and call `client.GetStateAsync(chargerId, CancellationToken.None)`.
    2. Set `ReadAtUtc` and `ExpiresAtUtc` (`+ GoodFor` or `+ FailedFor`).
    3. Log: `LogMessages.EvChargerStateRead(logger, locationId, elapsedMs)` on success, where `elapsedMs` is
       `(long)timeProvider.GetElapsedTime(started).TotalMilliseconds`, or
       `LogMessages.EvChargerStateFailed(logger, locationId, LogMessages.Reason(error))` on failure (T020).
    4. Return the result. Wrap it in a last-resort `try`/`catch (Exception)` that returns
       `EvChargerErrors.Unavailable`, so a faulted task is never cached.
  - **`ReadAtUtc`** is set before the task completes, so a caller can read it.

  The doc comment cites research R3: in memory, one container, lost on restart, and a read the caller can't cancel.
  The client is a transient typed `HttpClient` client, so the singleton resolves it per read and never holds one.
- [X] T019 [US2] Change `GetChargerStateQueryHandler` to take `ChargerStateCache cache` instead of
  `IEvChargerClient client`, but keep `client` for `IsConfigured`. It reads through `cache.GetAsync(charger.ChargerId,
  query.LocationId, ct)`, and gets `ReadAtUtc` from the result. Update T010's tests to build a real
  `ChargerStateCache` over the substituted client, registered as in T017. Add a test: two handler calls make one
  client call.
- [X] T020 [P] [US2] Add to `src/Features/EvChargers/Logging/LogMessages.cs` (research R7):
  - **1930** `EvChargerStateRead`, Information: "EV charger state of location {LocationId} read from Zaptec in
    {ElapsedMs} ms". The time is how long the Zaptec read took, sign-in included, for the SC-004 check.
  - **1931** `EvChargerStateFailed`, Warning: "EV charger state of location {LocationId} could not be read (reason:
    {Reason})".

  Update the class's doc comment to "1930 - 1939 the live state (spec 021)". If there is a `Reason(Error)` helper,
  reuse it. Otherwise add one that strips the `EvCharger.` prefix, as the comment describes.
- [X] T021 [US2] Register `services.AddSingleton<Features.EvChargers.Services.ChargerStateCache>();` in
  `src/Features/ServiceCollectionExtensions.cs`, next to the hosted services. Then run the API and check that it starts
  with no DI error.

**Checkpoint**: T017 passes, and quickstart §2 step 3 shows one `EvChargerStateRead` for three calls.

---

## Phase 5: User Story 3 - Only people who may see the location (Priority: P1)

**Goal**: An owner, a viewer or an Admin gets the state. Everyone else gets `404`. A location without a charger gets
`noCharger` with no Zaptec call (FR-005, clarification 1).

**Independent Test**: Quickstart §2 steps 4–5.

### Tests for User Story 3

- [X] T022 [P] [US3] Add to `tests/Features.Tests/EvChargers/GetChargerStateHandlerTests.cs`:
  - **Not linked, not Admin:** `Location.NotFound`, and neither `chargers.GetByLocationAsync` nor the client is
    called.
  - **Admin, not linked:** success.
  - **Admin, no such location:** `Location.NotFound`.
  - **Linked, no charger:** success with `State == "noCharger"`, every other field null, and the client not called.
  - **Not configured:** `client.IsConfigured == false` gives `EvChargerErrors.NotConfigured`, with `GetStateAsync`
    not called.
  - **Never counted:** for every case above, `_sessions` (`IChargingSessionRepository`) gets no calls
    (`_sessions.ReceivedCalls()` is empty) (FR-004).

### Implementation for User Story 3

- [X] T023 [US3] Fix anything in `GetChargerStateQueryHandler` that T022 shows is wrong. Check that the endpoint's
  `Summary` in `EvChargerEndpoints.cs` describes `404` as "Not linked to the location, or no such location", and the
  `200` as including `noCharger`.

**Checkpoint**: all handler tests pass, and quickstart §2 steps 4–5 behave as described.

---

## Phase 6: Polish & Cross-Cutting Concerns

- [X] T024 [P] Extend `tests/Features.Tests/EvChargers/NoSecretsInLogsTests.cs` with a theory over the same scenarios
  as the existing one, but on `StatePath` (`ok`, `token-401`, `data-401`, `data-503`, `bad-json`, `network`), plus
  `rate-limited` (429) and `slow` (`Delay` 5 s). It calls the real client's `GetStateAsync` and asserts the following
  (FR-006, FR-007):
  - `AssertNoSecrets(result)`;
  - no log line contains `ZaptecSamples.ChargerId.ToString()`;
  - no log line contains the fake serial, SIM or meter strings from T005.
- [X] T025 [P] Add a `## EV charger (Zaptec)` section to `DevelopmentGuide.md`, after the water heater section, with
  4–5 bullets:
  - spec 016's read job and spec 021's state endpoint (link the contract);
  - the shared `ChargerStateCache` (3 min, 1 min after a failure, single flight, in memory);
  - the state read gives up after 3 s and doesn't retry after a 429, while the session reads do;
  - only the six state ids are kept, and no charger id, URL or token is ever logged (`NoSecretsInLogsTests`);
  - the live figures are never stored or counted.
  - **Done (2026-11-24):** the section is placed *before* "Water heater (OSO)", not after it. Spec 020's branch
    appends a bullet at the end of the water heater section, and adding there too would make the two merges conflict.
- [ ] T026 Create `specs/_backlog/ev-charger-live-frontend.md` in the style of
  `specs/_backlog/water-heater-hot-water-frontend.md`. It is the handover for the frontend's spec 017 in
  `no.sanddata.ams.frontend`:
  - the endpoint and a link to the contract;
  - the seven states and which fields each has;
  - the Car tab shows the state and "updated …" from `readAt`/`lastReportedAt`;
  - the Now page shows "Car charging now: 4,4 kW · 13,0 kWh this charge · since 16:37", only with `charging`, in Oslo
    time;
  - ask every 3 minutes; asking more often gets the same answer;
  - on `502` or `503`, show "unavailable" and try at the next update;
  - `noCharger` hides the line and the tab section;
  - whether it is deployed (fill in the PR number once merged).
- [X] T027 Run `dotnet build` (no new warnings), `dotnet test` and `dotnet format --verify-no-changes` from the repo
  root. The test count should be T001's count plus the new tests.
  - **Done (2026-11-24):** 1,131 tests (T001: 1,073), the same 1 build warning as before, and format clean.
- [ ] T028 Run [quickstart.md](quickstart.md) §2, steps 1–7, locally with Roy's login while the car charges. Note
  anything that differs in research.md R1 "Verified by hand".
- [ ] T029 After deploy, run quickstart §3 steps 1–5 (SC-001 to SC-004, FR-004) and record the results at the end of
  `specs/021-ev-charger-live/quickstart.md` §3. Then set the spec's **Status** to `Implemented` in
  `specs/021-ev-charger-live/spec.md`.

---

## Dependencies & Execution Order

- **Setup (T001–T002):** T002 is a gate. If the state answer isn't what R1 expects, update the design first.
- **Foundational (T003–T008):** blocks every story.
  - T003–T006 can run in parallel, but T005 needs T002's filtered sample.
  - T007 needs T003–T006.
  - T008 needs T007, since the tests are written first.
- **US1 (T009–T016):** needs Phase 2.
  - T009–T013 can run in parallel.
  - T014 needs T011–T013.
  - T015 needs T012 and T014.
  - T016 needs T015, which declares the request.
- **US2 (T017–T021):** needs Phase 2 and T014.
  - T017 and T020 can run in parallel.
  - T018 needs T017 and T020.
  - T019 needs T018.
  - T021 needs T018.
- **US3 (T022–T023):** needs T014. Its tests are best written after T019, so they use the final constructor.
- **Polish (T024–T029):**
  - T024 needs T008.
  - T025 can be written any time after T015.
  - T026 is written after the PR is merged, so it can give the PR number.
  - T027 needs everything before it.
  - T028 needs T027.
  - T029 needs a deploy.

### Within each story

Tests are written first and fail. Then come the types and the mapper, then the handler, then the endpoint.

## Parallel Examples

```text
# Phase 2, together:
T003 EvChargerModels.cs | T004 IEvChargerClient.cs | T005 ZaptecSamples.cs | T006 ZaptecStubHandler.cs

# US1, together:
T009 ChargerLiveStateTests | T010 handler tests | T011 ChargerLiveState | T012 query + response | T013 mapper

# US2, together:
T017 ChargerStateCacheTests | T020 log messages

# Polish, together:
T024 NoSecretsInLogsTests | T025 DevelopmentGuide.md
```

## Implementation Strategy

- **MVP (Phase 2 + US1):** the endpoint answers the state. It isn't shared yet, so it must not be deployed without
  US2, because each request would call Zaptec.
- **The deployable unit is US1 + US2 + US3**, in one PR. US2 is what makes the endpoint safe to deploy, given Zaptec's
  fair-use rules, and US3 is what proves the access rules.
- **Commits:** follow the existing habit of one commit per phase or story, for example "Read the charger's state from
  Zaptec (spec 021, Phase 2)".
- **Handover:** T026 goes last, after the PR is merged, with its number.
