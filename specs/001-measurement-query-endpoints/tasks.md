---

description: "Task list for Measurement Query Endpoints"
---

# Tasks: Measurement Query Endpoints

**Input**: Design documents from `/specs/001-measurement-query-endpoints/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/endpoints.md, quickstart.md

**Tests**: Not included — the feature spec doesn't request TDD, and research.md §5 documents that
no automated test project exists in this repo yet; validation is the manual `quickstart.md` run
in the Polish phase.

**Organization**: Tasks are grouped by user story (spec.md) to enable independent implementation
and testing of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no unmet dependencies)
- **[Story]**: Maps the task to US1/US2/US3 from spec.md
- Paths are relative to the repository root

## Phase 1: Setup

- [ ] T001 Confirm `FastEndpoints`, `FluentValidation`, and `Npgsql.EntityFrameworkCore.PostgreSQL` are already present in `Directory.Packages.props` — no new package versions needed for this feature (verify only, no edit expected)

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Repository-layer capabilities every user story query depends on. No user story
endpoint can be implemented until this phase is done.

- [ ] T002 [P] Add `IsUserAssociatedAsync(Guid userId, Guid locationId, CancellationToken ct)` and `GetForUserAsync(Guid userId, CancellationToken ct)` to `ILocationEfRepository<T>` in `src/Application/Common/Interfaces/Repositories/ILocationEfRepository.cs`
- [ ] T003 [P] Implement `IsUserAssociatedAsync` and `GetForUserAsync` (eager-load `Meters`) in `src/Infrastructure/Database/Repositories/LocationEfRepository.cs` (depends on T002)
- [ ] T004 [P] Add `GetPagedAsync(Guid locationId, Guid? meterId, DateTime from, DateTime to, int page, int pageSize, CancellationToken ct)` and `GetLatestAsync(Guid locationId, Guid? meterId, CancellationToken ct)` to `IMeasurementEfRepository<T>` in `src/Application/Common/Interfaces/Repositories/IMeasurementEfRepository.cs`
- [ ] T005 [P] Implement `GetPagedAsync` (ordered by `Timestamp` ascending, paged) and `GetLatestAsync` (ordered by `Timestamp` descending, first-or-default) in `src/Infrastructure/Database/Repositories/MeasurementEfRepository.cs` (depends on T004)

**Checkpoint**: Repository layer ready — user story implementation can begin.

---

## Phase 3: User Story 1 - View recent usage for a location (Priority: P1) 🎯 MVP

**Goal**: A user can request recent (last-24h default window) measurements for one of their own
locations, and can also fetch a single latest reading as a lightweight "current status" query.

**Independent Test**: Authenticate as a user with a registered location that has ingested
measurements; call `GET /api/measurements?locationId=...` with no `from`/`to` and confirm only
last-24h readings return, oldest→newest; call `GET /api/measurements/latest?locationId=...` and
confirm the single most recent reading (or `204`) returns.

### Implementation for User Story 1

- [ ] T006 [P] [US1] Create `GetMeasurementsQuery`, `MeasurementResponse`, `PagedMeasurementsResponse` records in `src/Features/Measurements/Queries/GetMeasurementsQuery.cs`
- [ ] T007 [P] [US1] Create `GetLatestMeasurementQuery` record in `src/Features/Measurements/Queries/GetLatestMeasurementQuery.cs`
- [ ] T008 [US1] Implement `GetMeasurementsQueryHandler` in `src/Features/Measurements/Handlers/GetMeasurementsQueryHandler.cs` — call `IsUserAssociatedAsync` first and return `Error.NotFound("Location.NotFound", ...)` if false (same error whether the location is missing or not the caller's, per FR-001/research.md §3); when `from`/`to` are omitted, default to the last 24 hours (research.md §2); call `GetPagedAsync`; map via `MeasurementMapper` (depends on T003, T005, T006)
- [ ] T009 [US1] Implement `GetLatestMeasurementQueryHandler` in `src/Features/Measurements/Handlers/GetLatestMeasurementQueryHandler.cs` — same authorization check as T008; call `GetLatestAsync`; return a `Result` the endpoint can map to `204` when null (depends on T003, T005, T007)
- [ ] T010 [P] [US1] Extend `MeasurementMapper` with `ToResponse(Measurement)` and `ToPagedResponse(...)` in `src/Features/Measurements/Mappers/MeasurementMapper.cs`
- [ ] T011 [US1] Create `GetMeasurementsRequest` and `GetMeasurementsEndpoint` (`GET /api/measurements`, `AuthSchemes(JwtBearerDefaults.AuthenticationScheme)`, reads `UserId` from `ClaimTypes.NameIdentifier`) in `src/Features/Measurements/Endpoints/GetMeasurementsEndpoint.cs` (depends on T008, T010)
- [ ] T012 [US1] Create `GetLatestMeasurementRequest` and `GetLatestMeasurementEndpoint` (`GET /api/measurements/latest`, JWT auth, `200` with body or `204` when nothing found) in `src/Features/Measurements/Endpoints/GetLatestMeasurementEndpoint.cs` (depends on T009, T010)
- [ ] T013 [US1] Create `src/Features/Measurements/Logging/LogMessages.cs` claiming EventId range `1300-1399` (e.g. `MeasurementsQueried` = 1300, `LatestMeasurementQueried` = 1301, `LocationAccessDenied` = 1302) and call the compiled delegates from both handlers (depends on T008, T009)
- [ ] T014 [US1] Create `GetMeasurementsValidator : Validator<GetMeasurementsRequest>` (bounds: `page >= 1`, `1 <= pageSize <= 2000`) in `src/Features/Measurements/Validators/GetMeasurementsValidator.cs` — **must** target `GetMeasurementsRequest` (the endpoint's request DTO), not the query record; FastEndpoints binds validators by the endpoint's exact `TRequest` type, and a validator typed to the wrong class silently never runs (this exact bug was previously found and fixed in this codebase for the Auth/Users validators) (depends on T011)

**Checkpoint**: User Story 1 is fully functional and independently testable via `quickstart.md`'s US1 and FR-009 scenarios.

---

## Phase 4: User Story 2 - Browse historical usage over a chosen date range (Priority: P2)

**Goal**: A user can supply an explicit `from`/`to` range and get exactly the readings in that
window; an invalid range (`to` before `from`) is rejected with a clear error.

**Independent Test**: Call `GET /api/measurements?locationId=...&from=...&to=...` with a known
two-week window and confirm only in-range readings return; call it again with `to` before `from`
and confirm a `400 Validation.InvalidRange`.

### Implementation for User Story 2

- [ ] T015 [US2] Add a `to >= from` rule to `GetMeasurementsValidator` (only enforced when both `from` and `to` are supplied), producing `Validation.InvalidRange`, in `src/Features/Measurements/Validators/GetMeasurementsValidator.cs` (depends on T014)
- [ ] T016 [US2] Update `GetMeasurementsQueryHandler`'s range logic to also fill a *partial* range — only `from` given → `to` defaults to now; only `to` given → `from` defaults to `to` minus 24 hours (research.md §2) — in `src/Features/Measurements/Handlers/GetMeasurementsQueryHandler.cs` (depends on T008)

**Checkpoint**: User Stories 1 and 2 both work independently via `quickstart.md`.

---

## Phase 5: User Story 3 - Discover which locations and meters are available to query (Priority: P3)

**Goal**: A user can list the locations they're associated with, and the meters registered at
each, so a client can build a picker instead of hardcoding IDs.

**Independent Test**: Authenticate as a user associated with two locations, call
`GET /api/locations`, and confirm exactly those two locations (with their meters) come back —
none belonging to other users.

### Implementation for User Story 3

- [ ] T017 [P] [US3] Create `GetMyLocationsQuery`, `LocationSummaryResponse`, `MeterSummaryResponse` records in `src/Features/Locations/Queries/GetMyLocationsQuery.cs`
- [ ] T018 [US3] Implement `GetMyLocationsQueryHandler` in `src/Features/Locations/Handlers/GetMyLocationsQueryHandler.cs` — call `GetForUserAsync`, map via `LocationMapper` (depends on T003, T017)
- [ ] T019 [P] [US3] Create `LocationMapper` in `src/Features/Locations/Mappers/LocationMapper.cs`
- [ ] T020 [US3] Create `GetLocationsEndpoint` (`GET /api/locations`, JWT auth) in `src/Features/Locations/Endpoints/GetLocationsEndpoint.cs` (depends on T018, T019)
- [ ] T021 [US3] Create `src/Features/Locations/Logging/LogMessages.cs` claiming EventId range `1400-1499` (e.g. `LocationsListed` = 1400) and call it from the handler (depends on T018)

**Checkpoint**: All three user stories work independently via `quickstart.md`.

---

## Final Phase: Polish & Cross-Cutting Concerns

- [ ] T022 [P] Update the EventId allocation table in `CLAUDE.md` and `DevelopmentGuide.md` to add `1300-1399` (Measurements) and `1400-1499` (Locations) — required by the constitution's logging principle whenever a new range is claimed
- [ ] T023 [P] Fix `DevelopmentGuide.md`'s "Adding a New Feature" checklist step 6 ("Register Handler in DI... Handlers are not auto-discovered"), which contradicts that same file's "Custom Dispatcher" section stating `Cqrs.SourceGenerator` registers handlers automatically via `AddGeneratedCqrsHandlers()` — confirm the actual (generator) behavior and correct the stale step so a future feature isn't built around the wrong instructions
- [ ] T024 Run `dotnet format --verify-no-changes` and `dotnet build` from the repo root and confirm no new warnings
- [ ] T025 Execute every scenario in `specs/001-measurement-query-endpoints/quickstart.md` against local dev and confirm the documented status codes/payloads

---

## Dependencies & Execution Order

- **Setup (Phase 1)**: No dependencies.
- **Foundational (Phase 2)**: Depends on Setup. Blocks every user story (T006-T021 all need the repository methods from T002-T005).
- **User Story 1 (Phase 3)**: Depends on Foundational only. This is the MVP slice.
- **User Story 2 (Phase 4)**: Depends on Foundational **and** on US1's `GetMeasurementsValidator`/`GetMeasurementsQueryHandler` files (T014, T008) — it extends them rather than creating new files.
- **User Story 3 (Phase 5)**: Depends on Foundational only (T002/T003) — independent of US1/US2, could be built in parallel with either.
- **Polish (Final Phase)**: Depends on whichever stories were completed.

## Parallel Example: Foundational phase

```text
Task: "Add IsUserAssociatedAsync + GetForUserAsync to ILocationEfRepository<T> in src/Application/Common/Interfaces/Repositories/ILocationEfRepository.cs"
Task: "Add GetPagedAsync + GetLatestAsync to IMeasurementEfRepository<T> in src/Application/Common/Interfaces/Repositories/IMeasurementEfRepository.cs"
```
(T003/T005 can each start as soon as their own interface task finishes — they touch different files from each other too.)

## Parallel Example: User Story 1

```text
Task: "Create GetMeasurementsQuery/MeasurementResponse/PagedMeasurementsResponse in src/Features/Measurements/Queries/GetMeasurementsQuery.cs"
Task: "Create GetLatestMeasurementQuery in src/Features/Measurements/Queries/GetLatestMeasurementQuery.cs"
Task: "Extend MeasurementMapper in src/Features/Measurements/Mappers/MeasurementMapper.cs"
```

---

## Implementation Strategy

### MVP First (User Story 1 only)

1. Phase 1 (Setup) → Phase 2 (Foundational) → Phase 3 (US1).
2. Stop and validate against `quickstart.md`'s US1 + FR-009 scenarios.
3. This alone gives a frontend something real to show: recent usage + current status, for one
   location at a time.

### Incremental Delivery

1. Foundational → US1 (MVP: recent usage + latest reading).
2. US2 (explicit historical ranges) — small, additive on top of US1's files.
3. US3 (location/meter discovery) — independent, could be done in parallel with US1/US2 by a
   second contributor, or slotted in wherever convenient since it doesn't touch Measurements code.
4. Polish — doc fixes and the manual quickstart pass.

## Notes

- No manual CQRS handler registration task is included: `Cqrs.SourceGenerator` discovers every
  `ICommandHandler`/`IQueryHandler` in `Features` and generates the dispatcher + DI registration
  at compile time (see `DevelopmentGuide.md`'s "Custom Dispatcher" section). T023 exists because
  a different part of the same doc still describes manual registration — don't follow that stale
  step for this feature's handlers.
- Commit after each task or logical group; stop at any checkpoint to validate a story
  independently before moving on.
