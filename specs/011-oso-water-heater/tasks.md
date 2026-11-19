---

description: "Task list for Read the OSO Water Heater"
---

# Tasks: Read the OSO Water Heater

**Input**: Design documents from `/specs/011-oso-water-heater/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/endpoints.md, quickstart.md

**Tests**: Required, as for specs 004 to 010. xUnit + NSubstitute in `tests/Features.Tests/` (it references
`Features`, and through it `Application`, `Infrastructure` and `Domain`). `dotnet build` and `dotnet test` must pass,
and `dotnet format --verify-no-changes --include <touched files>` must be clean for the files this feature touches (a
repo-wide check already fails on old files).

**Organization**: grouped by user story from spec.md: US1 an Admin connects a heater, US2 a household member reads
its state, US3 power sampling. **Delivery order: US1, US2, US3.** US2 and US3 both need US1's stored connection.
After US1, either can be done first; US3 needs no endpoint from US2.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependency on an incomplete task)
- **[Story]**: US1-US3 from spec.md

## Path Conventions

Paths are relative to the repository root. Handlers are discovered by `Cqrs.SourceGenerator`; do not register them by
hand. Patterns to follow:

- `src/Infrastructure/External/EntsoeSpotPriceClient.cs` for the typed HTTP client, its `Error` constants and its
  `catch` filters.
- `src/Features/ElectricityCost/Services/PriceFetchService.cs` for the background service.
- `src/Features/Pricing/Endpoints/LocationPricingEndpoints.cs` for Admin endpoints and a feature-local
  `StatusOf(Error)`.
- `src/Features/ElectricityCost/Handlers/GetCurrentHourCostQueryHandler.cs` for the location access check.
- `ExchangeRateEfRepository` / `IExchangeRateRepository<T>` for repositories.
- `tests/Features.Tests/Http/EndpointHost.cs` for HTTP tests.
- `tests/Features.Tests/NorgesPris/NorgesPrisTestDoubles.cs` (`FixedTimeProvider`) for the clock.

**Secrets rule for every task**: the OSO subscription key and the device id must never be written to a log, an
exception message, an error description, a response (except the 6+6 hint) or a committed file. Test data uses made-up
values.

Files are written with CRLF line endings by `dotnet format`; run it on the touched files at the end. When writing
files with a shell heredoc, avoid apostrophes in the text.

---

## Phase 1: Setup

- [ ] T001 Add the configuration entries: `"WaterHeater": { "SampleIntervalSeconds": 60 }` in
  `src/api/appsettings.json`. Do not add a `KeyEncryptionKey` there. Add
  `WaterHeater__KeyEncryptionKey: ${WATER_HEATER_KEY_ENCRYPTION_KEY}` to the `api` service `environment:` in
  `compose.prod.yaml`, with a comment like the ENTSO-E one ("AES key for the OSO subscription keys; generate with
  openssl rand -base64 32. Without it a water heater cannot be connected.").
- [ ] T002 [P] Claim EventId range **1800-1899 WaterHeater**: add the row to the EventId table in `CLAUDE.md` and in
  `DevelopmentGuide.md` (logging section), between 1700-1799 Pricing and 2000-2099 Infra/Logging.

---

## Phase 2: Foundational (blocks US1, US2, US3)

**Purpose**: entities, tables, repositories, the secret protector and the OSO client. No endpoint yet.

### Domain and storage

- [ ] T003 [P] Create `src/Domain/Common/Entities/WaterHeater.cs` (`: Entity`), following data-model.md:
  - `Guid LocationId`, `string DeviceId`, `string ProtectedSubscriptionKey`, `DateTime ConnectedAtUtc`,
    `DateTime UpdatedAtUtc`, all with private setters, plus a parameterless constructor for EF.
  - Constructor `(Guid id, Guid locationId, string deviceId, string protectedKey, DateTime nowUtc)`: stores
    `deviceId.ToLowerInvariant()` and sets both times to `nowUtc`.
  - `Replace(string deviceId, string protectedKey, DateTime nowUtc)`: keeps `ConnectedAtUtc` and sets `UpdatedAtUtc`.
  - Override `ToString()` to return only `WaterHeater(LocationId=…)`.
- [ ] T004 [P] Create `src/Domain/Common/Entities/WaterHeaterPowerSample.cs` (`: Entity`) with `Guid LocationId`,
  `DateTime SampledAtUtc` and `decimal PowerKw`, a constructor `(Guid id, Guid locationId, DateTime sampledAtUtc,
  decimal powerKw)` and a parameterless constructor for EF.
- [ ] T005 Create `src/Infrastructure/Database/Configuration/WaterHeaterConfiguration.cs` and
  `WaterHeaterPowerSampleConfiguration.cs` (pattern: `ExchangeRateConfiguration.cs`):
  - `WaterHeater`: table `WaterHeater`; `DeviceId` `HasMaxLength(200)` required, unique index; unique index on
    `LocationId`; `ProtectedSubscriptionKey` `HasMaxLength(500)` required; FK to `Location` with
    `OnDelete(DeleteBehavior.Restrict)` and no navigation.
  - `WaterHeaterPowerSample`: table `WaterHeaterPowerSample`; `PowerKw` `HasPrecision(9, 6)`; index on
    `(LocationId, SampledAtUtc)`; FK to `Location` with `Restrict`; **no FK to `WaterHeater`** (research R8).

  Add `DbSet<WaterHeater> WaterHeater` and `DbSet<WaterHeaterPowerSample> WaterHeaterPowerSample` to
  `src/Infrastructure/Database/ApplicationDbContext.cs`, and to `IApplicationDbContext` if the other sets are there.
- [ ] T006 Create the migration per `DatabaseMigrations.md`: `dotnet ef migrations add AddWaterHeater --project
  src/Infrastructure/Infrastructure.csproj --startup-project src/api/api.csproj --output-dir Database/Migrations`.
  Read the generated file and confirm it touches no existing table.
- [ ] T007 [P] Create `src/Application/Common/Interfaces/Repositories/IWaterHeaterRepository.cs`
  (`IWaterHeaterRepository<T> : IRepository<T>`) with:
  - `GetByLocationAsync(Guid locationId, ct)`: tracked.
  - `GetByDeviceIdAsync(string deviceId, ct)`: lowercased comparison.
  - `GetForActiveLocationsAsync(ct)`: no tracking; heaters whose `Location.IsActive` is true.

  Create `IWaterHeaterPowerSampleRepository<T> : IRepository<T>` in the same folder, with nothing beyond the base for
  now.
- [ ] T008 Implement `src/Infrastructure/Database/Repositories/WaterHeaterEfRepository.cs` and
  `WaterHeaterPowerSampleEfRepository.cs` (`: GenericEfRepository<…>`). `GetForActiveLocationsAsync` joins
  `_context.Location` on `Id` with `IsActive`. Register both as scoped in `src/Infrastructure/AddInfrastructureToDI.cs`.

### Application models and interfaces

- [ ] T009 [P] Create `src/Application/WaterHeater/WaterHeaterModels.cs` per data-model.md "Not stored":
  - `record WaterHeaterCredentials(Guid LocationId, string DeviceId, string SubscriptionKey)`, with `ToString()`
    overridden to print only the location id.
  - `enum SpecialMode { None, Holiday, Supercharge, BlueBattery }`.
  - `record OsoCodes(int? ControlMode, int? OptimizationOption, int? OptimizationSubOption, int? ProfileId)`.
  - `record WaterHeaterReading(...)` with every field in the data-model table. Use `decimal` for °C, kW and kWh, and
    `int?` for litres, showers and baths.
  - A static class `WaterHeaterErrors` with the `Error` values from contracts/endpoints.md and fixed descriptions
    that contain no ids:
    - `Rejected` (`Error.Problem`, "OSO rejected the key or device id; an Admin must reconnect the water heater")
    - `Unavailable` (`Problem`)
    - `InvalidResponse` (`Problem`)
    - `KeyUnreadable` (`Problem`)
    - `EncryptionNotConfigured` (`Problem`)
    - `NotConnected` (`NotFound`, "No water heater is connected to this location")
    - `DeviceInUse` (`Conflict`)
- [ ] T010 [P] Create `src/Application/Common/Interfaces/External/IWaterHeaterClient.cs` with only
  `Task<Result<WaterHeaterReading>> GetStateAsync(WaterHeaterCredentials, CancellationToken)` and
  `Task<Result<decimal>> GetActualLoadAsync(WaterHeaterCredentials, CancellationToken)`. Its doc comment says it is
  read only on purpose (FR-015) and that it never throws for HTTP or parsing failures.
- [ ] T011 [P] Create `src/Application/Common/Interfaces/Security/ISecretProtector.cs`:
  - `bool IsConfigured { get; }`
  - `string Protect(string plaintext, Guid associatedId)`
  - `Result<string> Unprotect(string protectedValue, Guid associatedId)`, which fails with
    `WaterHeaterErrors.KeyUnreadable`.

### Secret protector (research R1)

- [ ] T012 Write `tests/Features.Tests/WaterHeater/AesGcmSecretProtectorTests.cs` first (fail before T013):
  - Round trip.
  - The output starts with `v1:` and does not contain the plaintext.
  - Two `Protect` calls give different outputs (a fresh nonce each time).
  - A tampered byte fails.
  - A different `associatedId` fails.
  - A different key fails.
  - Missing config gives `IsConfigured == false`.
  - A config value that is not base64 or not 32 bytes gives `IsConfigured == false` and no exception at
    construction.
- [ ] T013 Implement `src/Infrastructure/Security/AesGcmSecretProtector.cs`:
  - It reads `WaterHeater:KeyEncryptionKey` (base64, 32 bytes) from `IConfiguration` once.
  - `Protect` uses `AesGcm` with a 16-byte tag, a 12-byte `RandomNumberGenerator` nonce and the UTF-8 bytes of
    `associatedId.ToString("N")` as associated data, and returns `"v1:" + base64(nonce ‖ ciphertext ‖ tag)`.
  - `Unprotect` returns `KeyUnreadable` on a wrong prefix, bad base64, `AuthenticationTagMismatchException` or a
    missing key. It never throws, and never includes the value in an error.
  - `Protect` throws `InvalidOperationException` when not configured. Callers check `IsConfigured` first.

  Register it as a singleton for `ISecretProtector` in `AddInfrastructureToDI.cs`. Make T012 pass.

### OSO client (research R2, R3, R5)

- [ ] T014 [P] Create `tests/Features.Tests/WaterHeater/OsoSamples.cs`, a static class with the five JSON answers of
  2026-11-19 from `specs/_backlog/oso-water-heater.md` ("Tried by hand") as C# raw string constants. Also add one
  variant with `isInHolidayMode: true` and holiday times, one with `heater: 1`, and one with `temperatureOne: 55.0`.
  The device id appears nowhere in them. Add a stub `HttpMessageHandler` (`OsoStubHandler`) in the same folder that
  answers by path suffix (`/Status`, `/CurrentTemperature`, `/ActualLoad`, `/TappingCapacityKwh`), with a status code
  per path. It records each request's path and the `Ocp-Apim-Subscription-Key` header, and can be told to throw
  `HttpRequestException` or to delay past a timeout.
- [ ] T015 Write `tests/Features.Tests/WaterHeater/OsoWaterHeaterClientTests.cs` first (fail before T016), using
  `OsoStubHandler` and made-up credentials. It checks:
  - `GetStateAsync` maps the 2026-11-19 answers to: reading time `2026-11-19T18:06:48.703Z` UTC, online, not heating,
    set point 72, top 72.8 / mid 72.3 / low 70.5, fourth `null`, 344 of 360 litres, 3 showers, 1 bath, 0.011774 kW,
    13.97741 kWh, `Supercharge` from `2026-11-18T19:40:14.5919821` UTC to `2026-11-19T19:40:14.5919821` UTC,
    profile name `SuperchargeMode`, and codes (0, 1, 2, 3).
  - `temperatureOne` 55.0 is kept.
  - The holiday variant gives `Holiday` with its times.
  - `heater: 1` gives `IsHeating`.
  - `GetActualLoadAsync` returns 0.011774.
  - The request path is `1/Device/{deviceId}/Status` etc. under the base address, and the key header is sent.
  - Error mapping: 401, 403 and 404 give `Rejected`; 500 and 503 give `Unavailable`; `HttpRequestException` gives
    `Unavailable`; a timeout gives `Unavailable` (not an exception); 400 gives `InvalidResponse`; malformed JSON gives
    `InvalidResponse`. A failure in any one of the four state calls fails `GetStateAsync`.
  - No error description contains the device id or key.
- [ ] T016 Implement `src/Infrastructure/External/OsoWaterHeaterClient.cs` (`IWaterHeaterClient`, primary
  constructor `(HttpClient httpClient, ILogger<OsoWaterHeaterClient> logger)`):
  - Private DTO records with OSO's exact names (`holidyModeStartTime`, `tappingCapacitykWh`, …) and
    `JsonSerializerOptions { PropertyNameCaseInsensitive = true }`.
  - One `SendAsync(credentials, path, ct)` helper builds `HttpRequestMessage(GET, $"1/Device/{Uri.EscapeDataString(
    deviceId)}/{path}")` and adds the `Ocp-Apim-Subscription-Key` header on the request.
  - `GetStateAsync` runs the four calls with `Task.WhenAll`.
  - Times are parsed with `DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal`.
  - `temperatureOne == 0` becomes `null`.
  - Catch filters as in the ENTSO-E client, with `TaskCanceledException` when the caller did not cancel giving
    `Unavailable`.
  - Log with the new entries in `src/Infrastructure/External/Logging/LogMessages.cs` (`OsoRequestFailed`
    1820 with location id, path name and status code; `OsoUnavailable` 1821; `OsoInvalidResponse` 1822). Never log
    the URL, the device id, the key or `ex.Message`, and never pass the exception object.
- [ ] T017 Register the client in `src/Infrastructure/AddInfrastructureToDI.cs`:
  `services.AddHttpClient<IWaterHeaterClient, OsoWaterHeaterClient>(c => { c.BaseAddress = new
  Uri("https://api.osoenergy.no/water-heater-api/"); c.Timeout = TimeSpan.FromSeconds(10); }).RemoveAllLoggers();`.
  Keep the trailing slash on the base address. Add a comment saying why `RemoveAllLoggers` is there (research R3: the
  factory logs the URL path, which holds the device id). Make T015 pass.

**Checkpoint**: `dotnet build` and `dotnet test` pass; the migration exists; nothing is reachable over HTTP yet.

---

## Phase 3: User Story 1 - An Admin connects a water heater to a location (Priority: P1) MVP

**Goal**: an Admin can connect, read, replace and remove a heater per location; the key is checked with OSO, stored
encrypted, and never returned or logged.

**Independent Test**: as Admin, `PUT` a key and device id that OSO accepts, then `GET` and see a `deviceIdHint` and
no key; a wrong key gives `400 WaterHeater.Rejected` and nothing is saved; a regular user gets `403`.

### Tests for User Story 1

- [ ] T018 [P] [US1] Write `tests/Features.Tests/WaterHeater/ConnectWaterHeaterHandlerTests.cs`. Use NSubstitute for
  `IWaterHeaterClient`, `ISecretProtector`, `ILocationRepository<Location>` and `IWaterHeaterRepository<WaterHeater>`,
  plus a `FixedTimeProvider`. Cases:
  - OSO accepts: a heater is inserted with the protected key and the lowercased device id, and the settings come back.
  - OSO rejects: `Rejected`, nothing inserted or updated.
  - OSO unavailable: `Unavailable`, nothing saved.
  - Encryption not configured: `EncryptionNotConfigured`, and OSO is not called.
  - Unknown location: `Location.NotFound`.
  - Device connected to another location: `DeviceInUse`.
  - Same device at the same location with a new key: `Replace` keeps `ConnectedAtUtc` and updates `UpdatedAtUtc`.
  - OSO is checked with the new credentials before anything is saved.
- [ ] T019 [P] [US1] Write `tests/Features.Tests/WaterHeater/WaterHeaterAdminHandlerTests.cs`:
  - Get settings: `NotConnected` when there is no heater; the hint is first 6 + `…` + last 6.
  - Remove: deletes the heater row and touches no samples; `NotConnected` when there is none; `Location.NotFound`
    for an unknown location.
- [ ] T020 [P] [US1] Write `tests/Features.Tests/Http/WaterHeaterAdminHttpTests.cs` with `EndpointHost`:
  - `PUT` gives 200, and 400 for an empty key, an empty device id or a non-hex device id (validator, the dispatcher
    is not called).
  - `PUT` maps the errors from contracts/endpoints.md: Rejected 400, DeviceInUse 409, InvalidResponse 502,
    Unavailable 503, EncryptionNotConfigured 503, Location.NotFound 404.
  - `GET` gives 200 or 404; `DELETE` gives 204 or 404.
  - All three give 401 when not signed in and 403 as a regular user.
  - The 200 body has no `subscriptionKey` property.
- [ ] T021 [P] [US1] Extend `tests/Features.Tests/Authentication/RequestLoggingRedactionTests.cs`: with
  `LogRequestBody: true`, a `PUT /api/admin/locations/{id}/water-heater` request body is logged as the mask value.
  Paths with other casing or a trailing slash are also masked. `PUT /api/admin/location-pricing/{id}` is still logged
  as configured.

### Implementation for User Story 1

- [ ] T022 [P] [US1] Create `src/Features/WaterHeater/Logging/LogMessages.cs` (EventIds 1800-1819, the client uses
  1820-1829, the sampler 1830-1849). The class doc comment says the key and device id are never logged. Entries:
  - `WaterHeaterConnected` 1800 (userId, locationId, replaced bool)
  - `WaterHeaterConnectRefused` 1801 (userId, locationId, reason code)
  - `WaterHeaterRemoved` 1802 (userId, locationId)
  - `WaterHeaterStateRead` 1803 (userId, locationId)
  - `WaterHeaterStateFailed` 1804 (locationId, reason code)
  - `LocationAccessDenied` 1805 (userId, locationId)
- [ ] T023 [P] [US1] Create `src/Features/WaterHeater/Commands/WaterHeaterCommands.cs`:
  `ConnectWaterHeaterCommand(Guid UserId, Guid LocationId, string SubscriptionKey, string DeviceId) :
  ICommand<Result<WaterHeaterSettingsResponse>>` and `RemoveWaterHeaterCommand(Guid UserId, Guid LocationId) :
  ICommand<Result>`. Override `ToString()` on the connect command so it prints no key or device id. Create
  `src/Features/WaterHeater/Queries/WaterHeaterQueries.cs` with `GetWaterHeaterSettingsQuery(Guid LocationId)` and
  `record WaterHeaterSettingsResponse(Guid LocationId, string DeviceIdHint, DateTime ConnectedAt, DateTime
  UpdatedAt)`.
- [ ] T024 [P] [US1] Create `src/Features/WaterHeater/Mappers/WaterHeaterMapper.cs` with `ToSettings(WaterHeater)` and
  `DeviceIdHint(string)`. The hint is first 6 + `…` + last 6, or the device id masked entirely when it has 12
  characters or fewer.
- [ ] T025 [US1] Implement `src/Features/WaterHeater/Handlers/ConnectWaterHeaterCommandHandler.cs`, in this order:
  1. The location exists, otherwise `Location.NotFound`.
  2. `protector.IsConfigured`, otherwise `EncryptionNotConfigured`.
  3. Another location already has this device (`GetByDeviceIdAsync`), otherwise `DeviceInUse`.
  4. `client.GetStateAsync(new WaterHeaterCredentials(...))`; a failure returns its error unchanged.
  5. Insert, or `Replace` an existing heater, with `protector.Protect(key, locationId)`, then `SaveChangesAsync`.
  6. Log `WaterHeaterConnected` or `WaterHeaterConnectRefused` with the error code.

  Make T018 pass.
- [ ] T026 [US1] Implement `RemoveWaterHeaterCommandHandler.cs` and `GetWaterHeaterSettingsQueryHandler.cs` in
  `src/Features/WaterHeater/Handlers/`. Make T019 pass.
- [ ] T027 [US1] Create `src/Features/WaterHeater/Validators/ConnectWaterHeaterValidator.cs`
  (`Validator<ConnectWaterHeaterRequest>`):
  - `LocationId` is not empty.
  - `SubscriptionKey` is not empty, at most 200 characters, and has no whitespace.
  - `DeviceId` is not empty, at most 200 characters, and matches `^[0-9a-fA-F]+$`.

  Validation messages must not echo the values.
- [ ] T028 [US1] Create `src/Features/WaterHeater/Endpoints/WaterHeaterAdminEndpoints.cs`:
  - `record ConnectWaterHeaterRequest(Guid LocationId, string SubscriptionKey, string DeviceId)`.
  - `ConnectWaterHeaterEndpoint` (`Put("/api/admin/locations/{locationId}/water-heater")`),
    `GetWaterHeaterSettingsEndpoint` (`Get`) and `RemoveWaterHeaterEndpoint` (`Delete`, `204`).
  - All three use `AuthSchemes(JwtBearerDefaults.AuthenticationScheme)`, `Roles(RoleNames.Admin)`,
    `Tags("WaterHeater")` and a `Summary` with every status from contracts/endpoints.md.
  - A `public static class WaterHeaterErrorStatus { public static int Of(Error e, bool onConnect) }` maps by code:
    - Rejected: 400 when `onConnect`, otherwise 502
    - InvalidResponse and KeyUnreadable: 502
    - Unavailable and EncryptionNotConfigured: 503
    - NotFound type: 404
    - Conflict type: 409
    - anything else: 400

  Make T020 pass.
- [ ] T029 [US1] Add `CarriesSecretInRequest(string method, PathString path)` to
  `src/Infrastructure/Logging/SensitiveData.cs`. It is true for `PUT` on `api/admin/locations/{id}/water-heater`,
  matched case-insensitively per segment as `RevealsApiKey` does. In
  `src/Infrastructure/Middleware/RequestResponseLoggingMiddleware.cs`, set `requestBody` to the mask value when it is
  true, before it goes into `attributes`. Update the class doc comment of `SensitiveData`. Make T021 pass.

**Checkpoint**: US1 works on its own; quickstart step 2 can be run.

---

## Phase 4: User Story 2 - A household member sees the water heater's state (Priority: P1)

**Goal**: owners, viewers and Admins read the heater's live state with age, stale flag and clear OSO errors.

**Independent Test**: as a user linked to Hjemme, `GET /api/locations/{id}/water-heater` and compare litres,
showers, temperatures and heating with the OSO app; an unlinked user gets `404`.

### Tests for User Story 2

- [ ] T030 [P] [US2] Write `tests/Features.Tests/WaterHeater/GetWaterHeaterStateHandlerTests.cs`. Use NSubstitute for
  the client, protector and repositories, with `FixedTimeProvider` at `2026-11-19T18:08:31Z`. Cases:
  - A linked user gets the mapped state with `ageSeconds` 102 and `isStale` false.
  - An unlinked non-Admin gets `Location.NotFound`, and OSO is not called.
  - An unlinked Admin gets the state (research R7).
  - No heater: `NotConnected`.
  - Unprotect fails: `KeyUnreadable`, and OSO is not called.
  - Each client error is passed through.
  - A reading 16 minutes old is `isStale` true.
  - A reading 5 s in the future has age 0.
  - An unknown or inactive location gets `Location.NotFound`.
- [ ] T031 [P] [US2] Write `tests/Features.Tests/Http/WaterHeaterStateHttpTests.cs` with `EndpointHost`:
  - 200 with the JSON shape of contracts/endpoints.md: `temperatures`, `hotWater`, `specialMode.mode` as a string,
    `fourthCelsius` null, and `osoCodes`.
  - 401 when not signed in.
  - 404 for `Location.NotFound` and `NotConnected`.
  - 502 for Rejected, InvalidResponse and KeyUnreadable.
  - 503 for Unavailable.
  - A regular (non-Admin) user is allowed; the query gets `IsAdmin` false.

### Implementation for User Story 2

- [ ] T032 [P] [US2] Add `GetWaterHeaterStateQuery(Guid UserId, bool IsAdmin, Guid LocationId)` and the response
  records to `src/Features/WaterHeater/Queries/WaterHeaterQueries.cs`. Records:
  - `WaterHeaterStateResponse`
  - `WaterHeaterTemperatures(decimal? TopCelsius, decimal? MidCelsius, decimal? LowCelsius, decimal? FourthCelsius)`
  - `WaterHeaterHotWater(int? AvailableLitres40, int? MaxLitres40, int? Showers, int? Baths,
    decimal? StoredEnergyKwh)`
  - `WaterHeaterSpecialMode(string Mode, DateTime? Start, DateTime? End, string? ProfileName)`
  - `WaterHeaterOsoCodes(...)`

  The fields and names follow contracts/endpoints.md.
- [ ] T033 [P] [US2] Add `ToState(Guid locationId, WaterHeaterReading reading, DateTime nowUtc)` to
  `src/Features/WaterHeater/Mappers/WaterHeaterMapper.cs`. It works out `AgeSeconds` (never below 0) and `IsStale`
  (more than 900 s), and gives `SpecialMode` as its name. `Start` and `End` are null for `None` and `BlueBattery`.
- [ ] T034 [US2] Implement `src/Features/WaterHeater/Handlers/GetWaterHeaterStateQueryHandler.cs`, in this order:
  1. Access: `IsAdmin` or `IsUserAssociatedAsync`, otherwise log `LocationAccessDenied` and return
     `Location.NotFound`. For an Admin, the location must still exist and be active.
  2. Load the heater, otherwise `NotConnected`.
  3. Unprotect the key, otherwise `KeyUnreadable`.
  4. `client.GetStateAsync`.
  5. Map with `TimeProvider.GetUtcNow()`.
  6. Log `WaterHeaterStateRead`, or `WaterHeaterStateFailed` with the error code.

  Make T030 pass.
- [ ] T035 [US2] Create `src/Features/WaterHeater/Endpoints/GetWaterHeaterStateEndpoint.cs`:
  - `Get("/api/locations/{locationId}/water-heater")`, `AuthSchemes(JwtBearerDefaults.AuthenticationScheme)`, no
    role, `Tags("WaterHeater")`.
  - It passes `UserId` from `ClaimTypes.NameIdentifier` and `IsAdmin = User.IsInRole(RoleNames.Admin)`.
  - It maps errors with `WaterHeaterErrorStatus.Of(error, onConnect: false)`.
  - The summary documents every status.

  Make T031 pass.

**Checkpoint**: US1 and US2 work; quickstart step 3 can be run against a local API with Roy's heater.

---

## Phase 5: User Story 3 - The heater's power is sampled and stored (Priority: P2)

**Goal**: one power sample per heater per minute while connected and active; failures leave gaps, never stop the host,
and are logged at a limited rate.

**Independent Test**: connect a heater, wait 10 minutes and see about 10 rows in `WaterHeaterPowerSample`; break the
key and see one warning, no rows, every other endpoint working; fix it and see rows resume.

### Tests for User Story 3

- [ ] T036 [P] [US3] Write `tests/Features.Tests/WaterHeater/SampleFailureTrackerTests.cs` (a pure class with the time
  passed in):
  - The first failure returns `LogStart`.
  - Further failures within the hour return `None`.
  - A failure 60 minutes after the last log returns `LogStillFailing` with the minutes since the start.
  - A success after failures returns `LogRecovered` with the duration, and resets.
  - A success with no prior failure returns `None`.
  - Locations are tracked independently.
- [ ] T037 [P] [US3] Write `tests/Features.Tests/WaterHeater/WaterHeaterSamplingServiceTests.cs` against a public
  `SampleOnceAsync(IServiceProvider, CancellationToken)` (or an internal method with `InternalsVisibleTo` if the
  project already uses it). Use substituted repositories, client and protector, and `FixedTimeProvider`. Cases:
  - Two active heaters give two samples, with `SampledAtUtc` truncated to the second and the power as returned
    (0.011774 is not rounded).
  - The client fails for the first: the second is still sampled, only one row is inserted, and no 0 kW row.
  - Unprotect fails: no call to OSO, no row, `KeyUnreadable` reported to the tracker.
  - No heaters: no `SaveChangesAsync`.
  - An exception from the repository inside one heater's sample does not stop the others.

### Implementation for User Story 3

- [ ] T038 [P] [US3] Implement `src/Features/WaterHeater/Services/SampleFailureTracker.cs`. A `sealed class` holds a
  `Dictionary<Guid, (DateTime FailingSince, DateTime LastLoggedUtc)>`, with `Failed(Guid locationId, DateTime nowUtc)`
  and `Succeeded(Guid locationId, DateTime nowUtc)` returning a `SampleLogDecision` record (`Kind`, `Duration`). Make
  T036 pass.
- [ ] T039 [US3] Add the sampler log entries to `src/Features/WaterHeater/Logging/LogMessages.cs`:
  - `WaterHeaterSampleFailing` 1830, Warning (locationId, reason code)
  - `WaterHeaterSampleStillFailing` 1831, Warning (locationId, reason code, minutes)
  - `WaterHeaterSampleRecovered` 1832, Information (locationId, minutes)
  - `WaterHeaterSamplingCycleFailed` 1833, Error (exception type name only: `string`, not the exception)
- [ ] T040 [US3] Implement `src/Features/WaterHeater/Services/WaterHeaterSamplingService.cs` (`BackgroundService`,
  constructor `(IServiceScopeFactory, IConfiguration, TimeProvider, ILogger<…>)`), shaped like `PriceFetchService`:
  - A `PeriodicTimer` of `WaterHeater:SampleIntervalSeconds` (default 60).
  - A `do { try { await SampleOnceAsync(...) } catch (Exception ex) when (ex is not OperationCanceledException) { log
    1833 with ex.GetType().Name } } while (await timer.WaitForNextTickAsync(ct))`.
  - `SampleOnceAsync` opens a scope and loads `GetForActiveLocationsAsync`. For each heater, in its own
    `try/catch (Exception) when not OperationCanceledException`, it unprotects the key, calls `GetActualLoadAsync`,
    inserts a `WaterHeaterPowerSample` on success, and reports to the tracker. The tracker's decision is logged.
  - One `SaveChangesAsync` when at least one sample was inserted.
  - The tracker is a field (it lives as long as the service).

  Make T037 pass.
- [ ] T041 [US3] Register `services.AddHostedService<Features.WaterHeater.Services.WaterHeaterSamplingService>();` in
  `src/Features/ServiceCollectionExtensions.cs` next to `PriceFetchService`.

**Checkpoint**: all three stories work; quickstart step 4 can be run locally.

---

## Phase 6: Polish & Cross-Cutting Concerns

- [ ] T042 Write `tests/Features.Tests/WaterHeater/NoSecretsInLogsTests.cs` (SC-006). A capturing `ILoggerProvider`
  collects every formatted message, state value and exception text. It runs:
  - connect (accepted and rejected), state (success, Rejected, Unavailable) and one sampling cycle (success and
    failure);
  - the real `OsoWaterHeaterClient` over `OsoStubHandler`, the real `AesGcmSecretProtector` and the real handlers,
    with distinctive made-up values (key `TESTKEY-must-not-leak-0000`, device id `abcdef0123456789…`, 64 hex
    characters);
  - an `HttpClient` built through `IHttpClientFactory` with the same registration as `AddInfrastructureToDI` (so
    `RemoveAllLoggers` is covered).

  It asserts that neither value appears in any captured text, and that no response record serialised to JSON
  contains them (only the hint).
- [ ] T043 [P] Update `DockerDeployment.md`: in §3.4 add `WATER_HEATER_KEY_ENCRYPTION_KEY=<openssl rand -base64 32>`
  to the `.env` example with one line on what it protects and that losing it means reconnecting the heater; add it to
  the secrets checklist at the end. Note that `compose.prod.yaml` must be copied to the server again for this release.
- [ ] T044 [P] Add a short "Water heater (OSO)" section to `DevelopmentGuide.md` or the feature's endpoint summary
  pointing at `specs/011-oso-water-heater/contracts/endpoints.md`. It should say that the client is read only on
  purpose and that `RemoveAllLoggers` must stay.
- [ ] T045 Move the verified OSO API findings that the code relies on into a short comment block at the top of
  `OsoWaterHeaterClient.cs`: the misspelt fields, times without a zone, and `temperatureOne` 0. Then delete
  `specs/_backlog/oso-water-heater.md`, because research.md and this spec now hold its content (as the note itself
  says).
- [ ] T046 Run `dotnet build`, `dotnet test` and `dotnet format --verify-no-changes --include <every touched file>`,
  and fix anything they report.
- [ ] T047 Run quickstart steps 2-5 locally against Roy's real heater with a **new** OSO subscription key (the first
  was exposed in a chat transcript on 2026-11-19). Record the results in this task: the state matches the OSO app,
  the response time, rows per minute, and the log grep count of 0.
- [ ] T048 Production (quickstart step 6), after merge and deploy:
  1. Add `WATER_HEATER_KEY_ENCRYPTION_KEY` to `/opt/ams-api/.env` and copy `compose.prod.yaml` to the server.
  2. Deploy and confirm the migration `AddWaterHeater` ran.
  3. Connect the heater at Hjemme, read the state, and after a day confirm at least 1,368 samples (SC-004).

  Record the results here.
- [ ] T049 The next time a supercharge starts, compare `specialMode.start` with the start the OSO app shows (spec
  Assumptions). If they differ by 1 or 2 hours, change the parsing in `OsoWaterHeaterClient` to treat these times as
  Oslo time, add a test, and update the spec's assumption. Record the result here.

---

## Dependencies & Execution Order

### Phase dependencies

- **Setup (Phase 1)**: none.
- **Foundational (Phase 2)**: after Setup. Blocks all stories.
  - T003, T004, T007, T009, T010 and T011 can run in parallel.
  - T005 needs T003 and T004. T006 needs T005. T008 needs T005 and T007.
  - T012 needs T011, and T013 needs T012.
  - T014 needs nothing in this phase. T015 needs T009, T010 and T014. T016 needs T015. T017 needs T016.
- **US1 (Phase 3)**: after Phase 2.
- **US2 (Phase 4)**: after Phase 2. It uses US1's `WaterHeaterMapper` (T024) and `WaterHeaterErrorStatus` (T028). In
  practice it comes after US1, since without US1 a heater can only be connected through the database.
- **US3 (Phase 5)**: after Phase 2. Independent of US2. It needs a connected heater to run for real, which means US1.
- **Polish (Phase 6)**: after the stories it checks. T042 needs US1-US3. T047 needs T046. T048 needs the merge.
  T049 is open-ended.

### Within each story

The tests are written first and fail. Then records and mapper, then handler, then endpoint. Each story ends at its
checkpoint with `dotnet test` green.

### Parallel opportunities

- Phase 2: T003, T004, T007, T009, T010, T011 and T014 together, then T005 and T012.
- US1: T018, T019, T020 and T021 together, then T022, T023 and T024 together.
- US2: T030 and T031 together, then T032 and T033 together.
- US3: T036 and T037 together, then T038.
- Polish: T043 and T044 together.
- US2 and US3 can be built in parallel after US1.

## Parallel Example: User Story 1

```text
Task: "T018 ConnectWaterHeaterHandlerTests.cs"
Task: "T019 WaterHeaterAdminHandlerTests.cs"
Task: "T020 WaterHeaterAdminHttpTests.cs"
Task: "T021 RequestLoggingRedactionTests.cs (water-heater body masked)"
then
Task: "T022 LogMessages.cs", "T023 WaterHeaterCommands.cs + WaterHeaterQueries.cs", "T024 WaterHeaterMapper.cs"
```

## Implementation Strategy

### MVP first (US1 + US2)

1. Phase 1 and Phase 2: storage, protector, OSO client.
2. US1: connecting works, which proves the key is handled safely.
3. US2: the state is visible. **Stop and validate** with quickstart steps 2-3 against the real heater. This is the
   smallest version worth deploying.

### Incremental delivery

1. Ship US1 and US2 (one PR, or one each).
2. Add US3 sampling as soon as possible after that: every day not sampled is lost.
3. Polish: SC-006 test, deployment docs, backlog note removed, production check (T048), and the time-zone check
   (T049) when a supercharge happens.

## Notes

- `IWaterHeaterClient` has no write method. Adding one is a later spec (control), not a task here (FR-015).
- Nothing in this feature prints, logs or commits the real key or device id. Use made-up values in tests.
- Commit after each task or logical group, with the migration in the same commit as the configuration (constitution).
