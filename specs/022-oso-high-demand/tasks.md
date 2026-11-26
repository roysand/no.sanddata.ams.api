# Tasks: Turn the Water Heater's High Demand On and Off

**Input**: Design documents from `specs/022-oso-high-demand/`

**Prerequisites**: [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md), [data-model.md](data-model.md),
[contracts/endpoints.md](contracts/endpoints.md), [quickstart.md](quickstart.md)

**Tests**: the plan asks for them (quickstart §1), and the repo writes them first. Each test task comes before the code
it covers and should fail until that code exists.

**Format**: `[ID] [P?] [Story] Description`. [P] means it can run in parallel (different files, no unfinished
dependency).

---

## Phase 1: Setup

- [X] T001 On `feature/022-oso-high-demand`, check that the baseline is green: run `dotnet build`, `dotnet test` and
  `dotnet format --verify-no-changes` from the repo root, and note the test count.

---

## Phase 2: Foundational (blocks all stories)

### Application

- [X] T002 [P] Add to `WaterHeaterErrors` in `src/Application/WaterHeaters/WaterHeaterModels.cs`:
  `HighDemandNotOffered = Error.Problem("WaterHeater.HighDemandNotOffered", "OSO no longer offers High Demand for
  this heater")`, with a doc comment: OSO answered 404 to the undocumented switch call (research R2).
- [X] T003 [P] Add `Task<Result> SetHighDemandAsync(WaterHeaterCredentials credentials, bool on, CancellationToken
  cancellationToken);` to `src/Application/Common/Interfaces/External/IWaterHeaterClient.cs`.
  - Its doc comment says: on sends OSO's undocumented `POST …/HighDemand/` and off its `DELETE`; it's sent once and
    never retried; 404 gives `HighDemandNotOffered` (research R1–R3).
  - Change the interface's summary from "Read only on purpose (spec 011, FR-015)" to "Read only (spec 011, FR-015),
    except High Demand (spec 022, FR-008)".

### Client (tests first)

- [X] T004 Extend `tests/Features.Tests/WaterHeaters/OsoStubHandler.cs`:
  - record the HTTP method with each request, keeping the existing `Requests` users compiling (add a `Methods` list,
    or widen the tuple and update its users);
  - add a default answer `["HighDemand/"] = (HttpStatusCode.OK, "")`, since `Segments[^1]` keeps the trailing
    slash.
- [X] T005 Add switch tests to `tests/Features.Tests/WaterHeaters/OsoWaterHeaterClientTests.cs`. They fail until T006.
  - On sends exactly one `POST` to `/water-heater-api/1/Device/{id}/HighDemand/`, with the key header and no body,
    and succeeds on 200 and 204.
  - Off sends exactly one `DELETE` to the same path and succeeds on 200.
  - 401 and 403 give `WaterHeater.Rejected`.
  - 404 gives `WaterHeater.HighDemandNotOffered`.
  - 500, 503, 429 and 408 give `WaterHeater.Unavailable`, with exactly one request each (no retry).
  - A timeout (`Delay` above a short client timeout) and an `HttpRequestException` give `Unavailable`.
  - Any other status, for example 400, gives `InvalidResponse`.
- [X] T006 Implement `SetHighDemandAsync` in `src/Infrastructure/External/OsoWaterHeaterClient.cs`:
  - a private `SendAsync(credentials, HttpMethod, "HighDemand/", ct)` beside `GetAsync`, with the same key header,
    the same exception filter and the same `OsoLogMessages` calls (`call = "HighDemand"`);
  - `Content-Length: 0` on the `POST`;
  - the status map of research R2, where 404 → `HighDemandNotOffered` only for this call;
  - update the class summary: "Read-only client, except High Demand (spec 022)".

  Make T005 pass.

**Checkpoint**: the client can switch, and its tests pass.

---

## Phase 3: User Story 1 - An Admin turns High Demand on (Priority: P1) 🎯 MVP

**Goal**: `PUT /api/admin/locations/{id}/water-heater/high-demand` turns it on and answers with the state read back.

**Independent Test**: as an Admin, `PUT` for Hjemme. The answer's `state.specialMode.mode` is `Supercharge` with an
end time about 24 hours ahead (quickstart §2).

- [X] T007 [P] [US1] Add to `src/Features/WaterHeaters/Commands/WaterHeaterCommands.cs`:
  - `SetWaterHeaterHighDemandCommand(Guid ActingUserId, Guid LocationId, bool On) :
    ICommand<Result<WaterHeaterHighDemandResponse>>`;
  - `WaterHeaterHighDemandResponse(Guid LocationId, bool HighDemand, WaterHeaterStateResponse? State)`, with doc
    comments from data-model.md.
- [X] T008 [P] [US1] Add two `LoggerMessage` events to `src/Features/WaterHeaters/Logging/LogMessages.cs`
  (research R7):
  - 1807 `WaterHeaterHighDemandSwitched(ActingUserId, LocationId, On, StateRead)`, Information;
  - 1808 `WaterHeaterHighDemandFailed(ActingUserId, LocationId, On, Reason)`, Warning.
- [X] T009 [P] [US1] Add `ToHighDemandResponse(Guid locationId, bool on, WaterHeaterReading? reading, DateTime nowUtc)`
  to `src/Features/WaterHeaters/Mappers/WaterHeaterMapper.cs`. It reuses `ToState` when there's a reading, and gives
  `State = null` without one.
- [X] T010 [US1] Write `tests/Features.Tests/WaterHeaters/SetWaterHeaterHighDemandHandlerTests.cs` with NSubstitute
  and the helpers in `WaterHeaterTestData`. The tests fail until T011.
  - The location is missing: `Location.NotFound`, and the client isn't called.
  - No heater: `WaterHeater.NotConnected`, and the client isn't called.
  - The key can't be read: `KeyUnreadable`, and the client isn't called.
  - On succeeds and the read-back succeeds: `HighDemand = true`, `State.SpecialMode.Mode = "Supercharge"`,
    `SetHighDemandAsync(…, true, …)` called once, then `GetStateAsync` once.
  - The switch succeeds but the read-back fails: success with `State = null`.
  - The switch fails (`Unavailable`, `Rejected`, `HighDemandNotOffered`): that error, and `GetStateAsync` isn't
    called.
  - An inactive location with a heater: the switch goes through (research R5).
- [X] T011 [US1] Implement `src/Features/WaterHeaters/Handlers/SetWaterHeaterHighDemandCommandHandler.cs` with
  `ILocationRepository<Location>`, `IWaterHeaterRepository<WaterHeater>`, `IWaterHeaterClient`, `ISecretProtector`,
  `TimeProvider` and a logger.
  - Follow the order in research R5 and the rules in data-model.md.
  - Log 1807 on success and 1808 on every failure after the heater is found.
  - Pass the request's token to both OSO calls.

  Make T010 pass.
- [X] T012 [US1] No validator (research R5): the endpoints read only the route with `Route<Guid>("locationId")`, like
  `RemoveWaterHeaterEndpoint`, and have no body. Check that T015 covers a `locationId` that isn't a GUID (400).
- [X] T013 [US1] Add `src/Features/WaterHeaters/Endpoints/WaterHeaterHighDemandEndpoints.cs` with
  `TurnOnWaterHeaterHighDemandEndpoint : EndpointWithoutRequest<WaterHeaterHighDemandResponse>`:
  - `Put("/api/admin/locations/{locationId}/water-heater/high-demand")`;
  - `ActingUserId` from `ClaimTypes.NameIdentifier` and the location from `Route<Guid>("locationId")`, as in
    `RemoveWaterHeaterEndpoint`;
  - JWT only, `Roles(RoleNames.Admin)`, tags `WaterHeater`;
  - a summary and responses from contracts/endpoints.md;
  - it sends the command with `On = true` and maps errors with `WaterHeaterErrorStatus.Of(error, onConnect: false)`.
- [X] T014 [US1] In `WaterHeaterErrorStatus.Of` in `src/Features/WaterHeaters/Endpoints/WaterHeaterAdminEndpoints.cs`,
  map `"WaterHeater.HighDemandNotOffered"` to 502 (research R6).
- [X] T015 [US1] Write `tests/Features.Tests/Http/WaterHeaterHighDemandHttpTests.cs` with
  `[Collection(FastEndpointsHostCollection.Name)]`, modelled on `WaterHeaterAdminHttpTests`:
  - `PUT` as Admin gives 200, with the JSON `highDemand`, `state.specialMode.mode`, `start` and `end`;
  - a `null` state is written as `"state": null`;
  - a `locationId` that isn't a GUID gives 400, and no command is sent;
  - `NotConnected` gives 404, `Location.NotFound` 404, `Rejected` 502, `HighDemandNotOffered` 502 and
    `Unavailable` 503.

**Checkpoint**: High Demand can be turned on, and US1's tests pass.

---

## Phase 4: User Story 2 - An Admin turns High Demand off (Priority: P1)

**Goal**: `DELETE …/high-demand` turns it off.

**Independent Test**: as an Admin, `DELETE` for Hjemme. The answer's mode is `None` (quickstart §2).

- [X] T016 [US2] Add tests to `SetWaterHeaterHighDemandHandlerTests.cs`:
  - off calls `SetHighDemandAsync(…, false, …)` once, and answers `HighDemand = false` with the state read back;
  - off when it's already off (OSO answers 200 and the status shows `None`) is a success.
- [X] T017 [US2] Add `TurnOffWaterHeaterHighDemandEndpoint` to `WaterHeaterHighDemandEndpoints.cs`: `Delete(…)` on the
  same route, with the same auth, sending `On = false`.
- [X] T018 [US2] Add to `WaterHeaterHighDemandHttpTests.cs`: `DELETE` as Admin gives 200 with `highDemand: false`, and
  the command the stub receives has `On = false`.

**Checkpoint**: on and off both work.

---

## Phase 5: User Story 3 - Only an Admin may switch it (Priority: P1)

**Goal**: nobody else can switch it, and OSO isn't called for them (FR-003).

**Independent Test**: as an owner or a viewer, both calls answer 403 and the mode doesn't change.

- [X] T019 [US3] Add to `WaterHeaterHighDemandHttpTests.cs`:
  - without a token, `PUT` and `DELETE` give 401;
  - as a signed-in user who isn't an Admin (`HostCaller.User`), they give 403;
  - in both cases the dispatcher receives no `SetWaterHeaterHighDemandCommand`.

**Checkpoint**: all three stories pass.

---

## Phase 6: Polish & Cross-Cutting Concerns

- [X] T020 [P] Add to `tests/Features.Tests/WaterHeaters/NoSecretsInLogsTests.cs`: a switch on and off with the real
  client and stub, both succeeding and failing (401, 404, 503 and a timeout). No log line holds the key or the device
  id (SC-005, FR-007).
- [X] T021 [P] In `DevelopmentGuide.md`, "## Water heater (OSO)", add one line for
  `PUT`/`DELETE /api/admin/locations/{id}/water-heater/high-demand`. It says the switch is Admin only and uses
  undocumented OSO calls, and links `specs/022-oso-high-demand/contracts/endpoints.md`.
- [X] T022 Run `dotnet build` (no new warnings), `dotnet format --verify-no-changes` and `dotnet test`. Everything must
  pass. Then commit and open the PR.
  - **Checks done (2026-11-26):** build with no new warnings, format clean, 1224 tests pass (46 new). The API started
    locally lists `PUT` and `DELETE …/high-demand` in OpenAPI and answers 401 without a token. PR #79, merged and
    deployed on 2026-11-26 at 17:16 UTC. In production the route answers 401 without a token.
- [X] T023 After the deploy, Roy runs quickstart §3 against the real heater (FR-009, SC-001, SC-002, SC-003) and §4's
  log query. On and off are each done 3 times, timed, and `PUT` is run once more while on. Record per try the answer
  time and how long the state took to change, whether `end` moved on the second `PUT`, the set points, when the
  heating stopped and what the OSO app showed:
  - in research R8;
  - in the spec, under a new "Test against the real heater" note.

  If the heater doesn't follow the off call, write that down. The frontend handover (T024) then says to hide the
  switch.
  - **Replaced on 2026-11-26, on Roy's word:** "Assume it works until I tell opposite." The formal test was not run.
    Roy tests the switch in daily use and reports a problem if he finds one. The hand test of the OSO calls
    (research R1) stays the evidence that they work. The open question (does the heater follow the off call, and
    why didn't the OSO app?) is noted in research R8 and the spec.
- [X] T024 Create `specs/_backlog/oso-high-demand-frontend.md`, in the style of earlier handover notes, for the
  frontend's switch on the Heater tab. It covers:
  - the two endpoints and the answer, with a link to contracts/endpoints.md;
  - Admin only, so show the switch only to Admins;
  - put `state` into the cached heater state;
  - `HighDemandNotOffered` means hide the switch;
  - the 24 hours;
  - T023's outcome.

---

## Dependencies & Execution Order

- **Setup (T001)** comes first.
- **Foundational (T002–T006):**
  - T002 and T003 can run in parallel;
  - T004 goes before T005, and T005 before T006;
  - T006 needs T002 and T003.
- **US1 (T007–T015):**
  - it needs Phase 2;
  - T007, T008 and T009 can run in parallel;
  - T010 needs T007 and T009, and T011 needs T008 and T010;
  - T013 needs T007 and T011, and T012 is checked with T015;
  - T014 comes before T015, and T015 needs T013.
- **US2 (T016–T018):** it needs T011 and T013. On and off share the handler, so US2 mostly adds the `DELETE`
  endpoint and its tests.
- **US3 (T019):** it needs T013 and T017.
- **Polish:**
  - T020 and T021 need Phase 2 and T011;
  - T022 needs everything before it;
  - T023 needs a deploy, and T024 needs T023.

### Parallel opportunities

```text
Phase 2:  T002 ∥ T003
US1:      T007 ∥ T008 ∥ T009
Polish:   T020 ∥ T021
```

## Implementation Strategy

- **MVP:** Phases 1–3. High Demand can be turned on, and the read-back shows the result.
- **Then:** US2 (off) and US3 (Admin only) are small, so ship all three in one PR.
- **After the deploy:** the real-heater test (T023) decides whether the frontend shows the switch. If the heater
  doesn't follow the off call, the API stays as it is, unused, and the frontend hides the switch (spec Assumptions).
