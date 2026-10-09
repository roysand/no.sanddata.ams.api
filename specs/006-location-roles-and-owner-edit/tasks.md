---

description: "Task list for Location Roles and Owner Editing"
---

# Tasks: Location Roles and Owner Editing

**Input**: Design documents from `/specs/006-location-roles-and-owner-edit/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/endpoints.md, quickstart.md

**Tests**: Required. xUnit + NSubstitute in `tests/Features.Tests/`, as for features 004 and 005. `dotnet build`, `dotnet test` and `dotnet format --verify-no-changes` must pass.

**Organization**: Grouped by user story from spec.md (US1 link role, US2 owner edits a location, US3 meter comment and owner-only registration, US4 read models, US5 limit). **Delivery order follows plan.md: role on the link (US1), read models (US4), owner writes (US2, US3), limit (US5).** The web app (`no.sanddata.ams.frontend`, feature 002) is waiting for US1 and US4 first.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependency on an incomplete task)
- **[Story]**: US1–US5 from spec.md

## Path Conventions

Paths are relative to the repository root. Handlers are discovered by `Cqrs.SourceGenerator` at compile time; do not register them by hand.

---

## Phase 1: Setup

- [x] T001 Record the baseline before the migration: on the local database run `select "UserId","LocationId" from "UserLocation"` and note the count (the local data already has one location linked to two users). Used by T007 to prove no link is lost.

---

## Phase 2: Foundational (blocks all stories)

**Purpose**: the role on the link, the migration and the repository surface. No endpoint changes yet.

- [x] T002 Create `src/Domain/Common/LocationRole.cs` with `enum LocationRole { Owner, Viewer }`.
- [x] T003 Change `src/Domain/Common/Entities/UserLocation.cs`: add `LocationRole Role { get; private set; }`; keep the `(userId, locationId)` constructor creating an `Owner` link; add a `(userId, locationId, role)` overload and `ChangeRole(LocationRole role)`. Keep the parameterless constructor for EF Core.
- [x] T004 Change `src/Infrastructure/Database/Configuration/UserLocationConfiguration.cs`: `builder.Property(ul => ul.Role).HasConversion<string>().HasMaxLength(10).IsRequired().HasDefaultValue(LocationRole.Owner)` (stored as text `Owner` / `Viewer`; the default makes every existing row an owner link).
- [x] T005 Create the migration per `DatabaseMigrations.md`: `dotnet ef migrations add AddUserLocationRole --project src/Infrastructure/Infrastructure.csproj --startup-project src/api/api.csproj --output-dir Database/Migrations`. Review the generated SQL: a non-null `Role` column with default `'Owner'`, no other change. Do not hand-edit the snapshot.
- [x] T006 [P] Add to `src/Application/Common/Interfaces/Repositories/IUserLocationRepository.cs` (and implement in `src/Infrastructure/Database/Repositories/UserLocationEfRepository.cs`): `IsOwnerAsync(Guid userId, Guid locationId, ct)` (true if an `Owner` link exists); `CountOwnersAsync(Guid locationId, ct)`; `GetForUsersAsync(IReadOnlyCollection<Guid> userIds, ct)` returning `UserLinkInfo(UserId, LocationId, LocationName, Role)` records; `GetForLocationsAsync(IReadOnlyCollection<Guid> locationIds, ct)` returning `LocationUserInfo(LocationId, UserId, Email, FirstName, LastName, Role)` records. Define the two records in `src/Application/Common/Interfaces/Repositories/`.
- [x] T007 Apply the migration to the local database (`dotnet ef database update ...` or start the API with `RunMigrationsAtStartup`) and verify: the `UserLocation` row count equals T001's baseline and every row has `Role = 'Owner'`.

**Checkpoint**: `dotnet build` and `dotnet test` pass; nothing observable has changed for API callers.

---

## Phase 3: User Story 1 - Every user-location link has a role (Priority: P1)

**Goal**: An Admin links a user as owner or viewer, changes a role, and removes a link; a location never ends with no owner; existing callers are unaffected.

**Independent Test**: As Admin, link A without a role (owner), link B as viewer, change B to owner and back, try to remove or demote the only owner (409), remove B.

- [x] T008 [US1] Change `src/Features/Users/Commands/UserLocationCommands.cs`: `LinkUserLocationCommand(Guid UserId, Guid LocationId, LocationRole Role, Caller Caller)`. Change `src/Features/Users/Endpoints/LinkUserLocationEndpoint.cs` from `EndpointWithoutRequest` to `Endpoint<LinkUserLocationRequest>` with `record LinkUserLocationRequest(Guid Id, Guid LocationId, string? Role)` (route values bound from `{id}` and `{locationId}`, optional body role); map a missing role to `Owner`; keep path, JWT scheme, `Roles(RoleNames.Admin)`, `204`, and add `409` and `400` to the documented responses.
- [x] T009 [P] [US1] Create `src/Features/Users/Validators/LinkUserLocationValidator.cs`: `Role`, when present, must be `Owner` or `Viewer` (case-insensitive); message "Role must be Owner or Viewer".
- [x] T010 [P] [US1] Add `UserLocationRoleChanged(userId, locationId, role, actingUserId)` to `src/Features/Users/Logging/LogMessages.cs` as a compiled `LoggerMessage` with the next free event id and a documented reason code; no secrets.
- [x] T011 [US1] Change `src/Features/Users/Handlers/LinkUserLocationCommandHandler.cs`: no link yet → insert with `command.Role`; link exists with the same role → `Changed = false` (idempotent); link exists with a different role → if it would demote the location's last owner (`role == Viewer`, current role `Owner`, `CountOwnersAsync(locationId) <= 1`) return `Error.Conflict("Location.LastOwner", "A location must keep at least one owner")`, otherwise `ChangeRole`, save, log `UserLocationRoleChanged`, `Changed = true`. Several owners are allowed, so adding an owner never conflicts.
- [x] T012 [US1] Change `src/Features/Users/Handlers/UnlinkUserLocationCommandHandler.cs`: before deleting a link whose role is `Owner`, return `Error.Conflict("Location.LastOwner", ...)` when `CountOwnersAsync(locationId) <= 1`; viewer links and non-last owners delete as today. Make `LinkUserLocationEndpoint` / `UnlinkUserLocationEndpoint` map `ErrorType.Conflict` to `409`.
- [x] T013 [US1] Extend `tests/Features.Tests/Users/UserLocationHandlerTests.cs`: link without role creates an owner; link as viewer creates a viewer; linking again with the same role is `Changed = false`; viewer to owner changes the role and adds a second owner without conflict; owner to viewer is refused when it is the last owner and allowed when another owner exists; unlink of the last owner is refused; unlink of a viewer and of a non-last owner succeeds; unknown user and unknown location still return not-found. Add `tests/Features.Tests/Users/LinkUserLocationValidatorTests.cs` (no role ok, `Owner`, `viewer` ok, `Admin` rejected).

**Checkpoint**: US1 complete; the web app can set roles and the admin Locations page can use `409` messages.

---

## Phase 4: User Story 4 - Read models show roles, shared locations and inactive ones (Priority: P2)

**Goal**: `GET /api/locations`, `GET /api/admin/locations` and `GET /api/users` carry roles and the facts the web app needs.

**Independent Test**: With an owner and a viewer on a location, read `GET /api/locations` as each, deactivate the location and read again, then read the two admin lists.

- [x] T014 [US4] In `src/Application/Common/Interfaces/Repositories/ILocationRepository.cs` and `src/Infrastructure/Database/Repositories/LocationEfRepository.cs`: rename `CountForUserAsync` to `CountOwnedForUserAsync` (counts `Owner` links only); add `GetForUserWithRoleAsync(Guid userId, ct)` returning `(Location Location, LocationRole Role)` pairs with `Meters` included, for links where the role is `Owner` or the location `IsActive`. Leave `GetForUserAsync` and `IsUserAssociatedAsync` unchanged (all other reads keep using them).
- [x] T015 [US4] Extend `LocationSummaryResponse` in `src/Features/Locations/Queries/GetMyLocationsQuery.cs` with `string SerialNumber, bool HasNorgesPriceAgreement, bool IsActive, LocationRole Role` (the `Role` serialised as text); update `LocationMapper.ToResponse` in `src/Features/Locations/Mappers/LocationMapper.cs` and `src/Features/Locations/Handlers/GetMyLocationsQueryHandler.cs` to use `GetForUserWithRoleAsync`. The response must not carry any key information.
- [x] T016 [US4] Extend `AdminLocationResponse` in `src/Features/Locations/Queries/GetAdminLocationsQuery.cs` with `IReadOnlyList<LocationUserResponse> Users` (`UserId, Email, FirstName, LastName, Role`). Fill it in `GetAdminLocationsQueryHandler` from `GetForLocationsAsync` (one query for all locations); in `UpdateLocationCommandHandler` (admin edit) fill it the same way; in `CreateLocationCommandHandler` fill it with the linked user when `LinkToUserId` is set, otherwise empty. Update `LocationMapper.ToAdminResponse` accordingly. Done differently: the create response leaves `Users` empty (the create handler has no user lookup and the web app refetches the list); the list and the admin edit fill it. `GetForUserAsync` was removed (its only caller moved to `GetForUserWithRoleAsync`).
- [x] T017 [US4] Extend `UserListResponse` in `src/Features/Users/Queries/GetUsersQuery.cs` with `UserLocationAccessResponse[] LocationAccess` (`LocationId, Name, Role`), keeping `Locations` and `LocationIds`. In the users query handler call `GetForUsersAsync` once for the page's user ids and pass the result to `UserMapper` (`src/Features/Users/Mappers/UserMapper.cs`) for the list and detail responses.
- [x] T018 [P] [US4] Extend `tests/Features.Tests/Locations/GetMyLocationsQueryHandlerTests.cs`: owner sees an inactive location with `IsActive = false`, viewer does not; role and the new fields are returned; a shared active location is returned to the viewer with role `Viewer`. Extend `GetAdminLocationsQueryHandlerTests.cs` (users with roles) and `tests/Features.Tests/Users/GetUserQueryHandlerTests.cs` or a new `GetUsersQueryHandlerTests.cs` (location access with roles; `Locations` and `LocationIds` still present).

**Checkpoint**: US4 complete; the web app can show owner, viewers and roles.

---

## Phase 5: User Story 2 - An owner edits their location (Priority: P1)

**Goal**: An owner changes name, address and active flag; nothing else; non-owners get `404`.

**Independent Test**: As owner `PUT /api/locations/{id}` changes the three fields and `GET /api/locations` shows them; as viewer or stranger the same call returns 404; serial number, zone, Norgespris and key are unchanged.

- [ ] T019 [US2] Add `UpdateDetails(string name, string address)` to `src/Domain/Common/Entities/Location.cs` (changes only those two).
- [ ] T020 [US2] Create in `src/Features/Locations/`: `Commands/UpdateOwnLocationCommand.cs` (`ActingUserId, LocationId, Name, Address, IsActive`; returns `Result<LocationSummaryResponse>`), `Handlers/UpdateOwnLocationCommandHandler.cs` (not found or `!IsOwnerAsync` → `Error.NotFound("Location.NotFound", "Location not found")`; otherwise `UpdateDetails`, `SetActive`, save, `LocationUpdated` and, when the flag changed, `LocationActiveChanged` with the acting user; return the summary with role `Owner`), `Validators/UpdateOwnLocationValidator.cs` (id required; name and address required, at most 100 characters, same messages as `UpdateLocationValidator`), `Endpoints/UpdateOwnLocationEndpoint.cs` (`PUT /api/locations/{id}`, JWT, no role; request `UpdateOwnLocationRequest(Id, Name, Address, IsActive)`; `200/400/401/404`; map not-found to 404). The command and request have no serial number, zone, Norgespris or key member.
- [ ] T021 [P] [US2] Write `tests/Features.Tests/Locations/UpdateOwnLocationCommandHandlerTests.cs`: owner update saves name, address, active flag; serial number, zone and Norgespris on the entity are unchanged; viewer, unlinked user and missing location all return not-found; deactivating logs the active change; reactivating works; plus `UpdateOwnLocationValidatorTests.cs` (empty, 100 and 101 characters).

**Checkpoint**: US2 complete; the web app's owner edit works.

---

## Phase 6: User Story 3 - An owner edits a meter's comment; only owners register meters (Priority: P1)

**Goal**: Owners (and Admins) change a meter's comment; viewers cannot change meters or register new ones.

**Independent Test**: As owner `PUT /api/meters/{id}` changes the comment; as viewer the same call and `POST /api/meters` return 404.

- [ ] T022 [US3] Add `SetComment(string? comment)` to `src/Domain/Common/Entities/Meter.cs`.
- [ ] T023 [US3] Create in `src/Features/Meters/`: `Commands/UpdateMeterCommentCommand.cs` (`MeterId, UserId, IsAdmin, Comment`; returns `Result<MeterResponse>`), `Handlers/UpdateMeterCommentCommandHandler.cs` (load the meter; not found, or non-admin and `!IsOwnerAsync(userId, meter.LocationId)` → `Error.NotFound("Meter.NotFound", "Meter not found")`; `SetComment`, save, log `MeterCommentUpdated`; return `MeterMapper.ToResponse`), `Validators/UpdateMeterCommentValidator.cs` (comment at most 200 characters, message as in `CreateMeterValidator`), `Endpoints/UpdateMeterCommentEndpoint.cs` (`PUT /api/meters/{id}`, JWT; `200/400/401/404`; derive `IsAdmin` the way `CreateMeterEndpoint` does). Add `MeterCommentUpdated` to `src/Features/Meters/Logging/LogMessages.cs`.
- [ ] T024 [US3] Change `src/Features/Meters/Handlers/CreateMeterCommandHandler.cs`: inject `IUserLocationRepository<UserLocation>` and, for non-admins, replace `IsUserAssociatedAsync` with `IsOwnerAsync`; the not-found answer stays the same for viewers. Admin behaviour is unchanged.
- [ ] T025 [P] [US3] Write `tests/Features.Tests/Meters/UpdateMeterCommentCommandHandlerTests.cs` (owner and admin succeed, only the comment changes, `null` clears it, viewer/stranger/missing meter return not-found) and `UpdateMeterCommentValidatorTests.cs` (200 ok, 201 rejected). Extend `tests/Features.Tests/Meters/CreateMeterCommandHandlerTests.cs`: an owner registers a meter, a viewer gets not-found, an admin still can.

**Checkpoint**: US3 complete.

---

## Phase 7: User Story 5 - The limit counts owned locations only (Priority: P3)

- [x] T026 [US5] In `src/Features/Locations/Handlers/CreateLocationCommandHandler.cs` call `CountOwnedForUserAsync` instead of `CountForUserAsync`. Extend `tests/Features.Tests/Locations/CreateLocationCommandHandlerTests.cs`: four viewer links do not block a new location; four owned locations do (`Location.LimitReached`); the admin path (no `LinkToUserId`) is still unlimited. Update the explanatory comment in `specs/005-user-creates-own-location/spec.md` ("counts every location the user is linked to") to say owner links only, since 006. Done together with T014 (the rename forced the change). The "viewer links do not count" case is covered by a temporary check against the real local database (rolled back), since a handler test with a fake cannot prove an EF query; the `005` spec comment update is still to do.

---

## Phase 8: Polish and cross-cutting

- [ ] T027 [P] Update `UserCrudEndpoints.md` (and `AuthenticationGuide.md` if it lists the endpoints) with the link role body, the two new `PUT` endpoints, the changed list responses and the `409 Location.LastOwner` rule.
- [ ] T028 Run `dotnet build`, `dotnet test` and `dotnet format --verify-no-changes`; fix any failures. The three unrelated uncommitted files in the working tree (measurements ingest, meter repository) must not be part of these commits.
- [ ] T029 Walk through `quickstart.md` against a locally running API with a throwaway database, including the no-body `PUT` link call (research item 4) and the migration check; note the result in the PR description.
- [ ] T030 Open the PR, merge, and confirm the Build, Push & Deploy workflow succeeded; confirm in the deployed database that the migration ran and existing links are owners. Record the deployment date in `specs/006-location-roles-and-owner-edit/spec.md` (Status).
- [ ] T031 Tell the web app: in `no.sanddata.ams.frontend` tasks T040 of `specs/002-location-editing-sharing/tasks.md` (remove the flag and the compatibility shims) can now be done, and update its `contracts/backend-required.md` status line.

---

## Dependencies and execution order

- **Phase 1** (T001): first, before T005 applies the migration.
- **Phase 2** (T002-T007) blocks every story. T002 → T003 → T004 → T005 → T007 in order; T006 can run in parallel with T004-T005.
- **US1** (T008-T013) needs Phase 2. T009 and T010 are parallel; T011 and T012 need T006 and T008.
- **US4** (T014-T018) needs Phase 2; T015-T017 need T014 (T016 and T017 also need T006); T018 after them.
- **US2** (T019-T021) needs Phase 2 (`IsOwnerAsync`) and T015 (`LocationSummaryResponse`).
- **US3** (T022-T025) needs Phase 2.
- **US5** (T026) needs T014.
- **Polish** (T027-T031) after the stories; T030 and T031 only after review.

### Parallel opportunities

- After Phase 2: US1, US3 and US4 can be worked on in parallel (different files); US2 waits for T015.
- Within stories: T009 and T010; T018, T021, T025 (tests) alongside the next story's implementation.

## Implementation strategy

1. **First deploy (what the web app needs first)**: Phase 2, US1 and US4. The web app can then show roles, add viewers and show owner and viewer counts.
2. **Second deploy**: US2 and US3, so owners can edit; then US5.
3. Each deploy is safe on its own: the migration is additive and defaults to the current behaviour (every link is an owner).
4. The web app keeps its compatibility shims until T031.
