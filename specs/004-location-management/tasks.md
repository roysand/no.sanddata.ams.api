---

description: "Task list for Location Management and Hashed Sensor Keys"
---

# Tasks: Location Management and Hashed Sensor Keys

**Input**: Design documents from `/specs/004-location-management/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/endpoints.md, quickstart.md

**Tests**: Included, as unit tests in `tests/Features.Tests/` (xUnit + NSubstitute), following the 002/003 convention. The migration and the end-to-end behavior (sensor key before/after, inactive locations, log redaction, the UI) are validated by the `quickstart.md` run in the Polish phase. The frontend has no test framework; it is verified with a headless browser run (not committed).

**Organization**: Tasks are grouped by user story. **Phase order differs from story numbering on purpose**: US2 (hashed keys) is built *before* US1 (create a location) so a key is never stored in readable form, not even briefly. Tasks marked **(frontend)** are in the separate repo `C:\shared\repo\private\ams\no.sanddata.ams.frontend`; all other paths are in this repo. Deliver the API first; the frontend needs the new endpoints (and the API branch merged and restarted) to work end to end.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1-US5)
- Include exact file paths in descriptions

## Conventions (from CLAUDE.md / constitution / lessons so far)

- Validators target the request DTO type (not the command/query record).
- Admin endpoints: `AuthSchemes(JwtBearerDefaults.AuthenticationScheme)` + `Roles(RoleNames.Admin)`, `Tags("Locations")` + `Description(b => b.WithTags("Locations"))`, `using Microsoft.AspNetCore.Http;`. **Bodiless PUT/POST endpoints use `EndpointWithoutRequest` + `Route<Guid>("id")`** (a request DTO makes FastEndpoints demand a JSON body and answer 415).
- Handlers return `Result<T>`; endpoints translate with `AddError(...)` + `ThrowIfAnyErrors(<status>)`.
- Log with compiled `LoggerMessage` delegates in the Locations range 1400-1499; **never log keys, hashes or response bodies that contain keys**.
- When a Features project change is built while the API is running, build with `-o` into another folder or stop the API (DLL file locks).
- Run `dotnet format --include <touched files>` only, and check it did not over-reach. Frontend: `npx prettier --write` only on new files; `shadcn add` may insert an unwanted `cn` package: fix imports to `@/lib/utils` and `npm uninstall cn`.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Small shared pieces with no dependencies.

- [X] T001 [P] Create `src/Domain/Common/PriceZones.cs`: `public static class PriceZones { public static readonly IReadOnlyList<string> All = ["NO1","NO2","NO3","NO4","NO5"]; public static bool IsValid(string? zone) => ...; }` with a comment that the ENTSO-E client keeps its own map of the same codes
- [X] T002 [P] Create `src/Application/Common/ApiKeys/ApiKeyCrypto.cs`: static class with `Generate()` returning (key = 32 bytes from `RandomNumberGenerator` as lowercase hex, hash, hint), `Hash(string key)` = lowercase hex SHA-256 of the UTF-8 bytes, and `Hint(string key)` = last 4 characters; plus `KeyLifetime = TimeSpan.FromDays(730)`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Entity, mapping, repository and schema changes that every story depends on.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [X] T003 Change `src/Domain/Common/Entities/ApiKey.cs`: replace `Key` with `KeyHash` and `KeyHint`; constructor `(Guid id, string keyHash, string keyHint, string description, bool isActive, DateTime expiresAt)`; add `Rotate(string keyHash, string keyHint, DateTime expiresAt)` (also sets `IsActive = true`) and `SetActive(bool isActive)`
- [X] T004 [P] Change `src/Domain/Common/Entities/Location.cs`: add `Update(string name, string address, string serialNumber, string zone, bool hasNorgesPriceAgreement)`, `SetActive(bool isActive)` and `AssignApiKey(ApiKey apiKey)` (private setters stay private)
- [X] T005 Update `src/Infrastructure/Database/Configuration/ApiKeyConfiguration.cs` (drop `Key`; `KeyHash` required, max 64, unique index; `KeyHint` required, max 8) and `src/Infrastructure/Database/Configuration/LocationConfiguration.cs` (unique index on `SerialNumber`) (depends on T003)
- [X] T006 Update `src/Application/Common/Interfaces/Repositories/IApiKeyRepository.cs` and `src/Infrastructure/Database/Repositories/ApiKeyEfRepository.cs`: replace `FindActiveByKeyAsync(key)` with `FindActiveByKeyHashAsync(keyHash)` returning the key with its `Location` when `KeyHash` matches, `IsActive`, `ExpiresAt > now` **and `Location.IsActive`**; add `FindByLocationIdAsync(locationId)` for rotate/activate (depends on T003)
- [X] T007 Update `src/Application/Common/Interfaces/Repositories/ILocationRepository.cs` and `src/Infrastructure/Database/Repositories/LocationEfRepository.cs`: add `GetAllWithKeyAsync` (all locations, include `ApiKey` and `Meters`, ordered by name) and `SerialNumberExistsAsync(string serialNumber, Guid? exceptLocationId, CancellationToken)` (depends on T004)
- [X] T008 Create the migration `AddHashedApiKeys` per `DatabaseMigrations.md` (`dotnet ef migrations add AddHashedApiKeys --project src/Infrastructure/Infrastructure.csproj --startup-project src/api/api.csproj --output-dir Database/Migrations`), then edit it to follow data-model.md: add `KeyHash`/`KeyHint` nullable, one `migrationBuilder.Sql` backfill `UPDATE "ApiKey" SET "KeyHash" = encode(sha256(convert_to("Key",'UTF8')),'hex'), "KeyHint" = right("Key",4)`, alter both to NOT NULL, create the two unique indexes, drop `Key`; `Down` re-adds `Key` filled with `KeyHash` (documented: plain text is unrecoverable). **Verify on an isolated TimescaleDB container first** (migrate to the previous migration, insert a key row, apply, check the hash equals a C# `ApiKeyCrypto.Hash` of the same key, roll back and re-apply), using `--connection` so `local.settings.json` does not redirect it; then apply to the dev database (depends on T003, T005; `Key` is read by the backfill, so do not reorder) **Status: verified on an isolated container (hash equals an independent SHA-256, hint, indexes, rollback and re-apply); applying it to the dev database is deferred to T015 because the running API reads the old column**
- [X] T009 Create `tests/Features.Tests/Authentication/` helpers if needed and update any existing test or code that referenced `ApiKey.Key`, `FindActiveByKeyAsync` or the `ApiKey` claim so the solution builds (grep for them first)

**Checkpoint**: The schema holds hashes only, the old key was converted, the repositories and entities compile.

---

## Phase 3: User Story 2 - Sensor keys are stored hashed (Priority: P1) 🎯 MVP (security fix)

**Goal**: Readings are accepted by hash lookup only; the plain key never lives in the database, a claim, or a log; the running sensor keeps working.

**Independent Test**: After the migration, the forwarder's existing key still gets its batch past authentication (404 for an unknown reader, not 401) and new `Measurement` rows keep arriving; a wrong key gets 401; no key text is readable in the database (quickstart §0, §5).

### Tests for User Story 2

- [X] T010 [P] [US2] Tests for `ApiKeyCrypto` in `tests/Features.Tests/Authentication/ApiKeyCryptoTests.cs`: `Hash("abc")` equals the known SHA-256 vector `ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad`; `Generate()` returns a 64-character lowercase hex key, `Hash(key)` equals the returned hash, the hint is the last 4 characters, two calls differ
- [X] T011 [P] [US2] Tests for the key authentication handler in `tests/Features.Tests/Authentication/ApiKeyAuthenticationHandlerTests.cs`: a valid key succeeds with a `LocationId` claim and **no claim whose value contains the key**; the handler asks the repository for the **hash** of the header (never the plain key); no match (wrong/expired/inactive key or inactive location, as the repository returns null) fails; no header yields no result; a blank header fails
- [X] T012 [P] [US2] Tests for the logging redaction in `tests/Features.Tests/Authentication/RequestLoggingRedactionTests.cs`: with `Headers` allowed in `AttributesToLog`, the logged attributes show `***` (or a redaction marker) for `Authorization` and `X-API-Key` while other headers stay readable; with `LogResponseBody` on, a `POST /api/admin/locations` or `.../api-key/rotate` response body is masked while other bodies are logged. (If the test project cannot reference `HttpContext`, add `<FrameworkReference Include="Microsoft.AspNetCore.App" />` to `tests/Features.Tests/Features.Tests.csproj`.)

### Implementation for User Story 2

- [X] T013 [US2] Update `src/Infrastructure/Authentication/ApiKeyAuthenticationHandler.cs`: hash the header with `ApiKeyCrypto.Hash`, call `FindActiveByKeyHashAsync`, **remove the `ApiKey` claim** and the reflection-based `ApiKeyId` claim (use `apiKey.Id`), keep `LocationId` (depends on T002, T006)
- [X] T014 [P] [US2] Update `src/Infrastructure/Middleware/RequestResponseLoggingMiddleware.cs`: always replace the values of the `Authorization` and `X-API-Key` request headers with the mask value even when `Headers` is allowed, and replace the response body with the mask value for `POST /api/admin/locations` and `POST /api/admin/locations/{id}/api-key/rotate` even when `LogResponseBody` is on (depends on T012)
- [ ] T015 [US2] Run quickstart §0 and §5 against the dev database: the real forwarder key authenticates before and after the migration (unknown reader gives 404, wrong key gives 401), the forwarder's next batch is stored, no `Key` column remains, and with verbose logging settings a search of the log finds 0 occurrences of the key (depends on T008, T013, T014)

**Checkpoint**: Keys are hashed everywhere; the sensor is unaffected.

---

## Phase 4: User Story 1 - Add a location and get its sensor key (Priority: P1) 🎯 MVP

**Goal**: An Admin creates a location in the web app; the key is shown once.

**Independent Test**: Create a location in the Locations page, copy the key, register a reader, send a reading with the key (accepted), reopen the location (no key) (quickstart §1).

### Tests for User Story 1

- [ ] T016 [P] [US1] Tests for `CreateLocationCommandHandler` in `tests/Features.Tests/Locations/CreateLocationCommandHandlerTests.cs`: creates one location with its key; the returned plain key hashes to the stored `KeyHash`, the stored hint is its last 4 characters, expiry is about two years out, description is "Sensor key for <name>"; a duplicate serial number returns `Location.SerialNumberExists` and saves nothing; the plain key is only in the response, not on any stored entity
- [ ] T017 [P] [US1] Tests for `GetAdminLocationsQueryHandler` in `tests/Features.Tests/Locations/GetAdminLocationsQueryHandlerTests.cs`: returns all locations including unlinked ones; key status is `Active`, `Expired` (past expiry) or `Deactivated`; the response type exposes no key or hash property
- [ ] T018 [P] [US1] Validator tests in `tests/Features.Tests/Locations/CreateLocationValidatorTests.cs`: a zone outside NO1-NO5 fails; empty, whitespace or over-100-character name/address/serial number fail; a valid request passes

### Implementation for User Story 1

- [ ] T019 [P] [US1] Create the records in `src/Features/Locations/Commands/CreateLocationCommand.cs` (`CreateLocationCommand(Guid ActingUserId, name, address, serialNumber, zone, hasNorgesPriceAgreement, isActive)`, `CreatedLocationResponse(AdminLocationResponse Location, string ApiKey)`) and `src/Features/Locations/Queries/GetAdminLocationsQuery.cs` with `AdminLocationResponse` (location fields, `ApiKeyInfoResponse(description, hint, isActive, expiresAt, status)`, meters) per contracts/endpoints.md
- [ ] T020 [US1] Create `src/Features/Locations/Handlers/CreateLocationCommandHandler.cs`: check `SerialNumberExistsAsync`, generate the key with `ApiKeyCrypto`, build `ApiKey` + `Location` (`AssignApiKey`), insert, **one** `SaveChangesAsync`, log the creation (ids only), return the response with the plain key (depends on T002, T004, T007, T019)
- [ ] T021 [US1] Create `src/Features/Locations/Handlers/GetAdminLocationsQueryHandler.cs` and extend `src/Features/Locations/Mappers/LocationMapper.cs` with `ToAdminResponse` (compute `status` from `IsActive`/`ExpiresAt`; never include the hash) (depends on T007, T019)
- [ ] T022 [P] [US1] Create `src/Features/Locations/Validators/CreateLocationValidator.cs` targeting the endpoint's request DTO (zone via `PriceZones.IsValid` with code `Validation.InvalidZone`, text lengths 1-100) (depends on T001, T023)
- [ ] T023 [US1] Create `src/Features/Locations/Endpoints/GetAdminLocationsEndpoint.cs` (`GET /api/admin/locations`) and `CreateLocationEndpoint.cs` (`POST /api/admin/locations`, 201, `CreateLocationRequest` DTO, 409 for duplicate serial) with the admin conventions above (depends on T020, T021)
- [ ] T024 [P] [US1] Add events to `src/Features/Locations/Logging/LogMessages.cs` within 1400-1499: `LocationCreated` 1401, `LocationUpdated` 1402, `LocationActiveChanged` 1403, `KeyRotated` 1404, `KeyActiveChanged` 1405, `ReaderRegisteredByAdmin` 1406 (ids and acting user only; no secrets)
- [ ] T025 [P] [US1] **(frontend)** Add the shared admin-locations plumbing in `src/features/admin/locations/`: `types.ts` (`AdminLocation`, `ApiKeyInfo`, `CreatedLocation`), `api.ts` (`getAdminLocations`, `createLocation`), `hooks.ts` (`useAdminLocations`, create mutation that refreshes the list), `schema.ts` (zod: required text up to 100, zone `NO1`-`NO5`)
- [ ] T026 [US1] **(frontend)** Create `src/features/admin/locations/LocationsPage.tsx` (table: name, address, zone, Norgespris, active/inactive, key status badge with hint and expiry; "Add location" button), `LocationDialog.tsx` (create form with a zone `Select`, Norgespris and active checkboxes; server errors such as duplicate serial shown in the dialog) and `KeyRevealDialog.tsx` (shows the key once in a monospace box with a **Copy** button, a warning that it cannot be shown again, and a "I have copied the key" checkbox that must be ticked before **Close** is enabled) (depends on T025)
- [ ] T027 [US1] **(frontend)** Add the route `/admin/locations` under `AdminRoute` in `src/app/routes.tsx` and a "Locations" link for Admins in `src/components/Header.tsx` (depends on T026)
- [ ] T028 [US1] Verify US1 live (quickstart §1): create a location in the UI, copy the key, register a reader, send a reading with the key, reopen the location, and confirm no key appears; test duplicate serial and an invalid zone; confirm a regular user gets 403 (depends on T023, T027)

**Checkpoint**: A new location can be added end to end without touching the database.

---

## Phase 5: User Story 3 - Rotate or deactivate a sensor key (Priority: P2)

**Goal**: An Admin can replace or switch off a key; the old key dies immediately.

**Independent Test**: Rotate a location's key, then the old key gets 401 and the new key is accepted (quickstart §2).

### Tests for User Story 3

- [ ] T029 [P] [US3] Tests for `RotateLocationKeyCommandHandler` in `tests/Features.Tests/Locations/RotateLocationKeyCommandHandlerTests.cs`: stores a new hash and hint different from the old, expiry about two years out, reactivates a deactivated key, returns the plain key that hashes to the stored hash, unknown location returns `Location.NotFound`
- [ ] T030 [P] [US3] Tests for `SetLocationKeyActiveCommandHandler` in `tests/Features.Tests/Locations/SetLocationKeyActiveCommandHandlerTests.cs`: deactivate and reactivate update only the active flag (hash unchanged); unknown location returns `Location.NotFound`

### Implementation for User Story 3

- [ ] T031 [P] [US3] Create `src/Features/Locations/Commands/RotateLocationKeyCommand.cs` and `SetLocationKeyActiveCommand.cs` with their response records (`RotatedKeyResponse(apiKey, hint, expiresAt)`, key info)
- [ ] T032 [US3] Implement `src/Features/Locations/Handlers/RotateLocationKeyCommandHandler.cs` (uses `FindByLocationIdAsync`, `ApiKeyCrypto.Generate`, `ApiKey.Rotate`, one save, log `KeyRotated`) and `SetLocationKeyActiveCommandHandler.cs` (log `KeyActiveChanged`) (depends on T002, T006, T031)
- [ ] T033 [US3] Create `src/Features/Locations/Endpoints/RotateLocationKeyEndpoint.cs` (`POST /api/admin/locations/{id}/api-key/rotate`, `EndpointWithoutRequest`, 200 with the one-time key, 404) and `SetLocationKeyActiveEndpoint.cs` (`PUT /api/admin/locations/{id}/api-key`, body `{ "isActive": bool }`) plus `src/Features/Locations/Validators/SetLocationKeyActiveValidator.cs` (depends on T032)
- [ ] T034 [US3] **(frontend)** Extend `src/features/admin/locations/api.ts` and `hooks.ts` with `rotateKey` and `setKeyActive`; add `RotateKeyDialog.tsx` (warns that the sensor stops working until its key is updated, then shows the new key through `KeyRevealDialog`) and a per-row actions menu entry for **Rotate key** and **Deactivate/Activate key** on `LocationsPage.tsx` (depends on T026, T033)
- [ ] T035 [US3] Verify US3 live (quickstart §2): rotate and see the old key rejected at once and the new key accepted; deactivate and see 401; rotate to restore; set an expiry in the past on a throwaway location and see **Expired** in the UI (depends on T033, T034)

**Checkpoint**: Lost or exposed keys can be handled entirely from the app.

---

## Phase 6: User Story 4 - See and edit all locations, deactivate a location (Priority: P2)

**Goal**: Admins see and edit everything; an inactive location stops readings and disappears for regular users.

**Independent Test**: Edit a location, deactivate it (readings 401; the linked user sees it gone; the Admin still sees it), reactivate (everything back) (quickstart §3).

### Tests for User Story 4

- [ ] T036 [P] [US4] Tests for `UpdateLocationCommandHandler` in `tests/Features.Tests/Locations/UpdateLocationCommandHandlerTests.cs`: updates the fields and active flag without touching the key; unknown id returns `Location.NotFound`; changing to another location's serial number returns `Location.SerialNumberExists` while keeping its own serial number does not
- [ ] T037 [P] [US4] Validator tests for the update request in `tests/Features.Tests/Locations/UpdateLocationValidatorTests.cs` (same rules as create)

### Implementation for User Story 4

- [ ] T038 [P] [US4] Create `src/Features/Locations/Commands/UpdateLocationCommand.cs`, `src/Features/Locations/Handlers/UpdateLocationCommandHandler.cs` (serial check with `exceptLocationId`, `Location.Update` + `SetActive`, log `LocationUpdated` and `LocationActiveChanged` when it changed) and `src/Features/Locations/Validators/UpdateLocationValidator.cs`
- [ ] T039 [US4] Create `src/Features/Locations/Endpoints/UpdateLocationEndpoint.cs` (`PUT /api/admin/locations/{id}`, request DTO with body, 200 with the admin location shape, 404, 409) (depends on T038)
- [ ] T040 [US4] Make inactive locations vanish for regular users: in `src/Infrastructure/Database/Repositories/LocationEfRepository.cs` require `Location.IsActive` in `IsUserAssociatedAsync` (join to `Location`) and in `GetForUserAsync`; Admin management and link/unlink must keep working because they do not use these methods; verify the data endpoints (measurements, consumption, cost, meters) all go through `IsUserAssociatedAsync` (grep) (depends on T007)
- [ ] T041 [US4] **(frontend)** Extend `api.ts`/`hooks.ts` with `updateLocation`; make `LocationDialog.tsx` support edit mode; add **Edit** and **Deactivate/Activate location** actions to `LocationsPage.tsx`, show inactive rows greyed with an "Inactive" badge; show a confirmation warning when the zone or the Norgespris flag is changed ("costs shown for past hours will be recalculated") (depends on T026, T039)
- [ ] T042 [US4] **(frontend)** In `src/features/admin/UsersPage.tsx` take the location checkbox options from `useAdminLocations()` instead of `useLocations()`, so every location can be linked, and update the footnote text (depends on T025)
- [ ] T043 [US4] Verify US4 live (quickstart §3): the Admin lists a location they are not linked to and can link users to it from the Users page; edit with the zone warning; deactivate: a valid key gets 401, the linked user no longer sees the location and gets 404 on its data, the Admin still sees it marked inactive; reactivate restores everything (depends on T039, T040, T041, T042)

**Checkpoint**: Locations are fully manageable and "inactive" has a real effect.

---

## Phase 7: User Story 5 - Manage a location's readers (Priority: P3)

**Goal**: Admins register and list readers for any location from the Locations page.

**Independent Test**: Register a reader at a location the Admin is not linked to (works), register the same device id twice (409), and a linked user still registers at their own active location (quickstart §4).

### Tests for User Story 5

- [ ] T044 [P] [US5] Update `tests/Features.Tests/Meters/CreateMeterCommandHandlerTests.cs` for the new `IsAdmin` field: an Admin registers at a location they are not linked to, including an inactive one; a non-admin member registers at their own active location; a non-admin non-member (or member of an inactive location, as the repository reports) gets `Location.NotFound`; duplicate device id still returns `Meter.DeviceIdExists`

### Implementation for User Story 5

- [ ] T045 [US5] Add `bool IsAdmin` to `src/Features/Meters/Commands/CreateMeterCommand.cs`, `src/Features/Meters/Mappers/MeterMapper.cs` and `src/Features/Meters/Endpoints/CreateMeterEndpoint.cs` (`User.IsInRole(RoleNames.Admin)`); in `src/Features/Meters/Handlers/CreateMeterCommandHandler.cs` an Admin only needs the location to exist, otherwise the membership check applies; log `ReaderRegisteredByAdmin` when an Admin registers one (depends on T024)
- [ ] T046 [US5] **(frontend)** Add `RegisterReaderDialog.tsx` (device id + comment; calls `POST /api/meters`), list each location's readers in an expandable row on `LocationsPage.tsx`, and extend `api.ts`/`hooks.ts` with `registerReader` (depends on T026, T045)
- [ ] T047 [US5] Verify US5 live (quickstart §4): register a reader at an unlinked location from the UI, duplicate device id shows the 409 message, a regular linked user can still register at their own active location (depends on T045, T046)

**Checkpoint**: A new location can be set up completely, from location to first reading, in the app.

---

## Final Phase: Polish & Cross-Cutting Concerns

- [ ] T048 [P] Update `AuthenticationGuide.md`: sensor keys are stored hashed and shown once, how to rotate or deactivate a key, that a location is added from the Locations page, and replace the earlier "insert the location by SQL" guidance; keep the first-owner SQL
- [ ] T049 [P] Update `CLAUDE.md` (Authentication section: API key storage and rotation; Logging: keys are redacted) and `DatabaseMigrations.md` (the hash migration and its non-restorable `Down`)
- [ ] T050 [P] Update the forwarder-facing notes if they describe where to get a key (`C:\shared\repo\private\ams\no.sanddata.ams.services` README or docs, if present): "create the location in the Locations page and copy the key shown once"
- [ ] T051 Run `dotnet format --include <touched files>` and verify it did not over-reach; `dotnet build`, `dotnet test`; frontend `npx tsc -b`, `npm run lint`, `npx prettier --check` on new files, `npm run build`
- [ ] T052 Run every scenario in `specs/004-location-management/quickstart.md` against local dev (§0-§6), then remove temporary users/locations created for testing and confirm the dev database holds only real data and the running forwarder still delivers readings

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: no dependencies.
- **Foundational (Phase 2)**: depends on Setup; **blocks all user stories** (entities, repositories, migration).
- **US2 (Phase 3)**: depends on Foundational. Do this **first**: it is the security fix and protects the running sensor.
- **US1 (Phase 4)**: depends on Foundational and US2 (keys are created hashed).
- **US3 (Phase 5)**: depends on Foundational; the frontend part needs the Locations page from US1.
- **US4 (Phase 6)**: depends on Foundational; the frontend parts need the Locations page from US1.
- **US5 (Phase 7)**: depends on Foundational and US1 (frontend page); the API part is independent.
- **Polish**: after the desired stories are complete.

### Within Each User Story

- Tests are written alongside; write them first when practical.
- Commands/queries/records before handlers before endpoints before validators; API before frontend.
- A migration (T008) is never edited after it has been applied to any database; write a new one instead.
- Stop the API (or build with `-o`) before building the API project while it is running.

### Parallel Opportunities

- T001/T002; T003 with T004; T010-T012; T016-T018; T029/T030; T036/T037.
- US3, US4 and US5 API parts are independent of each other once Foundational is done.
- Docs tasks T048-T050.

---

## Parallel Example: User Story 1 tests

```bash
Task: "Tests for CreateLocationCommandHandler in tests/Features.Tests/Locations/CreateLocationCommandHandlerTests.cs"
Task: "Tests for GetAdminLocationsQueryHandler in tests/Features.Tests/Locations/GetAdminLocationsQueryHandlerTests.cs"
Task: "Validator tests in tests/Features.Tests/Locations/CreateLocationValidatorTests.cs"
```

---

## Implementation Strategy

### MVP First (US2 + US1)

1. Phase 1 Setup and Phase 2 Foundational (the migration first on an isolated container, then on dev).
2. Phase 3 US2: hashed keys, redaction, sensor still works (quickstart §0/§5).
3. Phase 4 US1: create a location end to end (API, then the Locations page).
4. **STOP and VALIDATE**: this delivers the main goal (add a location without SQL) and the security fix.

### Incremental Delivery

1. MVP above, then US3 (rotate/deactivate keys), US4 (edit, inactive, all locations), US5 (readers).
2. Each story adds value without breaking the previous ones.

---

## Notes

- [P] tasks = different files, no dependencies.
- [Story] label maps each task to its user story; **(frontend)** marks tasks in the other repository.
- Branches: this repo `feature/004-location-management`; create `feature/location-management-ui` in the frontend repo when starting its first task.
- Price data for a brand-new zone arrives with the next background fetch (up to 6 hours, or at restart); that is existing behavior, not changed here.
- Commit after each task or logical group; merging follows the usual local-merge-then-push flow, API first.
