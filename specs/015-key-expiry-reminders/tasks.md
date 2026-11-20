# Tasks: Reminders Before Keys Expire

**Input**: Design documents from `/specs/015-key-expiry-reminders/`

**Prerequisites**:
- [plan.md](plan.md) and [spec.md](spec.md)
- [research.md](research.md), with decisions R1 to R7
- [data-model.md](data-model.md)
- [contracts/endpoints.md](contracts/endpoints.md)
- [quickstart.md](quickstart.md)

**Tests**: Included. The plan and quickstart §1 name them. They use xUnit + NSubstitute in `tests/Features.Tests`, with
no database. The migration's SQL is checked by hand (quickstart §2).

**Organization**:
- Phase 2 is the stored date, which both stories need.
- US1 is setting and reading the date.
- US2 is the reminder list. It needs the date from Phase 2, but not US1's endpoints.

**Where to work**: in the worktree `no.sanddata.ams.api-015`, on branch `feature/015-key-expiry-reminders`. The main
checkout `no.sanddata.ams.api` has uncommitted work for spec 013. Leave it alone.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: US1 or US2 from spec.md

## Facts from the code that the tasks rely on

- **`WaterHeater`** (`src/Domain/Common/Entities/WaterHeater.cs`):
  - constructor `(Guid id, Guid locationId, string deviceId, string protectedSubscriptionKey)`
  - `Replace(deviceId, protectedSubscriptionKey)`
  - `CreatedAt` is when it was first connected, and `Replace` keeps it.
- **Where `new WaterHeater(` and `.Replace(` are called:**
  - `ConnectWaterHeaterCommandHandler` (lines ~70 and ~75)
  - `tests/Features.Tests/WaterHeaters/WaterHeaterTestData.cs` (`Heater(...)`)
  - `tests/Features.Tests/WaterHeaters/NoSecretsInLogsTests.cs` (2 places)
  - `tests/Features.Tests/Locations/UpdateOwnLocationCommandHandlerTests.cs` (1 place)
- **EF mapping:** `src/Infrastructure/Database/Configuration/WaterHeaterConfiguration.cs`. The table is
  `public."WaterHeater"`. The last migration is `20261119193232_AddWaterHeater`.
- **Migration command** (from `CLAUDE.md`): `dotnet ef migrations add <Name> --project
  src/Infrastructure/Infrastructure.csproj --startup-project src/api/api.csproj --output-dir Database/Migrations`.
- **The connect flow:**
  - `ConnectWaterHeaterRequest(Guid LocationId, string SubscriptionKey, string DeviceId)` and the endpoint are in
    `src/Features/WaterHeaters/Endpoints/WaterHeaterAdminEndpoints.cs`.
  - `ConnectWaterHeaterCommand` is in `Commands/WaterHeaterCommands.cs`.
  - The validator is `Validators/ConnectWaterHeaterValidator.cs`.
  - The handler has no `TimeProvider` yet.
  - Errors map to statuses through `WaterHeaterErrorStatus.Of(error, onConnect)`.
- **Heater settings and errors:**
  - `WaterHeaterSettingsResponse(LocationId, DeviceIdHint, ConnectedAt, UpdatedAt)` is in
    `Queries/WaterHeaterQueries.cs`, and `WaterHeaterMapper.ToSettings(heater)` maps it.
  - `WaterHeaterErrors` is in `src/Application/WaterHeaters/WaterHeaterModels.cs`, with `NotConnected`
    (`Error.NotFound("WaterHeater.NotConnected", …)`).
- **The reads the reminder list uses:**
  - `ILocationRepository.GetAllWithKeyAsync` returns all locations with `ApiKey`, no tracking, ordered by name.
  - `IWaterHeaterRepository.GetForActiveLocationsAsync` returns the heaters of active locations.
  - `ApiKey` has `IsActive` and `ExpiresAt` (UTC).
- **Oslo dates:** `NorgesPrisSchedule.OsloDateOf(DateTime utc)` in `src/Application/ElectricityCost`.
- **Endpoint patterns:**
  - Dates in requests are strings, checked by the validator and parsed in the endpoint, as in
    `GetMonthlyBillEndpoint`.
  - Admin endpoints use `AuthSchemes(JwtBearerDefaults.AuthenticationScheme)` and `Roles(RoleNames.Admin)`.
- **Free EventIds:** 1806 (WaterHeater, after 1805) and 1406 (Locations, after 1405).
- **HTTP test host:** `tests/Features.Tests/Http/EndpointHost.cs`, as used by `WaterHeaterAdminHttpTests.cs` and
  `MonthlyBillHttpTests.cs`.

---

## Phase 1: Setup

- [X] T001 In the worktree, check the baseline:
  - `dotnet build` and `dotnet test`, noting the test count.
  - `dotnet format --verify-no-changes --include` on the files you will change. The repo-wide check already fails on
    `main` because of line endings in other files.

---

## Phase 2: Foundational, the stored date (blocks US1 and US2)

- [X] T002 Add the date to `src/Domain/Common/Entities/WaterHeater.cs` (data-model):
  - **Property:** `public DateOnly SubscriptionKeyExpiresOn { get; private set; }`, with a doc comment: "The last Oslo
    day the OSO key is valid (spec 015)."
  - **Constructor:** add `DateOnly keyExpiresOn` as the last parameter.
  - **`Replace`:** becomes `Replace(deviceId, protectedSubscriptionKey, DateOnly keyExpiresOn)`.
  - **New method:** `public void SetKeyExpiry(DateOnly keyExpiresOn)`, which changes only the date.
  - **No range check here;** the handlers do it with the clock (research R1, R5).
- [X] T003 Map the column in `src/Infrastructure/Database/Configuration/WaterHeaterConfiguration.cs`:
  `builder.Property(w => w.SubscriptionKeyExpiresOn).IsRequired().HasColumnType("date");`.
- [X] T004 Create the migration `AddWaterHeaterKeyExpiry` with the command above, then edit `Up` (research R2):
  - **Add the column:** `AddColumn<DateOnly>("SubscriptionKeyExpiresOn", "WaterHeater", schema: "public", type:
    "date", nullable: false, defaultValue: new DateOnly(2000, 1, 1))`.
  - **Fill existing rows:** `migrationBuilder.Sql("UPDATE public.\"WaterHeater\" SET \"SubscriptionKeyExpiresOn\" =
    ((\"CreatedAt\" AT TIME ZONE 'Europe/Oslo')::date + interval '1 year')::date;")`.
  - **Drop the default:** `AlterColumn` with no default, so new rows always get the value from the code.
  - **`Down`:** drops the column.
  - **Snapshot:** check that the regenerated `ApplicationDbContextModelSnapshot.cs` changes only this column.
- [X] T005 Pass a date at every existing call site so the build compiles:
  - **`ConnectWaterHeaterCommandHandler`:** temporarily `DateOnly.FromDateTime(DateTime.UtcNow).AddYears(1)`. T010
    replaces it.
  - **`WaterHeaterTestData.Heater(...)`:** add `DateOnly? expiresOn = null` and default it to `new DateOnly(2027, 11,
    19)`.
  - **`NoSecretsInLogsTests.cs` and `UpdateOwnLocationCommandHandlerTests.cs`:** pass `new DateOnly(2027, 11, 19)`.
  - **Check:** `dotnet build` and `dotnet test` pass, with no behaviour change.

**Checkpoint**: The build and the tests pass, and the migration applies to the local database (`dotnet ef database
update …`).

---

## Phase 3: User Story 1, the system knows when the OSO key expires (P1) 🎯 MVP

**Goal**: The date is set on connect or replace, defaults to one year, can be changed alone, and is returned in the
settings (FR-001 to FR-006, FR-010).

**Independent Test**: Quickstart §3 steps 1–3. Connect with a date, read it back, change it alone, and have an
out-of-range date refused.

### Tests for User Story 1

- [X] T006 [P] [US1] Extend `tests/Features.Tests/WaterHeaters/ConnectWaterHeaterHandlerTests.cs`. Give the handler a
  fixed `TimeProvider`, at 2026-11-20 10:00 UTC:
  - **The date is stored:** `2027-11-19` is saved on the heater and returned in the settings.
  - **No date:** the heater gets `2027-11-20`, today in Oslo + 1 year.
  - **Out of range:** today (`2026-11-20`), yesterday, and `2031-11-21` (more than 5 years) each give
    `WaterHeater.KeyExpiryOutOfRange`.
    - The OSO client is never called (`DidNotReceive`).
    - Nothing is saved.
  - **Exactly 5 years:** `2031-11-20` is accepted.
  - **Replace:** replacing an existing heater sets the new date, and `CreatedAt` is kept.
- [X] T007 [P] [US1] Extend `tests/Features.Tests/WaterHeaters/WaterHeaterAdminHandlerTests.cs`, or create
  `SetWaterHeaterKeyExpiryHandlerTests.cs`:
  - **Changes only the date:** the device id, the protected key and `CreatedAt` are unchanged, and the client is never
    called.
  - **No heater:** gives `WaterHeater.NotConnected`.
  - **The range check:** the same as T006.
  - **The settings:** the settings handler returns `SubscriptionKeyExpiresOn`.
- [X] T008 [P] [US1] Extend `tests/Features.Tests/Http/WaterHeaterAdminHttpTests.cs`:
  - **Connect:** `keyExpiresOn: "2027-13-01"` gives `400`, before any handler runs.
  - **The date endpoint:** `PUT …/water-heater/key-expiry`:
    - gives `200` with the settings JSON, including `"keyExpiresOn":"2027-12-01"`
    - a missing date gives `400`
    - a non-administrator gets `403`, and anyone not signed in `401`.

### Implementation for User Story 1

- [X] T009 [P] [US1] Add the error to `src/Application/WaterHeaters/WaterHeaterModels.cs`:
  `WaterHeaterErrors.KeyExpiryOutOfRange = Error.Validation("WaterHeater.KeyExpiryOutOfRange", "The key's expiry date
  must be after today and at most five years ahead")`. Check that `WaterHeaterErrorStatus.Of` maps a validation error
  to `400`.
- [X] T010 [US1] Connect with a date, after T002, T005 and T009:
  - **Request:** `ConnectWaterHeaterRequest` gains `string? SubscriptionKeyExpiresOn`. Its `ToString` stays without
    the key.
  - **Validator:** when the field is present, it must match `^\d{4}-\d{2}-\d{2}$` and parse as a real date. Message:
    "The expiry date must be yyyy-MM-dd".
  - **Endpoint:** parse it into `DateOnly?`, and pass it to `ConnectWaterHeaterCommand`, which gains
    `DateOnly? SubscriptionKeyExpiresOn`.
  - **Handler:** inject `TimeProvider`.
    - Work out `today = NorgesPrisSchedule.OsloDateOf(now)`, and `date = command.Date ?? today.AddYears(1)`.
    - If `date <= today || date > today.AddYears(5)`, return `KeyExpiryOutOfRange` **before** calling OSO.
    - Pass `date` to the constructor or to `Replace`.
  - **Endpoint summary:** mention the field and the error.
- [X] T011 [US1] Return the date in the settings:
  - **`Queries/WaterHeaterQueries.cs`:** `WaterHeaterSettingsResponse` gains `DateOnly SubscriptionKeyExpiresOn` as
    its last parameter.
  - **`Mappers/WaterHeaterMapper.cs`:** `ToSettings` maps it.
  - **JSON:** check that `DateOnly` serialises as `"yyyy-MM-dd"`.
  - **Call sites:** fix every construction of the response in `src/` and `tests/`.
- [X] T012 [US1] Add the date endpoint (research R5):
  - **Command:** `SetWaterHeaterKeyExpiryCommand(Guid ActingUserId, Guid LocationId, DateOnly SubscriptionKeyExpiresOn)
    : ICommand<Result<WaterHeaterSettingsResponse>>`, in `Commands/WaterHeaterCommands.cs`.
  - **Handler:** `Handlers/SetWaterHeaterKeyExpiryCommandHandler.cs`, with `IWaterHeaterRepository`, `TimeProvider`
    and a logger.
    - Load the heater with `GetByLocationAsync`. If there is none, return `NotConnected`.
    - Apply the same range check as T010, then `SetKeyExpiry`, and save.
    - Log 1806 and return `ToSettings`.
  - **Endpoint:** `SetWaterHeaterKeyExpiryEndpoint` in `Endpoints/WaterHeaterAdminEndpoints.cs`.
    - `PUT /api/admin/locations/{locationId}/water-heater/key-expiry`, Admin only.
    - The request `{ keyExpiresOn: string }` has a validator: required, `yyyy-MM-dd`, a real date.
    - Statuses: `404` for `NotConnected`, `400` otherwise.
- [X] T013 [P] [US1] Add `WaterHeaterKeyExpiryChanged` to `src/Features/WaterHeaters/Logging/LogMessages.cs`:
  - EventId 1806, Information.
  - The template logs `LocationId`, `ActingUserId` and `ExpiresOn`, and nothing else.

**Checkpoint**: T006–T008 pass. Quickstart §3 steps 1–3 work locally.

---

## Phase 4: User Story 2, the list of keys that expire soon (P1)

**Goal**: `GET /api/admin/key-reminders` lists the sensor keys and OSO keys of active locations that expire within 30
days or have expired (FR-007, FR-008, FR-009).

**Independent Test**: Quickstart §3 steps 4–6. Set a key to expire in 10 days and see it listed with `daysLeft: 10`;
rotate it and see it leave the list.

### Tests for User Story 2

- [X] T014 [P] [US2] Create `tests/Features.Tests/Locations/KeyRemindersTests.cs`, pure, for T016:
  - **Window edges:** 31 days left → not listed; 30 → listed; 0 → listed with `Expired` false; −1 → listed with
    `Expired` true; −400 → still listed.
  - **A sensor key's last day:**
    - `ExpiresAtUtc` 2028-11-20T10:00Z gives the last day 2028-11-20.
    - `ExpiresAtUtc` exactly at Oslo midnight, 2028-11-19T23:00Z (the start of the 20th in Oslo), gives the last day
      2028-11-19.
  - **Clock change:** a key whose last day is 30 days after a day in late October counts 30 days, across the end of
    summer time.
  - **Left out:** a deactivated key, and a key at an inactive location.
  - **Order:** by days left, then by location name, then sensor key before heater key.
- [X] T015 [P] [US2] Create `tests/Features.Tests/Locations/GetKeyRemindersQueryHandlerTests.cs` and
  `tests/Features.Tests/Http/KeyRemindersHttpTests.cs`:
  - **Handler:** with substituted repositories and a fixed `TimeProvider`.
    - Both kinds are mapped, with the location names, `today` and `windowDays` 30.
    - With nothing due, `Items` is empty.
    - It reads each repository once.
  - **HTTP:** the JSON shape from the contract:
    - `kind` as text, `expiresOn` as `yyyy-MM-dd`, and `expiresAt` null for `WaterHeaterKey`
    - `401` for anyone not signed in, and `403` for a non-administrator
    - **No secrets:** the JSON has no property whose name contains "Key" other than `kind`'s values, and no "Hash" or
      "DeviceId". This follows the pattern of `Response_HasNoPropertyThatCouldCarryAKeyOrItsHash` in the locations
      tests.

### Implementation for User Story 2

- [X] T016 [P] [US2] Create `src/Application/Common/ApiKeys/KeyReminders.cs` (data-model):
  - **Types:**
    - `public enum KeyKind { SensorKey, WaterHeaterKey }`
    - the input records `SensorKeyFacts` and `HeaterKeyFacts`
    - `public sealed record KeyReminder(Guid LocationId, string LocationName, KeyKind Kind, DateOnly ExpiresOn,
      DateTime? ExpiresAtUtc, int DaysLeft, bool Expired)`
  - **Method:** `public static IReadOnlyList<KeyReminder> Due(DateOnly today, IEnumerable<SensorKeyFacts> sensorKeys,
    IEnumerable<HeaterKeyFacts> heaterKeys, int windowDays = 30)`.
    - **A sensor key's last day:** `NorgesPrisSchedule.OsloDateOf(ExpiresAtUtc.AddTicks(-1))`.
    - **Days left:** `ExpiresOn.DayNumber − today.DayNumber`.
    - **Kept:** keys with days left ≤ `windowDays`.
    - **Order:** as T014.
  - No I/O.
- [X] T017 [US2] Add the query, the handler and the mapper in `src/Features/Locations`:
  - **Query:** `Queries/GetKeyRemindersQuery.cs` has `GetKeyRemindersQuery(Guid ActingUserId) :
    IQuery<Result<KeyRemindersResponse>>`.
    - `KeyRemindersResponse(DateOnly Today, int WindowDays, IReadOnlyList<KeyReminderResponse> Items)`
    - `KeyReminderResponse(Guid LocationId, string LocationName, string Kind, DateOnly ExpiresOn, DateTime? ExpiresAt,
      int DaysLeft, bool Expired)`
  - **Handler:** `Handlers/GetKeyRemindersQueryHandler.cs`, with `ILocationRepository<Location>`,
    `IWaterHeaterRepository<WaterHeater>`, `TimeProvider` and a logger.
    - Read `GetAllWithKeyAsync` and `GetForActiveLocationsAsync`.
    - **Sensor keys:** build `SensorKeyFacts` for each location with a key.
    - **Heater keys:** build `HeaterKeyFacts`, with the name taken from the location list. Skip a heater whose location
      isn't in the list.
    - Call `KeyReminders.Due(today, …)`, log 1406 with the count, and map the result.
  - **Mapper:** `Mappers/KeyReminderMapper.cs` maps by hand, with `Kind.ToString()`.
- [X] T018 [US2] Add `Endpoints/GetKeyRemindersEndpoint.cs`:
  - `GET /api/admin/key-reminders`, `Tags("Locations")`, Admin only.
  - A summary from the contract.
- [X] T019 [P] [US2] Add `KeyRemindersListed` to `src/Features/Locations/Logging/LogMessages.cs`:
  - EventId 1406, Debug or Information, matching `LocationsListed`.
  - It logs `ActingUserId` and `Count` only.

**Checkpoint**: T014 and T015 pass, and quickstart §3 steps 4–6 work locally.

---

## Phase 5: Polish and checks

- [X] T020 Run `dotnet build`, `dotnet test`, and `dotnet format --verify-no-changes --include <changed files>`. There
  should be no new warnings, and the test count is T001's plus the new tests.
- [X] T021 Manual: quickstart §2, the migration. Done on 2026-11-20 on a scratch database (`amsdb_015check`, deleted
  afterwards), not on the shared `amsdb`:
  - a heater connected at 2026-11-19 19:32Z got 2027-11-19, and one at 23:30Z (already the 20th in Oslo) got 2027-11-20
  - the column is `date`, NOT NULL, with no default
  - undoing the migration drops the column again.
  - Connect a test heater first if there is none: `SubscriptionKeyExpiresOn` is the Oslo date of `CreatedAt` + 1 year.
  - The column is `NOT NULL`, with no default.
- [ ] T022 Manual: quickstart §3 against the local API. In production after deploy, check Hjemme's date: it must be
  **2027-11-19** (SC-001).
- [X] T023 [P] Docs:
  - `DevelopmentGuide.md`, "Water heater (OSO)": add a sentence about the expiry date and the reminder list.
  - Don't touch the EventId table: no new range.
  - Watch for a merge conflict with `feature/013-exception-handling-middleware`, which also edits this file, in another
    section.

---

## Dependencies and execution order

- **Phase 1 → Phase 2 (T002 → T003 → T004 → T005) → (US1 ∥ US2) → Phase 5.**
- **US1:**
  - T006–T008 first; they fail until done.
  - T009 and T013 can run in parallel.
  - Then T010, then T011, then T012.
- **US2:**
  - T014 and T015 first.
  - T016 and T019 can run in parallel.
  - Then T017, then T018.
  - It needs only Phase 2's stored date.
- **Shared files:**
  - US1 edits several WaterHeaters files in sequence: T010 → T011 → T012.
  - US2 touches only Locations and Application files, so it can run alongside US1.

## Parallel examples

```text
Phase 3:  T006 ∥ T007 ∥ T008 (tests) · T009 ∥ T013
Phase 4:  T014 ∥ T015 (tests) · T016 ∥ T019
Across:   US1 (WaterHeaters slice) ∥ US2 (Locations slice + Application/Common/ApiKeys)
```

## Implementation strategy

1. **MVP = Phases 1–3:** the date is stored, set and read. Without it, the reminder list has nothing to say about the
   heater key.
2. **Then Phase 4:** the reminder list, which the frontend's banner reads.
3. **Phase 5,** then the frontend spec: the banner, the expiry details in Admin → Locations, the date in the heater
   form, and the button to replace the sensor key.
4. **Commit and open a PR only when Roy asks.** Suggested commits:
   - "Store when the water heater's OSO key expires"
   - "Set and change the OSO key's expiry date"
   - "List the keys that expire soon for administrators"
