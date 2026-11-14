---

description: "Task list for Norgespris Rates with Start Dates"
---

# Tasks: Norgespris Rates with Start Dates

**Input**: Design documents from `/specs/007-norgespris-rates/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/endpoints.md, quickstart.md

**Tests**: Required. xUnit + NSubstitute in `tests/Features.Tests/`, as for features 004 to 006. `dotnet build`, `dotnet test` and `dotnet format --verify-no-changes` must pass.

**Organization**: Grouped by user story from spec.md (US1 costs use the rate valid at the time, US2 add a rate in advance and list, US3 correct a mistake, US4 Admin page in the frontend repo). **Delivery order: US1, US2, US3, US4.** US1 changes the cost maths and must be proven not to change any existing figure before anything can be added; US2 makes the 1 January change possible; US3 adds the safe corrections; US4 is the UI and lives in another repository.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependency on an incomplete task)
- **[Story]**: US1-US4 from spec.md

## Path Conventions

Paths are relative to the repository root. Handlers are discovered by `Cqrs.SourceGenerator` at compile time; do not register them by hand. Frontend paths (US4) are relative to `no.sanddata.ams.frontend/`.

---

## Phase 1: Setup

- [x] T001 Record the baseline before the migration: with the API running on the current database, save the JSON from `GET /api/electricity-cost/hourly` and `/daily` for one Norgespris location and one spot location over a period with data (for example the last 7 days). Used by T016 to prove SC-002 (no figure changes). (Done 2026-11-13 on a scratch copy of the local DB with the unchanged main code.)

---

## Phase 2: Foundational (blocks all stories)

**Purpose**: the entity, the table with its seed row, and the repository. No behaviour changes yet.

- [x] T002 Create `src/Domain/Common/Entities/NorgesPrisRate.cs`: derives from `Entity`; `DateOnly ValidFrom`, `decimal RatePerKwh`, `decimal TaxPerKwh`, `Guid AddedByUserId`, all `private set`; constructor `(Guid id, DateOnly validFrom, decimal ratePerKwh, decimal taxPerKwh, Guid addedByUserId)` that also sets `CreatedAt`/`UpdatedAt` to `DateTime.UtcNow`; keep a parameterless constructor for EF Core, as `ExchangeRate` does.
- [x] T003 [P] Create `src/Infrastructure/Database/Configuration/NorgesPrisRateConfiguration.cs`: key `Id`; `ValidFrom` required; `RatePerKwh` and `TaxPerKwh` required with `HasPrecision(9, 4)`; `AddedByUserId` required, no foreign key; unique index on `ValidFrom`; `HasData` for the seed row: fixed `Id` `b0000000-0000-0000-0000-000000000001`, `ValidFrom` `2020-01-01`, rate `0.4000`, tax `0.0000`, `AddedByUserId` `Guid.Empty`, `CreatedAt`/`UpdatedAt` `2026-11-13T00:00:00Z` (data-model.md "Seed").
- [x] T004 Add `DbSet<NorgesPrisRate> NorgesPrisRate` to `src/Infrastructure/Database/ApplicationDbContext.cs` and `src/Application/Abstractions/Data/IApplicationDbContext.cs` (both list every `DbSet`; the configuration is picked up with the others).
- [x] T005 Create the migration per `DatabaseMigrations.md`: `dotnet ef migrations add AddNorgesPrisRate --project src/Infrastructure/Infrastructure.csproj --startup-project src/api/api.csproj --output-dir Database/Migrations`. Review the generated code: a `NorgesPrisRate` table in schema `public`, the unique index, one `InsertData` row, and nothing else changed. Do not hand-edit the snapshot.
- [x] T006 [P] Create `src/Application/Common/Interfaces/Repositories/INorgesPrisRateRepository.cs`: `Task<IReadOnlyList<NorgesPrisRate>> GetAllAsync(CancellationToken ct)` (ordered by `ValidFrom` ascending, no-tracking), `Task<NorgesPrisRate?> GetByIdAsync(Guid id, ...)`, `Task<bool> DateExistsAsync(DateOnly validFrom, ...)`, `Task<bool> TryInsertAsync(NorgesPrisRate rate, ...)` (saves; returns false when the date already exists, including when the unique index rejects a concurrent insert), `void Remove(NorgesPrisRate rate)` and `Task<int> SaveChangesAsync(...)`. The interface is declared on its own and does not extend `IRepository<T>`, so the immediately-saving `Delete` is not exposed.
- [x] T007 Create `src/Infrastructure/Database/Repositories/NorgesPrisRateEfRepository.cs` implementing T006 over `ApplicationDbContext`, following `ExchangeRateEfRepository` for the query style (do not derive from `GenericEfRepository`). `Remove` must not call `SaveChanges` itself (the handler saves once). `TryInsertAsync` adds the entity and saves; on `DbUpdateException` it detaches the entity, and if `DateExistsAsync` is now true returns false, otherwise rethrows.
- [x] T008 In `src/Infrastructure/AddInfrastructureToDI.cs` register `services.AddScoped<INorgesPrisRateRepository, NorgesPrisRateEfRepository>()` and `services.AddSingleton(TimeProvider.System)` (research D5). Leave the `FlatRateOptions` lines for T013.
- [x] T009 [P] Claim the log range: add the row `| 1600-1699 | NorgesPris |` to the EventId tables in `CLAUDE.md` and `DevelopmentGuide.md` (constitution: the table must be updated when a range is claimed).
- [x] T010 Apply the migration to the local database (`dotnet ef database update ...`) and check `select * from "NorgesPrisRate"` shows exactly the one seeded row (2020-01-01, 0.4000, 0.0000). (Done on the scratch copy: migration applied, one seeded row 2020-01-01 / 0.4000 / 0.0000.)

**Checkpoint**: table and repository exist; nothing in the running behaviour has changed yet.

---

## Phase 3: User Story 1 - Costs use the rate that applied at the time (Priority: P1) 🎯 MVP

**Goal**: every Norgespris price (actual, comparison, day-prices flat rate) comes from the table by Oslo date, the rates are loaded once per request, and the configuration is gone, with no existing figure changing.

**Independent Test**: with the seeded rate only, the cost endpoints return what T001 recorded. In unit tests with two rates, hours before and after the start date use their own rate, including the midnight hour in Oslo time.

- [x] T011 [P] [US1] Create `src/Application/ElectricityCost/NorgesPrisSchedule.cs`: `public static class NorgesPrisSchedule` with `DateOnly OsloDateOf(DateTime utcInstant)` (convert with `Europe/Oslo`) and `NorgesPrisRate? RateFor(IReadOnlyList<NorgesPrisRate> ratesOrderedByValidFrom, DateTime hourStartUtc)` returning the last rate whose `ValidFrom` is on or before the Oslo date, or null if none (research D2).
- [x] T012 [US1] Change `src/Application/ElectricityCost/CostCalculator.cs`: replace the `FlatRateOptions` constructor parameter with `INorgesPrisRateRepository`; keep a lazily loaded field `Task<IReadOnlyList<NorgesPrisRate>>` that calls `GetAllAsync` once per instance (the calculator is registered scoped, so once per request); in `CalculateForModelAsync` the Norgespris rate is `RateFor(...)` `RatePerKwh + TaxPerKwh`, and when `RateFor` returns null the result is null (cost unavailable, never zero) (research D3).
- [x] T013 [US1] Remove the fixed configuration: delete `src/Application/ElectricityCost/FlatRateOptions.cs`, the two `FlatRateOptions` lines in `src/Infrastructure/AddInfrastructureToDI.cs`, and the `"NorgesPris"` section in `src/api/appsettings.json` (FR-018). Search the repo for any remaining `FlatRateOptions` or `NorgesPris:` reference (docs excluded) and fix them.
- [x] T014 [US1] Update the three existing test classes that built the calculator from `FlatRateOptions`: `tests/Features.Tests/ElectricityCost/CostCalculatorTests.cs` (rate 0.40, tax 0.05), `CostHandlerTests.cs` and `GetDayPricesQueryHandlerTests.cs` (rate 0.40). Add a small helper `tests/Features.Tests/NorgesPris/NorgesPrisRates.cs` that builds a substitute `INorgesPrisRateRepository` returning given rates, so all tests share it. Existing assertions must pass unchanged.
- [x] T015 [P] [US1] Write `tests/Features.Tests/NorgesPris/NorgesPrisScheduleTests.cs`: hour before and after a start date; the hour starting `2026-12-31T23:00Z` (00:00 on 1 January in Oslo) uses the new rate while `22:00Z` uses the old one; same for a summer start date (`22:00Z` the day before); an hour before the earliest rate returns null; the 23-hour day `2027-03-28` and the 25-hour day `2026-10-25` give every hour exactly one rate; a rate starting today applies from the first hour of today.
- [x] T016 [P] [US1] Extend `tests/Features.Tests/ElectricityCost/CostCalculatorTests.cs` (or add `CostCalculatorNorgesPrisTests.cs` in `tests/Features.Tests/NorgesPris/`): a Norgespris location is priced with the old rate before and the new rate after the start; a spot location's comparison figure follows the same rule; tax is added to the rate; no rate for the hour gives a null `RateCost`; pricing 24 hours calls `GetAllAsync` exactly once (`Received(1)`), proving FR-017/SC-006.
- [x] T017 [US1] Run `dotnet build`, `dotnet test`, `dotnet format --verify-no-changes`. Start the API on the migrated database and repeat the T001 requests: the JSON must be identical (SC-002). (Done: daily, hourly and prices responses are numerically identical to the T001 baseline; only trailing zeros differ, 0.40 became 0.4000.)

**Checkpoint**: costs read from the table; with only the seeded rate nothing differs; config is gone. Deployable on its own.

---

## Phase 4: User Story 2 - An Admin adds a new rate in advance (Priority: P1)

**Goal**: an Admin lists the rates and adds a future one without a redeploy.

**Independent Test**: as Admin, list (one rate, `InEffect`), add a rate starting a few days ahead (`201`), list again (two rates, the new one `Upcoming`), and the costs on each side of the date differ as expected.

- [x] T018 [P] [US2] Create `src/Features/NorgesPris/Logging/LogMessages.cs` in the style of `Features/Locations/Logging/LogMessages.cs` (internal static, `LoggerMessage.Define`, EventIds 1600-1699): `RatesListed` (1600, acting user id and count), `RateAdded` (1601: rate id, start date, rate, tax, acting user id), `RateDeleted` (1602: rate id, start date, acting user id), `RateRefused` (1603: reason code `DateExists` / `NotNewest` / `AlreadyStarted` / `NotFound`, acting user id). Reason codes are the stable ones from contracts/endpoints.md.
- [x] T019 [P] [US2] Create `src/Features/NorgesPris/Commands/NorgesPrisCommands.cs` (`AddNorgesPrisRateCommand(DateOnly ValidFrom, decimal RatePerKwh, decimal TaxPerKwh, Guid ActingUserId)` and `DeleteNorgesPrisRateCommand(Guid Id, Guid ActingUserId)`, both `ICommand<Result<...>>`; the delete command is used in US3), `src/Features/NorgesPris/Queries/ListNorgesPrisRatesQuery.cs` (`ListNorgesPrisRatesQuery(Guid ActingUserId)`), and the `NorgesPrisRateResponse(Guid Id, DateOnly ValidFrom, decimal RatePerKwh, decimal TaxPerKwh, decimal TotalPerKwh, string Status)` record.
- [x] T020 [P] [US2] Create `src/Features/NorgesPris/Mappers/NorgesPrisMapper.cs`: static `ToResponses(IReadOnlyList<NorgesPrisRate> ordered, DateOnly today)` computing `Status` (`InEffect` for the last rate whose `ValidFrom <= today`, `Upcoming` for `ValidFrom > today`, else `Superseded`) and `TotalPerKwh = RatePerKwh + TaxPerKwh`; plus a single-rate overload for the `201` body.
- [x] T021 [US2] Create `src/Features/NorgesPris/Handlers/ListNorgesPrisRatesQueryHandler.cs` (loads `GetAllAsync`, today = `NorgesPrisSchedule.OsloDateOf(timeProvider.GetUtcNow().UtcDateTime)`, maps, logs `RatesListed`). Inject `TimeProvider`.
- [x] T022 [US2] Create `src/Features/NorgesPris/Handlers/AddNorgesPrisRateCommandHandler.cs`: if `DateExistsAsync` then `Error.Conflict("NorgesPris.DateExists", ...)` and log `RateRefused`; otherwise `TryInsertAsync(new NorgesPrisRate(Guid.NewGuid(), ...))` with `AddedByUserId = ActingUserId`; a `false` result (lost a race) is also `DateExists`; log `RateAdded`, return the created rate mapped with today's status (reload the list for the status). The date-dependent "newest only" rule is added in T028.
- [x] T023 [P] [US2] Create `src/Features/NorgesPris/Validators/AddNorgesPrisRateValidator.cs` (`Validator<AddNorgesPrisRateRequest>`): `ValidFrom` not default (`DateOnly.MinValue`), `RatePerKwh` and `TaxPerKwh` `>= 0`, `<= 100`, and at most 4 decimals (`Math.Round(x, 4) == x`), each with a clear message and error code.
- [x] T024 [US2] Create the endpoints in `src/Features/NorgesPris/Endpoints/`, following `GetAdminLocationsEndpoint`/`CreateLocationEndpoint`: `ListNorgesPrisRatesEndpoint` (`Get("/api/admin/norgespris")`) and `AddNorgesPrisRateEndpoint` (`Post("/api/admin/norgespris")`, request record `AddNorgesPrisRateRequest(DateOnly ValidFrom, decimal RatePerKwh, decimal TaxPerKwh)`, answer `201` with `Location: /api/admin/norgespris`, `409` for `ErrorType.Conflict`). Both `AuthSchemes(JwtBearerDefaults.AuthenticationScheme)`, `Roles(RoleNames.Admin)`, Tags "NorgesPris", Summary text with the `401`/`403`/`400`/`409` responses from contracts/endpoints.md. The acting user id comes from `ClaimTypes.NameIdentifier`.
- [x] T025 [P] [US2] Write `tests/Features.Tests/NorgesPris/AddNorgesPrisRateValidatorTests.cs`: valid; negative rate; negative tax; five decimals; over 100; default date.
- [x] T026 [P] [US2] Write `tests/Features.Tests/NorgesPris/NorgesPrisHandlerTests.cs` with a small `TimeProvider` subclass (settable now) in the same folder: list marks exactly one rate `InEffect`, later ones `Upcoming`, earlier ones `Superseded`, and a rate starting today is `InEffect`; the Oslo-date boundary (now = `2026-12-31T23:30Z` means today is 1 January); add of a new future date saves and returns the rate; add of a future date earlier than another future date is accepted; add of an existing date returns `NorgesPris.DateExists` and saves nothing.
- [x] T027 [P] [US2] Write `tests/Features.Tests/Http/NorgesPrisHttpTests.cs` following `LinkUserLocationHttpTests.cs` and `EndpointHost.cs`. `EndpointHost` always signs in as an Admin, so first extend `StartAsync` with an optional caller parameter (`Anonymous`, `User`, `Admin`; default `Admin`, so existing tests are untouched) that its auth handler honours (no sign-in gives `401`, a `User` role gives `403`). Then: no token gives `401`, a signed-in non-Admin gives `403`, an Admin gets `200` on list and `201` on add, for the list and add endpoints.

**Checkpoint**: an Admin can prepare the 1 January change through Scalar. Together with US1 this is the whole business need.

---

## Phase 5: User Story 3 - An Admin corrects a mistake (Priority: P2)

**Goal**: a wrong rate can be fixed without silently rewriting history.

**Independent Test**: add a future rate and delete it; deleting the one in effect or the seeded one is refused; a past-dated newest rate is accepted, a past-dated one that is not the newest is refused.

- [x] T028 [US3] Extend `AddNorgesPrisRateCommandHandler` (`src/Features/NorgesPris/Handlers/`): when `ValidFrom` is today (Oslo date) or earlier, it must be later than the greatest existing `ValidFrom`, else `Error.Conflict("NorgesPris.NotNewest", ...)`; and the command must carry `ConfirmPastDate == true`, else `Error.Conflict("NorgesPris.ConfirmationRequired", ...)` (log `RateRefused` for both). Check order: `DateExists`, `NotNewest`, `ConfirmationRequired`. Add `bool ConfirmPastDate` to `AddNorgesPrisRateCommand` and `AddNorgesPrisRateRequest` (default false) and pass it through the endpoint. Update the endpoint's Summary text to describe the rule (the API holds the business rule; the web app only shows the warning and sends the flag).
- [x] T029 [US3] Create `src/Features/NorgesPris/Handlers/DeleteNorgesPrisRateCommandHandler.cs`: unknown id gives `Error.NotFound("NorgesPris.NotFound", ...)`; `ValidFrom` today or earlier gives `Error.Conflict("NorgesPris.AlreadyStarted", ...)`; otherwise remove, save once, log `RateDeleted`. Return a plain success result.
- [x] T030 [US3] Create `src/Features/NorgesPris/Endpoints/DeleteNorgesPrisRateEndpoint.cs` (`Delete("/api/admin/norgespris/{id}")`, Admin only, `204` on success, `404`/`409` from the error type, as `UnlinkUserLocationEndpoint` does).
- [x] T031 [P] [US3] Extend `tests/Features.Tests/NorgesPris/NorgesPrisHandlerTests.cs`: add dated today as the newest is accepted; add dated yesterday while a later rate exists is `NotNewest`; add dated yesterday as the newest is accepted; an existing date still answers `DateExists`; delete of an upcoming rate removes it; delete of the rate in effect, of one starting today and of the seeded one answers `AlreadyStarted` and removes nothing; delete of an unknown id answers `NotFound`; after deleting the newest upcoming rate the calculator prices with the previous rate again (use a real list in the stub, not mocks of the handler).
- [x] T032 [P] [US3] Extend `tests/Features.Tests/Http/NorgesPrisHttpTests.cs` with the delete endpoint (`401`, `403`, Admin `204`/`409`/`404`).
- [x] T033 [US3] Run quickstart.md sections 3 to 5 against the local API and record the result next to this task. Check in particular the midnight hour (quickstart step 3.5) and that deleting the upcoming rate leaves costs unchanged. (Done on the scratch copy: add, refusals, 401, deletes and the Oslo-midnight switch verified live; at 2026-10-02T22:00Z the rate changed from 0.40 to 0.70.)

**Checkpoint**: every correction path is safe; rates in effect cannot be changed.

---

## Phase 6: User Story 4 - Admin page in the web app (Priority: P2)

**Goal**: the Admin uses a page instead of Scalar. This work is in the separate repository `no.sanddata.ams.frontend`, on its own branch, built after the backend is deployed (the page needs the endpoints). Its own spec there is created first.

**Independent Test**: sign in as Admin, add a future rate and see it, delete it; sign in as a regular user and find neither the menu entry nor the page.

- [x] T034 [US4] **(frontend)** Run `speckit-specify` in `no.sanddata.ams.frontend` for "Admin page for Norgespris rates", pointing at `specs/007-norgespris-rates/contracts/endpoints.md` in this repository as the backend contract (the same way its 002 feature used `contracts/backend-required.md`). (Done: frontend spec `specs/004-norgespris-admin-page`, frontend PR #13 https://github.com/roysand/no.sanddata.ams.frontend/pull/13; T035-T039 are done there.)
- [x] T035 [US4] **(frontend)** Add to `src/features/admin/types.ts`, `api.ts` and `hooks.ts` the type `NorgesPrisRate`, the calls `getNorgesPrisRates`, `addNorgesPrisRate`, `deleteNorgesPrisRate` against `/api/admin/norgespris`, and query/mutation hooks that refresh the list, following `getAdminLocations` / `setLocationLink`.
- [x] T036 [P] [US4] **(frontend)** Create `src/features/admin/NorgesPrisPage.tsx`: table (start date, rate, tax, total), the `InEffect` row highlighted, `Upcoming` rows marked and with a delete action, `Superseded` rows without one; an "Add rate" button.
- [x] T037 [P] [US4] **(frontend)** Create `src/features/admin/AddNorgesPrisRateDialog.tsx`: start date, rate, tax with the same limits as the API (>= 0, at most 4 decimals); when the chosen date is today or earlier, show the warning "Costs shown for past hours will be recalculated", require the Admin to accept it, and only then send `confirmPastDate: true` (the API refuses such a request without it); show the server's `409` messages in the dialog and keep it open.
- [x] T038 [US4] **(frontend)** Add the route `/admin/norgespris` inside the existing `AdminRoute` children in `src/app/routes.tsx` and a menu entry in `src/components/Header.tsx` visible only to Admins, beside `/admin/users`.
- [x] T039 [P] [US4] **(frontend)** Tests in the frontend's style (`*.test.tsx`): the table highlights the rate in effect, delete only on upcoming rows, the warning appears for a past date and not for a future one and the flag is sent only after accepting it, a server error is shown, and a regular user has no menu entry and no access.

---

## Phase 7: Polish & cross-cutting

- [x] T040 [P] Update `specs/002-electricity-cost-tracking/research.md` section 8 ("Flat government rate is configuration, not data") with a one-line note that feature 007 replaced the configuration with dated rates in the database, so the old text is not mistaken for current.
- [x] T041 Run `dotnet build` (no new warnings), `dotnet test`, `dotnet format --verify-no-changes`. Confirm a clean search: no `FlatRateOptions` and no `NorgesPris:` configuration key remain in `src/`, `compose*.yaml` or the docs that describe configuration. (Done for the files this feature touched: build has no new warnings, 334 tests pass, `dotnet format --include` on them is clean. A repo-wide `dotnet format --verify-no-changes` already fails on old files, unrelated to this feature.)
- [ ] T042 Before deploy: check the production compose and environment files set no `NorgesPris__*` variable (the seed uses 0.40/0, research D6); if one exists with other values, add a second rate row by hand-written migration data instead of the default. After deploy, `GET /api/admin/norgespris` as Admin shows the one seeded rate as `InEffect`.
- [ ] T043 After deploy, add the 1 January 2027 rate once the government's figures are known (the Admin page, or Scalar before T034-T039 are done) and check costs on both sides of the date (quickstart section 3).

---

## Dependencies & Execution Order

- **Phase 1** first (baseline needs the unchanged system).
- **Phase 2** blocks everything. Within it: T002 before T003-T007; T003 and T004 before T005; T006 before T007 and T008; T005 before T010.
- **US1** (T011-T017) needs Phase 2. T011 before T012; T012 before T013 and T014; T014 before T016.
- **US2** needs US1 only for the migration being in place (the endpoints themselves only need Phase 2); do it after US1 so the cost maths is proven first. T018-T020 and T023 are independent of each other; T021, T022 need T018-T020; T024 needs T019-T023.
- **US3** needs US2 (extends its handler and test files). T028 before T031; T029 before T030 and T031.
- **US4** needs the backend deployed (US2 at least, US3 for delete). It is a separate repository and branch.
- **Polish** last.

### Parallel opportunities

- Phase 2: T003, T006 and T009 in parallel; T004 after T002.
- US1: T011 alone first, then T015 and T016 in parallel with T013/T014 once T012 is done.
- US2: T018, T019, T020, T023 in parallel; later T025, T026, T027 in parallel.
- US3: T031 and T032 in parallel.
- US4: T036, T037 and T039 in parallel after T035.

## Implementation Strategy

1. **MVP = Phase 1 + 2 + US1 + US2.** After these the Admin can add the 1 January rate, and costs follow it. Deploy at this point; the frontend page and the safe-correction rules are not needed to meet the date.
2. Add **US3** next (small, prevents a mistaken rate from being stuck).
3. Build **US4** in the frontend repo when convenient; until then use Scalar.
4. Deploy US1 well before 1 January so the migration and the "figures unchanged" check (T017, T042) are done calmly, not on the day.
