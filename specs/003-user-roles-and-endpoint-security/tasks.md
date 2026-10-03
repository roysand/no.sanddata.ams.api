---

description: "Task list for User Roles and Endpoint Security"
---

# Tasks: User Roles and Endpoint Security

**Input**: Design documents from `/specs/003-user-roles-and-endpoint-security/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/endpoints.md, quickstart.md

**Tests**: Included, as unit tests in `tests/Features.Tests/` (xUnit + NSubstitute), following the 002 convention and the owner's request for test coverage. The end-to-end auth behavior (role claim reaching `IsInRole`, 401/403/404) is validated by the manual `quickstart.md` run in the Polish phase.

**Organization**: Tasks are grouped by user story. **Phase order differs from story numbering on purpose**: US2 (roles, token, first Admin) is built *before* US1 (lock the endpoints), because once US1 closes the anonymous endpoints nobody can use them without an Admin from US2.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1-US5)
- Include exact file paths in descriptions

## Conventions (from CLAUDE.md / constitution)

- Validators target the request DTO type (not the command/query record).
- Admin-only endpoints: `AuthSchemes(JwtBearerDefaults.AuthenticationScheme)` + `Roles(RoleNames.Admin)`. Keep `Tags("Users")` + `Description(b => b.WithTags("Users"))` and `using Microsoft.AspNetCore.Http;`.
- Handlers return `Result<T>`; endpoints translate with `AddError(...)` + `ThrowIfAnyErrors(<status>)`.
- Log with compiled `LoggerMessage` delegates in the Users range 1000-1099; never log passwords or tokens.
- Run `dotnet format --include <touched files>` only, and check it did not over-reach.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Constants and configuration used by everything else.

- [ ] T001 [P] Create `src/Domain/Common/RoleNames.cs` with `public static class RoleNames { public const string Admin = "Admin"; public const string User = "User"; }`
- [ ] T002 [P] Add `"Bootstrap": { "OwnerEmail": "roy@sanddata.no" }` to `src/api/appsettings.json` (a deployment can override it via `local.settings.json` or environment variable; remember `local.settings.json` wins over env vars)

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Seed data, role loading, and shared helpers that every user story depends on.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [ ] T003 Seed the two roles in `src/Infrastructure/Database/Configuration/RoleConfiguration.cs` with `builder.HasData(...)`: `Role` `Admin` and `User` with **fixed, hard-coded GUIDs**, a description each, `IsActive = true`, and explicit `CreatedAt`/`UpdatedAt` (seed data bypasses `SaveChanges` audit columns) (depends on T001)
- [ ] T004 [P] Add `builder.Navigation(u => u.Roles).AutoInclude();` and `builder.Navigation(u => u.Locations).AutoInclude();` to `src/Infrastructure/Database/Configuration/UserConfiguration.cs` so `FindAsync(predicate)` returns roles and locations (note: `GetByIdAsync` uses `DbContext.Find`, which ignores AutoInclude, per research.md §1)
- [ ] T005 Create the migration `AddRolesAndSeed` per `DatabaseMigrations.md` (`dotnet ef migrations add AddRolesAndSeed --project src/Infrastructure/Infrastructure.csproj --startup-project src/api/api.csproj --output-dir Database/Migrations`); confirm `Up` contains only the two `InsertData` rows, then add one idempotent `migrationBuilder.Sql(...)` after them: `INSERT INTO "UserRole" ("UserId","RoleId","AssignedAt") SELECT u."Id", <User role id>, now() FROM "User" u WHERE NOT EXISTS (SELECT 1 FROM "UserRole" ur WHERE ur."UserId" = u."Id")`; give `Down` the matching delete of the seeded rows and backfill. **Verify first on an isolated TimescaleDB container** (clean DB and a copy with existing users), then apply to the dev database (depends on T003, T004)
- [ ] T006 [P] Create `src/Features/Users/Commands/Caller.cs`: `public record Caller(Guid Id, bool IsAdmin);` plus a small static `Caller.From(ClaimsPrincipal user)` that reads `ClaimTypes.NameIdentifier` and `user.IsInRole(RoleNames.Admin)`
- [ ] T007 [P] Add the new security events to `src/Features/Users/Logging/LogMessages.cs` within 1000-1099: `AdminGranted` 1010, `AdminRevoked` 1011, `UserLocationLinked` 1012, `UserLocationUnlinked` 1013, `AdminBootstrapped` 1014, `LastAdminProtected` 1015, `UserAccessDenied` 1016 (caller id, target id, reason code only; no secrets)
- [ ] T008 Create `src/Features/Users/Handlers/AdminGuard.cs`: an internal helper with `WouldLeaveNoActiveAdminAsync(Guid userId, IUserRepository<User>, IRoleRepository<Role>, IUserRoleRepository<UserRole>, CancellationToken)` that returns true when `userId` is currently an active Admin and no other active Admin exists; used by revoke, delete and deactivate (depends on T001)

**Checkpoint**: Roles exist in the database, user queries load roles and locations, and shared helpers are ready.

---

## Phase 3: User Story 2 - A first Admin exists, and roles carry into sign-in (Priority: P1) 🎯 MVP (with US1)

**Goal**: The `Admin` and `User` roles exist; the owner becomes Admin automatically; login and refresh tokens carry the user's real roles.

**Independent Test**: Start the API on the migrated dev database, log in as the owner, and confirm `GET /api/auth/me` lists `Admin` and `User`; log in as another user and confirm only `User` (quickstart §0 and §2).

### Tests for User Story 2

- [ ] T009 [P] [US2] Tests for `AdminBootstrapService` in `tests/Features.Tests/Users/AdminBootstrapServiceTests.cs`: promotes the owner when no Admin exists; adds the `User` role if missing; does nothing when any Admin already exists; does nothing when the owner email has no account; is idempotent on a second run
- [ ] T010 [P] [US2] Tests for `JwtTokenService` in `tests/Features.Tests/Users/JwtTokenServiceTests.cs`: a token generated for a user with roles `Admin` and `User` contains one `ClaimTypes.Role` claim per role, and the validated principal reports `IsInRole("Admin")` true and false for a role not held (covers research.md §2)

### Implementation for User Story 2

- [ ] T011 [US2] Create `src/Features/Users/Services/AdminBootstrapService.cs`: a startup-only hosted service (`IHostedService.StartAsync`, scoped repositories via `IServiceScopeFactory`) that reads `Bootstrap:OwnerEmail`; if no user holds the Admin role and a user with that email exists, inserts the missing `UserRole` rows for `Admin` and `User`, saves, and logs `AdminBootstrapped`; otherwise returns without changes (depends on T002, T007, T008)
- [ ] T012 [US2] Register `AdminBootstrapService` with `services.AddHostedService<...>()` in `src/Features/ServiceCollectionExtensions.cs` (depends on T011)
- [ ] T013 [US2] In `src/Features/Auth/Handlers/RefreshTokenCommandHandler.cs` load the user with `FindAsync(u => u.Id == refreshToken.UserId)` instead of `GetByIdAsync`, so roles are loaded and the new access token carries them (depends on T004)
- [ ] T014 [US2] Run quickstart §0 and §2 against the dev database (migration applied, API started): owner has both roles after startup, `/api/auth/me` shows them after login and after refresh, and an ordinary user shows only `User`. Record any deviation (depends on T005, T012, T013)

**Checkpoint**: Roles exist and reach tokens; the owner is Admin. User Story 2 is verified independently.

---

## Phase 4: User Story 1 - Close the anonymous user-management endpoints (Priority: P1) 🎯 MVP

**Goal**: Every user-management action requires sign-in and the right to perform it; only login and refresh stay anonymous.

**Independent Test**: Call each user-management action without a token (expect 401), as an ordinary user (403 for admin-only, success for own account, 404 for someone else's), and as Admin (success), per quickstart §1 and §3.

### Tests for User Story 1

- [ ] T015 [P] [US1] Tests for `GetUserQueryHandler` in `tests/Features.Tests/Users/GetUserQueryHandlerTests.cs`: own account succeeds; another user's account as a non-Admin returns `User.NotFound` identical to a missing user; Admin can view anyone; roles and locations appear in the response
- [ ] T016 [P] [US1] Tests for `UpdateUserCommandHandler` in `tests/Features.Tests/Users/UpdateUserCommandHandlerTests.cs`: self update succeeds; non-Admin on another user returns `User.NotFound`; non-Admin changing `IsActive` returns `User.IsActiveAdminOnly`; Admin updates anyone; deactivating the last active Admin returns `User.LastAdmin`
- [ ] T017 [P] [US1] Tests for `ChangePasswordCommandHandler` in `tests/Features.Tests/Users/ChangePasswordCommandHandlerTests.cs`: self change requires and verifies the current password; self change with a missing current password fails; Admin changing another user's password needs no current password; non-Admin on another user returns `User.NotFound`
- [ ] T018 [P] [US1] Tests for `DeleteUserCommandHandler` in `tests/Features.Tests/Users/DeleteUserCommandHandlerTests.cs`: deleting an ordinary user succeeds; deleting the last active Admin returns `User.LastAdmin`; deleting an Admin when another active Admin exists succeeds
- [ ] T019 [P] [US1] Update/add validator tests in `tests/Features.Tests/Users/ChangePasswordValidatorTests.cs`: `CurrentPassword` may be empty (admin reset) but the new-password rules still apply

### Implementation for User Story 1

- [ ] T020 [US1] Add a `Caller Caller` parameter to `GetUserQuery` in `src/Features/Users/Queries/GetUserQuery.cs` and update `src/Features/Users/Handlers/GetUserQueryHandler.cs`: load with `FindAsync(u => u.Id == query.Id)`, return `Error.NotFound("User.NotFound", ...)` when the target is missing **or** the caller is neither the owner nor an Admin; log `UserAccessDenied` for the latter (depends on T004, T006, T007)
- [ ] T021 [US1] Add `Caller` to `UpdateUserCommand` in `src/Features/Users/Commands/UpdateUserCommand.cs` and apply the same self-or-admin rule in `src/Features/Users/Handlers/UpdateUserCommandHandler.cs`; for a non-Admin whose `IsActive` differs from the stored value return `Error.Validation("User.IsActiveAdminOnly", ...)`; if setting `IsActive = false` on the last active Admin, return `Error.Conflict("User.LastAdmin", ...)` via `AdminGuard` (depends on T006, T008)
- [ ] T022 [US1] Add `Caller` to `ChangePasswordCommand` and make `CurrentPassword` nullable in `src/Features/Users/Commands/ChangePasswordCommand.cs`; update `src/Features/Users/Handlers/ChangePasswordCommandHandler.cs`: non-Admin on another user returns `User.NotFound`; when `Caller.Id == target` require and verify `CurrentPassword` (missing returns `Error.Validation("User.CurrentPasswordRequired", ...)`); an Admin changing another user's password skips the current-password check (depends on T006)
- [ ] T023 [P] [US1] Update `src/Features/Users/Validators/ChangePasswordValidator.cs`: remove the `NotEmpty` rule on `CurrentPassword` and make the "new password must differ from current" rule apply only when `CurrentPassword` is not empty (depends on T022)
- [ ] T024 [US1] Update `src/Features/Users/Handlers/DeleteUserCommandHandler.cs`: before deleting, return `Error.Conflict("User.LastAdmin", ...)` when `AdminGuard` says it would leave no active Admin (log `LastAdminProtected`); log `UserDeleted` with the acting admin's id (depends on T007, T008)
- [ ] T025 [US1] Secure the own-account endpoints in `src/Features/Users/Endpoints/GetUserEndpoint.cs`, `UpdateUserEndpoint.cs` and `ChangePasswordEndpoint.cs`: replace `AllowAnonymous()` with `AuthSchemes(JwtBearerDefaults.AuthenticationScheme)`, build the `Caller` with `Caller.From(User)`, pass it to the query/command, update `Summary` responses (401, 404) (depends on T020, T021, T022)
- [ ] T026 [US1] Secure the admin-only endpoints in `src/Features/Users/Endpoints/CreateUserEndpoint.cs`, `GetUsersEndpoint.cs` and `DeleteUserEndpoint.cs`: replace `AllowAnonymous()` with `AuthSchemes(JwtBearerDefaults.AuthenticationScheme)` + `Roles(RoleNames.Admin)`, update `Summary` responses (401, 403, and 409 for delete); map `User.LastAdmin` to 409 in `DeleteUserEndpoint` (depends on T024)

**Checkpoint**: Only login and refresh are anonymous; user management enforces sign-in, ownership and Admin. User Stories 1 and 2 together are the MVP.

---

## Phase 5: User Story 3 - Admins manage who is an Admin (Priority: P2)

**Goal**: Newly created users are ordinary users; an Admin can grant and revoke the Admin role; the last Admin can never be removed.

**Independent Test**: As Admin create a user (gets `User`), promote, check, demote, then try to demote/delete the last Admin (quickstart §4).

### Tests for User Story 3

- [ ] T027 [P] [US3] Tests for `CreateUserCommandHandler` role assignment in `tests/Features.Tests/Users/CreateUserCommandHandlerTests.cs`: a new user gets exactly the `User` role; duplicate email still returns `User.EmailExists`
- [ ] T028 [P] [US3] Tests for the grant/revoke handlers in `tests/Features.Tests/Users/AdminRoleHandlerTests.cs`: grant adds `Admin` once (idempotent); revoke removes it; revoke on a non-Admin succeeds; revoking the last active Admin returns `User.LastAdmin`; unknown user returns `User.NotFound`; the `User` role is never removed
- [ ] T029 [P] [US3] Validator tests for the new requests in `tests/Features.Tests/Users/AdminRoleValidatorTests.cs` (empty id fails)

### Implementation for User Story 3

- [ ] T030 [US3] In `src/Features/Users/Handlers/CreateUserCommandHandler.cs` look up the `User` role by name (`RoleNames.User`) and insert a `UserRole` for the new user through `IUserRoleRepository` in the same save (depends on T001, T003)
- [ ] T031 [P] [US3] Create `src/Features/Users/Commands/GrantAdminCommand.cs` and `src/Features/Users/Commands/RevokeAdminCommand.cs` (`Guid UserId, Caller Caller` -> `Result<...>`)
- [ ] T032 [US3] Implement `src/Features/Users/Handlers/GrantAdminCommandHandler.cs` (idempotent insert of the Admin `UserRole`; log `AdminGranted`) and `src/Features/Users/Handlers/RevokeAdminCommandHandler.cs` (idempotent delete; `AdminGuard` check returns `User.LastAdmin`; log `AdminRevoked`/`LastAdminProtected`) (depends on T008, T031)
- [ ] T033 [US3] Create `src/Features/Users/Endpoints/GrantAdminEndpoint.cs` (`PUT /api/users/{id}/roles/admin`, 204) and `src/Features/Users/Endpoints/RevokeAdminEndpoint.cs` (`DELETE /api/users/{id}/roles/admin`, 204/409), both `AuthSchemes(JwtBearerDefaults.AuthenticationScheme)` + `Roles(RoleNames.Admin)` with Users tags (depends on T032)
- [ ] T034 [P] [US3] Create `src/Features/Users/Validators/GrantAdminValidator.cs` and `RevokeAdminValidator.cs` targeting the endpoint request DTOs (non-empty `Id`) (depends on T033)

**Checkpoint**: Admins can promote and demote users; the system cannot lose its last Admin.

---

## Phase 6: User Story 4 - Meter endpoints respect location membership (Priority: P2)

**Goal**: Reading or registering a meter requires being linked to its location.

**Independent Test**: As a user not linked to the `Home` location, read and register a meter (both 404); as a linked user both succeed (quickstart §5).

### Tests for User Story 4

- [ ] T035 [P] [US4] Tests in `tests/Features.Tests/Meters/GetMeterQueryHandlerTests.cs`: linked user gets the meter; unlinked user gets `Meter.NotFound` (same as a missing meter)
- [ ] T036 [P] [US4] Tests in `tests/Features.Tests/Meters/CreateMeterCommandHandlerTests.cs`: linked user registers a meter; unlinked user gets `Location.NotFound`; duplicate device id still returns `Meter.DeviceIdExists`

### Implementation for User Story 4

- [ ] T037 [US4] Add `UserId` to `CreateMeterCommand` (`src/Features/Meters/Commands/CreateMeterCommand.cs`) and `GetMeterQuery` (`src/Features/Meters/Queries/GetMeterQuery.cs`)
- [ ] T038 [US4] Update `src/Features/Meters/Handlers/CreateMeterCommandHandler.cs` to call `ILocationRepository.IsUserAssociatedAsync(command.UserId, command.LocationId, ct)` first and return `Location.NotFound` when false; update `src/Features/Meters/Handlers/GetMeterQueryHandler.cs` to load the meter then check `IsUserAssociatedAsync(query.UserId, meter.LocationId, ct)` and return `Meter.NotFound` when false (duplicate the check; do not share it with other slices) (depends on T037)
- [ ] T039 [US4] Pass the caller's id from `ClaimTypes.NameIdentifier` in `src/Features/Meters/Endpoints/CreateMeterEndpoint.cs` and `GetMeterEndpoint.cs`; update `Summary` responses (401, 404) (depends on T038)

**Checkpoint**: Meters follow the same membership rule as measurements and cost data.

---

## Phase 7: User Story 5 - Admins link users to locations (Priority: P3)

**Goal**: An Admin can link and unlink users and locations without touching the database.

**Independent Test**: Link a new user to the `Home` location and confirm they can read its measurements; unlink and confirm they cannot (quickstart §6).

### Tests for User Story 5

- [ ] T040 [P] [US5] Tests in `tests/Features.Tests/Users/UserLocationHandlerTests.cs`: link adds the row once (idempotent); unlink removes it (and a missing link is still success); unknown user returns `User.NotFound`; unknown location returns `Location.NotFound`

### Implementation for User Story 5

- [ ] T041 [P] [US5] Create `src/Features/Users/Commands/LinkUserLocationCommand.cs` and `src/Features/Users/Commands/UnlinkUserLocationCommand.cs` (`Guid UserId, Guid LocationId, Caller Caller` -> `Result<...>`)
- [ ] T042 [US5] Implement `src/Features/Users/Handlers/LinkUserLocationCommandHandler.cs` and `UnlinkUserLocationCommandHandler.cs` using `IUserRepository`, `ILocationRepository` (existence) and `IUserLocationRepository` (insert/delete); log `UserLocationLinked`/`UserLocationUnlinked` (depends on T007, T041)
- [ ] T043 [US5] Create `src/Features/Users/Endpoints/LinkUserLocationEndpoint.cs` (`PUT /api/users/{id}/locations/{locationId}`, 204) and `UnlinkUserLocationEndpoint.cs` (`DELETE ...`, 204), both JWT + `Roles(RoleNames.Admin)` with Users tags (depends on T042)
- [ ] T044 [P] [US5] Create `src/Features/Users/Validators/LinkUserLocationValidator.cs` and `UnlinkUserLocationValidator.cs` targeting the request DTOs (non-empty ids) (depends on T043)

**Checkpoint**: A new user can be created, given access to locations, and promoted entirely through the API.

---

## Final Phase: Polish & Cross-Cutting Concerns

- [ ] T045 [P] Update `AuthenticationGuide.md`: role model (Admin superset of User), 401/403/404 behavior, token refresh picks up role changes, **and copy the "creating the first owner account in an empty database" procedure (hash generator + `INSERT`) from `specs/003-user-roles-and-endpoint-security/quickstart.md`**
- [ ] T046 [P] Update `UserCrudEndpoints.md` (and `src/Features/Users/README.md` if it lists endpoints) with the auth requirement of each endpoint and the four new endpoints
- [ ] T047 [P] Update `CLAUDE.md` Authentication section with the role-gating examples (`Roles(RoleNames.Admin)`) and the `Bootstrap:OwnerEmail` setting
- [ ] T048 [P] Amend `.specify/memory/constitution.md` Principle V (PATCH, 1.0.2): handlers are auto-registered by `Cqrs.SourceGenerator`, not manually in `AddInfrastructureToDI.cs` (noted in research.md §9)
- [ ] T049 Run `dotnet format --include <touched files>` and verify it did not over-reach; `dotnet build` with no new warnings; `dotnet test`
- [ ] T050 Run every scenario in `specs/003-user-roles-and-endpoint-security/quickstart.md` against local dev (§0-§7), including the endpoint review for SC-007 (the only anonymous endpoints are login, refresh and documentation pages)

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: no dependencies.
- **Foundational (Phase 2)**: depends on Setup; **blocks all user stories** (seed, AutoInclude, migration, `Caller`, log events, `AdminGuard`).
- **US2 (Phase 3)**: depends on Foundational. Do this **before** US1 so an Admin exists and tokens carry roles.
- **US1 (Phase 4)**: depends on Foundational and, for its Admin scenarios, on US2.
- **US3 (Phase 5)**: depends on Foundational and US2 (needs the role rows and token roles); independent of US4/US5.
- **US4 (Phase 6)**: depends only on Foundational (no role logic); can run in parallel with US3/US5.
- **US5 (Phase 7)**: depends on Foundational and US2 (Admin gate).
- **Polish**: after the desired stories are complete.

### Within Each User Story

- Tests are written alongside; write them first when practical.
- Commands/queries before handlers before endpoints before validators.
- A migration (T005) is never edited after it has been applied to any database; write a new one instead.

### Parallel Opportunities

- T001/T002; T004 with T006/T007 in Foundational.
- All test tasks within a story (T009/T010; T015-T019; T027-T029; T035/T036).
- US4 is fully independent of US3 and US5 once Foundational is done.
- Docs tasks T045-T048.

---

## Parallel Example: User Story 1 tests

```bash
Task: "Tests for GetUserQueryHandler in tests/Features.Tests/Users/GetUserQueryHandlerTests.cs"
Task: "Tests for UpdateUserCommandHandler in tests/Features.Tests/Users/UpdateUserCommandHandlerTests.cs"
Task: "Tests for ChangePasswordCommandHandler in tests/Features.Tests/Users/ChangePasswordCommandHandlerTests.cs"
Task: "Tests for DeleteUserCommandHandler in tests/Features.Tests/Users/DeleteUserCommandHandlerTests.cs"
```

---

## Implementation Strategy

### MVP First (US2 + US1)

1. Phase 1 Setup and Phase 2 Foundational (including the migration on a scratch DB first).
2. Phase 3 US2: roles, bootstrap, token roles; run quickstart §0/§2.
3. Phase 4 US1: close the endpoints; run quickstart §1/§3.
4. **STOP and VALIDATE**: this is the security fix that must land before any internet deployment. US3-US5 are not needed to be safe, only to be convenient.

### Incremental Delivery

1. MVP above, then US3 (admin management), US4 (meters), US5 (location links) in any order.
2. Each story adds value without breaking the previous ones.

---

## Notes

- [P] tasks = different files, no dependencies.
- [Story] label maps each task to its user story for traceability.
- The Admin role does **not** bypass location membership for measurement/cost data (spec assumption).
- Role/deletion changes apply at the user's next sign-in or refresh (no token revocation, spec decision Q1-A).
- Commit after each task or logical group; merging to `main` follows the usual local-merge-then-push flow.
